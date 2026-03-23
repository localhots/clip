using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

/// <summary>
/// Redactor edge cases: regex timeout, null field values, empty patterns.
/// </summary>
public class RedactorEdgeCaseTests
{
    private static readonly JsonFormatConfig NestedConfig = new() { FieldsKey = "fields" };

    private static (Logger logger, MemoryStream ms) MakeLogger(
        Action<LoggerConfig> configure)
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c =>
        {
            c.MinimumLevel(LogLevel.Trace).WriteTo.Json(NestedConfig, ms);
            configure(c);
        });
        return (logger, ms);
    }

    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    //
    // PatternRedactor null field values
    //

    [Fact]
    public void PatternRedactor_NullStringField_Skipped()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Pattern(@"\d+"));
        logger.Info("test", new Field("val", null!));

        var docs = ReadLines(ms);
        Assert.Single(docs);
        // Should not crash — null strings are skipped
    }

    //
    // PatternRedactor empty replacement
    //

    [Fact]
    public void PatternRedactor_EmptyReplacement()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Pattern(@"\d+", ""));
        logger.Info("test", new { Data = "code-123-end" });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("code--end", fields.GetProperty("Data").GetString());
    }

    //
    // PatternRedactor with backreferences
    //

    [Fact]
    public void PatternRedactor_Backreference_Works()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"(\d{3})-(\d{3})-(\d{4})", "***-***-$3"));
        logger.Info("test", new { Phone = "555-123-4567" });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("***-***-4567", fields.GetProperty("Phone").GetString());
    }

    //
    // FieldRedactor on various field types
    //

    [Fact]
    public void FieldRedactor_BoolField_Redacted()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Secret"));
        logger.Info("test", new { Secret = true });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("***", fields.GetProperty("Secret").GetString());
    }

    [Fact]
    public void FieldRedactor_DoubleField_Redacted()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Balance"));
        logger.Info("test", new { Balance = 1234.56 });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("***", fields.GetProperty("Balance").GetString());
    }

    [Fact]
    public void FieldRedactor_LongField_Redacted()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("SSN"));
        logger.Info("test", new { SSN = 123456789L });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("***", fields.GetProperty("SSN").GetString());
    }

    //
    // Multiple redactors chaining
    //

    [Fact]
    public void ChainedRedactors_PatternThenKey()
    {
        var (logger, ms) = MakeLogger(c => c
            .Redact.Pattern(@"@\S+", "@***")
            .Redact.Fields("Password"));

        logger.Info("test", new { Email = "alice@example.com", Password = "secret" });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("alice@***", fields.GetProperty("Email").GetString());
        Assert.Equal("***", fields.GetProperty("Password").GetString());
    }

    //
    // PatternRedactor with no matches in value
    //

    [Fact]
    public void PatternRedactor_NoMatch_SameReference()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Pattern(@"\d{16}"));
        logger.Info("test", new { Name = "alice" });

        var fields = ReadLines(ms)[0].RootElement.GetProperty("fields");
        Assert.Equal("alice", fields.GetProperty("Name").GetString());
    }

    //
    // Regex timeout (catastrophic backtracking)
    //

    [Fact]
    public void PatternRedactor_RegexTimeout_DoesNotCrash()
    {
        // Pattern that can cause catastrophic backtracking
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"(a+)+b"));

        // Input designed to trigger backtracking, but within reason
        logger.Info("test", new { Data = new string('a', 25) + "c" });

        // Should not hang or crash — timeout causes RegexMatchTimeoutException
        // which is caught by the redactor error handling in Logger
        var docs = ReadLines(ms);
        Assert.Single(docs);
    }
}
