using System.Runtime.CompilerServices;

namespace Clip.Tests;

public class FieldTests
{
    [Fact]
    public void Field_Int_StoresCorrectly()
    {
        var f = new Field("count", 42);
        Assert.Equal(FieldType.Int, f.Type);
        Assert.Equal("count", f.Key);
        Assert.Equal(42, f.IntValue);
    }

    [Fact]
    public void Field_Long_StoresCorrectly()
    {
        var f = new Field("size", 9_999_999_999L);
        Assert.Equal(FieldType.Long, f.Type);
        Assert.Equal("size", f.Key);
        Assert.Equal(9_999_999_999L, f.LongValue);
    }

    [Fact]
    public void Field_Double_StoresCorrectly()
    {
        var f = new Field("ratio", 3.14);
        Assert.Equal(FieldType.Double, f.Type);
        Assert.Equal("ratio", f.Key);
        Assert.Equal(3.14, f.DoubleValue);
    }

    [Fact]
    public void Field_Bool_StoresCorrectly()
    {
        var f = new Field("active", true);
        Assert.Equal(FieldType.Bool, f.Type);
        Assert.Equal("active", f.Key);
        Assert.True(f.BoolValue);
    }

    [Fact]
    public void Field_String_StoresCorrectly()
    {
        var f = new Field("name", "alice");
        Assert.Equal(FieldType.String, f.Type);
        Assert.Equal("name", f.Key);
        Assert.Equal("alice", f.RefValue);
    }

    [Fact]
    public void Field_Object_StoresCorrectly()
    {
        var obj = new object();
        var f = new Field("data", obj);
        Assert.Equal(FieldType.Object, f.Type);
        Assert.Equal("data", f.Key);
        Assert.Same(obj, f.RefValue);
    }

    [Fact]
    public void Field_Float_StoresCorrectly()
    {
        var f = new Field("rate", 1.5f);
        Assert.Equal(FieldType.Float, f.Type);
        Assert.Equal("rate", f.Key);
        Assert.Equal(1.5f, f.FloatValue);
    }

    [Fact]
    public void Field_DateTimeOffset_StoresUtcTicks()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(5));
        var f = new Field("ts", dto);
        Assert.Equal(FieldType.DateTime, f.Type);
        Assert.Equal("ts", f.Key);
        Assert.Equal(dto.UtcTicks, f.LongValue);
    }

    [Fact]
    public void Field_Byte_WidensToInt()
    {
        var f = new Field("val", (byte)255);
        Assert.Equal(FieldType.Int, f.Type);
        Assert.Equal(255, f.IntValue);
    }

    [Fact]
    public void Field_SByte_WidensToInt()
    {
        var f = new Field("val", (sbyte)-1);
        Assert.Equal(FieldType.Int, f.Type);
        Assert.Equal(-1, f.IntValue);
    }

    [Fact]
    public void Field_Short_WidensToInt()
    {
        var f = new Field("val", (short)-32000);
        Assert.Equal(FieldType.Int, f.Type);
        Assert.Equal(-32000, f.IntValue);
    }

    [Fact]
    public void Field_UShort_WidensToInt()
    {
        var f = new Field("val", (ushort)65535);
        Assert.Equal(FieldType.Int, f.Type);
        Assert.Equal(65535, f.IntValue);
    }

    [Fact]
    public void Field_UInt_WidensToLong()
    {
        var f = new Field("val", uint.MaxValue);
        Assert.Equal(FieldType.Long, f.Type);
        Assert.Equal(uint.MaxValue, f.LongValue);
    }

    [Fact]
    public void Field_ULong_StoresCorrectly()
    {
        var f = new Field("val", ulong.MaxValue);
        Assert.Equal(FieldType.ULong, f.Type);
        Assert.Equal(ulong.MaxValue, unchecked((ulong)f.LongValue));
    }

    [Fact]
    public void Field_DateTime_Utc_StoresUtcTicks()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var f = new Field("ts", dt);
        Assert.Equal(FieldType.DateTime, f.Type);
        Assert.Equal(dt.Ticks, f.LongValue);
    }

    [Fact]
    public void Field_DateTime_Local_ConvertsToUtc()
    {
        var local = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Local);
        var f = new Field("ts", local);
        Assert.Equal(FieldType.DateTime, f.Type);
        Assert.Equal(local.ToUniversalTime().Ticks, f.LongValue);
    }

    [Fact]
    public void Field_DateTime_Unspecified_ConvertsToUtc()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified);
        var f = new Field("ts", dt);
        Assert.Equal(FieldType.DateTime, f.Type);
        Assert.Equal(dt.ToUniversalTime().Ticks, f.LongValue);
    }

    [Fact]
    public void Field_Decimal_StoresCorrectly()
    {
        var f = new Field("price", 19.99m);
        Assert.Equal(FieldType.Decimal, f.Type);
        Assert.Equal("price", f.Key);
        Assert.Equal(19.99m, f.DecimalValue);
    }

    [Fact]
    public void Field_Guid_StoresCorrectly()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var f = new Field("id", guid);
        Assert.Equal(FieldType.Guid, f.Type);
        Assert.Equal("id", f.Key);
        Assert.Equal(guid, f.GuidValue);
    }

    [Fact]
    public void Field_Size_Is40Bytes()
    {
        Assert.Equal(40, Unsafe.SizeOf<Field>());
    }
}
