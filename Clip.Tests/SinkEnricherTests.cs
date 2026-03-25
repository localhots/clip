using Clip.Sinks;

namespace Clip.Tests;

public class SinkEnricherTests
{
    [Fact]
    public void PerSinkEnricher_OnlyAppliestoTargetSink()
    {
        var plain = new ListSink();
        var enriched = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(plain)
            .WriteTo.Enriched(
                e => e.Field("region", "us-east-1"),
                s => s.Sink(enriched)));

        logger.Info("test");

        Assert.Single(plain.Records);
        Assert.DoesNotContain(plain.Records[0].Fields, f => f.Key == "region");

        Assert.Single(enriched.Records);
        Assert.Contains(enriched.Records[0].Fields, f => f.Key == "region");
        Assert.Equal("us-east-1", enriched.Records[0].Fields.First(f => f.Key == "region").RefValue);
    }

    [Fact]
    public void GlobalAndPerSink_EnrichersCombine()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.Field("app", "my-service")
            .WriteTo.Enriched(
                e => e.Field("region", "us-east-1"),
                s => s.Sink(sink)));

        logger.Info("test");

        var fields = sink.Records[0].Fields;
        Assert.Contains(fields, f => f.Key == "app");
        Assert.Contains(fields, f => f.Key == "region");
    }

    [Fact]
    public void PerSinkEnricher_LevelGating()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Enriched(
                e => e.Field("verbose", "data", minLevel: LogLevel.Warning),
                s => s.Sink(sink)));

        logger.Info("info msg");
        logger.Warning("warn msg");

        Assert.Equal(2, sink.Records.Count);
        Assert.DoesNotContain(sink.Records[0].Fields, f => f.Key == "verbose");
        Assert.Contains(sink.Records[1].Fields, f => f.Key == "verbose");
    }

    [Fact]
    public void PerSinkEnricher_OverridesBaseFieldOnDuplicate()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.Field("env", "global-val")
            .WriteTo.Enriched(
                e => e.Field("env", "sink-val"),
                s => s.Sink(sink)));

        logger.Info("test");

        var fields = sink.Records[0].Fields;
        var envFields = fields.Where(f => f.Key == "env").ToArray();
        Assert.Single(envFields);
        Assert.Equal("sink-val", envFields[0].RefValue);
    }

    [Fact]
    public void GlobalRedactors_ApplyToPerSinkEnricherFields()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Redact.Fields("secret")
            .WriteTo.Enriched(
                e => e.Field("secret", "hunter2"),
                s => s.Sink(sink)));

        logger.Info("test");

        var field = sink.Records[0].Fields.First(f => f.Key == "secret");
        Assert.Equal("***", field.RefValue);
    }

    [Fact]
    public void GlobalFilters_ApplyToPerSinkEnricherFields()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Filter.Fields("internal_id")
            .WriteTo.Enriched(
                e => e.Field("internal_id", "abc123"),
                s => s.Sink(sink)));

        logger.Info("test");

        Assert.DoesNotContain(sink.Records[0].Fields, f => f.Key == "internal_id");
    }

    [Fact]
    public void EnrichedWithBackground_Compose()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Enriched(
                e => e.Field("region", "us-east-1"),
                s => s.Background(bg => bg.Sink(sink))));

        logger.Info("test");
        logger.Dispose();

        Assert.Single(sink.Records);
        Assert.Contains(sink.Records[0].Fields, f => f.Key == "region");
    }

    [Fact]
    public void EmptyEnricherConfig_NoWrapping()
    {
        var sink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Enriched(
                e => { },
                s => s.Sink(sink)));

        logger.Info("test", new { Key = "val" });

        Assert.Single(sink.Records);
        Assert.Contains(sink.Records[0].Fields, f => f.Key == "Key");
    }

    [Fact]
    public void CustomEnricher_PerSink()
    {
        var sink = new ListSink();
        var counter = new CountingEnricher();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Enriched(
                e => e.With(counter),
                s => s.Sink(sink)));

        logger.Info("first");
        logger.Info("second");

        Assert.Equal(1, sink.Records[0].Fields.First(f => f.Key == "SeqNo").IntValue);
        Assert.Equal(2, sink.Records[1].Fields.First(f => f.Key == "SeqNo").IntValue);
    }

    [Fact]
    public void MultipleSinks_DifferentEnrichers()
    {
        var gcpSink = new ListSink();
        var grafanaSink = new ListSink();

        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.Field("app", "my-service")
            .WriteTo.Enriched(
                e => e.Field("gcp.project", "my-gcp-project"),
                s => s.Sink(gcpSink))
            .WriteTo.Enriched(
                e => e.Field("grafana.org", "my-org"),
                s => s.Sink(grafanaSink)));

        logger.Info("test");

        // GCP sink: has app + gcp.project, no grafana.org
        Assert.Contains(gcpSink.Records[0].Fields, f => f.Key == "app");
        Assert.Contains(gcpSink.Records[0].Fields, f => f.Key == "gcp.project");
        Assert.DoesNotContain(gcpSink.Records[0].Fields, f => f.Key == "grafana.org");

        // Grafana sink: has app + grafana.org, no gcp.project
        Assert.Contains(grafanaSink.Records[0].Fields, f => f.Key == "app");
        Assert.Contains(grafanaSink.Records[0].Fields, f => f.Key == "grafana.org");
        Assert.DoesNotContain(grafanaSink.Records[0].Fields, f => f.Key == "gcp.project");
    }

    //
    // Helpers
    //

    private class CountingEnricher : ILogEnricher
    {
        private int _counter;

        public void Enrich(List<Field> target)
        {
            target.Add(new Field("SeqNo", Interlocked.Increment(ref _counter)));
        }
    }
}
