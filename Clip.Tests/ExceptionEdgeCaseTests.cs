using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Exception handling edge cases: Data dictionary, nested exceptions, special chars.
/// </summary>
public class ExceptionEdgeCaseTests
{
    private static (JsonSink sink, MemoryStream ms) MakeJsonSink()
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

    private static string CaptureConsole(Action<ConsoleSink> write)
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, false);
        write(sink);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    //
    // Exception.Data edge cases (JsonSink)
    //

    [Fact]
    public void JsonSink_ExceptionData_NullValue()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["key"] = null },
        };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("null", data.GetProperty("key").GetString());
    }

    [Fact]
    public void JsonSink_ExceptionData_IntKey()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { [42] = "numeric key" },
        };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("numeric key", data.GetProperty("42").GetString());
    }

    [Fact]
    public void JsonSink_ExceptionData_KeyNeedsEscaping()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["key\"with\"quotes"] = "value" },
        };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("value", data.GetProperty("key\"with\"quotes").GetString());
    }

    [Fact]
    public void JsonSink_ExceptionData_ValueNeedsEscaping()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["key"] = "line1\nline2\ttab" },
        };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("line1\nline2\ttab", data.GetProperty("key").GetString());
    }

    [Fact]
    public void JsonSink_ExceptionData_ManyEntries()
    {
        var ex = new InvalidOperationException("boom");
        for (var i = 0; i < 50; i++)
            ex.Data[$"key_{i}"] = $"value_{i}";

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal(50, data.EnumerateObject().Count());
    }

    [Fact]
    public void JsonSink_ExceptionData_EmptyDictionary_NoDataProperty()
    {
        var ex = new InvalidOperationException("boom");
        // Data is empty by default

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.False(error.TryGetProperty("data", out _));
    }

    //
    // Nested exception depth
    //

    [Fact]
    public void JsonSink_DeeplyNestedExceptions()
    {
        Exception ex = new InvalidOperationException("level-0");
        for (var i = 1; i <= 10; i++)
            ex = new InvalidOperationException($"level-{i}", ex);

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "deep", [], ex);
        using var doc = ParseLine(ms);

        // Walk the nesting
        var current = doc.RootElement.GetProperty("error");
        for (var i = 10; i >= 1; i--)
        {
            Assert.Equal($"level-{i}", current.GetProperty("msg").GetString());
            current = current.GetProperty("inner");
        }

        Assert.Equal("level-0", current.GetProperty("msg").GetString());
    }

    [Fact]
    public void JsonSink_NestedExceptionWithData_AllDataPreserved()
    {
        var inner = new ArgumentException("inner") { Data = { ["inner_key"] = "inner_val" } };
        var middle = new InvalidOperationException("middle", inner) { Data = { ["mid_key"] = "mid_val" } };
        var outer = new Exception("outer", middle) { Data = { ["out_key"] = "out_val" } };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], outer);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("out_val", error.GetProperty("data").GetProperty("out_key").GetString());
        Assert.Equal("mid_val", error.GetProperty("inner").GetProperty("data").GetProperty("mid_key").GetString());
        Assert.Equal("inner_val",
            error.GetProperty("inner").GetProperty("inner").GetProperty("data").GetProperty("inner_key").GetString());
    }

    //
    // Exception message with special chars
    //

    [Fact]
    public void JsonSink_ExceptionMessage_WithSpecialChars()
    {
        var ex = new InvalidOperationException("Error: \"file\\not\\found\"\tat\nline 42");
        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var msg = doc.RootElement.GetProperty("error").GetProperty("msg").GetString();
        Assert.Equal("Error: \"file\\not\\found\"\tat\nline 42", msg);
    }

    [Fact]
    public void JsonSink_ExceptionType_FullName()
    {
        // "param" is intentional test data, not a parameter name reference
        // ReSharper disable once NotResolvedInText
        var ex = new ArgumentNullException("param");
        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        Assert.Equal("System.ArgumentNullException",
            doc.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    //
    // Exception without stack trace
    //

    [Fact]
    public void JsonSink_ExceptionNoStackTrace_NoStackProperty()
    {
        var ex = new InvalidOperationException("no stack");
        // Not thrown, so StackTrace is null

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var error = doc.RootElement.GetProperty("error");
        Assert.False(error.TryGetProperty("stack", out _));
    }

    //
    // ConsoleSink exception edge cases
    //

    [Fact]
    public void ConsoleSink_ExceptionData_NullValue()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { ["key"] = null },
        };

        var output = CaptureConsole(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex));
        Assert.Contains("key = null", output);
    }

    [Fact]
    public void ConsoleSink_ExceptionData_IntKey()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { [42] = "value" },
        };

        var output = CaptureConsole(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex));
        Assert.Contains("42 = value", output);
    }

    [Fact]
    public void ConsoleSink_DeeplyNestedExceptions()
    {
        var inner = new ArgumentException("root cause");
        var middle = new InvalidOperationException("middle", inner);
        var outer = new Exception("outer", middle);

        var output = CaptureConsole(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], outer));

        Assert.Contains("outer", output);
        Assert.Contains("---> System.InvalidOperationException: middle", output);
        Assert.Contains("---> System.ArgumentException: root cause", output);
        Assert.Contains("--- End of inner exception stack trace ---", output);
    }

    [Fact]
    public void ConsoleSink_ExceptionData_ObjectKey()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { [new Uri("https://example.com")] = "value" },
        };

        var output = CaptureConsole(sink => sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex));
        Assert.Contains("https://example.com/ = value", output);
    }

    [Fact]
    public void JsonSink_ExceptionData_ObjectKey()
    {
        var ex = new InvalidOperationException("boom")
        {
            Data = { [new Uri("https://example.com")] = "value" },
        };

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], ex);
        using var doc = ParseLine(ms);
        var data = doc.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal("value", data.GetProperty("https://example.com/").GetString());
    }

    //
    // AggregateException
    //

    [Fact]
    public void JsonSink_AggregateException_InnerWritten()
    {
        var agg = new AggregateException("batch",
            new InvalidOperationException("first"),
            new ArgumentException("second"));

        var (sink, ms) = MakeJsonSink();
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], agg);
        using var doc = ParseLine(ms);

        var error = doc.RootElement.GetProperty("error");
        Assert.Contains("AggregateException", error.GetProperty("type").GetString());
        Assert.True(error.TryGetProperty("inner", out var inner));
        Assert.Contains("InvalidOperationException", inner.GetProperty("type").GetString());
    }

    [Fact]
    public void ConsoleSink_AggregateException_InnerWritten()
    {
        var agg = new AggregateException("batch",
            new InvalidOperationException("first"),
            new ArgumentException("second"));

        var output = CaptureConsole(sink =>
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "err", [], agg));

        Assert.Contains("AggregateException", output);
        Assert.Contains("InvalidOperationException", output);
        Assert.Contains("first", output);
    }
}
