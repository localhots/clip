using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Verifies that both sinks bound <see cref="Exception.InnerException"/> recursion so a
/// pathologically deep chain (easy to construct from deserialization) cannot blow the
/// stack of the host process. The depth cap is configurable; default is 32.
/// </summary>
public class InnerExceptionDepthTests
{
    private static Exception BuildChain(int depth)
    {
        Exception ex = new InvalidOperationException("leaf");
        for (var i = 0; i < depth; i++)
            ex = new InvalidOperationException($"level-{i}", ex);
        return ex;
    }

    private static string CaptureConsole(int? cap, Exception ex)
    {
        var ms = new MemoryStream();
        var config = cap is { } c
            ? new ConsoleFormatConfig { Colors = false, MaxInnerExceptionDepth = c }
            : new ConsoleFormatConfig { Colors = false };
        using var sink = new ConsoleSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "failed", [], ex);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string CaptureJson(int? cap, Exception ex)
    {
        var ms = new MemoryStream();
        var config = cap is { } c
            ? new JsonFormatConfig { MaxInnerExceptionDepth = c }
            : new JsonFormatConfig();
        using var sink = new JsonSink(config, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "failed", [], ex);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void Console_DefaultCap_TruncatesDeepChain()
    {
        var ex = BuildChain(100);

        var output = CaptureConsole(cap: null, ex);

        Assert.Contains("(inner exceptions truncated)", output);
        // Default cap is 32; the outer + 31 inners render. level-99 (outermost wrapper) renders,
        // level-(99-32) = level-67 is the deepest fully rendered. The leaf and lower levels are truncated.
        Assert.Contains("level-99", output);
        Assert.DoesNotContain("level-0:", output); // leaf wrapper at the bottom, behind the cap
    }

    [Fact]
    public void Json_DefaultCap_TruncatesDeepChain()
    {
        var ex = BuildChain(100);

        var output = CaptureJson(cap: null, ex);

        Assert.Contains("\"truncated\":true", output);
        Assert.Contains("level-99", output);
    }

    [Fact]
    public void Console_DeepChain_DoesNotOverflowStack()
    {
        // 50_000 is well past the default thread stack limit on .NET (~1 MB ≈ a few thousand
        // frames). Without the cap this would StackOverflow and crash the test process.
        var ex = BuildChain(50_000);

        var output = CaptureConsole(cap: null, ex);

        Assert.Contains("(inner exceptions truncated)", output);
    }

    [Fact]
    public void Json_DeepChain_DoesNotOverflowStack()
    {
        var ex = BuildChain(50_000);

        var output = CaptureJson(cap: null, ex);

        Assert.Contains("\"truncated\":true", output);
    }

    [Fact]
    public void Console_RaisedCap_RendersFullChain()
    {
        var ex = BuildChain(20);

        var output = CaptureConsole(cap: 100, ex);

        Assert.DoesNotContain("(inner exceptions truncated)", output);
        Assert.Contains("level-19", output); // outermost
        Assert.Contains("level-0", output);  // innermost wrapper
        Assert.Contains("leaf", output);     // deepest
    }

    [Fact]
    public void Json_RaisedCap_RendersFullChain()
    {
        var ex = BuildChain(20);

        var output = CaptureJson(cap: 100, ex);

        Assert.DoesNotContain("\"truncated\":true", output);
        Assert.Contains("level-19", output);
        Assert.Contains("leaf", output);
    }

    [Fact]
    public void Console_SingleException_NoTruncationMarker()
    {
        var ex = new InvalidOperationException("oops");

        var output = CaptureConsole(cap: null, ex);

        Assert.DoesNotContain("(inner exceptions truncated)", output);
        Assert.Contains("oops", output);
    }

    [Fact]
    public void Json_TruncatedOutput_IsValidJson()
    {
        var ex = BuildChain(100);

        var output = CaptureJson(cap: null, ex).TrimEnd('\n');
        using var doc = JsonDocument.Parse(output);

        // Walk to the deepest "inner" and confirm it's the {"truncated":true} sentinel.
        var node = doc.RootElement.GetProperty("error");
        var depth = 0;
        while (node.TryGetProperty("inner", out var inner))
        {
            node = inner;
            depth++;
            // Guard against an unexpectedly deep walk so a regression here can't loop forever.
            Assert.True(depth <= 64, "walked deeper than the configured cap allows");
        }

        // The leaf object must be the truncation marker (no "type"/"msg" fields).
        Assert.True(node.TryGetProperty("truncated", out var t) && t.GetBoolean());
        Assert.False(node.TryGetProperty("type", out _));
    }

    [Fact]
    public void Json_NonTruncatedOutput_IsValidJson()
    {
        var ex = BuildChain(5);

        var output = CaptureJson(cap: null, ex).TrimEnd('\n');
        using var doc = JsonDocument.Parse(output);

        var node = doc.RootElement.GetProperty("error");
        while (node.TryGetProperty("inner", out var inner))
            node = inner;

        // Leaf is the original "leaf" InvalidOperationException with no further inner.
        Assert.Equal("leaf", node.GetProperty("msg").GetString());
        Assert.False(node.TryGetProperty("truncated", out _));
    }
}
