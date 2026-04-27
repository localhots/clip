using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class EnricherTests
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

    [Fact]
    public void SingleEnricher_AddsField()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Region", "us-east-1"));
        logger.Info("test");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("us-east-1", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void MultipleEnrichers_AllFieldsPresent()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("Region", "us-east-1")
            .Enrich.Field("Env", "prod"));
        logger.Info("test");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("us-east-1", fields.GetProperty("Region").GetString());
        Assert.Equal("prod", fields.GetProperty("Env").GetString());
    }

    [Fact]
    public void CallSiteFields_OverrideEnricherFields()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Region", "us-east-1"));
        logger.Info("test", new { Region = "eu-west-1" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("eu-west-1", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void ContextFields_OverrideEnricherFields()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Region", "us-east-1"));
        using (Logger.AddContext(new { Region = "from-context" }))
        {
            logger.Info("test");
        }

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("from-context", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void CallSite_OverridesContext_OverridesEnricher()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Tier", "enricher"));
        using (Logger.AddContext(new { Tier = "context" }))
        {
            logger.Info("test", new { Tier = "call-site" });
        }

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("call-site", fields.GetProperty("Tier").GetString());
    }

    [Fact]
    public void EnricherThatThrows_DoesNotCrash_OtherEnrichersStillRun()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.With(new BoomEnricher())
            .Enrich.Field("Region", "us-east-1"));
        logger.Info("still works");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("still works", docs[0].RootElement.GetProperty("msg").GetString());
        var fields = GetFields(docs[0]);
        Assert.Equal("us-east-1", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void ZeroAllocTier_WithEnrichers()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Region", "us-east-1"));
        logger.Info("test", new Field("Key", "value"));

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("value", fields.GetProperty("Key").GetString());
        Assert.Equal("us-east-1", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void ZeroAllocTier_CallSiteOverridesEnricher()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Region", "us-east-1"));
        logger.Info("test", new Field("Region", "override"));

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("override", fields.GetProperty("Region").GetString());
    }

    [Fact]
    public void NoEnrichers_BehaviorUnchanged()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c.WriteTo.Json(NestedConfig, ms));
        logger.Info("test", new { Key = "val" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("val", fields.GetProperty("Key").GetString());
    }

    [Fact]
    public void DynamicEnricher_PerCallValue()
    {
        var counter = new CountingEnricher();
        var (logger, ms) = MakeLogger(c => c.Enrich.With(counter));
        logger.Info("first");
        logger.Info("second");

        var docs = ReadLines(ms);
        Assert.Equal(1, GetFields(docs[0]).GetProperty("SeqNo").GetInt32());
        Assert.Equal(2, GetFields(docs[1]).GetProperty("SeqNo").GetInt32());
    }

    [Fact]
    public void IntEnricher_AddsField()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Version", 3));
        logger.Info("test");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal(3, fields.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void BoolEnricher_AddsField()
    {
        var (logger, ms) = MakeLogger(c => c.Enrich.Field("Debug", true));
        logger.Info("test");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.True(fields.GetProperty("Debug").GetBoolean());
    }

    //
    // Helpers
    //

    private class BoomEnricher : ILogEnricher
    {
        public void Enrich(List<Field> target)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private class CountingEnricher : ILogEnricher
    {
        private int _counter;

        public void Enrich(List<Field> target)
        {
            target.Add(new Field("SeqNo", Interlocked.Increment(ref _counter)));
        }
    }

    //
    // Typed Enrich.Field overloads — verifies each public typed shortcut wires through correctly
    //

    [Fact]
    public void EnrichField_TypedOverloads_AllEmitCorrectFieldType()
    {
        var guid = Guid.NewGuid();
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("S", "str")
            .Enrich.Field("I", 1)
            .Enrich.Field("L", 2L)
            .Enrich.Field("B", true)
            .Enrich.Field("D", 3.14)
            .Enrich.Field("M", 9.99m)
            .Enrich.Field("G", guid));
        logger.Info("hello");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("str", fields.GetProperty("S").GetString());
        Assert.Equal(1, fields.GetProperty("I").GetInt32());
        Assert.Equal(2L, fields.GetProperty("L").GetInt64());
        Assert.True(fields.GetProperty("B").GetBoolean());
        Assert.Equal(3.14, fields.GetProperty("D").GetDouble());
        Assert.Equal(9.99m, fields.GetProperty("M").GetDecimal());
        Assert.Equal(guid.ToString(), fields.GetProperty("G").GetString());
    }
}
