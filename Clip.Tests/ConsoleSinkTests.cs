using System.Text;
using Clip.Sinks;

namespace Clip.Tests;

public class ConsoleSinkTests
{
    private static (ConsoleSink sink, MemoryStream ms) MakeSink()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        return (sink, ms);
    }

    private static string Capture(Action<ConsoleSink> write)
    {
        var (sink, ms) = MakeSink();
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void Write_BasicFormat_ContainsTimestampLevelAndMessage()
    {
        var output = Capture(sink =>
            sink.Write(new DateTimeOffset(2024, 1, 1, 12, 0, 0, 123, TimeSpan.Zero),
                LogLevel.Info, "Server started", [], null));

        Assert.Contains("2024-01-01 12:00:00.123", output);
        Assert.Contains("INFO", output);
        Assert.Contains("Server started", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void Write_AllLevels_ProduceCorrectLabels()
    {
        var ts = DateTimeOffset.UtcNow;
        var levels = new[]
        {
            (LogLevel.Trace, "TRAC"),
            (LogLevel.Debug, "DEBU"),
            (LogLevel.Info, "INFO"),
            (LogLevel.Warning, "WARN"),
            (LogLevel.Error, "ERRO"),
            (LogLevel.Fatal, "FATA"),
        };

        foreach (var (level, label) in levels)
        {
            var output = Capture(sink => sink.Write(ts, level, "msg", [], null));
            Assert.Contains(label, output);
        }
    }

    [Fact]
    public void Write_Fields_OutputsSortedKeyEqualsValue()
    {
        var fields = new Field[]
        {
            new("z_last", 99),
            new("a_first", "hello"),
        };

        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        // Sorted: a_first before z_last
        var aIdx = output.IndexOf("a_first", StringComparison.Ordinal);
        var zIdx = output.IndexOf("z_last", StringComparison.Ordinal);
        Assert.True(aIdx < zIdx, "Fields should be sorted by key");
        Assert.Contains("a_first=hello", output);
        Assert.Contains("z_last=99", output);
    }

    [Fact]
    public void Write_MessagePaddedTo40Chars_WhenFieldsPresent()
    {
        // "Hi" is 2 chars, needs 38 spaces padding before the 2-space separator
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Hi",
                [new Field("k", "v")], null));

        // Message should be followed by padding + 2 spaces before the first field
        Assert.Contains("Hi" + new string(' ', 38) + "  k=v", output);
    }

    [Fact]
    public void Write_MessageLongerThan40_NoTruncation()
    {
        var longMsg = new string('X', 50);
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, longMsg,
                [new Field("k", "v")], null));

        Assert.Contains(longMsg, output);
        // No extra padding when the message >= 40 chars, still 2-space separator
        Assert.Contains(longMsg + "  k=v", output);
    }

    [Fact]
    public void Write_NoFields_NoPaddingOrSeparator()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Hi", [], null));

        // No key=value pattern
        Assert.DoesNotContain("=", output);
    }

    [Fact]
    public void Write_Exception_AppendsIndented()
    {
        var ex = new InvalidOperationException("boom");
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], ex));

        Assert.Contains("\n  System.InvalidOperationException: boom", output);
    }

    [Fact]
    public void Write_IntFieldValue_FormattedAsDecimal()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("n", 12345)], null));
        Assert.Contains("n=12345", output);
    }

    [Fact]
    public void Write_BoolFieldValue_FormattedLowercase()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("ok", true)], null));
        Assert.Contains("ok=true", output);
    }

    [Fact]
    public void Write_ObjectFieldValue_CallsToString()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("data", new Version(1, 2, 3))], null));
        Assert.Contains("data=1.2.3", output);
    }

    [Fact]
    public void Write_WithColors_ContainsAnsiCodes()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", [], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void Write_FloatFieldValue_Formatted()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("rate", 1.5f)], null));
        Assert.Contains("rate=1.5", output);
    }

    [Fact]
    public void Write_DateTimeFieldValue_FormattedAsIso()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("ts", dto)], null));
        Assert.Contains("ts=2024-06-15T10:30:00", output);
    }

    [Fact]
    public void Write_ExceptionWithData_ShowsDataEntries()
    {
        var ex = new InvalidOperationException("boom");
        ex.Data["code"] = 42;
        ex.Data["source"] = "test";

        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Failed", [], ex));

        Assert.Contains("Data:", output);
        Assert.Contains("code = 42", output);
        Assert.Contains("source = test", output);
    }


    [Fact]
    public void Write_EmojiMessage_Output()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info,
                "Hello \ud83d\ude00 World", [], null));
        Assert.Contains("Hello \ud83d\ude00 World", output);
    }

    [Fact]
    public void Write_CjkMessage_Output()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info,
                "\u4f60\u597d\u4e16\u754c", [], null));
        Assert.Contains("\u4f60\u597d\u4e16\u754c", output);
    }

    [Fact]
    public void Write_LargePayload_DoesNotCrash()
    {
        var longMsg = new string('X', 100_000);
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, longMsg, [], null));
        Assert.Contains(longMsg, output);
    }

    [Fact]
    public void Write_ManyFields_DoesNotCrash()
    {
        var fields = Enumerable.Range(0, 200)
            .Select(i => new Field($"f{i}", i))
            .ToArray();

        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));
        Assert.Contains("f0=0", output);
        Assert.Contains("f199=199", output);
    }

    [Fact]
    public void Write_EmptyMessage_DoesNotCrash()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "", [], null));
        Assert.Contains("INFO", output);
    }

    //
    // New type coverage
    //

    [Fact]
    public void Write_ULongFieldValue_Formatted()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("val", ulong.MaxValue)], null));
        Assert.Contains("val=18446744073709551615", output);
    }

    [Fact]
    public void Write_DecimalFieldValue_Formatted()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("price", 19.99m)], null));
        Assert.Contains("price=19.99", output);
    }

    [Fact]
    public void Write_GuidFieldValue_Formatted()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("id", guid)], null));
        Assert.Contains("id=550e8400-e29b-41d4-a716-446655440000", output);
    }

    [Fact]
    public void Write_DateTimeValue_FormattedAsIso()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("ts", dt)], null));
        Assert.Contains("ts=2024-06-15T10:30:00", output);
    }

    [Fact]
    public void Write_ByteFieldValue_Formatted()
    {
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("val", (byte)255)], null));
        Assert.Contains("val=255", output);
    }

    [Fact]
    public void Write_TimeSpanViaUtf8Formattable_Formatted()
    {
        var ts = TimeSpan.FromMinutes(5);
        var output = Capture(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
                [new Field("elapsed", ts)], null));
        Assert.Contains("elapsed=00:05:00", output);
    }

    [Fact]
    public void Write_ThreadSafe_ConcurrentWrites()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var ts = DateTimeOffset.UtcNow;

        Parallel.For(0, 100, i =>
            sink.Write(ts, LogLevel.Info, $"msg-{i}", [], null));

        var output = Encoding.UTF8.GetString(ms.ToArray());
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
    }

    [Fact]
    public void Write_ConcurrentWithFields_NoCorruption()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var ts = DateTimeOffset.UtcNow;

        Parallel.For(0, 1000, i =>
            sink.Write(ts, LogLevel.Info, $"msg-{i}",
                [new Field("i", i)], null));

        var output = Encoding.UTF8.GetString(ms.ToArray());
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1000, lines.Length);
    }
}
