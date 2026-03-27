using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace Clip.Benchmarks;

/// <summary>
/// Pipeline feature benchmarks: enrichers, field filters, redactors.
/// Measures the overhead of each pipeline stage on a console log call with fields.
/// Run: dotnet run -c Release --project Clip.Benchmarks -- --filter '*PipelineBenchmarks*'
/// </summary>
[Config(typeof(FastConfig))]
[HideColumns("Job", "RatioSD", "Alloc Ratio")]
public class PipelineBenchmarks : BenchmarkBase
{
    //
    // Enriched — an enricher adds one constant field ("app"="benchmark") to every call
    //

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Enriched")]
    public void Enriched_Clip()
    {
        ClipEnriched.Info("Request handled",
            new { Method, Status, Elapsed });
    }

    [Benchmark]
    [BenchmarkCategory("Enriched")]
    public void Enriched_ClipZero()
    {
        ClipZeroEnriched.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Elapsed", Elapsed));
    }

    [Benchmark]
    [BenchmarkCategory("Enriched")]
    public void Enriched_Serilog()
    {
        SerilogEnriched.Information(
            "Request handled {Method} {Status} {Elapsed}",
            Method, Status, Elapsed);
    }

    [Benchmark]
    [BenchmarkCategory("Enriched")]
    public void Enriched_NLog()
    {
        NlogEnriched.Info(
            "Request handled {Method} {Status} {Elapsed}",
            Method, Status, Elapsed);
    }

    //
    // FieldFiltered — a filter removes fields named "password"
    //

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FieldFiltered")]
    public void FieldFiltered_Clip()
    {
        ClipFieldFiltered.Info("Request handled",
            new { Method, Status, password = "secret123" });
    }

    [Benchmark]
    [BenchmarkCategory("FieldFiltered")]
    public void FieldFiltered_ClipZero()
    {
        ClipZeroFieldFiltered.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("password", "secret123"));
    }

    //
    // Redacted — a redactor replaces "Token" values with "***"
    //

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Redacted")]
    public void Redacted_Clip()
    {
        ClipRedacted.Info("Request handled",
            new { Method, Status, Token = "bearer-abc123" });
    }

    [Benchmark]
    [BenchmarkCategory("Redacted")]
    public void Redacted_ClipZero()
    {
        ClipZeroRedacted.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Token", "bearer-abc123"));
    }

    //
    // FullPipeline — enricher + filter + redactor all active
    //

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FullPipeline")]
    public void FullPipeline_Clip()
    {
        ClipFullPipeline.Info("Request handled",
            new { Method, Status, Token = "bearer-abc123", password = "secret123" });
    }

    [Benchmark]
    [BenchmarkCategory("FullPipeline")]
    public void FullPipeline_ClipZero()
    {
        ClipZeroFullPipeline.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Token", "bearer-abc123"),
            new Field("password", "secret123"));
    }
}
