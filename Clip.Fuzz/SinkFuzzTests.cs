using System.Text;
using System.Text.Json;
using Clip.Sinks;
using CsCheck;

namespace Clip.Fuzz;

public class SinkFuzzTests
{
    private static readonly Gen<char> SafeChar = Gen.Char.Where(c => !char.IsSurrogate(c));

    // Field keys: alphanumeric so the structural assertion (key appears as JSON property)
    // is meaningful without having to track JSON-escape transformations.
    private static readonly Gen<string> KeyGen = Gen.String[Gen.Char.AlphaNumeric, 1, 16];

    // String values: full BMP minus lone surrogates.
    private static readonly Gen<string> StrGen = Gen.String[SafeChar, 0, 64];

    // Floats and doubles: finite only. Utf8Formatter doesn't emit NaN/±Infinity, which would
    // produce invalid JSON; that's a separate concern from what we're fuzzing here.
    private static readonly Gen<float> FiniteFloatGen =
        Gen.Float.Where(f => !float.IsNaN(f) && !float.IsInfinity(f));
    private static readonly Gen<double> FiniteDoubleGen =
        Gen.Double.Where(d => !double.IsNaN(d) && !double.IsInfinity(d));

    private static Gen<Field> FieldGen(string key) =>
        Gen.OneOf<Field>(
            Gen.Bool.Select(v => new Field(key, v)),
            Gen.Int.Select(v => new Field(key, v)),
            Gen.Long.Select(v => new Field(key, v)),
            Gen.ULong.Select(v => new Field(key, v)),
            FiniteFloatGen.Select(v => new Field(key, v)),
            FiniteDoubleGen.Select(v => new Field(key, v)),
            StrGen.Select(v => new Field(key, v)),
            Gen.Decimal.Select(v => new Field(key, v)),
            Gen.Guid.Select(v => new Field(key, v)),
            Gen.DateTime.Select(v => new Field(key, v)));

    // An array of fields with unique keys.
    private static readonly Gen<Field[]> FieldsGen =
        KeyGen.Array[0, 8].SelectMany(keys =>
        {
            var distinct = keys.Distinct().ToArray();
            if (distinct.Length == 0) return Gen.Const(Array.Empty<Field>());
            // Build one Gen<Field> per key, then sequence them.
            var perKey = distinct.Select(FieldGen).ToArray();
            return SequenceGen(perKey);
        });

    private static Gen<T[]> SequenceGen<T>(Gen<T>[] gens)
    {
        if (gens.Length == 0) return Gen.Const(Array.Empty<T>());
        var acc = gens[0].Select(x => new[] { x });
        for (var i = 1; i < gens.Length; i++)
        {
            var next = gens[i];
            acc = acc.SelectMany(arr => next.Select(x => arr.Append(x).ToArray()));
        }
        return acc;
    }

    [Fact]
    public void JsonSink_RandomFields_OutputIsParseableJson()
    {
        FieldsGen.Sample(fields =>
        {
            var ms = new MemoryStream();
            var sink = new JsonSink(ms);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);

            var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
            using var doc = JsonDocument.Parse(text);

            // All field names must appear as top-level JSON properties (no FieldsKey configured).
            var present = new HashSet<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                present.Add(prop.Name);

            foreach (var f in fields)
                Assert.Contains(f.Key, present);
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void JsonSink_RandomFields_NoUnescapedControlChars()
    {
        FieldsGen.Sample(fields =>
        {
            var ms = new MemoryStream();
            var sink = new JsonSink(ms);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);

            // Output is one JSON line; the trailing '\n' is the line terminator.
            var bytes = ms.ToArray();
            Assert.True(bytes.Length > 0 && bytes[^1] == (byte)'\n');
            for (var i = 0; i < bytes.Length - 1; i++)
            {
                if (bytes[i] < 0x20)
                    Assert.Fail($"raw control byte 0x{bytes[i]:X2} at offset {i}");
            }
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void ConsoleSink_RandomFields_NeverThrows()
    {
        FieldsGen.Sample(fields =>
        {
            var ms = new MemoryStream();
            var sink = new ConsoleSink(ms, false);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void JsonSink_ExtremeStringMessages_RoundTrip()
    {
        // Long, control-char-heavy, and emoji-laden messages all need to come back intact.
        var ctrlChar = Gen.Int[0, 0x1F].Select(i => (char)i);
        var msgGen = Gen.OneOf(
            Gen.String[SafeChar, 0, 4096],
            Gen.String[ctrlChar, 0, 64]);

        msgGen.Sample(message =>
        {
            var ms = new MemoryStream();
            var sink = new JsonSink(ms);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, message, [], null);

            var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(message, doc.RootElement.GetProperty("msg").GetString());
        }, iter: FuzzConfig.Iter);
    }
}
