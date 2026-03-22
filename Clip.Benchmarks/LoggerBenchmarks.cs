using BenchmarkDotNet.Attributes;

namespace Clip.Benchmarks;

/// <summary>
/// Measures the two logging tiers against each other.
///
/// Run with: dotnet run -c Release --project benchmarks/Clip.Benchmarks
/// </summary>
[Config(typeof(FastConfig))]
[HideColumns("Job", "RatioSD", "Alloc Ratio")]
public class LoggerBenchmarks
{
    private const string Method = "GET";
    private const int Status = 200;
    private const double Elapsed = 1.234;
    private static readonly Guid ReqId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
    private const decimal Amount = 49.95m;

    private Logger _jsonLogger = null!;
    private Logger _nullLogger = null!;
    private Logger _filteredLogger = null!;

    [GlobalSetup]
    public void Setup()
    {
        // NullStream — discards output, so we measure only logger overhead, not I/O
        _jsonLogger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(Stream.Null));

        _nullLogger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(Stream.Null));

        _filteredLogger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Info)
            .WriteTo.Json(Stream.Null));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jsonLogger.Dispose();
        _nullLogger.Dispose();
        _filteredLogger.Dispose();
    }


    /// <summary>No fields, no context — exercises the fast path.</summary>
    [Benchmark(Baseline = true)]
    public void Clip_NoFields()
    {
        _jsonLogger.Info("Request handled");
    }

    /// <summary>Anonymous object with 5 diverse typed fields.</summary>
    [Benchmark]
    public void Clip_FiveFields()
    {
        _jsonLogger.Info("Request handled",
            new { Method, Status, Elapsed, RequestId = ReqId, Amount });
    }


    /// <summary>params ReadOnlySpan&lt;Field&gt; — no fields, no context.</summary>
    [Benchmark]
    public void ClipZero_NoFields()
    {
        _jsonLogger.Info("Request handled", []);
    }

    /// <summary>params ReadOnlySpan&lt;Field&gt; — 5 diverse typed fields, stack-allocated.</summary>
    [Benchmark]
    public void ClipZero_FiveFields()
    {
        _jsonLogger.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Elapsed", Elapsed),
            new Field("RequestId", ReqId),
            new Field("Amount", Amount));
    }


    /// <summary>Log with 2 active context fields.</summary>
    [Benchmark]
    public void Clip_WithContext()
    {
        using (Logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
        {
            _jsonLogger.Info("Processing", new { Step = "auth" });
        }
    }

    /// <summary>Zero-alloc tier with active context — zero-alloc AddContext.</summary>
    [Benchmark]
    public void ClipZero_WithContext()
    {
        using (Logger.AddContext(
                   new Field("RequestId", "abc-123"),
                   new Field("UserId", 42)))
        {
            _jsonLogger.Info("Processing", new Field("Step", "auth"));
        }
    }


    /// <summary>Debug call filtered out at the Info level — essentially free.</summary>
    [Benchmark]
    public void Clip_Filtered()
    {
        _filteredLogger.Debug("This is filtered");
    }
}
