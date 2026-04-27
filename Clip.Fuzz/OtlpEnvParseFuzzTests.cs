using Clip.OpenTelemetry;
using CsCheck;

namespace Clip.Fuzz;

public class OtlpEnvParseFuzzTests
{
    [Fact]
    public void ParseKeyValuePairs_NeverThrows_AndPreservesInvariants()
    {
        Gen.String[Gen.Char, 0, 200].Sample(input =>
        {
            var dict = new Dictionary<string, string>();
            OtlpSinkOptions.ParseKeyValuePairs(input, dict);

            var commaCount = 0;
            for (var i = 0; i < input.Length; i++) if (input[i] == ',') commaCount++;
            Assert.True(dict.Count <= commaCount + 1,
                $"dict.Count={dict.Count} exceeds commaCount+1={commaCount + 1}");

            foreach (var (k, v) in dict)
            {
                Assert.Equal(k, k.Trim());
                Assert.Equal(v, v.Trim());
                Assert.True(input.Contains(k, StringComparison.Ordinal),
                    $"key '{k}' not found in input '{input}'");
                Assert.True(input.Contains(v, StringComparison.Ordinal),
                    $"value '{v}' not found in input '{input}'");
            }
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void ParseKeyValuePairs_StructuredInput_RoundTripsKeyCount()
    {
        // Build structurally valid input from generated key/value pieces (no '=', ',', whitespace),
        // assert every emitted key shows up in the parsed dict with the expected value.
        var piece = Gen.String[Gen.Char.AlphaNumeric, 1, 16];

        Gen.Select(piece.Array[0, 8], piece.Array[0, 8]).Sample((keys, values) =>
        {
            var n = Math.Min(keys.Length, values.Length);
            if (n == 0) return;

            // Deduplicate keys to avoid last-write-wins ambiguity.
            var seen = new HashSet<string>();
            var pairs = new List<(string K, string V)>();
            for (var i = 0; i < n; i++)
            {
                if (seen.Add(keys[i])) pairs.Add((keys[i], values[i]));
            }
            if (pairs.Count == 0) return;

            var input = string.Join(",", pairs.Select(p => $"{p.K}={p.V}"));
            var dict = new Dictionary<string, string>();
            OtlpSinkOptions.ParseKeyValuePairs(input, dict);

            Assert.Equal(pairs.Count, dict.Count);
            foreach (var (k, v) in pairs)
            {
                Assert.True(dict.TryGetValue(k, out var got), $"missing key '{k}'");
                Assert.Equal(v, got);
            }
        }, iter: FuzzConfig.Iter);
    }
}
