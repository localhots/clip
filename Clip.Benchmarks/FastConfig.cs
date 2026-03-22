using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;

namespace Clip.Benchmarks;

//
// Modes (env vars):
//   BENCH_MODE=fast (default) — InProcess, 1 warmup, 3 × 100 ms iterations (~5 min)
//   BENCH_MODE=full — InProcess, 2 warmups, 5 × 250 ms iterations (~10-15 min)
//   BENCH_CONFIG=asm — out-of-process + DisassemblyDiagnoser, artifacts in tmp/BenchmarkDotNet.AsmArtifacts/
//

internal sealed class FastConfig : ManualConfig
{
    public FastConfig()
    {
        var asm = string.Equals(
            Environment.GetEnvironmentVariable("BENCH_CONFIG"),
            "asm", StringComparison.OrdinalIgnoreCase);

        ArtifactsPath = Path.Combine("tmp", asm
            ? "BenchmarkDotNet.AsmArtifacts"
            : "BenchmarkDotNet.Artifacts");

        if (asm)
        {
            AddJob(Job.Default.WithWarmupCount(1).WithIterationCount(2));
            AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(3)));
        }
        else
        {
            var full = string.Equals(
                Environment.GetEnvironmentVariable("BENCH_MODE"),
                "full", StringComparison.OrdinalIgnoreCase);

            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(full
                    ? 2
                    : 1)
                .WithIterationCount(full
                    ? 5
                    : 3)
                .WithIterationTime(TimeInterval.FromMilliseconds(full
                    ? 250
                    : 100)));
        }

        AddExporter(MarkdownExporter.GitHub);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(CategoriesColumn.Default);
        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);
    }
}
