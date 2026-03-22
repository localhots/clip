using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Numeric formatting edge cases: min/max values, NaN, Infinity, special doubles.
/// </summary>
public class NumericEdgeCaseTests
{
    private static (JsonSink sink, MemoryStream ms) MakeSink()
    {
        var ms = new MemoryStream();
        return (new JsonSink(ms), ms);
    }

    private static JsonDocument ParseLine(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return JsonDocument.Parse(text.TrimEnd('\n'));
    }

    private static string GetFieldRaw(MemoryStream ms, string key)
    {
        ms.Position = 0;
        var json = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
        // Extract the raw value for fields that may not be valid JSON numbers
        var fieldsStart = json.IndexOf("\"fields\":{", StringComparison.Ordinal) + 10;
        var keyStr = $"\"{key}\":";
        var valueStart = json.IndexOf(keyStr, fieldsStart, StringComparison.Ordinal) + keyStr.Length;
        var valueEnd = json.IndexOfAny([',', '}'], valueStart);
        return json[valueStart..valueEnd];
    }

    //
    // Integer edge cases
    //

    [Fact]
    public void IntField_MinValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", int.MinValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(int.MinValue, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt32());
    }

    [Fact]
    public void IntField_MaxValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", int.MaxValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(int.MaxValue, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt32());
    }

    [Fact]
    public void IntField_Zero()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", 0)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(0, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt32());
    }

    [Fact]
    public void IntField_NegativeOne()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", -1)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(-1, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt32());
    }

    [Fact]
    public void LongField_MinValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", long.MinValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(long.MinValue, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt64());
    }

    [Fact]
    public void LongField_MaxValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", long.MaxValue)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(long.MaxValue, doc.RootElement.GetProperty("fields").GetProperty("v").GetInt64());
    }

    //
    // Double edge cases
    //

    [Fact]
    public void DoubleField_NaN_ProducesOutput()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", double.NaN)], null);
        // NaN is not valid JSON per spec, but we should at least not crash
        var raw = GetFieldRaw(ms, "v");
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void DoubleField_PositiveInfinity_ProducesOutput()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", double.PositiveInfinity)], null);
        var raw = GetFieldRaw(ms, "v");
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void DoubleField_NegativeInfinity_ProducesOutput()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", double.NegativeInfinity)], null);
        var raw = GetFieldRaw(ms, "v");
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void DoubleField_NegativeZero()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", -0.0)], null);
        using var doc = ParseLine(ms);
        Assert.Equal(0.0, doc.RootElement.GetProperty("fields").GetProperty("v").GetDouble());
    }

    [Fact]
    public void DoubleField_VerySmall()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", 1e-308)], null);
        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.GetProperty("fields").GetProperty("v").GetDouble() > 0);
    }

    [Fact]
    public void DoubleField_VeryLarge()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", 1e308)], null);
        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.GetProperty("fields").GetProperty("v").GetDouble() > 1e307);
    }

    [Fact]
    public void DoubleField_Epsilon()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", double.Epsilon)], null);
        using var doc = ParseLine(ms);
        Assert.True(doc.RootElement.GetProperty("fields").GetProperty("v").GetDouble() > 0);
    }

    //
    // Float edge cases
    //

    [Fact]
    public void FloatField_NaN_ProducesOutput()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", float.NaN)], null);
        var raw = GetFieldRaw(ms, "v");
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void FloatField_PositiveInfinity_ProducesOutput()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", float.PositiveInfinity)], null);
        var raw = GetFieldRaw(ms, "v");
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void FloatField_MaxValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", float.MaxValue)], null);
        // Just verify it doesn't crash and produces output
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"v\":", text);
    }

    [Fact]
    public void FloatField_MinValue()
    {
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", float.MinValue)], null);
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"v\":", text);
    }

    //
    // Console sink numeric formatting
    //

    [Fact]
    public void ConsoleSink_IntMinValue()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, colors: false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", int.MinValue)], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains($"v={int.MinValue}", output);
    }

    [Fact]
    public void ConsoleSink_LongMaxValue()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, colors: false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", long.MaxValue)], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains($"v={long.MaxValue}", output);
    }

    [Fact]
    public void ConsoleSink_DoubleNaN()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, colors: false);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("v", double.NaN)], null);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("v=", output);
    }

    //
    // DateTime edge cases
    //

    [Fact]
    public void DateTimeField_MinValue()
    {
        var dto = DateTimeOffset.MinValue;
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("ts", dto)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        Assert.StartsWith("0001-01-01", tsStr);
    }

    [Fact]
    public void DateTimeField_MaxValue()
    {
        var dto = DateTimeOffset.MaxValue;
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("ts", dto)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        Assert.StartsWith("9999-12-31", tsStr);
    }

    [Fact]
    public void DateTimeField_WithMicroseconds()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero)
            .AddTicks(1234567); // Sub-second precision
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("ts", dto)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        Assert.Contains(".", tsStr); // Has fractional seconds
    }

    [Fact]
    public void DateTimeField_NonUtcOffset_StoredAsUtc()
    {
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(5));
        var (sink, ms) = MakeSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "m",
                   [new Field("ts", dto)], null);
        using var doc = ParseLine(ms);
        var tsStr = doc.RootElement.GetProperty("fields").GetProperty("ts").GetString()!;
        // UTC conversion: 10:30 +05:00 => 05:30 UTC
        Assert.Contains("05:30:00", tsStr);
        Assert.EndsWith("Z", tsStr);
    }
}
