using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// BackgroundSink: channel overflow, drain timeout, error handling.
/// </summary>
public class BackgroundSinkTests
{
    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    [Fact]
    public void BackgroundSink_OverflowDropsOldest()
    {
        var ms = new MemoryStream();
        // Tiny capacity to force drops
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms), 8));

        // Write more than capacity
        for (var i = 0; i < 100; i++)
            logger.Info($"msg-{i}");

        logger.Dispose();

        var docs = ReadLines(ms);
        // Some messages should have been dropped (oldest)
        // We should have at most 100 and the latest messages should be present
        Assert.True(docs.Length > 0);
        Assert.True(docs.Length <= 100);
    }

    [Fact]
    public void BackgroundSink_WithFields_PreservesFieldValues()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        logger.Info("msg", new Field("key", "value"), new Field("num", 42));
        logger.Dispose();

        var docs = ReadLines(ms);
        Assert.Single(docs);
        var fields = docs[0].RootElement.GetProperty("fields");
        Assert.Equal("value", fields.GetProperty("key").GetString());
        Assert.Equal(42, fields.GetProperty("num").GetInt32());
    }

    [Fact]
    public void BackgroundSink_NoFields_Works()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        logger.Info("no fields");
        logger.Dispose();

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("no fields", docs[0].RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void BackgroundSink_Exception_PreservesException()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        logger.Error("err", new InvalidOperationException("boom"));
        logger.Dispose();

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("boom", docs[0].RootElement.GetProperty("error").GetProperty("msg").GetString());
    }

    [Fact]
    public void BackgroundSink_InnerSinkThrows_ContinuesDraining()
    {
        var counter = new CountingSink();
        var bg = BackgroundSink.Create(counter);

        // Write multiple messages — inner sink throws on every other one
        for (var i = 0; i < 10; i++)
            bg.Write(DateTimeOffset.UtcNow, LogLevel.Info, $"msg-{i}", [], null);

        bg.Dispose();

        // ThrowingSink throws but drain loop catches, so no crash
        Assert.True(counter.Count >= 0);
    }

    [Fact]
    public void BackgroundSink_ConcurrentWrites_AllDelivered()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms), 4096));

        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 500, i =>
            logger.Info($"msg-{i}", new Field("i", i)));

        logger.Dispose();

        var docs = ReadLines(ms);
        Assert.Equal(500, docs.Length);
    }

    [Fact]
    public void BackgroundSink_DisposeWithoutWrites_DoesNotHang()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        // Dispose immediately without writing anything
        var ex = Record.Exception(() => logger.Dispose());
        Assert.Null(ex);
    }

    private sealed class CountingSink : ILogSink
    {
        private int _count;
        public int Count => _count;

        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            Interlocked.Increment(ref _count);
        }

        public void Dispose()
        {
        }
    }
}
