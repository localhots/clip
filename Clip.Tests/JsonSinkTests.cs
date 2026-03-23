using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class JsonSinkTests
{
    private static JsonDocument ParseLine(Stream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(((MemoryStream)ms).ToArray());
        var line = text.TrimEnd('\n');
        return JsonDocument.Parse(line);
    }

    private static readonly JsonFormatConfig NestedConfig = new() { FieldsKey = "fields" };

    private static (JsonSink sink, MemoryStream ms) MakeSink()
    {
        var ms = new MemoryStream();
        return (new JsonSink(NestedConfig, ms), ms);
    }

    [Fact]
    public void Write_ProducesValidJsonLine()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "hello", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.EndsWith("\n", text);
        JsonDocument.Parse(text.TrimEnd('\n')); // Must not throw
    }

    [Fact]
    public void Write_ContainsTsLevelMsg()
    {
        var (sink, ms) = MakeSink();
        var ts = new DateTimeOffset(2024, 6, 15, 10, 30, 0, 500, TimeSpan.Zero);
        sink.Write(ts, LogLevel.Warning, "Something happened", [], null);

        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("ts").GetString()!;
        Assert.EndsWith("Z", tsStr);
        Assert.StartsWith("2024-06-15T10:30:00", tsStr);
        Assert.Equal("warning", doc.RootElement.GetProperty("level").GetString());
        Assert.Equal("Something happened", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_AllLevelStrings()
    {
        var expected = new[]
        {
            (LogLevel.Trace, "trace"),
            (LogLevel.Debug, "debug"),
            (LogLevel.Info, "info"),
            (LogLevel.Warning, "warning"),
            (LogLevel.Error, "error"),
            (LogLevel.Fatal, "fatal"),
        };

        foreach (var (level, str) in expected)
        {
            var (sink, ms) = MakeSink();
            sink.Write(DateTimeOffset.UtcNow, level, "m", [], null);
            using var doc = ParseLine(ms);
            Assert.Equal(str, doc.RootElement.GetProperty("level").GetString());
        }
    }

    [Fact]
    public void Write_IntField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("count", 42)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(42, doc.RootElement.GetProperty("fields").GetProperty("count").GetInt32());
    }

    [Fact]
    public void Write_LongField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("size", 9_999_999_999L)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(9_999_999_999L, doc.RootElement.GetProperty("fields").GetProperty("size").GetInt64());
    }

    [Fact]
    public void Write_DoubleField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("ratio", 3.14)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(3.14, doc.RootElement.GetProperty("fields").GetProperty("ratio").GetDouble(), 6);
    }

    [Fact]
    public void Write_BoolField_WrittenAsBoolean()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("ok", true)], null);
        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.GetProperty("fields").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Write_StringField_WrittenAsString()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("name", "alice")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("alice", doc.RootElement.GetProperty("fields").GetProperty("name").GetString());
    }

    [Fact]
    public void Write_ObjectField_Serialized()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("data", new { x = 1 })], null);
        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.GetProperty("fields").TryGetProperty("data", out _));
    }

    [Fact]
    public void Write_Exception_WrittenAsStructuredObject()
    {
        var (sink, ms) = MakeSink();
        var ex = new InvalidOperationException("boom");
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], ex);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonValueKind.Object, error.ValueKind);
        Assert.Contains("InvalidOperationException", error.GetProperty("type").GetString());
        Assert.Equal("boom", error.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_MultipleLines_EachValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "first", [], null);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "second", [], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
            JsonDocument.Parse(line); // Must not throw
    }

    [Fact]
    public void Write_Exception_WithStackTrace_IncludesStack()
    {
        var (sink, ms) = MakeSink();
        Exception ex;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception e)
        {
            ex = e;
        }

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], ex);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        var stack = error.GetProperty("stack").GetString()!;
        Assert.Contains("JsonSinkTests", stack);
    }

    [Fact]
    public void Write_Exception_WithInner_IncludesNestedObject()
    {
        var (sink, ms) = MakeSink();
        var inner = new ArgumentException("bad arg");
        var outer = new InvalidOperationException("outer", inner);

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], outer);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.Contains("InvalidOperationException", error.GetProperty("type").GetString());
        var innerObj = error.GetProperty("inner");
        Assert.Contains("ArgumentException", innerObj.GetProperty("type").GetString());
        Assert.Equal("bad arg", innerObj.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_MessageWithQuotes_EscapedCorrectly()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "said \"hello\" and\\done", [], null);
        using var doc = ParseLine(ms);
        Assert.Equal("said \"hello\" and\\done", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_MessageWithNewlines_EscapedCorrectly()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "line1\nline2\r\nline3", [], null);
        using var doc = ParseLine(ms);
        Assert.Equal("line1\nline2\r\nline3", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_FloatField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("rate", 1.5f)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(1.5, doc.RootElement.GetProperty("fields").GetProperty("rate").GetDouble(), 6);
    }

    [Fact]
    public void Write_DateTimeField_WrittenAsQuotedIsoString()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("ts", dto)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        Assert.StartsWith("2024-06-15T10:30:00", tsStr);
        Assert.EndsWith("Z", tsStr);
    }

    [Fact]
    public void Write_ExceptionWithData_IncludesDataObject()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["code"] = 42, ["source"] = "test" },
        };

        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], ex);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        var data = error.GetProperty("data");
        Assert.Equal("42", data.GetProperty("code").GetString());
        Assert.Equal("test", data.GetProperty("source").GetString());
    }


    [Theory]
    [MemberData(nameof(JsonRoundTripData))]
    public void Write_JsonRoundTrip_ProducesValidJson(string message, Field[] fields, Exception? exception)
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, message, fields, exception);
        using var doc = ParseLine(ms);
        Assert.Equal(message, doc.RootElement.GetProperty("msg").GetString());
    }

    public static TheoryData<string, Field[], Exception?> JsonRoundTripData => new()
    {
        { "simple ASCII", [], null },
        { "emoji \ud83d\ude00 in message", [], null },
        { "CJK \u4f60\u597d\u4e16\u754c", [], null },
        { "control chars \t\n\r", [], null },
        { "", [], null },
        { "with fields", [new Field("k", "v"), new Field("n", 42)], null },
        { "with exception", [], new InvalidOperationException("boom") },
    };

    [Fact]
    public void Write_LongMessage_ProducesValidJson()
    {
        var longMsg = new string('X', 100_000);
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, longMsg, [], null);
        using var doc = ParseLine(ms);
        Assert.Equal(longMsg, doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_ManyFields_ProducesValidJson()
    {
        var fields = Enumerable.Range(0, 200)
            .Select(i => new Field($"field_{i}", i))
            .ToArray();

        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "many fields", fields, null);
        using var doc = ParseLine(ms);
        Assert.Equal(200, doc.RootElement.GetProperty("fields").EnumerateObject().Count());
    }

    [Fact]
    public void Write_EmptyStringKey_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
            [new Field("", "empty key")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("empty key", doc.RootElement.GetProperty("fields").GetProperty("").GetString());
    }

    [Fact]
    public void Write_NullFieldValue_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
            [new Field("val", null!)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("fields").GetProperty("val").ValueKind);
    }

    [Fact]
    public void Write_ExceptionWithData_RoundTripsValidJson()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["key1"] = "value1", ["key2"] = 42 },
        };

        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("value1", data.GetProperty("key1").GetString());
    }

    [Fact]
    public void Write_NestedExceptionWithData_ProducesValidJson()
    {
        var inner = new ArgumentException("bad arg")
        {
            Data = { ["detail"] = "missing field" },
        };
        var outer = new InvalidOperationException("outer", inner)
        {
            Data = { ["code"] = 500 },
        };

        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "nested", [], outer);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("500", error.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("missing field", error.GetProperty("inner").GetProperty("data").GetProperty("detail").GetString());
    }

    [Fact]
    public void Write_ThreadSafe_ConcurrentWrites()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts = DateTimeOffset.UtcNow;

        Parallel.For(0, 50, i =>
            sink.Write(ts, LogLevel.Info, $"msg-{i}", [], null));

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(50, lines.Length);
        foreach (var line in lines)
            JsonDocument.Parse(line); // Each line must be valid JSON
    }

    //
    // New type coverage
    //

    [Fact]
    public void Write_ULongField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("val", ulong.MaxValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(ulong.MaxValue, doc.RootElement.GetProperty("fields").GetProperty("val").GetUInt64());
    }

    [Fact]
    public void Write_DecimalField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("price", 19.99m)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(19.99m, doc.RootElement.GetProperty("fields").GetProperty("price").GetDecimal());
    }

    [Fact]
    public void Write_GuidField_WrittenAsQuotedString()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("id", guid)], null);
        using var doc = ParseLine(ms);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000",
            doc.RootElement.GetProperty("fields").GetProperty("id").GetString());
    }

    [Fact]
    public void Write_DateTimeValue_WrittenAsQuotedIsoString()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("ts", dt)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        Assert.StartsWith("2024-06-15T10:30:00", tsStr);
        Assert.EndsWith("Z", tsStr);
    }

    [Fact]
    public void Write_ByteField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("val", (byte)255)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(255, doc.RootElement.GetProperty("fields").GetProperty("val").GetInt32());
    }

    [Fact]
    public void Write_UIntField_WrittenAsNumber()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("val", uint.MaxValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(uint.MaxValue, doc.RootElement.GetProperty("fields").GetProperty("val").GetUInt32());
    }

    [Fact]
    public void Write_ConcurrentWithFields_AllValidJson()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts = DateTimeOffset.UtcNow;

        Parallel.For(0, 1000, i =>
            sink.Write(ts, LogLevel.Info, $"msg-{i}",
                [new Field("i", i), new Field("s", $"val-{i}")], null));

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1000, lines.Length);
        foreach (var line in lines)
            JsonDocument.Parse(line);
    }

    //
    // Flat mode (FieldsKey = null, default)
    //

    private static (JsonSink sink, MemoryStream ms) MakeFlatSink()
    {
        var ms = new MemoryStream();
        return (new JsonSink(ms), ms);
    }

    [Fact]
    public void Write_FlatMode_FieldsAtRoot()
    {
        var (sink, ms) = MakeFlatSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("count", 42), new Field("name", "alice")], null);
        using var doc = ParseLine(ms);
        Assert.Equal(42, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("alice", doc.RootElement.GetProperty("name").GetString());
        Assert.False(doc.RootElement.TryGetProperty("fields", out _));
    }

    [Fact]
    public void Write_FlatMode_NoFields_ValidJson()
    {
        var (sink, ms) = MakeFlatSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m", [], null);
        using var doc = ParseLine(ms);
        Assert.Equal("m", doc.RootElement.GetProperty("msg").GetString());
        Assert.False(doc.RootElement.TryGetProperty("fields", out _));
    }

    [Fact]
    public void Write_FlatMode_WithException_FieldsAndErrorCoexist()
    {
        var (sink, ms) = MakeFlatSink();
        var ex = new InvalidOperationException("boom");
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "fail",
            [new Field("code", 500)], ex);
        using var doc = ParseLine(ms);
        Assert.Equal(500, doc.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("boom", doc.RootElement.GetProperty("error").GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_FlatMode_AllFieldTypes()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var (sink, ms) = MakeFlatSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
        [
            new Field("i", 42),
            new Field("l", 9_999_999_999L),
            new Field("d", 3.14),
            new Field("f", 1.5f),
            new Field("b", true),
            new Field("s", "hello"),
            new Field("n", null!),
            new Field("ts", dto),
            new Field("g", guid),
            new Field("dec", 19.99m),
            new Field("obj", new { x = 1 }),
        ], null);
        using var doc = ParseLine(ms);
        var root = doc.RootElement;
        Assert.Equal(42, root.GetProperty("i").GetInt32());
        Assert.Equal(9_999_999_999L, root.GetProperty("l").GetInt64());
        Assert.Equal(3.14, root.GetProperty("d").GetDouble(), 6);
        Assert.Equal(1.5, root.GetProperty("f").GetDouble(), 6);
        Assert.True(root.GetProperty("b").GetBoolean());
        Assert.Equal("hello", root.GetProperty("s").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("n").ValueKind);
        Assert.StartsWith("2024-06-15T10:30:00", root.GetProperty("ts").GetString());
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", root.GetProperty("g").GetString());
        Assert.Equal(19.99m, root.GetProperty("dec").GetDecimal());
        Assert.Equal(1, root.GetProperty("obj").GetProperty("x").GetInt32());
    }

    [Fact]
    public void Write_FlatMode_DefaultConfig_HasNullFieldsKey()
    {
        var config = new JsonFormatConfig();
        Assert.Null(config.FieldsKey);
    }

    [Fact]
    public void Write_FlatMode_ConcurrentWrites_AllValidJson()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var ts = DateTimeOffset.UtcNow;

        Parallel.For(0, 1000, i =>
            sink.Write(ts, LogLevel.Info, $"msg-{i}",
                [new Field("i", i), new Field("s", $"val-{i}")], null));

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1000, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("i", out _));
            Assert.False(doc.RootElement.TryGetProperty("fields", out _));
        }
    }
}
