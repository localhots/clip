using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using ZLogger;

namespace Clip.Benchmarks;

/// <summary>
/// JSON / structured-output sink benchmarks.
/// Measures: full JSON serialization of timestamp + level + message + fields.
/// Run: dotnet run -c Release --project benchmarks/Clip.Benchmarks -- --filter '*JsonBenchmarks*'
/// </summary>
[Config(typeof(FastConfig))]
[HideColumns("Job", "RatioSD", "Alloc Ratio")]
public class JsonBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_Clip()
    {
        ClipJson.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_ClipZero()
    {
        ClipZeroJson.Info("Request handled", []);
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_Serilog()
    {
        SerilogJson.Information("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_NLog()
    {
        NlogJson.Info("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_ZLogger()
    {
        ZloggerJson.ZLogInformation($"Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_MEL()
    {
        MelJson.LogInformation("Request handled");
    }

    [Benchmark]
    [BenchmarkCategory("Json_NoFields")]
    public void NoFields_MELSrcGen()
    {
        MelSourceGen.LogRequestHandled(MelJson);
    }


    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_Clip()
    {
        ClipJson.Info("Request handled",
            new { Method, Status, Elapsed, RequestId = ReqId, Amount });
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_ClipZero()
    {
        ClipZeroJson.Info("Request handled",
            new Field("Method", Method),
            new Field("Status", Status),
            new Field("Elapsed", Elapsed),
            new Field("RequestId", ReqId),
            new Field("Amount", Amount));
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_Serilog()
    {
        SerilogJson.Information(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_NLog()
    {
        NlogJson.Info(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_ZLogger()
    {
        ZloggerJson.ZLogInformation(
            $"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}");
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_MEL()
    {
        MelJson.LogInformation(
            "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount);
    }

    [Benchmark]
    [BenchmarkCategory("Json_FiveFields")]
    public void FiveFields_MELSrcGen()
    {
        MelSourceGen.LogRequestHandledFields(MelJson, Method, Status, Elapsed, ReqId, Amount);
    }


    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_Clip()
    {
        ClipJson.Error("Connection failed", SampleException, new { Host, Port });
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_ClipZero()
    {
        ClipZeroJson.Error("Connection failed", SampleException,
            new Field("Host", Host),
            new Field("Port", Port));
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_Serilog()
    {
        SerilogJson.Error(SampleException,
            "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_NLog()
    {
        NlogJson.Error(SampleException,
            "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_ZLogger()
    {
        ZloggerJson.ZLogError(SampleException,
            $"Connection failed {Host} {Port}");
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_MEL()
    {
        MelJson.LogError(SampleException,
            "Connection failed {Host} {Port}", Host, Port);
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithException")]
    public void WithException_MELSrcGen()
    {
        MelSourceGen.LogConnectionFailed(MelJson, SampleException, Host, Port);
    }


    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_Clip()
    {
        using (Logger.AddContext(new { RequestId, UserId }))
        {
            ClipJson.Info("Processing", new { Step });
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_ClipZero()
    {
        using (Logger.AddContext(
                   new Field("RequestId", RequestId),
                   new Field("UserId", UserId)))
        {
            ClipZeroJson.Info("Processing", new Field("Step", Step));
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_Serilog()
    {
        using (LogContext.PushProperty("RequestId", RequestId))
        using (LogContext.PushProperty("UserId", UserId))
        {
            SerilogJson.Information("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_NLog()
    {
        using (NLog.ScopeContext.PushProperty("RequestId", RequestId))
        using (NLog.ScopeContext.PushProperty("UserId", UserId))
        {
            NlogJson.Info("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_ZLogger()
    {
        using (ZloggerJson.BeginScope(new KeyValuePair<string, object?>[]
        { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            ZloggerJson.ZLogInformation($"Processing {Step}");
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_MEL()
    {
        using (MelJson.BeginScope(new KeyValuePair<string, object?>[]
        { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            MelJson.LogInformation("Processing {Step}", Step);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Json_WithContext")]
    public void WithContext_MELSrcGen()
    {
        using (MelJson.BeginScope(new KeyValuePair<string, object?>[]
        { new("RequestId", RequestId), new("UserId", UserId) }))
        {
            MelSourceGen.LogProcessing(MelJson, Step);
        }
    }
}
