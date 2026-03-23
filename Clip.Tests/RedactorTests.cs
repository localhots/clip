using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clip.Redactors;
using Clip.Sinks;

namespace Clip.Tests;

public class RedactorTests
{
    private static readonly JsonFormatConfig NestedConfig = new() { FieldsKey = "fields" };

    private static (Logger logger, MemoryStream ms) MakeLogger(
        Action<LoggerConfig> configure, LogLevel minLevel = LogLevel.Trace)
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c =>
        {
            c.MinimumLevel(minLevel).WriteTo.Json(NestedConfig, ms);
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

    private static JsonElement GetFields(JsonDocument doc)
    {
        return doc.RootElement.GetProperty("fields");
    }

    //
    // FieldRedactor
    //

    [Fact]
    public void FieldRedactor_RedactsMatchingField()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Password"));
        logger.Info("login", new { User = "alice", Password = "s3cret" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("alice", fields.GetProperty("User").GetString());
        Assert.Equal("***", fields.GetProperty("Password").GetString());
    }

    [Fact]
    public void FieldRedactor_CaseInsensitive()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("password"));
        logger.Info("login", new { Password = "s3cret" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Password").GetString());
    }

    [Fact]
    public void FieldRedactor_MultipleKeys()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Password", "Token", "Secret"));
        logger.Info("auth", new { Password = "p", Token = "t", Secret = "s", User = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Password").GetString());
        Assert.Equal("***", fields.GetProperty("Token").GetString());
        Assert.Equal("***", fields.GetProperty("Secret").GetString());
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    [Fact]
    public void FieldRedactor_CustomMask()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.With(new FieldRedactor(["Password"], "[REDACTED]")));
        logger.Info("login", new { Password = "s3cret" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("[REDACTED]", fields.GetProperty("Password").GetString());
    }

    [Fact]
    public void FieldRedactor_NonMatchingFieldsUntouched()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Password"));
        logger.Info("test", new { User = "alice", Count = 42 });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("alice", fields.GetProperty("User").GetString());
        Assert.Equal(42, fields.GetProperty("Count").GetInt32());
    }

    [Fact]
    public void FieldRedactor_RedactsIntField()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("SSN"));
        logger.Info("test", new { SSN = 123456789 });

        var fields = GetFields(ReadLines(ms)[0]);
        // int field replaced with string "***"
        Assert.Equal("***", fields.GetProperty("SSN").GetString());
    }

    //
    // PatternRedactor
    //

    [Fact]
    public void PatternRedactor_RedactsMatchingValue()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "[EMAIL]"));
        logger.Info("contact", new { Email = "alice@example.com" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("[EMAIL]", fields.GetProperty("Email").GetString());
    }

    [Fact]
    public void PatternRedactor_PartialReplacement()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"\d{4}-\d{4}-\d{4}-(\d{4})", "****-****-****-$1"));
        logger.Info("payment", new { Card = "1234-5678-9012-3456" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("****-****-****-3456", fields.GetProperty("Card").GetString());
    }

    [Fact]
    public void PatternRedactor_SkipsNonStringFields()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"\d+"));
        logger.Info("test", new { Count = 42, Name = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal(42, fields.GetProperty("Count").GetInt32());
        Assert.Equal("alice", fields.GetProperty("Name").GetString());
    }

    [Fact]
    public void PatternRedactor_NoMatchLeavesValueUnchanged()
    {
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(@"\d{3}-\d{2}-\d{4}", "[SSN]"));
        logger.Info("test", new { Data = "no match here" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("no match here", fields.GetProperty("Data").GetString());
    }

    [Fact]
    public void PatternRedactor_WithPrecompiledRegex()
    {
        var regex = new Regex(@"Bearer\s+\S+", RegexOptions.Compiled);
        var (logger, ms) = MakeLogger(c =>
            c.Redact.Pattern(regex, "Bearer ***"));
        logger.Info("request", new { Auth = "Bearer eyJhbGciOi..." });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("Bearer ***", fields.GetProperty("Auth").GetString());
    }

    //
    // Multiple Redactors
    //

    [Fact]
    public void MultipleRedactors_RunInOrder()
    {
        var (logger, ms) = MakeLogger(c => c
            .Redact.Fields("Token")
            .Redact.Pattern(@"@\S+", "@***"));
        logger.Info("test", new { Token = "abc", Email = "alice@example.com" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Token").GetString());
        Assert.Equal("alice@***", fields.GetProperty("Email").GetString());
    }

    //
    // Redactor + Enricher
    //

    [Fact]
    public void Redactor_AppliesToEnrichedFields()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("Host", "prod-db-01")
            .Redact.Fields("Host"));
        logger.Info("test");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Host").GetString());
    }

    [Fact]
    public void Redactor_AppliesToContextFields()
    {
        var (logger, ms) = MakeLogger(c => c
            .Redact.Fields("SessionToken"));
        using (Logger.AddContext(new { SessionToken = "secret-token" }))
        {
            logger.Info("test", new { User = "alice" });
        }

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("SessionToken").GetString());
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    //
    // Error handling
    //

    [Fact]
    public void RedactorThatThrows_DoesNotCrash()
    {
        var (logger, ms) = MakeLogger(c => c
            .Redact.With(new BoomRedactor())
            .Redact.Fields("Password"));
        logger.Info("test", new { Password = "secret", User = "alice" });

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    //
    // Zero-alloc tier
    //

    [Fact]
    public void ZeroAllocTier_WithRedactor()
    {
        var (logger, ms) = MakeLogger(c => c.Redact.Fields("Token"));
        logger.Info("test", new Field("Token", "secret"), new Field("User", "alice"));

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Token").GetString());
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    [Fact]
    public void NoRedactors_BehaviorUnchanged()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c.WriteTo.Json(NestedConfig, ms));
        logger.Info("test", new { Password = "visible" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("visible", fields.GetProperty("Password").GetString());
    }

    [Fact]
    public void CustomRedactor_ViaWith()
    {
        var custom = new UpperCaseRedactor();
        var (logger, ms) = MakeLogger(c => c.Redact.With(custom));
        logger.Info("test", new { Name = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("ALICE", fields.GetProperty("Name").GetString());
    }

    //
    // Helpers
    //

    private class BoomRedactor : ILogRedactor
    {
        public void Redact(Span<Field> fields)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private class UpperCaseRedactor : ILogRedactor
    {
        public void Redact(Span<Field> fields)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i].Type != FieldType.String) continue;
                var value = (string?)fields[i].RefValue;
                if (value is null) continue;
                fields[i] = new Field(fields[i].Key, value.ToUpperInvariant());
            }
        }
    }
}
