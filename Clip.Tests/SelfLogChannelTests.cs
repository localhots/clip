using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies that <c>LoggerConfig.OnInternalError</c> receives exceptions thrown by
/// sinks, enrichers, filters, redactors, and BackgroundSink-wrapped inner sinks —
/// without breaking the contract that a log call cannot crash the application.
/// </summary>
public class SelfLogChannelTests
{
    private sealed class ThrowingSink(string message = "sink boom") : ILogSink
    {
        public int Calls;
        public void Write(DateTimeOffset timestamp, LogLevel level, string message_,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            Calls++;
            throw new InvalidOperationException(message);
        }

        public void Dispose() { }
    }

    private sealed class CapturingSink : ILogSink
    {
        public int Calls;
        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception) => Calls++;
        public void Dispose() { }
    }

    private sealed class ThrowingEnricher : ILogEnricher
    {
        public void Enrich(List<Field> target) => throw new InvalidOperationException("enricher boom");
    }

    private sealed class ThrowingFilter : ILogFilter
    {
        public bool ShouldSkip(string key) => throw new InvalidOperationException("filter boom");
    }

    private sealed class ThrowingRedactor : ILogRedactor
    {
        public void Redact(ref Field field) => throw new InvalidOperationException("redactor boom");
    }

    [Fact]
    public void Sink_Throws_HandlerInvoked()
    {
        var captured = new List<Exception>();
        using var logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink()));

        logger.Info("hello");

        Assert.Single(captured);
        Assert.IsType<InvalidOperationException>(captured[0]);
        Assert.Equal("sink boom", captured[0].Message);
    }

    [Fact]
    public void Sink_Throws_OtherSinksStillReceiveEntry()
    {
        var captured = new List<Exception>();
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink())
            .WriteTo.Sink(capturing));

        logger.Info("hello");

        Assert.Single(captured);
        Assert.Equal(1, capturing.Calls);
    }

    [Fact]
    public void Enricher_Throws_HandlerInvoked()
    {
        var captured = new List<Exception>();
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(new ThrowingEnricher())
            .WriteTo.Sink(capturing));

        logger.Info("hello");

        Assert.Single(captured);
        Assert.Equal("enricher boom", captured[0].Message);
        // Pipeline still emits the entry — enricher failure is non-fatal
        Assert.Equal(1, capturing.Calls);
    }

    [Fact]
    public void Filter_Throws_HandlerInvoked()
    {
        var captured = new List<Exception>();
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .Filter.With(new ThrowingFilter())
            .WriteTo.Sink(capturing));

        logger.Info("hello", new Field("k", "v"));

        Assert.NotEmpty(captured);
        Assert.All(captured, e => Assert.Equal("filter boom", e.Message));
        Assert.Equal(1, capturing.Calls);
    }

    [Fact]
    public void Redactor_Throws_HandlerInvoked()
    {
        var captured = new List<Exception>();
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(captured.Add)
            .MinimumLevel(LogLevel.Trace)
            .Redact.With(new ThrowingRedactor())
            .WriteTo.Sink(capturing));

        logger.Info("hello", new Field("k", "v"));

        Assert.NotEmpty(captured);
        Assert.All(captured, e => Assert.Equal("redactor boom", e.Message));
        Assert.Equal(1, capturing.Calls);
    }

    [Fact]
    public void Handler_Throws_LoggerStillWorks()
    {
        // A handler that itself throws must not break the logger or affect subsequent calls.
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(_ => throw new Exception("handler boom"))
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink())
            .WriteTo.Sink(capturing));

        logger.Info("first");
        logger.Info("second");

        Assert.Equal(2, capturing.Calls);
    }

    [Fact]
    public void NoHandler_ExceptionsSilentlySwallowed()
    {
        // Default behavior (no handler) is unchanged: failures are silent, app does not crash.
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink())
            .WriteTo.Sink(capturing));

        logger.Info("hello");

        Assert.Equal(1, capturing.Calls);
    }

    [Fact]
    public async Task BackgroundSink_InnerThrows_HandlerInvoked()
    {
        var captured = new List<Exception>();
        var captureLock = new object();
        var inner = new ThrowingSink("background boom");

        var logger = Logger.Create(c => c
            .OnInternalError(ex => { lock (captureLock) captured.Add(ex); })
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Sink(inner)));

        logger.Info("hello");
        logger.Dispose();

        // Drain may be async; give it a moment past dispose.
        for (var i = 0; i < 50 && captured.Count == 0; i++) await Task.Delay(20);

        Assert.NotEmpty(captured);
        Assert.Equal("background boom", captured[0].Message);
    }

    [Fact]
    public void Handler_InvokedConcurrently_NoLost_AllReachHandler()
    {
        // Many threads logging in parallel against a throwing enricher: the user's handler
        // is invoked from arbitrary threads. The handler the user writes is responsible for
        // its own synchronization — but the logger itself must not lose or duplicate calls.
        const int threads = 16;
        const int perThread = 50;
        var captured = new List<Exception>();
        var captureLock = new object();
        using var logger = Logger.Create(c => c
            .OnInternalError(ex => { lock (captureLock) captured.Add(ex); })
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(new ThrowingEnricher())
            .WriteTo.Sink(new CapturingSink()));

        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < perThread; i++) logger.Info("hello");
        });

        Assert.Equal(threads * perThread, captured.Count);
        Assert.All(captured, e => Assert.Equal("enricher boom", e.Message));
    }

    [Fact]
    public void Handler_ThrowsWithInnerException_LoggerStillWorks()
    {
        // A handler that throws an exception with an inner exception (e.g. AggregateException
        // or wrapped failure) must be swallowed just like a plain throw — subsequent log
        // calls must continue to function.
        var capturing = new CapturingSink();
        using var logger = Logger.Create(c => c
            .OnInternalError(_ => throw new InvalidOperationException("outer",
                new ApplicationException("inner")))
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink())
            .WriteTo.Sink(capturing));

        logger.Info("first");
        logger.Info("second");
        logger.Info("third");

        Assert.Equal(3, capturing.Calls);
    }

    [Fact]
    public async Task BackgroundSink_HandlerThrows_DrainContinues()
    {
        // The drain loop must not die when the handler itself throws while reporting an
        // inner-sink failure. After the handler-throws, subsequent entries must still drain.
        var counter = new CapturingSink();
        var inner = new SometimesThrowingSink(counter);

        var logger = Logger.Create(c => c
            .OnInternalError(_ => throw new Exception("handler boom"))
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Sink(inner)));

        // Half throw, half succeed — interleaving exercises the handler-throw path repeatedly
        // without killing the drain task.
        for (var i = 0; i < 20; i++) logger.Info($"msg-{i}");
        logger.Dispose();

        // Drain is async; allow time for entries to be processed past dispose.
        for (var i = 0; i < 50 && counter.Calls < 10; i++) await Task.Delay(20);

        // Half the entries were the throwing kind — only the non-throwing reach the counter
        // sink, but the drain loop must have processed all 20 (no entries lost to a dead drain).
        Assert.True(counter.Calls >= 10,
            $"Drain task appears to have died after handler throw — only {counter.Calls} entries reached counter (expected ~10).");
    }

    private sealed class SometimesThrowingSink(CapturingSink success) : ILogSink
    {
        private int _n;
        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            // Odd calls throw, even calls succeed — exercises the handler-throws-after-sink-throws
            // path repeatedly within a single drain.
            if (Interlocked.Increment(ref _n) % 2 == 1)
                throw new InvalidOperationException("alternating boom");
            success.Write(timestamp, level, message, fields, exception);
        }

        public void Dispose() { }
    }

    [Fact]
    public void BackgroundSink_HandlerSet_RegardlessOfConfigOrder()
    {
        // .Background() before .OnInternalError() must still wire up.
        var captured = new List<Exception>();
        var inner = new ThrowingSink("ordering boom");

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Sink(inner))
            .OnInternalError(captured.Add));

        logger.Info("hello");
        logger.Dispose();

        // Brief settle for the drain thread.
        for (var i = 0; i < 50 && captured.Count == 0; i++) Thread.Sleep(20);

        Assert.NotEmpty(captured);
    }
}
