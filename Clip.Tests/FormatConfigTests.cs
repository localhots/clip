using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class ConsoleFormatConfigTests
{
    private static string Capture(ConsoleFormatConfig config, Action<ConsoleSink> write)
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void CustomLevelLabels_AppearInOutput()
    {
        var config = new ConsoleFormatConfig
        {
            Colors = false,
            LevelLabels = ["TRC", "DBG", "INF", "WRN", "ERR", "FTL"],
        };
        var ts = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var output = Capture(config, s => s.Write(ts, LogLevel.Info, "test", [], null));
        Assert.Contains("INF", output);
        Assert.DoesNotContain("INFO", output);
    }

    [Fact]
    public void CustomTimestampFormat_AppearInOutput()
    {
        var config = new ConsoleFormatConfig
        {
            Colors = false,
            TimestampFormat = "HH:mm:ss",
        };
        var ts = new DateTimeOffset(2024, 1, 1, 14, 30, 45, TimeSpan.Zero);
        var output = Capture(config, s => s.Write(ts, LogLevel.Info, "test", [], null));
        Assert.Contains("14:30:45", output);
        Assert.DoesNotContain("2024", output);
    }

    [Fact]
    public void CustomMinMessageWidth_AffectsPadding()
    {
        var narrow = new ConsoleFormatConfig { Colors = false, MinMessageWidth = 10 };
        var wide = new ConsoleFormatConfig { Colors = false, MinMessageWidth = 80 };
        var ts = DateTimeOffset.UtcNow;
        var msg = "hi";

        var narrowOut = Capture(narrow, s => s.Write(ts, LogLevel.Info, msg, [new Field("k", "v")], null));
        var wideOut = Capture(wide, s => s.Write(ts, LogLevel.Info, msg, [new Field("k", "v")], null));

        Assert.True(wideOut.Length > narrowOut.Length, "Wide config should produce longer output due to padding");
    }

    [Fact]
    public void DefaultConfig_MatchesOriginalBehavior()
    {
        var config = new ConsoleFormatConfig { Colors = false };
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, 500, TimeSpan.Zero);

        var output = Capture(config, s => s.Write(ts, LogLevel.Warning, "oops", [], null));
        Assert.Contains("2024-06-15 10:30:00.500", output);
        Assert.Contains("WARN", output);
        Assert.Contains("oops", output);
    }
}

public class JsonFormatConfigTests
{
    private static JsonDocument CaptureJson(JsonFormatConfig config, Action<JsonSink> write)
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        write(sink);
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
        return JsonDocument.Parse(text);
    }

    [Fact]
    public void CustomKeys_AppearInOutput()
    {
        var config = new JsonFormatConfig
        {
            TimestampKey = "@timestamp",
            LevelKey = "log.level",
            MessageKey = "message",
            FieldsKey = "labels",
            ErrorKey = "err",
        };
        var ts = DateTimeOffset.UtcNow;

        var doc = CaptureJson(config, s => s.Write(ts, LogLevel.Info, "hello",
            [new Field("k", "v")], new Exception("boom")));

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("@timestamp", out _));
        Assert.True(root.TryGetProperty("log.level", out _));
        Assert.True(root.TryGetProperty("message", out _));
        Assert.True(root.TryGetProperty("labels", out _));
        Assert.True(root.TryGetProperty("err", out _));

        Assert.False(root.TryGetProperty("ts", out _));
        Assert.False(root.TryGetProperty("level", out _));
        Assert.False(root.TryGetProperty("msg", out _));
        Assert.False(root.TryGetProperty("fields", out _));
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public void CustomLevelLabels_AppearInOutput()
    {
        var config = new JsonFormatConfig
        {
            LevelLabels = ["TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"],
        };
        var ts = DateTimeOffset.UtcNow;

        var doc = CaptureJson(config, s => s.Write(ts, LogLevel.Warning, "test", [], null));
        Assert.Equal("WARN", doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void DefaultConfig_MatchesOriginalBehavior()
    {
        var config = new JsonFormatConfig();
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, 500, TimeSpan.Zero);

        var doc = CaptureJson(config, s => s.Write(ts, LogLevel.Info, "hello", [], null));
        var root = doc.RootElement;

        Assert.Equal("2024-06-15T10:30:00.500Z", root.GetProperty("ts").GetString());
        Assert.Equal("info", root.GetProperty("level").GetString());
        Assert.Equal("hello", root.GetProperty("msg").GetString());
    }

    [Fact]
    public void BuilderOverload_Console_WithConfig()
    {
        var config = new ConsoleFormatConfig { Colors = false, LevelLabels = ["T", "D", "I", "W", "E", "F"] };
        var ms = new MemoryStream();

        var log = Logger.Create(c => c.WriteTo.Console(config, ms));
        log.Info("test");

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("I", output);
        Assert.DoesNotContain("INFO", output);
    }

    [Fact]
    public void BuilderOverload_Json_WithConfig()
    {
        var config = new JsonFormatConfig { MessageKey = "message" };
        var ms = new MemoryStream();

        var log = Logger.Create(c => c.WriteTo.Json(config, ms));
        log.Info("test");

        ms.Position = 0;
        var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n'));
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
        Assert.False(doc.RootElement.TryGetProperty("msg", out _));
    }
}
