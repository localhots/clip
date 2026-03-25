using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;

namespace Clip.Benchmarks;

//
// BENCH_MODE env var:
//   fast (default) — InProcess, 2 warmups, 5 × 200 ms iterations
//   full — out-of-process, 3 warmups, 50 × 1000 ms iterations
//   asm — out-of-process + DisassemblyDiagnoser, artifacts in tmp/BenchmarkDotNet.AsmArtifacts/
//

internal sealed class FastConfig : ManualConfig
{
    public FastConfig()
    {
        var mode = (Environment.GetEnvironmentVariable("BENCH_MODE") ?? "fast")
            .ToLowerInvariant();

        ArtifactsPath = Path.Combine("tmp", mode == "asm"
            ? "BenchmarkDotNet.AsmArtifacts"
            : "BenchmarkDotNet.Artifacts");

        switch (mode)
        {
            case "asm": ConfigureAsm(); break;
            case "full": ConfigureFull(); break;
            default: ConfigureFast(); break;
        }

        AddExporter(MarkdownExporter.GitHub);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(CategoriesColumn.Default);
        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);
    }

    //
    // Fast — quick iteration during development
    //

    private void ConfigureFast()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(2)
            .WithIterationCount(5)
            .WithIterationTime(TimeInterval.FromMilliseconds(200)));
    }

    //
    // Full — publication-quality, out-of-process
    //

    private void ConfigureFull()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(50)
            .WithIterationTime(TimeInterval.FromMilliseconds(1000)));
    }

    //
    // Asm — JIT disassembly
    //

    private void ConfigureAsm()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)
            .WithIterationCount(2));
        AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(3)));
    }
}
