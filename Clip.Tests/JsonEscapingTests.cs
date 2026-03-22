using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Comprehensive JSON escaping tests: control chars, unicode, edge combos.
/// Every test validates output via System.Text.Json round-trip.
/// </summary>
public class JsonEscapingTests
{
    private static (JsonSink sink, MemoryStream ms) MakeSink()
    {
        var ms = new MemoryStream();
        return (new JsonSink(ms), ms);
    }

    private static JsonDocument ParseLine(Stream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(((MemoryStream)ms).ToArray());
        return JsonDocument.Parse(text.TrimEnd('\n'));
    }

    private static string WriteAndReadMessage(string message)
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, message, [], null);
        using var doc = ParseLine(ms);
        return doc.RootElement.GetProperty("msg").GetString()!;
    }

    private static string WriteAndReadFieldValue(string key, string value)
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field(key, value)], null);
        using var doc = ParseLine(ms);
        return doc.RootElement.GetProperty("fields").GetProperty(key).GetString()!;
    }

    //
    // Individual control characters (0x00–0x1F)
    //

    [Theory]
    [InlineData('\0')]
    [InlineData('\u0001')]
    [InlineData('\u0002')]
    [InlineData('\u0003')]
    [InlineData('\u0004')]
    [InlineData('\u0005')]
    [InlineData('\u0006')]
    [InlineData('\u0007')]
    [InlineData('\b')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('\u000B')]
    [InlineData('\f')]
    [InlineData('\r')]
    [InlineData('\u000E')]
    [InlineData('\u000F')]
    [InlineData('\u0010')]
    [InlineData('\u0011')]
    [InlineData('\u0012')]
    [InlineData('\u0013')]
    [InlineData('\u0014')]
    [InlineData('\u0015')]
    [InlineData('\u0016')]
    [InlineData('\u0017')]
    [InlineData('\u0018')]
    [InlineData('\u0019')]
    [InlineData('\u001A')]
    [InlineData('\u001B')]
    [InlineData('\u001C')]
    [InlineData('\u001D')]
    [InlineData('\u001E')]
    [InlineData('\u001F')]
    public void ControlChar_InMessage_RoundTrips(char c)
    {
        var input = $"before{c}after";
        var result = WriteAndReadMessage(input);
        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\u0001')]
    [InlineData('\u001F')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void ControlChar_InFieldValue_RoundTrips(char c)
    {
        var input = $"val{c}ue";
        var result = WriteAndReadFieldValue("key", input);
        Assert.Equal(input, result);
    }

    //
    // Quote and backslash combinations
    //

    [Fact]
    public void BackslashAtEndOfString()
    {
        Assert.Equal("trailing\\", WriteAndReadMessage("trailing\\"));
    }

    [Fact]
    public void MultipleConsecutiveBackslashes()
    {
        Assert.Equal("a\\\\\\b", WriteAndReadMessage("a\\\\\\b"));
    }

    [Fact]
    public void BackslashFollowedByQuote()
    {
        Assert.Equal("say\\\"hi\\\"", WriteAndReadMessage("say\\\"hi\\\""));
    }

    [Fact]
    public void QuoteAtStartAndEnd()
    {
        Assert.Equal("\"quoted\"", WriteAndReadMessage("\"quoted\""));
    }

    [Fact]
    public void MultipleConsecutiveQuotes()
    {
        Assert.Equal("a\"\"\"b", WriteAndReadMessage("a\"\"\"b"));
    }

    [Fact]
    public void MixedQuotesAndBackslashes()
    {
        var input = "\\\"\\\\\"\\";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void AllCommonEscapesInOneString()
    {
        var input = "tab\there\nnewline\rCR\bBS\fFF\"quote\\backslash";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    //
    // Tab, newline, carriage return combos
    //

    [Fact]
    public void CrLfSequence()
    {
        Assert.Equal("line1\r\nline2\r\n", WriteAndReadMessage("line1\r\nline2\r\n"));
    }

    [Fact]
    public void MultipleTabs()
    {
        Assert.Equal("\t\t\tindented", WriteAndReadMessage("\t\t\tindented"));
    }

    [Fact]
    public void MixedWhitespaceControl()
    {
        var input = "a\tb\nc\rd\r\ne";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    //
    // Unicode / multi-byte
    //

    [Fact]
    public void Emoji_FourByteUtf8()
    {
        Assert.Equal("\U0001F600", WriteAndReadMessage("\U0001F600")); // 😀
    }

    [Fact]
    public void EmojiWithVariationSelector()
    {
        var input = "\u2764\uFE0F"; // ❤️
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void ZeroWidthJoinerEmoji()
    {
        var input = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // Family emoji
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void CjkCharacters()
    {
        Assert.Equal("你好世界", WriteAndReadMessage("你好世界"));
    }

    [Fact]
    public void ArabicText()
    {
        Assert.Equal("مرحبا", WriteAndReadMessage("مرحبا"));
    }

    [Fact]
    public void MixedAsciiAndMultiByte()
    {
        var input = "Hello 你好 World مرحبا 🌍";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    //
    // String length boundary conditions (fast path thresholds)
    //

    [Fact]
    public void ExactlyLength16_AsciiOnly()
    {
        var input = "0123456789ABCDEF"; // 16 chars
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void ExactlyLength17_JustOverFastPath()
    {
        var input = "0123456789ABCDEFG"; // 17 chars
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void ExactlyLength24_AsciiOnly()
    {
        var input = "012345678901234567890123"; // 24 chars
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void ExactlyLength25_JustOverFastPath()
    {
        var input = "0123456789012345678901234"; // 25 chars
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void NonAsciiAtPosition0_ShortString()
    {
        Assert.Equal("ñ", WriteAndReadMessage("ñ"));
    }

    [Fact]
    public void NonAsciiAtPosition16_BoundaryTransition()
    {
        var input = "0123456789ABCDEñ"; // 16 chars, non-ASCII at last position
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void NonAsciiAtPosition24_BoundaryTransition()
    {
        var input = "01234567890123456789012ñ"; // 24 chars, non-ASCII at last position
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    //
    // Field key escaping
    //

    [Fact]
    public void FieldKey_WithNonAscii_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("clé", "value")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty("clé").GetString());
    }

    [Fact]
    public void FieldKey_WithQuote_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("key\"quoted", "value")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty("key\"quoted").GetString());
    }

    [Fact]
    public void FieldKey_WithBackslash_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("path\\key", "value")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty("path\\key").GetString());
    }

    [Fact]
    public void FieldKey_WithControlChar_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("key\ttab", "value")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty("key\ttab").GetString());
    }

    [Fact]
    public void FieldKey_WithDel0x7F_ProducesValidJson()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("key\u007Fdel", "value")], null);
        using var doc = ParseLine(ms);
        // DEL triggers fallback path; just verify it's valid JSON
        var fields = doc.RootElement.GetProperty("fields");
        Assert.True(fields.EnumerateObject().Any());
    }

    [Fact]
    public void VeryLongFieldKey_ProducesValidJson()
    {
        var longKey = new string('k', 500);
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field(longKey, "value")], null);
        using var doc = ParseLine(ms);
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty(longKey).GetString());
    }

    //
    // Empty / edge strings
    //

    [Fact]
    public void EmptyMessage_ProducesValidJson()
    {
        Assert.Equal("", WriteAndReadMessage(""));
    }

    [Fact]
    public void EmptyFieldValue_ProducesValidJson()
    {
        Assert.Equal("", WriteAndReadFieldValue("key", ""));
    }

    [Fact]
    public void StringOfOnlyControlChars()
    {
        var input = "\0\u0001\u0002\u001F";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void StringOfOnlyBackslashes()
    {
        var input = "\\\\\\\\";
        Assert.Equal(input, WriteAndReadMessage(input));
    }

    [Fact]
    public void StringOfOnlyQuotes()
    {
        var input = "\"\"\"\"";
        Assert.Equal(input, WriteAndReadMessage(input));
    }
}
