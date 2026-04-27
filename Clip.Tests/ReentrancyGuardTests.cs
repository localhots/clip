using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies the per-thread reentry guard: a log call made from inside a sink, enricher,
/// filter, or redactor returns silently rather than recursing infinitely. Cross-thread
/// log calls are unaffected.
/// </summary>
public class ReentrancyGuardTests
{
    private sealed class ReentrantSink(Func<Logger> getLogger) : ILogSink
    {
        public int Calls;
        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            Calls++;
            // Try to log from inside the sink. The reentry guard must short-circuit
            // this so we don't recurse forever.
            getLogger().Info("from inside sink");
        }

        public void Dispose() { }
    }

    private sealed class ReentrantEnricher(Func<Logger> getLogger) : ILogEnricher
    {
        public int Calls;
        public void Enrich(List<Field> target)
        {
            Calls++;
            getLogger().Info("from inside enricher");
        }
    }

    private sealed class CountingSink : ILogSink
    {
        public int Calls;
        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception) => Calls++;
        public void Dispose() { }
    }

    private sealed class ReentrantFilter(Func<Logger> getLogger) : ILogFilter
    {
        public int Calls;
        public bool ShouldSkip(string key)
        {
            Calls++;
            getLogger().Info("from inside filter");
            return false;
        }
    }

    private sealed class ReentrantRedactor(Func<Logger> getLogger) : ILogRedactor
    {
        public int Calls;
        public void Redact(ref Field field)
        {
            Calls++;
            getLogger().Info("from inside redactor");
        }
    }

    [Fact]
    public void Sink_LogsRecursively_SinkInvokedExactlyOnce()
    {
        Logger? logger = null;
        var sink = new ReentrantSink(() => logger!);
        logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(sink));

        logger.Info("outer");

        // The outer call entered Write once; the recursive call from inside Write
        // hit the reentry guard and returned without re-entering.
        Assert.Equal(1, sink.Calls);
    }

    [Fact]
    public void Enricher_LogsRecursively_SinkInvokedExactlyOnce()
    {
        Logger? logger = null;
        var enricher = new ReentrantEnricher(() => logger!);
        var counter = new CountingSink();
        logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(enricher)
            .WriteTo.Sink(counter));

        logger.Info("outer");

        Assert.Equal(1, enricher.Calls);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public void Reentrancy_DoesNotInvokeErrorHandler()
    {
        // A reentrant log call returning silently is not an error — the handler must
        // not be called for it. (If it were, a logging-from-handler bug would loop.)
        var captured = new List<Exception>();
        Logger? logger = null;
        var sink = new ReentrantSink(() => logger!);
        logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(sink));

        logger.Info("outer");

        Assert.Empty(captured);
    }

    [Fact]
    public void Filter_LogsRecursively_FilterInvokedOncePerKey()
    {
        // A filter that logs from inside ShouldSkip must not recurse into the pipeline.
        // The filter is consulted per field key; with a single field, ShouldSkip runs once.
        Logger? logger = null;
        var filter = new ReentrantFilter(() => logger!);
        var counter = new CountingSink();
        logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Filter.With(filter)
            .WriteTo.Sink(counter));

        logger.Info("outer", new Field("k", "v"));

        Assert.Equal(1, filter.Calls);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public void Redactor_LogsRecursively_RedactorInvokedOncePerField()
    {
        // A redactor that logs from inside Redact must not recurse into the pipeline.
        Logger? logger = null;
        var redactor = new ReentrantRedactor(() => logger!);
        var counter = new CountingSink();
        logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Redact.With(redactor)
            .WriteTo.Sink(counter));

        logger.Info("outer", new Field("k", "v"));

        Assert.Equal(1, redactor.Calls);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task Reentrancy_GuardIsPerThread()
    {
        // The guard is [ThreadStatic], so concurrent log calls on different threads
        // do not interfere. Run two threads logging in parallel and confirm both
        // entries reach the sink.
        var counter = new CountingSink();
        using var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(counter));

        await Task.WhenAll(
            Task.Run(() => { for (var i = 0; i < 1000; i++) logger.Info("a"); }),
            Task.Run(() => { for (var i = 0; i < 1000; i++) logger.Info("b"); }));

        Assert.Equal(2000, counter.Calls);
    }
}
