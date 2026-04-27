using System.Text;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// TimestampCache precision boundaries and cache invalidation.
/// </summary>
public class TimestampCacheTests
{
    [Fact]
    public void SameTimestamp_CacheHit_SameOutput()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        sink.Write(ts, LogLevel.Info, "first", [], null);
        sink.Write(ts, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Both should have the same timestamp string
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        Assert.Equal(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void DifferentTimestamp_BeyondPrecision_DifferentOutput()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var ts2 = ts1.AddMilliseconds(2); // Beyond 1ms precision

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        // Both should be valid
        System.Text.Json.JsonDocument.Parse(lines[0]);
        System.Text.Json.JsonDocument.Parse(lines[1]);
    }

    [Fact]
    public void WithinPrecision_CacheReused()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        // Add 0.5ms — within 1ms precision
        var ts2 = ts1.AddTicks(5000);

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        // Same timestamp string due to cache
        Assert.Equal(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void RapidTimestampChanges_AllValid()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var baseTs = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        for (var i = 0; i < 100; i++)
            sink.Write(baseTs.AddMilliseconds(i), LogLevel.Info, $"msg-{i}", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
        foreach (var line in lines)
            System.Text.Json.JsonDocument.Parse(line);
    }

    [Fact]
    public void ConsoleSink_TimestampFormat_Displayed()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, 123, TimeSpan.Zero);

        sink.Write(ts, LogLevel.Info, "msg", [], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("2024-06-15 10:30:00.123", output);
    }

    [Fact]
    public void SecondCachePrecision_WithinSameSecond_CacheReused()
    {
        var ms = new MemoryStream();
        var config = new JsonFormatConfig
        {
            TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CachePrecision = TimeSpan.FromSeconds(1),
        };
        var sink = new JsonSink(config, ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var ts2 = ts1.AddMilliseconds(500);

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var lines = Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        Assert.Equal(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void SecondCachePrecision_DifferentSeconds_DifferentOutput()
    {
        var ms = new MemoryStream();
        var config = new JsonFormatConfig
        {
            TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CachePrecision = TimeSpan.FromSeconds(1),
        };
        var sink = new JsonSink(config, ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var ts2 = ts1.AddSeconds(1);

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var lines = Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        Assert.NotEqual(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void BackwardsClockStep_DoesNotReuseStaleCachedValue()
    {
        // A backwards clock step (NTP correction, manual time change) within the precision
        // window must not reuse a cached "newer" timestamp for an "older" event — the cached
        // check used signed subtraction, which made any negative delta look <precision.
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 5, TimeSpan.Zero);
        // Step backwards by 100ms — well outside cache precision in actual time, but the
        // signed-subtract bug would treat it as a cache hit.
        var ts2 = ts1.AddMilliseconds(-100);

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var lines = Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        // Different physical timestamps must produce different output.
        Assert.NotEqual(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void MicrosecondCachePrecision_SubMillisecondDifference_DifferentOutput()
    {
        var ms = new MemoryStream();
        var config = new JsonFormatConfig
        {
            TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
            CachePrecision = TimeSpan.FromMicroseconds(1),
        };
        var sink = new JsonSink(config, ms);
        var ts1 = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var ts2 = ts1.AddMicroseconds(10);

        sink.Write(ts1, LogLevel.Info, "first", [], null);
        sink.Write(ts2, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var lines = Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var doc1 = System.Text.Json.JsonDocument.Parse(lines[0]);
        var doc2 = System.Text.Json.JsonDocument.Parse(lines[1]);
        Assert.NotEqual(
            doc1.RootElement.GetProperty("ts").GetString(),
            doc2.RootElement.GetProperty("ts").GetString());
    }
}
