using System.Text;
using System.Text.Json;

namespace Clip.Tests;

public class LevelGatedEnricherTests
{
    private static (Logger logger, MemoryStream ms) MakeLogger(
        Action<LoggerConfig> configure, LogLevel minLevel = LogLevel.Trace)
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c =>
        {
            c.MinimumLevel(minLevel).WriteTo.Json(ms);
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
    public void EnricherFires_AtMinLevel()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("verbose", true, LogLevel.Warning));
        logger.Warning("at threshold");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.True(fields.GetProperty("verbose").GetBoolean());
    }

    [Fact]
    public void EnricherFires_AboveMinLevel()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("verbose", true, LogLevel.Warning));
        logger.Error("above threshold");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.True(fields.GetProperty("verbose").GetBoolean());
    }

    [Fact]
    public void EnricherSkipped_BelowMinLevel()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("verbose", true, LogLevel.Warning));
        logger.Info("below threshold");

        var root = ReadLines(ms)[0].RootElement;
        Assert.False(root.TryGetProperty("fields", out _));
    }

    [Fact]
    public void DefaultMinLevel_AlwaysFires()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("app", "test"));
        logger.Trace("lowest level");

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("test", fields.GetProperty("app").GetString());
    }

    [Fact]
    public void MultipleEnrichers_DifferentMinLevels()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("always", "yes")
            .Enrich.Field("warn-only", "yes", LogLevel.Warning));

        logger.Info("info message");
        logger.Warning("warning message");

        var docs = ReadLines(ms);

        // Info: only "always" present
        var infoFields = GetFields(docs[0]);
        Assert.Equal("yes", infoFields.GetProperty("always").GetString());
        Assert.False(infoFields.TryGetProperty("warn-only", out _));

        // Warning: both present
        var warnFields = GetFields(docs[1]);
        Assert.Equal("yes", warnFields.GetProperty("always").GetString());
        Assert.Equal("yes", warnFields.GetProperty("warn-only").GetString());
    }

    [Fact]
    public void Deduplication_WorksWithPartialEnrichment()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("key", "from-enricher", LogLevel.Warning));
        logger.Warning("test", new { key = "from-callsite" });

        var fields = GetFields(ReadLines(ms)[0]);
        Assert.Equal("from-callsite", fields.GetProperty("key").GetString());
    }

    [Fact]
    public void DynamicEnricher_WithMinLevel()
    {
        var counter = new CountingEnricher();
        var (logger, ms) = MakeLogger(c => c
            .Enrich.With(counter, LogLevel.Warning));

        logger.Info("skipped");
        logger.Warning("counted");
        logger.Error("counted again");

        var docs = ReadLines(ms);

        // Info: no fields at all (enricher skipped, no call-site fields)
        Assert.False(docs[0].RootElement.TryGetProperty("fields", out _));

        // Warning and Error: SeqNo is 1 and 2 (Info didn't increment)
        Assert.Equal(1, GetFields(docs[1]).GetProperty("SeqNo").GetInt32());
        Assert.Equal(2, GetFields(docs[2]).GetProperty("SeqNo").GetInt32());
    }

    [Fact]
    public void ThrowingEnricher_WithMinLevel_DoesNotCrash()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.With(new BoomEnricher(), LogLevel.Warning)
            .Enrich.Field("safe", "yes"));
        logger.Warning("should not crash");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("yes", GetFields(docs[0]).GetProperty("safe").GetString());
    }

    [Fact]
    public void ZeroAllocTier_RespectsMinLevel()
    {
        var (logger, ms) = MakeLogger(c => c
            .Enrich.Field("verbose", true, LogLevel.Warning));

        logger.Info("info", new Field("key", "val"));
        logger.Warning("warn", new Field("key", "val"));

        var docs = ReadLines(ms);

        // Info: no verbose field
        Assert.False(GetFields(docs[0]).TryGetProperty("verbose", out _));
        Assert.Equal("val", GetFields(docs[0]).GetProperty("key").GetString());

        // Warning: verbose field present
        Assert.True(GetFields(docs[1]).GetProperty("verbose").GetBoolean());
        Assert.Equal("val", GetFields(docs[1]).GetProperty("key").GetString());
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
}
