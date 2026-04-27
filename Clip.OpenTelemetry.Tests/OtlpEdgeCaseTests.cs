using Clip.OpenTelemetry.Export;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Tests;

/// <summary>
/// Edge cases not covered by integration or retry suites: empty-batch suppression,
/// queue overflow under DropOldest, and write-after-dispose safety. Each exercises
/// a code path the existing tests don't reach.
/// </summary>
public class OtlpEdgeCaseTests
{
    [Fact]
    public async Task EmptyChannel_FlushIntervalExpires_NoExportInvoked()
    {
        // The export loop must not call ExportAsync with an empty batch when the flush
        // interval ticks. Otherwise an idle process would emit empty OTLP requests forever.
        var exporter = new RecordingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            // Wait several flush intervals, write nothing.
            await Task.Delay(300);
        }

        Assert.Equal(0, exporter.Calls);
    }

    [Fact]
    public async Task Overflow_DropOldest_DropsEarliestNotNewest()
    {
        // QueueCapacity small enough to overflow; writes faster than the drain. With
        // DropOldest semantics the latest entries must reach the exporter.
        var exporter = new RecordingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(20),
            QueueCapacity = 4,
        };

        using (var sink = new OtlpSink(options, exporter))
        {
            // Write far more than capacity in a tight loop. Some will be dropped.
            for (var i = 0; i < 200; i++)
                sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, $"entry-{i:D3}",
                    ReadOnlySpan<Field>.Empty, null);

            await Task.Delay(500);
        }

        var seen = exporter.SeenMessages;
        Assert.NotEmpty(seen);
        // Some of the latest entries must be present — the channel overflow can't drop
        // *everything* recent. Look for any of the last 20 entries.
        var lastTwenty = Enumerable.Range(180, 20).Select(i => $"entry-{i:D3}").ToHashSet();
        Assert.Contains(seen, m => lastTwenty.Contains(m));
    }

    [Fact]
    public void WriteAfterDispose_DoesNotCrashOrLeak()
    {
        // Once Dispose has completed the channel, TryWrite returns false and the rented
        // Field[] must be returned to the pool — same invariant as BackgroundSink.
        var exporter = new RecordingExporter();
        var options = new OtlpSinkOptions
        {
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

        var sink = new OtlpSink(options, exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "before", ReadOnlySpan<Field>.Empty, null);
        sink.Dispose();

        for (var i = 0; i < 100; i++)
        {
            var ex = Record.Exception(() => sink.Write(
                DateTimeOffset.UtcNow, LogLevel.Info, $"after-{i}",
                [new Field("k", i), new Field("k2", i * 2)], null));
            Assert.Null(ex);
        }
    }

    /// <summary>
    /// Captures every exported message and counts ExportAsync invocations.
    /// Distinct from CapturingExporter in OtlpSinkIntegrationTests in that it tracks
    /// the *raw* call count (including empty-batch checks, if any) rather than batches.
    /// </summary>
    private sealed class RecordingExporter : IExporter
    {
        private readonly Lock _lock = new();
        private readonly List<string> _seen = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<string> SeenMessages
        {
            get
            {
                lock (_lock) return [.. _seen];
            }
        }

        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            lock (_lock)
                foreach (var record in request.ResourceLogs[0].ScopeLogs[0].LogRecords)
                    _seen.Add(record.Body.StringValue);
            return Task.FromResult(new ExportLogsServiceResponse());
        }

        public void Dispose() { }
    }
}
