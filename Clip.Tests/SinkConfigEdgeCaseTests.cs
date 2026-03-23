using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Sink configuration edge cases: weird format configs, custom keys, empty labels,
/// extreme values. Validates JSON never breaks regardless of config.
/// </summary>
public class SinkConfigEdgeCaseTests
{
    private static JsonDocument ParseLine(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return JsonDocument.Parse(text.TrimEnd('\n'));
    }

    //
    // JsonFormatConfig edge cases
    //

    [Fact]
    public void JsonSink_CustomKeys_AllRenamed()
    {
        var config = new JsonFormatConfig
        {
            TimestampKey = "time",
            LevelKey = "severity",
            MessageKey = "text",
            FieldsKey = "attributes",
            ErrorKey = "exception",
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        var ex = new InvalidOperationException("boom");
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "msg",
            [new Field("k", 1)], ex);

        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.TryGetProperty("time", out _));
        Assert.True(doc.RootElement.TryGetProperty("severity", out _));
        Assert.True(doc.RootElement.TryGetProperty("text", out _));
        Assert.True(doc.RootElement.TryGetProperty("attributes", out _));
        Assert.True(doc.RootElement.TryGetProperty("exception", out _));
        // Old keys should not be present
        Assert.False(doc.RootElement.TryGetProperty("ts", out _));
        Assert.False(doc.RootElement.TryGetProperty("level", out _));
        Assert.False(doc.RootElement.TryGetProperty("msg", out _));
    }

    [Fact]
    public void JsonSink_CustomLevelLabels_Used()
    {
        var config = new JsonFormatConfig
        {
            LevelLabels =
            [
                "VERBOSE", // Trace
                "DBG", // Debug
                "INFORMATION", // Info
                "WARN", // Warning
                "ERR", // Error
                "CRITICAL", // Fatal
            ],
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Warning, "msg", [], null);

        using var doc = ParseLine(ms);
        Assert.Equal("WARN", doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void JsonSink_EmptyLevelLabels_ProducesValidJson()
    {
        var config = new JsonFormatConfig
        {
            LevelLabels = ["", "", "", "", "", ""],
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);

        using var doc = ParseLine(ms);
        Assert.Equal("", doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void JsonSink_KeysWithDotsAndAt_ProducesValidJson()
    {
        var config = new JsonFormatConfig
        {
            TimestampKey = "@timestamp",
            LevelKey = "log.level",
            MessageKey = "message_key",
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);

        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.TryGetProperty("@timestamp", out _));
        Assert.True(doc.RootElement.TryGetProperty("log.level", out _));
        Assert.True(doc.RootElement.TryGetProperty("message_key", out _));
    }

    [Fact]
    public void JsonSink_KeysWithQuotes_ProducesValidJson()
    {
        var config = new JsonFormatConfig { MessageKey = "msg\"key" };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "hello", [], null);

        using var doc = ParseLine(ms);
        Assert.Equal("hello", doc.RootElement.GetProperty("msg\"key").GetString());
    }

    [Fact]
    public void JsonSink_UnicodeKeys_ProducesValidJson()
    {
        var config = new JsonFormatConfig
        {
            TimestampKey = "時間",
            LevelKey = "レベル",
            MessageKey = "メッセージ",
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "hello", [], null);

        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.TryGetProperty("時間", out _));
        Assert.Equal("hello", doc.RootElement.GetProperty("メッセージ").GetString());
    }

    [Fact]
    public void JsonSink_CustomTimestampFormat_Used()
    {
        var config = new JsonFormatConfig
        {
            TimestampFormat = "yyyy-MM-dd",
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        sink.Write(ts, LogLevel.Info, "msg", [], null);

        using var doc = ParseLine(ms);
        Assert.Equal("2024-06-15", doc.RootElement.GetProperty("ts").GetString());
    }

    [Fact]
    public void JsonSink_LevelLabelsWithEscapeChars_ProducesValidJson()
    {
        var config = new JsonFormatConfig
        {
            LevelLabels =
            [
                "trace\t1",
                "debug\"2",
                "info\\3",
                "warn\n4",
                "error\r5",
                "fatal\06",
            ],
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            ms.SetLength(0);
            sink.Write(DateTimeOffset.UtcNow, level, "msg", [], null);
            using var doc = ParseLine(ms);
            Assert.True(doc.RootElement.TryGetProperty("level", out _));
        }
    }

    [Fact]
    public void JsonSink_LevelLabelsSimpleAscii_ProducesValidJson()
    {
        var config = new JsonFormatConfig
        {
            LevelLabels = ["VERBOSE", "DBG", "INFORMATION", "WARN", "ERR", "CRITICAL"],
        };
        var ms = new MemoryStream();
        var sink = new JsonSink(config, ms);

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            ms.SetLength(0);
            sink.Write(DateTimeOffset.UtcNow, level, "msg", [], null);
            using var doc = ParseLine(ms);
            Assert.True(doc.RootElement.TryGetProperty("level", out _));
        }
    }

    //
    // ConsoleFormatConfig edge cases
    //

    [Fact]
    public void ConsoleSink_CustomLabels_Used()
    {
        var config = new ConsoleFormatConfig
        {
            LevelLabels = ["TRC", "DBG", "INF", "WRN", "ERR", "FTL"],
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("INF", output);
    }

    [Fact]
    public void ConsoleSink_EmptyLabels_DoesNotCrash()
    {
        var config = new ConsoleFormatConfig
        {
            LevelLabels = ["", "", "", "", "", ""],
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("msg", output);
    }

    [Fact]
    public void ConsoleSink_VeryLongLabels_DoesNotCrash()
    {
        var longLabel = new string('X', 200);
        var config = new ConsoleFormatConfig
        {
            LevelLabels = [longLabel, longLabel, longLabel, longLabel, longLabel, longLabel],
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains(longLabel, output);
    }

    [Fact]
    public void ConsoleSink_CustomTimestampFormat_Used()
    {
        var config = new ConsoleFormatConfig
        {
            TimestampFormat = "HH:mm:ss",
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        sink.Write(ts, LogLevel.Info, "msg", [], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("10:30:00", output);
        Assert.DoesNotContain("2024", output); // Date part stripped
    }

    [Fact]
    public void ConsoleSink_ZeroMessageWidth_NoPadding()
    {
        var config = new ConsoleFormatConfig
        {
            MinMessageWidth = 0,
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Hi",
            [new Field("k", "v")], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Hi  k=v", output); // 2-space separator, no padding
    }

    [Fact]
    public void ConsoleSink_LargeMessageWidth_Padded()
    {
        var config = new ConsoleFormatConfig
        {
            MinMessageWidth = 80,
            Colors = false,
        };
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Hi",
            [new Field("k", "v")], null);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        // "Hi" is 2 chars, 80 - 2 = 78 spaces padding + 2-space separator
        var idx = output.IndexOf("Hi", StringComparison.Ordinal);
        var fieldIdx = output.IndexOf("k=v", StringComparison.Ordinal);
        Assert.True(fieldIdx - idx >= 80);
    }

    //
    // Builder configuration edge cases
    //

    [Fact]
    public void NoSinksConfigured_DefaultsToConsole()
    {
        var logger = Logger.Create(_ => { });
        // Should not throw; defaults to console sink
        var ex = Record.Exception(() => logger.Info("test"));
        Assert.Null(ex);
    }

    [Fact]
    public void DuplicateSinks_BothReceive()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(ms)
            .WriteTo.Json(ms)); // Same stream, two sinks

        logger.Info("msg");

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // Written twice
        foreach (var line in lines)
            JsonDocument.Parse(line);
    }

    [Fact]
    public void ThreeSinks_DifferentLevels()
    {
        var msAll = new MemoryStream();
        var msWarn = new MemoryStream();
        var msErr = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(msAll, LogLevel.Trace)
            .WriteTo.Json(msWarn, LogLevel.Warning)
            .WriteTo.Json(msErr, LogLevel.Error));

        logger.Trace("t");
        logger.Info("i");
        logger.Warning("w");
        logger.Error("e");

        Assert.Equal(4, ReadLineCount(msAll));
        Assert.Equal(2, ReadLineCount(msWarn));
        Assert.Single(ReadLines(msErr));
    }

    [Fact]
    public void MixedSinkTypes_ConsoleAndJson()
    {
        var msConsole = new MemoryStream();
        var msJson = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Console(msConsole, false)
            .WriteTo.Json(msJson));

        logger.Info("hello", new { Key = "val" });

        var consoleOutput = Encoding.UTF8.GetString(msConsole.ToArray());
        Assert.Contains("hello", consoleOutput);
        Assert.Contains("Key=val", consoleOutput);

        var jsonDocs = ReadLines(msJson);
        Assert.Single(jsonDocs);
        Assert.Equal("hello", jsonDocs[0].RootElement.GetProperty("msg").GetString());
    }

    //
    // JSON with all field types
    //

    [Fact]
    public void JsonSink_AllFieldTypes_InSingleEntry_ValidJson()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(new JsonFormatConfig { FieldsKey = "fields" }, ms);
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var fields = new Field[]
        {
            new("intF", 42),
            new("longF", 9999999999L),
            new("doubleF", 3.14),
            new("floatF", 1.5f),
            new("boolF", true),
            new("stringF", "hello"),
            new("nullStringF", null!),
            new("dateF", dto),
            new("objF", new { nested = true }),
            new("nullObjF", (object?)null),
        };

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "all types", fields, null);
        using var doc = ParseLine(ms);
        var f = doc.RootElement.GetProperty("fields");
        Assert.Equal(42, f.GetProperty("intF").GetInt32());
        Assert.Equal(9999999999L, f.GetProperty("longF").GetInt64());
        Assert.True(f.GetProperty("boolF").GetBoolean());
        Assert.Equal("hello", f.GetProperty("stringF").GetString());
        Assert.Equal(JsonValueKind.Null, f.GetProperty("nullStringF").ValueKind);
    }

    //
    // Helpers
    //

    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    private static int ReadLineCount(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
