using BenchmarkDotNet.Running;
using Clip.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(LoggerBenchmarks).Assembly).Run(args);
