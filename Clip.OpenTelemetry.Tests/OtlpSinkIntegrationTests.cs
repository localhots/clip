using Clip.OpenTelemetry.Export;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Clip.OpenTelemetry.Tests;

public class OtlpSinkIntegrationTests
{
    [Fact]
    public async Task SingleEntry_ExportedWithCorrectFields()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            ReadOnlySpan<Field> fields =
            [
                new Field("host", "localhost"),
                new Field("port", 8080),
            ];
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Server started", fields, null);

            // Wait for the background export.
            await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));
        }

        Assert.Single(exporter.Requests);

        var request = exporter.Requests[0];
        var record = request.ResourceLogs[0].ScopeLogs[0].LogRecords[0];

        Assert.Equal("Server started", record.Body.StringValue);
        Assert.Equal(SeverityNumber.Info, record.SeverityNumber);
        Assert.Equal("INFO", record.SeverityText);
        Assert.Equal(2, record.Attributes.Count);
        Assert.Equal("host", record.Attributes[0].Key);
        Assert.Equal("localhost", record.Attributes[0].Value.StringValue);
        Assert.Equal("port", record.Attributes[1].Key);
        Assert.Equal(8080, record.Attributes[1].Value.IntValue);
    }

    [Fact]
    public async Task Batching_AccumulatesEntries()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 5,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            for (var i = 0; i < 5; i++)
                sink.Write(DateTimeOffset.UtcNow, LogLevel.Debug, $"msg {i}",
                    ReadOnlySpan<Field>.Empty, null);

            await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));
        }

        Assert.Single(exporter.Requests);
        Assert.Equal(5, exporter.Requests[0].ResourceLogs[0].ScopeLogs[0].LogRecords.Count);
    }

    [Fact]
    public async Task Exception_MappedToSemanticAttributes()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        var ex = new InvalidOperationException("test error");

        using (var sink = new OtlpSink(options, exporter))
        {
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed",
                ReadOnlySpan<Field>.Empty, ex);

            await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));
        }

        var record = exporter.Requests[0].ResourceLogs[0].ScopeLogs[0].LogRecords[0];
        Assert.Equal(SeverityNumber.Error, record.SeverityNumber);

        var attrs = record.Attributes;
        Assert.Contains(attrs, a => a.Key == "exception.type"
            && a.Value.StringValue == "System.InvalidOperationException");
        Assert.Contains(attrs, a => a.Key == "exception.message"
            && a.Value.StringValue == "test error");
        Assert.Contains(attrs, a => a.Key == "exception.stacktrace");
    }

    [Fact]
    public async Task Resource_ContainsServiceName()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
            ServiceName = "test-service",
            ServiceVersion = "1.2.3",
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "hello",
                ReadOnlySpan<Field>.Empty, null);

            await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));
        }

        var resource = exporter.Requests[0].ResourceLogs[0].Resource;
        Assert.Contains(resource.Attributes,
            a => a.Key == "service.name" && a.Value.StringValue == "test-service");
        Assert.Contains(resource.Attributes,
            a => a.Key == "service.version" && a.Value.StringValue == "1.2.3");
    }

    [Fact]
    public async Task FlushInterval_ExportsPartialBatch()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 100,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            // Write fewer entries than batch size.
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "partial",
                ReadOnlySpan<Field>.Empty, null);

            // Flush interval should trigger export before batch is full.
            await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));
        }

        Assert.Single(exporter.Requests);
        Assert.Single(exporter.Requests[0].ResourceLogs[0].ScopeLogs[0].LogRecords);
    }

    [Fact]
    public async Task Dispose_FlushesPendingEntries()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1000,
            FlushInterval = TimeSpan.FromSeconds(60),
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "before dispose",
                ReadOnlySpan<Field>.Empty, null);
            // Don't wait — let Dispose() flush it.
        }

        // Give background task a moment to complete after Dispose.
        await Task.Delay(100);

        var allRecords = exporter.Requests
            .SelectMany(r => r.ResourceLogs)
            .SelectMany(rl => rl.ScopeLogs)
            .SelectMany(sl => sl.LogRecords)
            .ToList();

        Assert.Single(allRecords);
        Assert.Equal("before dispose", allRecords[0].Body.StringValue);
    }

    [Fact]
    public async Task Counters_ZeroOnSuccess()
    {
        var exporter = new CapturingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using var sink = new OtlpSink(options, exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "ok", ReadOnlySpan<Field>.Empty, null);
        await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));

        Assert.Equal(0, sink.RejectedRecords);
        Assert.Equal(0, sink.FailedExports);
    }

    [Fact]
    public async Task FailedExports_IncrementedOnExportFailure()
    {
        var exporter = new FailingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using var sink = new OtlpSink(options, exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "will fail", ReadOnlySpan<Field>.Empty, null);

        // Wait for the export attempt.
        await Task.Delay(500);

        Assert.True(sink.FailedExports >= 1);
    }

    [Fact]
    public async Task RejectedRecords_IncrementedOnPartialSuccess()
    {
        var exporter = new PartialSuccessExporter(rejectedCount: 3);
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using var sink = new OtlpSink(options, exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "partial", ReadOnlySpan<Field>.Empty, null);

        await exporter.WaitForExportsAsync(1, timeout: TimeSpan.FromSeconds(2));

        Assert.Equal(3, sink.RejectedRecords);
        Assert.Equal(0, sink.FailedExports);
    }

    //
    // Test doubles
    //

    /// <summary>
    /// Captures export batches by snapshotting the log records and resource.
    /// The sink reuses the request object, so we extract the data we need
    /// rather than holding a reference to a mutated object.
    /// </summary>
    private sealed class CapturingExporter : IExporter
    {
        private readonly Lock _lock = new();
        private readonly List<ExportBatch> _batches = [];
        private readonly SemaphoreSlim _signal = new(0);

        public IReadOnlyList<ExportBatch> Batches
        {
            get
            {
                lock (_lock)
                    return [.. _batches];
            }
        }

        // Convenience: rebuild request-shaped objects for assertions.
        public IReadOnlyList<ExportLogsServiceRequest> Requests
        {
            get
            {
                lock (_lock)
                    return _batches.Select(b =>
                    {
                        var scopeLogs = new ScopeLogs();
                        scopeLogs.LogRecords.AddRange(b.Records);
                        var resourceLogs = new ResourceLogs { Resource = b.Resource };
                        resourceLogs.ScopeLogs.Add(scopeLogs);
                        var req = new ExportLogsServiceRequest();
                        req.ResourceLogs.Add(resourceLogs);
                        return req;
                    }).ToList();
            }
        }

        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
        {
            // Snapshot the records and resource before the sink clears them.
            var rl = request.ResourceLogs[0];
            var records = rl.ScopeLogs[0].LogRecords.ToList();
            var resource = rl.Resource;

            lock (_lock)
                _batches.Add(new ExportBatch(resource, records));
            _signal.Release();
            return Task.FromResult(new ExportLogsServiceResponse());
        }

        public async Task WaitForExportsAsync(int count, TimeSpan timeout)
        {
            for (var i = 0; i < count; i++)
                if (!await _signal.WaitAsync(timeout))
                    throw new TimeoutException(
                        $"Expected {count} exports, got {_batches.Count} after {timeout}");
        }

        public void Dispose() => _signal.Dispose();

        internal sealed record ExportBatch(Resource Resource, List<LogRecord> Records);
    }

    private sealed class FailingExporter : IExporter
    {
        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");

        public void Dispose() { }
    }

    private sealed class PartialSuccessExporter(long rejectedCount) : IExporter
    {
        private readonly SemaphoreSlim _signal = new(0);

        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
        {
            var response = new ExportLogsServiceResponse
            {
                PartialSuccess = new ExportLogsPartialSuccess
                {
                    RejectedLogRecords = rejectedCount,
                    ErrorMessage = "quota exceeded",
                },
            };
            _signal.Release();
            return Task.FromResult(response);
        }

        public async Task WaitForExportsAsync(int count, TimeSpan timeout)
        {
            for (var i = 0; i < count; i++)
                if (!await _signal.WaitAsync(timeout))
                    throw new TimeoutException($"Expected {count} exports");
        }

        public void Dispose() => _signal.Dispose();
    }
}
