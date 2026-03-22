using System.Text;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Buffer management, field sorting stackalloc boundary, and ConsoleSink edge cases.
/// </summary>
public class BufferAndSortingTests
{
    //
    // ConsoleSink WriteFieldsSorted stackalloc boundary (64 fields)
    //

    [Fact]
    public void ConsoleSink_Exactly64Fields_StackAlloc()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var fields = Enumerable.Range(0, 64)
            .Select(i => new Field($"f{i:D3}", i))
            .ToArray();

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        // All fields present and sorted
        Assert.Contains("f000=0", output);
        Assert.Contains("f063=63", output);
        var idx0 = output.IndexOf("f000=", StringComparison.Ordinal);
        var idx63 = output.IndexOf("f063=", StringComparison.Ordinal);
        Assert.True(idx0 < idx63, "Fields should be sorted");
    }

    [Fact]
    public void ConsoleSink_65Fields_HeapFallback()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var fields = Enumerable.Range(0, 65)
            .Select(i => new Field($"f{i:D3}", i))
            .ToArray();

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("f000=0", output);
        Assert.Contains("f064=64", output);
    }

    [Fact]
    public void ConsoleSink_100Fields_AllSorted()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        var fields = Enumerable.Range(0, 100)
            .Select(i => new Field($"f{i:D3}", i))
            .ToArray();

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg", fields, null);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Verify sort order by checking sequential positions
        var prevIdx = -1;
        for (var i = 0; i < 100; i++)
        {
            var idx = output.IndexOf($"f{i:D3}=", StringComparison.Ordinal);
            Assert.True(idx > prevIdx, $"Field f{i:D3} should appear after f{i - 1:D3}");
            prevIdx = idx;
        }
    }

    //
    // Single field
    //

    [Fact]
    public void ConsoleSink_SingleField_NoSeparator()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "msg",
            [new Field("only", 1)], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("only=1", output);
        // Only one field, so no space-separated second field
        Assert.DoesNotContain("only=1 ", output.Substring(output.IndexOf("only=1", StringComparison.Ordinal) + 6));
    }

    //
    // Buffer growth with large data
    //

    [Fact]
    public void JsonSink_VeryLargeField_DoesNotCorrupt()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var bigValue = new string('X', 50_000);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("big", bigValue)], null);
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var doc = System.Text.Json.JsonDocument.Parse(text.TrimEnd('\n'));
        Assert.Equal(bigValue, doc.RootElement.GetProperty("fields").GetProperty("big").GetString());
    }

    [Fact]
    public void JsonSink_ManyLargeFields_DoesNotCorrupt()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        var fields = Enumerable.Range(0, 50)
            .Select(i => new Field($"f{i}", new string((char)('A' + i % 26), 1000)))
            .ToArray();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m", fields, null);
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var doc = System.Text.Json.JsonDocument.Parse(text.TrimEnd('\n'));
        Assert.Equal(50, doc.RootElement.GetProperty("fields").EnumerateObject().Count());
    }

    //
    // Buffer reuse across resets
    //

    [Fact]
    public void JsonSink_MultipleWrites_BufferReused()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);

        for (var i = 0; i < 100; i++)
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, $"msg-{i}",
                [new Field("i", i)], null);

        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
        foreach (var line in lines)
            System.Text.Json.JsonDocument.Parse(line); // All must be valid
    }

    //
    // Object field null handling
    //

    [Fact]
    public void ConsoleSink_ObjectFieldNull_PrintsNull()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("data", (object?)null)], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("data=null", output);
    }

    //
    // ConsoleSink non-ASCII field keys
    //

    [Fact]
    public void ConsoleSink_NonAsciiFieldKey()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
            [new Field("clé", "value")], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("clé=value", output);
    }
}
