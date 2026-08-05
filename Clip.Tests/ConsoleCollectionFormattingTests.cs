using System.Collections;
using System.Text;
using Clip.Sinks;

namespace Clip.Tests;

public class ConsoleCollectionFormattingTests
{
    private static string Capture(object? value, ConsoleFormatConfig? config = null)
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(config ?? new ConsoleFormatConfig { Colors = false }, ms);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m", [new Field("v", value)], null);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Value(object? value, ConsoleFormatConfig? config = null)
    {
        var output = Capture(value, config);
        var idx = output.IndexOf("v=", StringComparison.Ordinal);
        return output[(idx + 2)..].TrimEnd('\n');
    }

    //
    // Sequences
    //

    [Fact]
    public void StringList_RendersElements()
    {
        Assert.Equal("[a, b, c]", Value(new List<string> { "a", "b", "c" }));
    }

    [Fact]
    public void StringArray_RendersElements()
    {
        Assert.Equal("[a, b]", Value(new[] { "a", "b" }));
    }

    [Fact]
    public void PrimitiveCollections_RenderElements()
    {
        Assert.Equal("[1, 2, 3]", Value(new[] { 1, 2, 3 }));
        Assert.Equal("[1, 2, 3]", Value(new List<int> { 1, 2, 3 }));
        Assert.Equal("[1, 2]", Value(new[] { 1L, 2L }));
        Assert.Equal("[1, 2]", Value(new List<long> { 1, 2 }));
        Assert.Equal("[1.5, 2.5]", Value(new[] { 1.5, 2.5 }));
        Assert.Equal("[1.5, 2.5]", Value(new List<double> { 1.5, 2.5 }));
    }

    [Fact]
    public void EmptyCollection_RendersEmptyBrackets()
    {
        Assert.Equal("[]", Value(new List<string>()));
        Assert.Equal("[]", Value(Array.Empty<int>()));
    }

    [Fact]
    public void NullElements_RenderAsNull()
    {
        Assert.Equal("[a, null]", Value(new string?[] { "a", null }));
        Assert.Equal("[null, 1]", Value(new object?[] { null, 1 }));
    }

    [Fact]
    public void MixedObjectCollection_RendersEachElement()
    {
        Assert.Equal("[1, x, True]", Value(new object[] { 1, "x", true }));
    }

    [Fact]
    public void LazyEnumerable_IsExpanded()
    {
        Assert.Equal("[0, 1, 2]", Value(Enumerable.Range(0, 3).Select(i => i)));
    }

    [Fact]
    public void HashSet_IsExpanded()
    {
        Assert.Equal("[a]", Value(new HashSet<string> { "a" }));
    }

    //
    // Dictionaries
    //

    [Fact]
    public void Dictionary_RendersKeyValuePairs()
    {
        Assert.Equal("{a=1}", Value(new Dictionary<string, int> { ["a"] = 1 }));
    }

    [Fact]
    public void Dictionary_WithObjectValues_RendersNestedCollections()
    {
        Assert.Equal("{a=[1, 2]}", Value(new Dictionary<string, object> { ["a"] = new[] { 1, 2 } }));
    }

    [Fact]
    public void EmptyDictionary_RendersEmptyBraces()
    {
        Assert.Equal("{}", Value(new Dictionary<string, int>()));
    }

    //
    // Nesting and limits
    //

    [Fact]
    public void NestedCollections_AreExpandedToConfiguredDepth()
    {
        var nested = new List<object> { new List<object> { new List<object> { new List<object> { 1 } } } };
        Assert.Equal("[[[[...]]]]", Value(nested));
    }

    [Fact]
    public void NestingWithinDepthLimit_IsFullyExpanded()
    {
        Assert.Equal("[[1, 2], [3]]", Value(new List<object> { new[] { 1, 2 }, new[] { 3 } }));
    }

    [Fact]
    public void CountedCollectionOverLimit_IsClippedWithCount()
    {
        var config = new ConsoleFormatConfig { Colors = false, MaxCollectionItems = 2 };
        Assert.Equal("[1, 2, ... (3 more)]", Value(new[] { 1, 2, 3, 4, 5 }, config));
        Assert.Equal("[a, b, ... (1 more)]", Value(new List<string> { "a", "b", "c" }, config));
    }

    [Fact]
    public void LazyEnumerableOverLimit_IsClipped()
    {
        var config = new ConsoleFormatConfig { Colors = false, MaxCollectionItems = 2 };
        Assert.Equal("[0, 1, ...]", Value(Enumerable.Range(0, 100).Select(i => i), config));
    }

    [Fact]
    public void InfiniteEnumerable_Terminates()
    {
        var config = new ConsoleFormatConfig { Colors = false, MaxCollectionItems = 3 };
        Assert.Equal("[0, 1, 2, ...]", Value(Forever(), config));

        static IEnumerable<int> Forever()
        {
            var i = 0;
            while (true) yield return i++;
        }
    }

    [Fact]
    public void ZeroItemLimit_RendersOnlyMarker()
    {
        var config = new ConsoleFormatConfig { Colors = false, MaxCollectionItems = 0 };
        Assert.Equal("[... (3 more)]", Value(new[] { 1, 2, 3 }, config));
    }

    [Fact]
    public void ZeroDepthLimit_RendersSentinel()
    {
        var config = new ConsoleFormatConfig { Colors = false, MaxCollectionDepth = 0 };
        Assert.Equal("[...]", Value(new[] { 1, 2 }, config));
        Assert.Equal("{...}", Value(new Dictionary<string, int> { ["a"] = 1 }, config));
    }

    [Fact]
    public void SelfReferencingCollection_Terminates()
    {
        var list = new ArrayList();
        list.Add(list);
        Assert.Equal("[[[[...]]]]", Value(list));
    }

    //
    // Non-collections keep their existing rendering
    //

    [Fact]
    public void String_IsNotTreatedAsCollection()
    {
        Assert.Equal("hello", Value("hello"));
    }

    [Fact]
    public void PlainObject_UsesToString()
    {
        Assert.Equal("custom", Value(new Stringy()));
    }

    [Fact]
    public void ControlCharactersInElements_AreSanitized()
    {
        Assert.Equal("[ab, c]", Value(new[] { "a\nb", "c\e" }));
    }

    private sealed class Stringy
    {
        public override string ToString() => "custom";
    }
}
