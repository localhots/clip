using Clip.OpenTelemetry.Mapping;
using OpenTelemetry.Proto.Common.V1;

namespace Clip.OpenTelemetry.Tests;

public class FieldMapperTests
{
    [Fact]
    public void Bool_MapsCorrectly()
    {
        var field = new Field("enabled", true);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("enabled", kv.Key);
        Assert.True(kv.Value.BoolValue);
    }

    [Fact]
    public void Int_MapsToIntValue()
    {
        var field = new Field("port", 8080);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("port", kv.Key);
        Assert.Equal(8080, kv.Value.IntValue);
    }

    [Fact]
    public void Long_MapsToIntValue()
    {
        var field = new Field("bytes", 123456789L);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("bytes", kv.Key);
        Assert.Equal(123456789L, kv.Value.IntValue);
    }

    [Fact]
    public void Float_MapsToDoubleValue()
    {
        var field = new Field("ratio", 0.5f);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("ratio", kv.Key);
        Assert.Equal(0.5f, kv.Value.DoubleValue, 0.001);
    }

    [Fact]
    public void Double_MapsToDoubleValue()
    {
        var field = new Field("latency", 1.234);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("latency", kv.Key);
        Assert.Equal(1.234, kv.Value.DoubleValue, 0.001);
    }

    [Fact]
    public void String_MapsToStringValue()
    {
        var field = new Field("host", "localhost");
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("host", kv.Key);
        Assert.Equal("localhost", kv.Value.StringValue);
    }

    [Fact]
    public void DateTime_MapsToIso8601String()
    {
        var ts = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var field = new Field("created_at", ts);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("created_at", kv.Key);
        Assert.Contains("2024-06-15", kv.Value.StringValue);
    }

    [Fact]
    public void Guid_MapsToStringValue()
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var field = new Field("request_id", id);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("request_id", kv.Key);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", kv.Value.StringValue);
    }

    [Fact]
    public void Decimal_MapsToStringValue()
    {
        var field = new Field("price", 99.99m);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("price", kv.Key);
        Assert.Equal("99.99", kv.Value.StringValue);
    }

    [Fact]
    public void Object_MapsToStringViaToString()
    {
        var field = new Field("data", (object)42);
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("data", kv.Key);
        Assert.Equal("42", kv.Value.StringValue);
    }

    //
    // Collections
    //

    [Fact]
    public void GuidList_MapsToArrayOfStrings()
    {
        var a = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var b = Guid.Parse("87654321-4321-4321-4321-cba987654321");
        var field = new Field("EnrollmentIds", (object)new List<Guid> { a, b });
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal(AnyValue.ValueOneofCase.ArrayValue, kv.Value.ValueCase);
        Assert.Equal([a.ToString(), b.ToString()], kv.Value.ArrayValue.Values.Select(v => v.StringValue));
    }

    [Fact]
    public void IntArray_KeepsIntType()
    {
        var field = new Field("ids", (object)new[] { 1, 2, 3 });
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal([1L, 2L, 3L], kv.Value.ArrayValue.Values.Select(v => v.IntValue));
    }

    [Fact]
    public void Dictionary_MapsToKvlist()
    {
        var field = new Field("counts", (object)new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal(AnyValue.ValueOneofCase.KvlistValue, kv.Value.ValueCase);
        var values = kv.Value.KvlistValue.Values;
        Assert.Equal(["a", "b"], values.Select(v => v.Key));
        Assert.Equal([1L, 2L], values.Select(v => v.Value.IntValue));
    }

    [Fact]
    public void NestedCollections_MapRecursively()
    {
        var field = new Field("matrix", (object)new List<int[]> { new[] { 1 }, new[] { 2, 3 } });
        var kv = FieldMapper.ToKeyValue(in field);

        var rows = kv.Value.ArrayValue.Values;
        Assert.Equal(2, rows.Count);
        Assert.Equal([2L, 3L], rows[1].ArrayValue.Values.Select(v => v.IntValue));
    }

    [Fact]
    public void NullElement_MapsToEmptyValue()
    {
        var field = new Field("names", (object)new List<string?> { "a", null });
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal(AnyValue.ValueOneofCase.None, kv.Value.ArrayValue.Values[1].ValueCase);
    }

    [Fact]
    public void LargeCollection_IsTruncatedWithMarker()
    {
        var field = new Field("ids", (object)Enumerable.Range(0, 150).ToList());
        var kv = FieldMapper.ToKeyValue(in field);

        var values = kv.Value.ArrayValue.Values;
        Assert.Equal(FieldMapper.MaxCollectionItems + 1, values.Count);
        Assert.Equal("... (50 more)", values[^1].StringValue);
    }

    [Fact]
    public void InfiniteSequence_StopsAtItemCap()
    {
        static IEnumerable<int> Forever() { while (true) yield return 1; }
        var field = new Field("stream", (object)Forever());
        var kv = FieldMapper.ToKeyValue(in field);

        var values = kv.Value.ArrayValue.Values;
        Assert.Equal(FieldMapper.MaxCollectionItems + 1, values.Count);
        Assert.Equal("...", values[^1].StringValue);
    }

    [Fact]
    public void NestedCollections_AreCappedByTotalElementCount()
    {
        var grid = Enumerable.Range(0, 100).Select(_ => Enumerable.Range(0, 100).ToList()).ToList();
        var field = new Field("grid", (object)grid);
        var kv = FieldMapper.ToKeyValue(in field);

        var total = CountValues(kv.Value);
        Assert.True(total <= FieldMapper.MaxCollectionElements + 100, $"converted {total} values");
    }

    [Fact]
    public void ThrowingSequence_MapsToErrorString()
    {
        static IEnumerable<int> Throws() { yield return 1; throw new InvalidOperationException(); }
        var field = new Field("bad", (object)Throws());
        var kv = FieldMapper.ToKeyValue(in field);

        Assert.Equal("<enumeration failed: InvalidOperationException>", kv.Value.StringValue);
    }

    [Fact]
    public void NonCollectionObject_StillUsesToString()
    {
        Assert.Null(FieldMapper.SnapshotCollection("text"));
        Assert.Null(FieldMapper.SnapshotCollection(42));
        Assert.Null(FieldMapper.SnapshotCollection(null));
    }

    private static int CountValues(AnyValue value)
    {
        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.ArrayValue => 1 + value.ArrayValue.Values.Sum(CountValues),
            AnyValue.ValueOneofCase.KvlistValue => 1 + value.KvlistValue.Values.Sum(kv => CountValues(kv.Value)),
            _ => 1,
        };
    }

    [Fact]
    public void DeepNesting_IsCappedAtMaxDepth()
    {
        var field = new Field("deep", (object)new object[] { new object[] { new object[] { new object[] { 1 } } } });
        var kv = FieldMapper.ToKeyValue(in field);

        var level3 = kv.Value.ArrayValue.Values[0].ArrayValue.Values[0].ArrayValue.Values[0];
        Assert.Equal("[...]", level3.StringValue);
    }
}
