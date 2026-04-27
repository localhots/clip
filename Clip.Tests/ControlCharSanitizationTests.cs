using System.Text;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies that <see cref="ConsoleSink"/> strips C0 control characters and DEL from
/// user-supplied strings by default, defending against ANSI / terminal-control injection
/// from attacker-influenced log values. JSON output is unaffected (its own escaping
/// already neutralizes control characters).
/// </summary>
public class ControlCharSanitizationTests
{
    private static string Capture(ConsoleFormatConfig config, Action<ConsoleSink> write)
    {
        var ms = new MemoryStream();
        using var sink = new ConsoleSink(config, ms);
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void Default_StripsEscapeFromMessage()
    {
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info,
                "user logged in \x1b[2J\x1b[H hijacked", [], null));

        Assert.DoesNotContain('\x1b', output);
        Assert.Contains("user logged in", output);
        Assert.Contains("hijacked", output);
    }

    [Fact]
    public void Default_StripsEscapeFromStringField()
    {
        var fields = new Field[] { new("user", "alice\x1b[31mFAKE") };
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "login", fields, null));

        Assert.DoesNotContain('\x1b', output);
        // The ESC byte is dropped; the literal "[31m" that followed it survives as plain
        // text but is harmless to a terminal without the ESC prefix.
        Assert.Contains("user=alice[31mFAKE", output);
    }

    [Fact]
    public void Default_StripsNewlineFromFieldValue()
    {
        var fields = new Field[] { new("user", "alice\nERROR forged log line") };
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "login", fields, null));

        // One log entry produces exactly one terminating newline; a forged \n in a field
        // value would create a second line.
        Assert.Equal(1, output.Count(c => c == '\n'));
    }

    [Fact]
    public void Default_PreservesNewlineInMessage()
    {
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "line one\nline two", [], null));

        // Multiline messages are legitimate; the embedded \n stays.
        Assert.Equal(2, output.Count(c => c == '\n'));
        Assert.Contains("line one", output);
        Assert.Contains("line two", output);
    }

    [Fact]
    public void Default_PreservesTabEverywhere()
    {
        var fields = new Field[] { new("path", "a\tb") };
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg\twith\ttab", fields, null));

        Assert.Contains("msg\twith\ttab", output);
        Assert.Contains("a\tb", output);
    }

    [Fact]
    public void Default_StripsEscapeFromExceptionMessage()
    {
        var ex = new InvalidOperationException("boom \x1b[31mfake error \x1b[0m");
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "failed", [], ex));

        Assert.DoesNotContain('\x1b', output);
        Assert.Contains("boom [31mfake error [0m", output);
    }

    [Fact]
    public void Default_StripsControlsFromExceptionData()
    {
        var ex = new InvalidOperationException("boom");
        ex.Data["k\x1by"] = "v\nspan";
        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "failed", [], ex));

        Assert.DoesNotContain('\x1b', output);
        Assert.Contains("ky", output);
        Assert.Contains("vspan", output);
    }

    [Fact]
    public void Disabled_PassesEscapeThroughVerbatim()
    {
        var fields = new Field[] { new("user", "alice\x1b[31mraw") };
        var output = Capture(new ConsoleFormatConfig { Colors = false, SanitizeControlCharacters = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "raw \x1b[2Jmsg", fields, null));

        Assert.Contains('\x1b', output);
        Assert.Contains("\x1b[2J", output);
        Assert.Contains("\x1b[31m", output);
    }

    [Fact]
    public void Default_StripsAllOtherC0Controls()
    {
        // Every C0 control 0x01–0x08, 0x0B–0x0C, 0x0E–0x1F, plus DEL (0x7F).
        // Tab (0x09), LF (0x0A), CR (0x0D) are excluded — they are tested separately.
        var sb = new StringBuilder("clean");
        for (var c = 0x01; c <= 0x1F; c++)
        {
            if (c == 0x09 || c == 0x0A || c == 0x0D) continue;
            sb.Append((char)c);
        }
        sb.Append((char)0x7F);
        sb.Append("end");

        var output = Capture(new ConsoleFormatConfig { Colors = false }, sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, sb.ToString(), [], null));

        for (var c = 0x01; c <= 0x1F; c++)
        {
            if (c == 0x09 || c == 0x0A || c == 0x0D) continue;
            Assert.DoesNotContain((char)c, output);
        }
        Assert.DoesNotContain('\x7f', output);
        Assert.Contains("cleanend", output);
    }
}
