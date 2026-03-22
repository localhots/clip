using Clip.Fields;

namespace Clip.Tests;

public class FieldExtractorTests
{
    [Fact]
    public void ExtractInto_AnonymousType_ExtractsAllProperties()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { UserId = 42, Name = "alice", Active = true }, list);

        Assert.Equal(3, list.Count);

        var userId = list.Find(f => f.Key == "UserId");
        Assert.Equal(FieldType.Int, userId.Type);
        Assert.Equal(42, userId.IntValue);

        var name = list.Find(f => f.Key == "Name");
        Assert.Equal(FieldType.String, name.Type);
        Assert.Equal("alice", name.RefValue);

        var active = list.Find(f => f.Key == "Active");
        Assert.Equal(FieldType.Bool, active.Type);
        Assert.True(active.BoolValue);
    }

    [Fact]
    public void ExtractInto_AnonymousType_NoBoxingForPrimitives()
    {
        // Int, bool, double should use typed constructors (FieldType, not Object)
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Count = 1, Flag = false, Ratio = 2.5 }, list);

        Assert.Equal(FieldType.Int, list.Find(f => f.Key == "Count").Type);
        Assert.Equal(FieldType.Bool, list.Find(f => f.Key == "Flag").Type);
        Assert.Equal(FieldType.Double, list.Find(f => f.Key == "Ratio").Type);
    }

    [Fact]
    public void ExtractInto_GenericDictionary_ExtractsEntries()
    {
        var dict = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "two" };
        var list = new List<Field>();
        FieldExtractor.ExtractInto(dict, list);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void ExtractInto_ReadOnlyDictionary_ExtractsEntries()
    {
        IReadOnlyDictionary<string, object?> dict = new Dictionary<string, object?> { ["x"] = 99 };
        var list = new List<Field>();
        FieldExtractor.ExtractInto(dict, list);

        Assert.Single(list);
        Assert.Equal("x", list[0].Key);
    }

    [Fact]
    public void ExtractInto_PrimitiveThrows()
    {
        var list = new List<Field>();
        Assert.Throws<ArgumentException>(() => FieldExtractor.ExtractInto(42, list));
        Assert.Throws<ArgumentException>(() => FieldExtractor.ExtractInto("oops", list));
    }

    [Fact]
    public void ExtractInto_ArrayThrows()
    {
        var list = new List<Field>();
        Assert.Throws<ArgumentException>(() => FieldExtractor.ExtractInto(new[] { 1, 2, 3 }, list));
    }

    [Fact]
    public void ExtractInto_CachesCompileResult()
    {
        // The second call with the same type should use cache, not recompile
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { X = 1 }, list);
        list.Clear();
        FieldExtractor.ExtractInto(new { X = 2 }, list);

        Assert.Single(list);
        Assert.Equal(2, list[0].IntValue);
    }

    [Fact]
    public void ExtractInto_LongProperty_UseTypedConstructor()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Size = 1_234_567_890_123L }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.Long, list[0].Type);
        Assert.Equal(1_234_567_890_123L, list[0].LongValue);
    }

    [Fact]
    public void ExtractInto_FloatProperty_UsesTypedConstructor()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Rate = 2.5f }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.Float, list[0].Type);
        Assert.Equal(2.5f, list[0].FloatValue);
    }

    [Fact]
    public void ExtractInto_DateTimeOffsetProperty_UsesTypedConstructor()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Timestamp = dto }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.DateTime, list[0].Type);
        Assert.Equal(dto.UtcTicks, list[0].LongValue);
    }
}
