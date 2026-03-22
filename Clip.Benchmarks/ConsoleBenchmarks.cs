using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using ZLogger;

namespace Clip.Benchmarks;

/// <summary>
/// Console / text-format sink benchmarks.
/// Measures: timestamp + level + message text formatting cost.
/// Run: dotnet run -c Release --project benchmarks/Clip.Benchmarks -- --filter '*ConsoleBenchmarks*'
/// </summary>
[Config(typeof(FastConfig))]
[HideColumns("Job", "RatioSD", "Alloc Ratio")]
public class ConsoleBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_Clip()
    {
        ClipConsole.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_ClipZero()
    {
        ClipZeroConsole.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_Serilog()
    {
        SerilogConsole.Information("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_NLog()
    {
        NlogConsole.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_MEL()
    {
        MelConsole.LogInformation("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_ZLogger()
    {
        ZloggerConsole.ZLogInformation($"Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_Log4Net()
    {
        Log4NetConsole.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_MELSrcGen()
    {
        MelSourceGen.LogRequestHandled(MelConsole);
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_ClipMEL()
    {
        ClipMelConsole.LogInformation("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Console_NoFields")]
    public void NoFields_ZeroLog()
    {
        ZeroLogConsole.Info("Request handled");
    }

    // Clip: compiled delegate, no boxing for value-union types
    // ClipZero: Field structs, zero-alloc for value-union; Guid/decimal boxed but Utf8Formatter
    // Serilog/NLog/MEL: all value types boxed into object[]
    // MELSrcGen: source-generated, no boxing, no template parse
    // ZLogger: interpolated-string handler, no boxing
    // log4net: InfoFormat args boxed

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_Clip()
    {
        ClipConsole.Info("Request handled",
            new { Method, Status, Elapsed, RequestId = ReqId, Amount });
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_ClipZero()
    {
        ClipZeroConsole.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Elapsed", Elapsed),
            new Field("RequestId", ReqId),
            new Field("Amount", Amount));
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_Serilog()
    {
        SerilogConsole.Information(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_NLog()
    {
        NlogConsole.Info(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_MEL()
    {
        MelConsole.LogInformation(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_ZLogger()
    {
        ZloggerConsole.ZLogInformation(
            $"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}");
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_Log4Net()
    {
        Log4NetConsole.InfoFormat(
            "Request handled {0} {1} {2} {3} {4}", Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_MELSrcGen()
    {
        MelSourceGen.LogRequestHandledFields(MelConsole, Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_ClipMEL()
    {
        ClipMelConsole.LogInformation(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Console_FiveFields")]
    public void FiveFields_ZeroLog()
    {
        ZeroLogConsole.Info()
            .Append("Request handled")
            .AppendKeyValue("Method", Method)
            .AppendKeyValue("Status", Status)
            .AppendKeyValue("Elapsed", Elapsed)
            .AppendKeyValue("RequestId", ReqId)
            .AppendKeyValue("Amount", Amount)
            .Log();
    }

    // Uses a pre-thrown exception; throw/catch cost excluded.

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_Clip()
    {
        ClipConsole.Error("Connection failed", SampleException, new { Host, Port });
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_ClipZero()
    {
        ClipZeroConsole.Error("Connection failed", SampleException,
            new Field("Host", Host),
            new Field("Port", Port));
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_Serilog()
    {
        SerilogConsole.Error(SampleException, "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_NLog()
    {
        NlogConsole.Error(SampleException, "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_MEL()
    {
        MelConsole.LogError(SampleException, "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_ZLogger()
    {
        ZloggerConsole.ZLogError(SampleException, $"Connection failed {Host} {Port}");
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_Log4Net()
    {
        Log4NetConsole.Error("Connection failed", SampleException);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_MELSrcGen()
    {
        MelSourceGen.LogConnectionFailed(MelConsole, SampleException, Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_ClipMEL()
    {
        ClipMelConsole.LogError(SampleException,
            "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithException")]
    public void WithException_ZeroLog()
    {
        ZeroLogConsole.Error()
            .Append("Connection failed")
            .AppendKeyValue("Host", Host)
            .AppendKeyValue("Port", Port)
            .WithException(SampleException)
            .Log();
    }

    // Push 2 context fields, log with 1 call-site field.
    // log4net excluded: no scoped context API.

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_Clip()
    {
        using (Logger.AddContext(new { RequestId, UserId }))
        {
            ClipConsole.Info("Processing", new { Step });
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_ClipZero()
    {
        using (Logger.AddContext(
                   new Field("RequestId", RequestId),
                   new Field("UserId", UserId)))
        {
            ClipZeroConsole.Info("Processing", new Field("Step", Step));
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_Serilog()
    {
        using (LogContext.PushProperty("RequestId", RequestId))
        using (LogContext.PushProperty("UserId", UserId))
        {
            SerilogConsole.Information("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_NLog()
    {
        using (NLog.ScopeContext.PushProperty("RequestId", RequestId))
        using (NLog.ScopeContext.PushProperty("UserId", UserId))
        {
            NlogConsole.Info("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_MEL()
    {
        using (MelConsole.BeginScope(new KeyValuePair<string, object?>[]
                   { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            MelConsole.LogInformation("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_ZLogger()
    {
        using (ZloggerConsole.BeginScope(new KeyValuePair<string, object?>[]
                   { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            ZloggerConsole.ZLogInformation($"Processing {Step}");
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_MELSrcGen()
    {
        using (MelConsole.BeginScope(new KeyValuePair<string, object?>[]
                   { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            MelSourceGen.LogProcessing(MelConsole, Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Console_WithContext")]
    public void WithContext_ClipMEL()
    {
        using (ClipMelConsole.BeginScope(new KeyValuePair<string, object?>[]
                   { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            ClipMelConsole.LogInformation("Processing {Step}", Step);
        }
    }
}
