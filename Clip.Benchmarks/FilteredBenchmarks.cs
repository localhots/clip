using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Clip.Benchmarks;

/// <summary>
/// Debug call at Info minimum level — the message never reaches any sink.
/// Tests pure level-check short-circuit cost.
/// Run: dotnet run -c Release --project benchmarks/Clip.Benchmarks -- --filter '*FilteredBenchmarks*'
/// </summary>
[Config(typeof(FastConfig))]
[HideColumns("Job", "RatioSD", "Alloc Ratio")]
public class FilteredBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Filtered")]
    public void Filtered_Clip()
    {
        ClipFiltered.Debug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_ClipZero()
    {
        ClipZeroFiltered.Debug("This is filtered out", []);
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_Serilog()
    {
        SerilogFiltered.Debug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_NLog()
    {
        NlogFiltered.Debug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_MEL()
    {
        MelFiltered.LogDebug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_ZLogger()
    {
        ZloggerFiltered.ZLogDebug($"This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_Log4Net()
    {
        Log4NetFiltered.Debug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_MELSrcGen()
    {
        MelSourceGen.LogFiltered(MelFiltered);
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_ClipMEL()
    {
        ClipMelFiltered.LogDebug("This is filtered out");
    }

    [Benchmark]
    [BenchmarkCategory("Filtered")]
    public void Filtered_ZeroLog()
    {
        ZeroLogFiltered.Debug("This is filtered out");
    }
}
