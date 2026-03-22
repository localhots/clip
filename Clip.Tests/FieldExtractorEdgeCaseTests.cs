using System.Collections;
using Clip.Fields;

namespace Clip.Tests;

/// <summary>
/// FieldExtractor edge cases: non-generic IDictionary, nullable properties,
/// various property types, concurrent compilation.
/// </summary>
public class FieldExtractorEdgeCaseTests
{
    //
    // Non-generic IDictionary
    //

    [Fact]
    public void NonGenericDictionary_ExtractsEntries()
    {
        var dict = new Hashtable { ["name"] = "alice", ["count"] = 42 };
        var list = new List<Field>();
        FieldExtractor.ExtractInto(dict, list);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, f => f.Key == "name");
        Assert.Contains(list, f => f.Key == "count");
    }

    [Fact]
    public void NonGenericDictionary_IntKeys()
    {
        var dict = new Hashtable { [1] = "one", [2] = "two" };
        var list = new List<Field>();
        FieldExtractor.ExtractInto(dict, list);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, f => f.Key == "1");
        Assert.Contains(list, f => f.Key == "2");
    }

    [Fact]
    public void NonGenericDictionary_NullValue()
    {
        var dict = new Hashtable { ["key"] = null };
        var list = new List<Field>();
        FieldExtractor.ExtractInto(dict, list);

        Assert.Single(list);
        Assert.Equal("key", list[0].Key);
        Assert.Null(list[0].RefValue);
    }

    //
    // Property types
    //

    [Fact]
    public void NullableIntProperty_ExtractsAsObject()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new NullableHolder { Value = 42 }, list);

        Assert.Single(list);
        Assert.Equal("Value", list[0].Key);
        // Nullable<int> doesn't match the int constructor, falls back to object
        Assert.Equal(FieldType.Object, list[0].Type);
    }

    [Fact]
    public void NullableIntProperty_Null_ExtractsAsObject()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new NullableHolder { Value = null }, list);

        Assert.Single(list);
        Assert.Equal("Value", list[0].Key);
    }

    [Fact]
    public void EnumProperty_ExtractsAsString()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Level = LogLevel.Error }, list);

        Assert.Single(list);
        Assert.Equal("Level", list[0].Key);
        Assert.Equal(FieldType.String, list[0].Type);
        Assert.Equal("Error", (string)list[0].RefValue!);
    }

    [Fact]
    public void GuidProperty_ExtractsAsGuid()
    {
        var guid = Guid.NewGuid();
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Id = guid }, list);

        Assert.Single(list);
        Assert.Equal("Id", list[0].Key);
        Assert.Equal(FieldType.Guid, list[0].Type);
        Assert.Equal(guid, list[0].GuidValue);
    }

    [Fact]
    public void DecimalProperty_ExtractsAsDecimal()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Price = 19.99m }, list);

        Assert.Single(list);
        Assert.Equal("Price", list[0].Key);
        Assert.Equal(FieldType.Decimal, list[0].Type);
        Assert.Equal(19.99m, list[0].DecimalValue);
    }

    [Fact]
    public void DateTimeProperty_ExtractsAsDateTime()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Timestamp = dt }, list);

        Assert.Single(list);
        Assert.Equal("Timestamp", list[0].Key);
        Assert.Equal(FieldType.DateTime, list[0].Type);
        Assert.Equal(dt.Ticks, list[0].LongValue);
    }

    [Fact]
    public void ByteProperty_ExtractsAsInt()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Val = (byte)255 }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.Int, list[0].Type);
        Assert.Equal(255, list[0].IntValue);
    }

    [Fact]
    public void UIntProperty_ExtractsAsLong()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Val = uint.MaxValue }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.Long, list[0].Type);
        Assert.Equal(uint.MaxValue, list[0].LongValue);
    }

    [Fact]
    public void ULongProperty_ExtractsAsULong()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new { Val = ulong.MaxValue }, list);

        Assert.Single(list);
        Assert.Equal(FieldType.ULong, list[0].Type);
        Assert.Equal(ulong.MaxValue, unchecked((ulong)list[0].LongValue));
    }

    //
    // Concurrent extraction of different types
    //

    [Fact]
    public void ConcurrentExtraction_DifferentTypes()
    {
        var exceptions = new List<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                var list = new List<Field>();
                switch (i % 3)
                {
                    // Use different anonymous types to stress the cache
                    case 0:
                        FieldExtractor.ExtractInto(new { A = i }, list);
                        break;
                    case 1:
                        FieldExtractor.ExtractInto(new { B = i, C = "x" }, list);
                        break;
                    default:
                        FieldExtractor.ExtractInto(new { D = i, E = true, F = 1.0 }, list);
                        break;
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        Assert.Empty(exceptions);
    }

    //
    // Write-only properties (should be skipped since CanRead is false)
    //

    [Fact]
    public void WriteOnlyProperty_Skipped()
    {
        var list = new List<Field>();
        FieldExtractor.ExtractInto(new WriteOnlyHolder(), list);

        // Only ReadProp should be extracted, WriteOnly should be skipped
        Assert.Single(list);
        Assert.Equal("ReadProp", list[0].Key);
    }

    //
    // Empty object
    //

    [Fact]
    public void EmptyAnonymousType_NoFields()
    {
        var list = new List<Field>();
        // Empty anonymous type has no properties
        // Use a class with no readable properties instead
        FieldExtractor.ExtractInto(new EmptyHolder(), list);
        Assert.Empty(list);
    }

    //
    // Helpers
    //

    private class NullableHolder
    {
        public int? Value { get; set; }
    }

    private class WriteOnlyHolder
    {
        public string ReadProp { get; } = "visible";

        public string WriteProp
        {
            set => _ = value;
        }
    }

    private class EmptyHolder
    {
    }
}
