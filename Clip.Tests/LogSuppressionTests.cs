using Clip.Context;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies the AsyncLocal suppression scope used by the OTLP sink to break
/// telemetry feedback loops, and exposed publicly via <see cref="Logger.SuppressLogging"/>.
/// </summary>
public class LogSuppressionTests
{
    [Fact]
    public void Logger_DropsCallsInsideScope()
    {
        var sink = new RecordingSink();
        using var logger = Logger.Create(c => c.WriteTo.Sink(sink));

        logger.Info("before");
        using (Logger.SuppressLogging())
        {
            logger.Info("during-1");
            logger.Info("during-2");
        }
        logger.Info("after");

        Assert.Equal(["before", "after"], sink.Messages);
    }

    [Fact]
    public async Task Scope_FlowsAcrossAwait()
    {
        var sink = new RecordingSink();
        using var logger = Logger.Create(c => c.WriteTo.Sink(sink));

        using (Logger.SuppressLogging())
        {
            logger.Info("sync");
            await Task.Yield();
            logger.Info("after-yield");
        }
        logger.Info("after-scope");

        Assert.Equal(["after-scope"], sink.Messages);
    }

    [Fact]
    public async Task Scope_FlowsIntoTaskRun()
    {
        var sink = new RecordingSink();
        using var logger = Logger.Create(c => c.WriteTo.Sink(sink));

        using (Logger.SuppressLogging())
            await Task.Run(() => logger.Info("from-task"));

        Assert.Empty(sink.Messages);
    }

    [Fact]
    public void NestedScopes_RestoreLifo()
    {
        Assert.False(LogSuppression.IsActive);
        using (Logger.SuppressLogging())
        {
            Assert.True(LogSuppression.IsActive);
            using (Logger.SuppressLogging())
                Assert.True(LogSuppression.IsActive);
            // Inner dispose must not leak the outer scope's suppression.
            Assert.True(LogSuppression.IsActive);
        }
        Assert.False(LogSuppression.IsActive);
    }

    [Fact]
    public void OtherFlow_NotSuppressed()
    {
        // AsyncLocal must not bleed across independent execution contexts.
        var sink = new RecordingSink();
        using var logger = Logger.Create(c => c.WriteTo.Sink(sink));

        var captured = ExecutionContext.Capture();
        using (Logger.SuppressLogging())
        {
            ExecutionContext.Run(captured!, _ => logger.Info("isolated"), null);
        }

        Assert.Equal(["isolated"], sink.Messages);
    }

    private sealed class RecordingSink : ILogSink
    {
        private readonly Lock _lock = new();
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages
        {
            get { lock (_lock) return [.. _messages]; }
        }

        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            lock (_lock) _messages.Add(message);
        }

        public void Dispose() { }
    }
}
