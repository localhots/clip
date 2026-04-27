using System.Text;
using System.Text.Json;
using Clip.Internal;
using CsCheck;

namespace Clip.Fuzz;

public class JsonEscapeFuzzTests
{
    private static string Encode(string s)
    {
        var buf = new LogBuffer();
        buf.WriteJsonString(s);
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    [Fact]
    public void WriteJsonString_NeverThrows_OnAnyString()
    {
        // Includes lone surrogates and full UTF-16 range — escape must not throw, period.
        Gen.String[Gen.Char, 0, 256].Sample(s =>
        {
            var _ = Encode(s);
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void WriteJsonString_RoundTrips_WellFormedUtf16()
    {
        // Lone surrogates can't be UTF-8 encoded, so they're excluded from round-trip
        // (covered separately by the never-throws test). Everything else — control chars,
        // quotes, backslashes, full BMP — must round-trip exactly.
        var safeChar = Gen.Char.Where(c => !char.IsSurrogate(c));
        Gen.String[safeChar, 0, 256].Sample(s =>
        {
            var encoded = Encode(s);
            using var doc = JsonDocument.Parse(encoded);
            Assert.Equal(s, doc.RootElement.GetString());
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void WriteJsonString_RoundTrips_SupplementaryPlane()
    {
        // Build strings of well-formed surrogate PAIRS (codepoints U+10000..U+10FFFF).
        Gen.Int[0x10000, 0x10FFFF].Array[0, 32].Sample(cps =>
        {
            var sb = new StringBuilder();
            foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
            var s = sb.ToString();

            var encoded = Encode(s);
            using var doc = JsonDocument.Parse(encoded);
            Assert.Equal(s, doc.RootElement.GetString());
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void WriteJsonString_OutputHasNoUnescapedControlChars()
    {
        var safeChar = Gen.Char.Where(c => !char.IsSurrogate(c));
        Gen.String[safeChar, 0, 256].Sample(s =>
        {
            var encoded = Encode(s);
            // Must start and end with a quote (the framing) — strip those, then walk.
            Assert.StartsWith("\"", encoded);
            Assert.EndsWith("\"", encoded);
            var body = encoded.AsSpan(1, encoded.Length - 2);
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c < 0x20)
                    Assert.Fail($"raw control char 0x{(int)c:X2} at offset {i} in encoded output");
                if (c == '\\')
                {
                    // Skip the escape sequence: \", \\, \n, \r, \t, \b, \f, or \uXXXX
                    Assert.True(i + 1 < body.Length, "trailing backslash with no escape");
                    var next = body[i + 1];
                    if (next == 'u') i += 5; else i += 1;
                }
            }
        }, iter: FuzzConfig.Iter);
    }
}
