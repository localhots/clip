using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class FieldFilterTests
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

    private static JsonElement GetFields(JsonDocument doc) => doc.RootElement.GetProperty("fields");

    //
    // Basic filtering
    //

    [Fact]
    public void FilterField_RemovesMatchingField()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("Password"));
        logger.Info("login", new { User = "alice", Password = "s3cret" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("alice", fields.GetProperty("User").GetString());
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Password"));
    }

    [Fact]
    public void FilterField_CaseInsensitive()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("password"));
        logger.Info("login", new { Password = "s3cret", User = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Password"));
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    [Fact]
    public void FilterFields_MultipleKeys()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("Password", "Token", "Secret"));
        logger.Info("auth", new { Password = "p", Token = "t", Secret = "s", User = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Password"));
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Token"));
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Secret"));
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    [Fact]
    public void FilterField_NonMatchingFieldsUntouched()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("Removed"));
        logger.Info("test", new { Keep1 = "a", Keep2 = 42 });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("a", fields.GetProperty("Keep1").GetString());
        Assert.Equal(42, fields.GetProperty("Keep2").GetInt32());
    }

    //
    // Field sources
    //

    [Fact]
    public void FilterField_AppliesToEnrichedFields()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("noisy", "value")
            .Filter.Fields("noisy"));
        logger.Info("test", new { Keep = "yes" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("noisy"));
        Assert.Equal("yes", fields.GetProperty("Keep").GetString());
    }

    [Fact]
    public void FilterField_AppliesToContextFields()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("rid"));
        using (Logger.AddContext(new { rid = "req-123", keep = "yes" }))
        {
            logger.Info("test");
        }

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("rid"));
        Assert.Equal("yes", fields.GetProperty("keep").GetString());
    }

    //
    // Zero-alloc tier
    //

    [Fact]
    public void FilterField_ZeroAllocTier()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("Removed"));
        logger.Info("test", new Field("Keep", "yes"), new Field("Removed", "gone"));

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Removed"));
        Assert.Equal("yes", fields.GetProperty("Keep").GetString());
    }

    //
    // Interaction with redaction
    //

    [Fact]
    public void FilteredFieldsNeverReachRedactors()
    {
        var spy = new SpyRedactor();
        var (logger, ms) = MakeLogger(c => c
            .Filter.Fields("Filtered")
            .Redact.With(spy));
        logger.Info("test", new { Filtered = "gone", Kept = "here" });

        // The redactor should only see "Kept", not "Filtered"
        Assert.Single(spy.SeenKeys);
        Assert.Contains("Kept", spy.SeenKeys);
        Assert.DoesNotContain("Filtered", spy.SeenKeys);
    }

    [Fact]
    public void MixedFilterAndRedaction()
    {
        var (logger, ms) = MakeLogger(c => c
            .Redact.Fields("Token")
            .Filter.Fields("Secret"));
        logger.Info("auth", new { Token = "abc", Secret = "xyz", User = "alice" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("***", fields.GetProperty("Token").GetString());
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("Secret"));
        Assert.Equal("alice", fields.GetProperty("User").GetString());
    }

    //
    // Edge cases
    //

    [Fact]
    public void FilterAllFields_NoFieldsInOutput()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Fields("A", "B"));
        logger.Info("test", new { A = 1, B = 2 });

        var root = ReadLines(ms)[0].RootElement;
        if (root.TryGetProperty("fields", out var fields))
            Assert.Empty(fields.EnumerateObject());
    }

    [Fact]
    public void NoFilter_FieldsUnchanged()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c.WriteTo.Json(NestedConfig, ms));
        logger.Info("test", new { X = 1, Y = "two" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal(1, fields.GetProperty("X").GetInt32());
        Assert.Equal("two", fields.GetProperty("Y").GetString());
    }

    [Fact]
    public void FilterField_MultipleCallsAccumulate()
    {
        var (logger, ms) = MakeLogger(c => c
            .Filter.Fields("A")
            .Filter.Fields("B"));
        logger.Info("test", new { A = 1, B = 2, C = 3 });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("A"));
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("B"));
        Assert.Equal(3, fields.GetProperty("C").GetInt32());
    }

    //
    // Pattern filtering
    //

    [Fact]
    public void FilterPattern_RemovesMatchingKeys()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Pattern("^_"));
        logger.Info("test", new { _internal = "hidden", _debug = "hidden", visible = "shown" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("_internal"));
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("_debug"));
        Assert.Equal("shown", fields.GetProperty("visible").GetString());
    }

    [Fact]
    public void FilterPattern_NonMatchingKeysUntouched()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.Pattern("^temp_"));
        logger.Info("test", new { Keep = "yes", temp_data = "gone" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("yes", fields.GetProperty("Keep").GetString());
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("temp_data"));
    }

    //
    // Custom filter via With()
    //

    [Fact]
    public void CustomFilter_ViaWith()
    {
        var (logger, ms) = MakeLogger(c => c.Filter.With(new PrefixFilter("_")));
        logger.Info("test", new Field("_internal", "hidden"), new Field("visible", "shown"));

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Throws<KeyNotFoundException>(() => fields.GetProperty("_internal"));
        Assert.Equal("shown", fields.GetProperty("visible").GetString());
    }

    /// <summary>Records field keys seen during redaction.</summary>
    private sealed class SpyRedactor : ILogRedactor
    {
        public List<string> SeenKeys { get; } = [];

        public void Redact(ref Field field)
        {
            SeenKeys.Add(field.Key);
        }
    }

    /// <summary>Filters fields whose key starts with a prefix.</summary>
    private sealed class PrefixFilter(string prefix) : ILogFilter
    {
        public bool ShouldSkip(string key) => key.StartsWith(prefix, StringComparison.Ordinal);
    }
}
