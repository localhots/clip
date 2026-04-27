using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies that <see cref="LogBuffer"/>'s size cap bounds memory exposure when an
/// attacker-influenced field value is unexpectedly large. On saturation, both sinks
/// rewind to the last safe boundary (after the previous complete element) and append
/// a small marker — preserving everything up to that point rather than discarding the
/// whole entry. For JSON the rewind keeps the line parseable.
/// </summary>
public class LogEntrySizeCapTests
{
    private const int SmallCap = 64 * 1024; // 64 KiB — comfortably above any test fixture.

    private static string CaptureConsole(int? cap, Action<ConsoleSink> write)
    {
        var ms = new MemoryStream();
        var config = cap is { } c
            ? new ConsoleFormatConfig { Colors = false, MaxLogEntryBytes = c }
            : new ConsoleFormatConfig { Colors = false };
        using var sink = new ConsoleSink(config, ms);
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string CaptureJson(int? cap, Action<JsonSink> write)
    {
        var ms = new MemoryStream();
        var config = cap is { } c
            ? new JsonFormatConfig { MaxLogEntryBytes = c }
            : new JsonFormatConfig();
        using var sink = new JsonSink(config, ms);
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void Console_OversizedField_PreservesPartialAndAppendsMarker()
    {
        var huge = new string('A', 8 * 1024 * 1024); // 8 MiB
        var fields = new Field[] { new("data", huge) };

        var output = CaptureConsole(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        Assert.True(output.Length < SmallCap + 1024,
            $"Output {output.Length} exceeded cap {SmallCap} by more than the marker slack");
        // The original message is preserved (it sits before the saturating field).
        Assert.Contains(" INFO msg", output);
        Assert.Contains("<truncated>", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void Json_OversizedField_PreservesPartialAndStaysValid()
    {
        var huge = new string('A', 8 * 1024 * 1024);
        var fields = new Field[] { new("data", huge) };

        var output = CaptureJson(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        Assert.True(output.Length < SmallCap + 1024);

        // Still valid JSON.
        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        // The message rendered before the saturating field is preserved.
        Assert.Equal("msg", doc.RootElement.GetProperty("msg").GetString());
        // The oversized field itself was rewound out (its mid-string truncation would have
        // produced invalid JSON, so the safe-point rewind drops it).
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public void Json_OversizedException_PreservesPartialAndStaysValid()
    {
        var hugeMsg = new string('B', 8 * 1024 * 1024);
        var ex = new InvalidOperationException(hugeMsg);

        var output = CaptureJson(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "failed", [], ex));

        Assert.True(output.Length < SmallCap + 1024);
        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        // The message wrote successfully before the exception saturation.
        Assert.Equal("failed", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Json_OversizedLateField_PreservesEarlyFields()
    {
        // No fieldsPrefix — fields render at outer-object level so each one is a safe
        // point. A small early field followed by a saturating late field should leave
        // the early one intact in the output.
        var fields = new Field[]
        {
            new("ok", "yes"),
            new("data", new string('C', 8 * 1024 * 1024)),
        };

        var output = CaptureJson(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("yes", doc.RootElement.GetProperty("ok").GetString());
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public void Console_RaisedCap_RendersFullEntry()
    {
        // 200 KiB string with the default 4 MiB cap must render in full.
        var content = new string('C', 200 * 1024);
        var fields = new Field[] { new("data", content) };

        var output = CaptureConsole(cap: null, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        Assert.DoesNotContain("<log entry truncated>", output);
        Assert.Contains(content, output);
    }

    [Fact]
    public void Json_RaisedCap_RendersFullEntry()
    {
        var content = new string('D', 200 * 1024);
        var fields = new Field[] { new("data", content) };

        var output = CaptureJson(cap: null, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null));

        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.False(doc.RootElement.TryGetProperty("truncated", out _));
        Assert.Equal("msg", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Json_AfterTruncation_NextEntryRendersNormally()
    {
        var ms = new MemoryStream();
        var config = new JsonFormatConfig { MaxLogEntryBytes = SmallCap };
        using var sink = new JsonSink(config, ms);

        // First entry oversized — must rewind and emit the truncated marker.
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "big",
            [new Field("data", new string('E', 8 * 1024 * 1024))], null);

        // Second entry small — must render in full, not stuck in saturated state
        // (Reset clears the saturation flag and the safe point).
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "small",
            [new Field("ok", "yes")], null);

        var lines = Encoding.UTF8.GetString(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.True(first.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("big", first.RootElement.GetProperty("msg").GetString());

        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal("small", second.RootElement.GetProperty("msg").GetString());
        Assert.False(second.RootElement.TryGetProperty("truncated", out _));
    }

    [Fact]
    public void Json_OversizedStackTrace_PreservesPartialAndStaysValid()
    {
        // The existing oversize-exception test inflates the message. This one inflates the
        // stack trace by recursing deeply: stack-trace rendering is a separate render path
        // from message rendering and must also yield valid JSON when saturation hits mid-trace.
        Exception caught;
        try
        {
            DeepRecurse(2000);
            throw new InvalidOperationException("unreached");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        var output = CaptureJson(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "deep failure", [], caught));

        Assert.True(output.Length < SmallCap + 1024);
        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("deep failure", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Console_OversizedException_TruncatesCleanlyWithNewline()
    {
        // Console renders exceptions on follow-up indented lines. Saturation in the middle
        // of the trace must still close the entry with a newline (not leave the terminal
        // mid-line) and emit the truncation marker.
        var hugeMsg = new string('X', 8 * 1024 * 1024);
        var ex = new InvalidOperationException(hugeMsg);

        var output = CaptureConsole(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "boom", [], ex));

        Assert.True(output.Length < SmallCap + 1024);
        Assert.Contains("<truncated>", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void Cap_BelowFloor_FloorEnforced_LogStillRenders()
    {
        // The LogBuffer floor is InitialSize + MarkerReserve (1024 + 64 = 1088).
        // Requesting a smaller cap must still produce a valid log line (not crash, not
        // saturate immediately on the first byte).
        var output = CaptureJson(cap: 256, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "small", [new Field("k", "v")], null));

        using var doc = JsonDocument.Parse(output.TrimEnd('\n'));
        Assert.Equal("small", doc.RootElement.GetProperty("msg").GetString());
        Assert.Equal("v", doc.RootElement.GetProperty("k").GetString());
    }

    private static void DeepRecurse(int depth)
    {
        if (depth <= 0) throw new InvalidOperationException("deep");
        DeepRecurse(depth - 1);
    }

    [Fact]
    public void Console_OversizedMessage_TruncatesWithMarker()
    {
        var huge = new string('F', 8 * 1024 * 1024);

        var output = CaptureConsole(SmallCap, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, huge, [], null));

        Assert.True(output.Length < SmallCap + 1024);
        Assert.Contains("<truncated>", output);
        Assert.EndsWith("\n", output);
    }
}
