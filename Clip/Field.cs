using System.Runtime.InteropServices;

namespace Clip;

/// <summary>Discriminator for the value stored in a <see cref="Field"/>.</summary>
public enum FieldType : byte
{
    // Value-type union (offset 8)
    Bool, Int, Long, ULong, Float, Double, DateTime,
    // Reference / boxed (RefValue)
    String, Decimal, Guid, Object,
}

/// <summary>
/// 40-byte discriminated union for log field values. Uses explicit layout so all
/// value-type slots share memory at offset 8 (16-byte region covers Guid/decimal
/// without boxing), while reference-type slots occupy separate offsets to satisfy
/// the GC's requirement that references don't overlap with value types.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct Field
{
    [FieldOffset(0)] public readonly FieldType Type;

    // Value-type region — all overlapped at offset 8 (only one is valid per Type).
    // 16 bytes wide so Guid and decimal fit without boxing.
    [FieldOffset(8)] internal readonly long LongValue;
    [FieldOffset(8)] internal readonly double DoubleValue;
    [FieldOffset(8)] internal readonly int IntValue;
    [FieldOffset(8)] internal readonly float FloatValue;
    [FieldOffset(8)] internal readonly bool BoolValue;
    [FieldOffset(8)] internal readonly decimal DecimalValue;
    [FieldOffset(8)] internal readonly Guid GuidValue;

    // Reference-type region — must not overlap with value types (GC constraint)
    [FieldOffset(24)] public readonly string Key;
    [FieldOffset(32)] internal readonly object? RefValue;

    //
    // Constructors — ordered by FieldType
    //

    // `this = default` zeroes all overlapping fields before setting the active ones,
    // required by the compiler for explicit-layout structs with mixed slot types.

    // Bool
    public Field(string key, bool value) { this = default; Key = key; Type = FieldType.Bool; BoolValue = value; }

    // Int (+ widening from byte, sbyte, short, ushort)
    public Field(string key, int value) { this = default; Key = key; Type = FieldType.Int; IntValue = value; }
    public Field(string key, byte value) { this = default; Key = key; Type = FieldType.Int; IntValue = value; }
    public Field(string key, sbyte value) { this = default; Key = key; Type = FieldType.Int; IntValue = value; }
    public Field(string key, short value) { this = default; Key = key; Type = FieldType.Int; IntValue = value; }
    public Field(string key, ushort value) { this = default; Key = key; Type = FieldType.Int; IntValue = value; }

    // Long (+ widening from uint)
    public Field(string key, long value) { this = default; Key = key; Type = FieldType.Long; LongValue = value; }
    public Field(string key, uint value) { this = default; Key = key; Type = FieldType.Long; LongValue = value; }

    // ULong
    public Field(string key, ulong value) { this = default; Key = key; Type = FieldType.ULong; LongValue = unchecked((long)value); }

    // Float
    public Field(string key, float value) { this = default; Key = key; Type = FieldType.Float; FloatValue = value; }

    // Double
    public Field(string key, double value) { this = default; Key = key; Type = FieldType.Double; DoubleValue = value; }

    // DateTime (DateTimeOffset and DateTime both store as UTC ticks)
    public Field(string key, DateTimeOffset value)
    { this = default; Key = key; Type = FieldType.DateTime; LongValue = value.UtcTicks; }
    public Field(string key, DateTime value)
    { this = default; Key = key; Type = FieldType.DateTime; LongValue = value.ToUniversalTime().Ticks; }

    // String
    public Field(string key, string value) { this = default; Key = key; Type = FieldType.String; RefValue = value; }

    // Decimal (16 bytes — fits in value union, no boxing)
    public Field(string key, decimal value)
    { this = default; Key = key; Type = FieldType.Decimal; DecimalValue = value; }

    // Guid (16 bytes — fits in value union, no boxing)
    public Field(string key, Guid value)
    { this = default; Key = key; Type = FieldType.Guid; GuidValue = value; }

    // Object (catch-all fallback)
    public Field(string key, object? value) { this = default; Key = key; Type = FieldType.Object; RefValue = value; }
}
