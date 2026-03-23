using System.Buffers;
using System.Threading.Channels;
using Clip.OpenTelemetry.Export;
using Clip.OpenTelemetry.Mapping;
using Clip.Sinks;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Clip.OpenTelemetry;

/// <summary>
/// OTLP log exporter sink. Enqueues log entries on the calling thread and exports
/// them in batches on a background thread via gRPC or HTTP/protobuf.
/// </summary>
public sealed class OtlpSink : ILogSink
{
    private readonly Channel<LogEntry> _channel;
    private readonly IExporter _exporter;
    private readonly ExportLogsServiceRequest _request;
    private readonly ScopeLogs _scopeLogs;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly RetryPolicy _retryPolicy;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryBaseDelay;
    private readonly TimeSpan _retryMaxDelay;
    private readonly Task _exportTask;
    private readonly CancellationTokenSource _cts = new();
    private long _rejectedRecords;
    private long _failedExports;

    /// <summary>Total number of log records rejected by the collector via partial success responses.</summary>
    public long RejectedRecords => Interlocked.Read(ref _rejectedRecords);

    /// <summary>Total number of export batches that failed (network error, timeout, etc.).</summary>
    public long FailedExports => Interlocked.Read(ref _failedExports);

    public OtlpSink(OtlpSinkOptions options)
        : this(options, options.Protocol switch
        {
            OtlpProtocol.HttpProtobuf => new HttpExporter(options),
            _ => new GrpcExporter(options),
        })
    {
    }

    internal OtlpSink(OtlpSinkOptions options, IExporter exporter)
    {
        options.ApplyEnvironment();

        _batchSize = options.BatchSize;
        _flushInterval = options.FlushInterval;
        _retryPolicy = options.RetryPolicy;
        _maxRetries = options.MaxRetries;
        _retryBaseDelay = options.RetryBaseDelay;
        _retryMaxDelay = options.RetryMaxDelay;

        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        _exporter = exporter;

        // Pre-build the resource and scope — these are constant for the lifetime of the sink.
        var resource = new Resource();
        resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = options.ServiceName },
        });
        if (options.ServiceVersion is { Length: > 0 } version)
            resource.Attributes.Add(new KeyValue
            {
                Key = "service.version",
                Value = new AnyValue { StringValue = version },
            });
        foreach (var (key, value) in options.ResourceAttributes)
            resource.Attributes.Add(new KeyValue
            {
                Key = key,
                Value = new AnyValue { StringValue = value },
            });

        _scopeLogs = new ScopeLogs
        {
            Scope = new InstrumentationScope
            {
                Name = "Clip",
                Version = typeof(OtlpSink).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            },
        };

        var resourceLogs = new ResourceLogs { Resource = resource };
        resourceLogs.ScopeLogs.Add(_scopeLogs);

        _request = new ExportLogsServiceRequest();
        _request.ResourceLogs.Add(resourceLogs);

        _exportTask = Task.Run(ExportLoopAsync);
    }

    //
    // ILogSink — hot path
    //

    public void Write(DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        Field[] fieldArray;
        var fieldCount = fields.Length;
        if (fieldCount == 0)
        {
            fieldArray = [];
        }
        else
        {
            fieldArray = ArrayPool<Field>.Shared.Rent(fieldCount);
            fields.CopyTo(fieldArray);
        }

        var entry = new LogEntry(timestamp, level, message, fieldArray, fieldCount, exception);
        if (!_channel.Writer.TryWrite(entry) && fieldCount > 0)
            ArrayPool<Field>.Shared.Return(fieldArray, true);
    }

    //
    // Background export loop
    //

    private async Task ExportLoopAsync()
    {
        var reader = _channel.Reader;
        var batch = new List<LogEntry>(_batchSize);
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
            try
            {
                // Wait for at least one entry, or until flush interval elapses.
                // The linked CTS ensures WaitToReadAsync completes (via cancellation)
                // before we loop back — required by SingleReader: true.
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                flushCts.CancelAfter(_flushInterval);

                try
                {
                    await reader.WaitToReadAsync(flushCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Flush interval elapsed — export whatever we have.
                }

                // Drain available entries up to batch size.
                while (batch.Count < _batchSize && reader.TryRead(out var entry))
                    batch.Add(entry);

                if (batch.Count > 0)
                    await ExportBatchAsync(batch, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Export failure must not crash the loop. Entries in the batch are lost.
                Interlocked.Increment(ref _failedExports);
            }
            finally
            {
                ReturnFields(batch);
                batch.Clear();
            }

        // Final drain on shutdown.
        while (reader.TryRead(out var entry))
            batch.Add(entry);

        if (batch.Count > 0)
            try
            {
                await ExportBatchAsync(batch, CancellationToken.None);
            }
            catch
            {
                // Best-effort flush.
            }
            finally
            {
                ReturnFields(batch);
            }
    }

    private async Task ExportBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        _scopeLogs.LogRecords.Clear();

        foreach (var entry in batch)
        {
            var (severityNumber, severityText) = FieldMapper.ToSeverity(entry.Level);

            var record = new OtlpLogRecord
            {
                TimeUnixNano = ToUnixNano(entry.Timestamp),
                ObservedTimeUnixNano = ToUnixNano(entry.Timestamp),
                SeverityNumber = severityNumber,
                SeverityText = severityText,
                Body = new AnyValue { StringValue = entry.Message },
            };

            var fields = entry.Fields.AsSpan(0, entry.FieldCount);
            foreach (ref readonly var field in fields)
                record.Attributes.Add(FieldMapper.ToKeyValue(in field));

            if (entry.Exception is { } ex)
                FieldMapper.AddExceptionAttributes(record.Attributes, ex);

            _scopeLogs.LogRecords.Add(record);
        }

        for (var attempt = 0;; attempt++)
            try
            {
                var response = await _exporter.ExportAsync(_request, ct);

                if (response.PartialSuccess is { RejectedLogRecords: > 0 } partial)
                    Interlocked.Add(ref _rejectedRecords, partial.RejectedLogRecords);

                return; // Success.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var canRetry = _retryPolicy == RetryPolicy.ExponentialBackoff
                               && attempt < _maxRetries
                               && RetryClassifier.IsRetryable(ex);

                if (!canRetry)
                    throw; // Bubbles to export loop catch → increments FailedExports.

                var baseMs = _retryBaseDelay.TotalMilliseconds * (1 << attempt);
                var cappedMs = Math.Min(baseMs, _retryMaxDelay.TotalMilliseconds);
                var jitter = Random.Shared.NextDouble() * 0.5 * cappedMs;
                await Task.Delay(TimeSpan.FromMilliseconds(cappedMs + jitter), ct);
            }
    }

    //
    // Helpers
    //

    private static ulong ToUnixNano(DateTimeOffset ts)
    {
        // OTLP uses nanoseconds since Unix epoch.
        return (ulong)(ts.UtcDateTime - DateTime.UnixEpoch).Ticks * 100;
    }

    private static void ReturnFields(List<LogEntry> batch)
    {
        foreach (var entry in batch)
            if (entry.FieldCount > 0)
                ArrayPool<Field>.Shared.Return(entry.Fields, true);
    }

    //
    // Disposal
    //

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            _exportTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Export loop may fault during cancellation — safe to ignore on disposal.
        }

        _exporter.Dispose();
        _cts.Dispose();
    }

    //
    // Internal entry type
    //

    private readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Message,
        Field[] Fields,
        int FieldCount,
        Exception? Exception);
}
