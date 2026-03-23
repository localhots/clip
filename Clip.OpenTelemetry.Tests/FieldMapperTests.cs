using Clip.OpenTelemetry.Mapping;

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
}
