using System.Text;
using System.Text.Json;
using Clip.Sinks;
using CsCheck;

namespace Clip.Fuzz;

public class ExceptionFuzzTests
{
    private static readonly Gen<char> SafeChar = Gen.Char.Where(c => !char.IsSurrogate(c));
    private static readonly Gen<string> SafeString = Gen.String[SafeChar, 0, 64];

    // One "layer" of the exception chain: the message and a data dict.
    private static readonly Gen<(string Msg, string[] Keys, string[] Values)> Layer =
        Gen.Select(SafeString, SafeString.Array[0, 4], SafeString.Array[0, 4]);

    private static Exception BuildException((string Msg, string[] Keys, string[] Values)[] layers)
    {
        Exception? ex = null;
        // Build outermost-first, so layers[0] is the outer exception.
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            var l = layers[i];
            ex = ex is null
                ? new InvalidOperationException(l.Msg)
                : new InvalidOperationException(l.Msg, ex);
            var n = Math.Min(l.Keys.Length, l.Values.Length);
            for (var j = 0; j < n; j++)
                if (!ex.Data.Contains(l.Keys[j])) ex.Data[l.Keys[j]] = l.Values[j];
        }
        return ex ?? new InvalidOperationException("empty");
    }

    [Fact]
    public void JsonSink_WriteException_NeverThrows_OnRandomTree()
    {
        Layer.Array[1, 9].Sample(layers =>
        {
            var ex = BuildException(layers);
            var ms = new MemoryStream();
            var sink = new JsonSink(ms);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "msg", [], ex);
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void JsonSink_WriteException_OutputIsParseableJson()
    {
        Layer.Array[1, 9].Sample(layers =>
        {
            var ex = BuildException(layers);
            var ms = new MemoryStream();
            var sink = new JsonSink(ms);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "msg", [], ex);

            var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
            using var doc = JsonDocument.Parse(text);
            var err = doc.RootElement.GetProperty("error");
            Assert.True(err.TryGetProperty("type", out _));
            Assert.True(err.TryGetProperty("msg", out _));
        }, iter: FuzzConfig.Iter);
    }

    [Fact]
    public void JsonSink_WriteException_DeepInnerChain_DoesNotCrash()
    {
        // Current implementation has no depth cap; we don't probe for stack-overflow territory.
        // 64 levels is well within a 1 MiB thread stack and exercises the recursion.
        const int depth = 64;
        Exception ex = new InvalidOperationException("leaf");
        for (var i = 0; i < depth; i++)
            ex = new InvalidOperationException($"layer-{i}", ex);

        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "msg", [], ex);

        var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 256 });

        var node = doc.RootElement.GetProperty("error");
        var seen = 0;
        while (node.TryGetProperty("inner", out var inner))
        {
            seen++;
            node = inner;
        }
        Assert.Equal(depth, seen);
    }

    [Fact]
    public void ConsoleSink_WriteException_NeverThrows_OnRandomTree()
    {
        Layer.Array[1, 9].Sample(layers =>
        {
            var ex = BuildException(layers);
            var ms = new MemoryStream();
            var sink = new ConsoleSink(ms, false);
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "msg", [], ex);
        }, iter: FuzzConfig.Iter);
    }
}
