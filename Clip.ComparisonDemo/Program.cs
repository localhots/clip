using System.Reflection;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;
using ZLogger;
using ClipLL = Clip.LogLevel;
using MelLL = Microsoft.Extensions.Logging.LogLevel;

// PascalCase matches benchmark field names and is used in interpolated strings
// ReSharper disable InconsistentNaming
const string Method = "GET";
const int Status = 200;
const double Elapsed = 1.234;
var ReqId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
const decimal Amount = 49.95m;
const string Host = "db.local";
const int Port = 5432;
const string Step = "auth";
// ReSharper restore InconsistentNaming

Exception exception;
try
{
    throw new InvalidOperationException("simulated database error");
}
catch (Exception ex)
{
    exception = ex;
}


var asm = Assembly.GetExecutingAssembly();
var hierarchy = (Hierarchy)LogManager.GetRepository(asm);
hierarchy.Root.Level = log4net.Core.Level.Debug;

var l4NConsoleSw = new StringWriter();
SetupLog4Net(hierarchy, "demo-console", new Log4NetStreamAppender(l4NConsoleSw)
{
    Name = "l4n-demo-console",
    Layout = new log4net.Layout.PatternLayout(
        "%date{yyyy-MM-dd HH:mm:ss.fff} %-5level %message%newline%exception"),
});

var l4NJsonSw = new StringWriter();
SetupLog4Net(hierarchy, "demo-json", new Log4NetStreamAppender(l4NJsonSw)
{
    Name = "l4n-demo-json",
    Layout = new log4net.Layout.PatternLayout(
        "{\"ts\":\"%utcdate{ISO8601}\",\"level\":\"%level\",\"msg\":\"%message\"}%newline%exception"),
});

hierarchy.Configured = true;
var l4NConsole = LogManager.GetLogger(asm, "demo-console");
var l4NJson = LogManager.GetLogger(asm, "demo-json");


Emit("NoFields", "Clip",
    """
    clip.Info("Request handled");

    clipZero.Info("Request handled");
    """,
    CaptureClip(false, l => l.Info("Request handled")),
    CaptureClip(true, l => l.Info("Request handled")));

Emit("NoFields", "Serilog",
    """serilog.Information("Request handled");""",
    CaptureSerilog(false, l => l.Information("Request handled")),
    CaptureSerilog(true, l => l.Information("Request handled")));

Emit("NoFields", "NLog",
    """nlog.Info("Request handled");""",
    CaptureNLog(false, l => l.Info("Request handled")),
    CaptureNLog(true, l => l.Info("Request handled")));

Emit("NoFields", "MEL",
    """mel.LogInformation("Request handled");""",
    CaptureMel(false, l => l.LogInformation("Request handled")),
    CaptureMel(true, l => l.LogInformation("Request handled")));

Emit("NoFields", "ZLogger",
    """zlogger.ZLogInformation($"Request handled");""",
    CaptureZLogger(false, l => l.ZLogInformation($"Request handled")),
    CaptureZLogger(true, l => l.ZLogInformation($"Request handled")));

Emit("NoFields", "log4net",
    """log4net.Info("Request handled");""",
    CaptureLog4Net(false, l => l.Info("Request handled")),
    CaptureLog4Net(true, l => l.Info("Request handled")));

Emit("NoFields", "ZeroLog",
    """zerolog.Info("Request handled");""",
    CaptureZeroLog(l => l.Info("Request handled")));


Emit("FiveFields", "Clip",
    """
    clip.Info("Request handled",
        new { Method, Status, Elapsed, RequestId = ReqId, Amount });

    clipZero.Info("Request handled",
        new Field("Method", Method),
        new Field("Status", Status),
        new Field("Elapsed", Elapsed),
        new Field("RequestId", ReqId),
        new Field("Amount", Amount));
    """,
    CaptureClip(false, l =>
        l.Info("Request handled",
            new { Method, Status, Elapsed, RequestId = ReqId, Amount })),
    CaptureClip(true, l =>
        l.Info("Request handled",
            new { Method, Status, Elapsed, RequestId = ReqId, Amount })));

Emit("FiveFields", "Serilog",
    """serilog.Information("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);""",
    CaptureSerilog(false, l =>
        l.Information("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)),
    CaptureSerilog(true, l =>
        l.Information("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)));

Emit("FiveFields", "NLog",
    """nlog.Info("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);""",
    CaptureNLog(false, l =>
        l.Info("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)),
    CaptureNLog(true, l =>
        l.Info("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)));

Emit("FiveFields", "MEL",
    """mel.LogInformation("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);""",
    CaptureMel(false, l =>
        l.LogInformation("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)),
    CaptureMel(true, l =>
        l.LogInformation("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
            Method, Status, Elapsed, ReqId, Amount)));

Emit("FiveFields", "ZLogger",
    """zlogger.ZLogInformation($"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}");""",
    CaptureZLogger(false, l =>
        l.ZLogInformation($"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}")),
    CaptureZLogger(true, l =>
        l.ZLogInformation($"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}")));

Emit("FiveFields", "log4net",
    """log4net.InfoFormat("Request handled {0} {1} {2} {3} {4}", Method, Status, Elapsed, ReqId, Amount);""",
    CaptureLog4Net(false, l =>
        l.InfoFormat("Request handled {0} {1} {2} {3} {4}",
            Method, Status, Elapsed, ReqId, Amount)),
    CaptureLog4Net(true, l =>
        l.InfoFormat("Request handled {0} {1} {2} {3} {4}",
            Method, Status, Elapsed, ReqId, Amount)));

Emit("FiveFields", "ZeroLog",
    """
    zerolog.Info()
        .Append("Request handled")
        .AppendKeyValue("Method", Method)
        .AppendKeyValue("Status", Status)
        .AppendKeyValue("Elapsed", Elapsed)
        .AppendKeyValue("RequestId", ReqId)
        .AppendKeyValue("Amount", Amount)
        .Log();
    """,
    CaptureZeroLog(l => l.Info()
        .Append("Request handled")
        .AppendKeyValue("Method", Method)
        .AppendKeyValue("Status", Status)
        .AppendKeyValue("Elapsed", Elapsed)
        .AppendKeyValue("RequestId", ReqId)
        .AppendKeyValue("Amount", Amount)
        .Log()));

// log4net and ZeroLog excluded — no scoped context API.

Emit("WithContext", "Clip",
    """
    using (clip.AddContext(new { RequestId = "abc-123", UserId = 42 }))
        clip.Info("Processing", new { Step = "auth" });

    using (clipZero.AddContext(
        new Field("RequestId", "abc-123"),
        new Field("UserId", 42)))
        clipZero.Info("Processing", new Field("Step", "auth"));
    """,
    CaptureClip(false, l =>
    {
        using (Clip.Logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
        {
            l.Info("Processing", new { Step = "auth" });
        }
    }),
    CaptureClip(true, l =>
    {
        using (Clip.Logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
        {
            l.Info("Processing", new { Step = "auth" });
        }
    }));

Emit("WithContext", "Serilog",
    """
    using (LogContext.PushProperty("RequestId", "abc-123"))
    using (LogContext.PushProperty("UserId", 42))
        serilog.Information("Processing {Step}", "auth");
    """,
    CaptureSerilog(false, l =>
    {
        using (LogContext.PushProperty("RequestId", "abc-123"))
        using (LogContext.PushProperty("UserId", 42))
        {
            l.Information("Processing {Step}", "auth");
        }
    }),
    CaptureSerilog(true, l =>
    {
        using (LogContext.PushProperty("RequestId", "abc-123"))
        using (LogContext.PushProperty("UserId", 42))
        {
            l.Information("Processing {Step}", "auth");
        }
    }));

Emit("WithContext", "NLog",
    """
    using (ScopeContext.PushProperty("RequestId", "abc-123"))
    using (ScopeContext.PushProperty("UserId", 42))
        nlog.Info("Processing {Step}", "auth");
    """,
    CaptureNLog(false, l =>
    {
        using (NLog.ScopeContext.PushProperty("RequestId", "abc-123"))
        using (NLog.ScopeContext.PushProperty("UserId", 42))
        {
            l.Info("Processing {Step}", "auth");
        }
    }),
    CaptureNLog(true, l =>
    {
        using (NLog.ScopeContext.PushProperty("RequestId", "abc-123"))
        using (NLog.ScopeContext.PushProperty("UserId", 42))
        {
            l.Info("Processing {Step}", "auth");
        }
    }));

Emit("WithContext", "MEL",
    """
    using (mel.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        mel.LogInformation("Processing {Step}", "auth");
    """,
    CaptureMel(false, l =>
    {
        using (l.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        {
            l.LogInformation("Processing {Step}", "auth");
        }
    }),
    CaptureMel(true, l =>
    {
        using (l.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        {
            l.LogInformation("Processing {Step}", "auth");
        }
    }));

Emit("WithContext", "ZLogger",
    """
    using (zlogger.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        zlogger.ZLogInformation($"Processing {Step}");
    """,
    CaptureZLogger(false, l =>
    {
        using (l.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        {
            l.ZLogInformation($"Processing {Step}");
        }
    }),
    CaptureZLogger(true, l =>
    {
        using (l.BeginScope(new Dictionary<string, object?>
        { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
        {
            l.ZLogInformation($"Processing {Step}");
        }
    }));


Emit("WithException", "Clip",
    """
    clip.Error("Connection failed", exception,
        new { Host = "db.local", Port = 5432 });

    clipZero.Error("Connection failed", exception,
        new Field("Host", "db.local"),
        new Field("Port", 5432));
    """,
    CaptureClip(false, l =>
        l.Error("Connection failed", exception,
            new { Host = "db.local", Port = 5432 })),
    CaptureClip(true, l =>
        l.Error("Connection failed", exception,
            new { Host = "db.local", Port = 5432 })));

Emit("WithException", "Serilog",
    """serilog.Error(exception, "Connection failed {Host} {Port}", "db.local", 5432);""",
    CaptureSerilog(false, l =>
        l.Error(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)),
    CaptureSerilog(true, l =>
        l.Error(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)));

Emit("WithException", "NLog",
    """nlog.Error(exception, "Connection failed {Host} {Port}", "db.local", 5432);""",
    CaptureNLog(false, l =>
        l.Error(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)),
    CaptureNLog(true, l =>
        l.Error(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)));

Emit("WithException", "MEL",
    """mel.LogError(exception, "Connection failed {Host} {Port}", "db.local", 5432);""",
    CaptureMel(false, l =>
        l.LogError(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)),
    CaptureMel(true, l =>
        l.LogError(exception,
            "Connection failed {Host} {Port}", "db.local", 5432)));

Emit("WithException", "ZLogger",
    """zlogger.ZLogError(exception, $"Connection failed {Host} {Port}");""",
    CaptureZLogger(false, l =>
        l.ZLogError(exception,
            $"Connection failed {Host} {Port}")),
    CaptureZLogger(true, l =>
        l.ZLogError(exception,
            $"Connection failed {Host} {Port}")));

Emit("WithException", "log4net",
    """log4net.Error("Connection failed", exception);""",
    CaptureLog4Net(false, l =>
        l.Error("Connection failed", exception)),
    CaptureLog4Net(true, l =>
        l.Error("Connection failed", exception)));

Emit("WithException", "ZeroLog",
    """
    zerolog.Error()
        .Append("Connection failed")
        .AppendKeyValue("Host", "db.local")
        .AppendKeyValue("Port", 5432)
        .WithException(exception)
        .Log();
    """,
    CaptureZeroLog(l => l.Error()
        .Append("Connection failed")
        .AppendKeyValue("Host", "db.local")
        .AppendKeyValue("Port", 5432)
        .WithException(exception)
        .Log()));

return;


void Emit(string scenario, string logger, string code,
    string consoleOutput, string? jsonOutput = null)
{
    var o = Console.Out;
    o.WriteLine($"@@@ scenario={scenario} logger={logger} @@@");
    o.WriteLine("--- code ---");
    o.Write(code.TrimEnd());
    o.WriteLine();
    o.WriteLine("--- console ---");
    o.Write(consoleOutput.TrimEnd());
    o.WriteLine();
    if (jsonOutput is not null)
    {
        o.WriteLine("--- json ---");
        o.Write(jsonOutput.TrimEnd());
        o.WriteLine();
    }

    o.WriteLine("@@@ end @@@");
    o.WriteLine();
}


string CaptureClip(bool json, Action<Clip.Logger> action)
{
    var ms = new MemoryStream();
    var logger = json
        ? Clip.Logger.Create(c => c.MinimumLevel(ClipLL.Info).WriteTo.Json(ms))
        : Clip.Logger.Create(c => c.MinimumLevel(ClipLL.Info).WriteTo.Console(ms, false));
    action(logger);
    logger.Dispose();
    return Encoding.UTF8.GetString(ms.ToArray());
}

string CaptureSerilog(bool json, Action<Serilog.ILogger> action)
{
    var sw = new StringWriter();
    ITextFormatter fmt = json
        ? new JsonFormatter()
        : new MessageTemplateTextFormatter(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u4} {Message:lj} {Properties:j}{NewLine}{Exception}");
    var logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Sink(new SerilogStreamSink(fmt, sw))
        .CreateLogger();
    action(logger);
    logger.Dispose();
    return sw.ToString();
}

string CaptureNLog(bool json, Action<NLog.Logger> action)
{
    var sw = new StringWriter();
    Target target;
    if (json)
        target = new NLogStreamTarget(sw)
        {
            Name = "json",
            Layout = new JsonLayout
            {
                Attributes =
                {
                    new JsonAttribute("ts", "${longdate}"),
                    new JsonAttribute("level", "${level}"),
                    new JsonAttribute("msg", "${message}"),
                    new JsonAttribute("exception", "${exception:format=tostring}")
                        { IncludeEmptyValue = false },
                },
                IncludeEventProperties = true,
                IncludeScopeProperties = true,
            },
        };
    else
        target = new NLogStreamTarget(sw)
        {
            Name = "console",
            Layout =
                "${date:format=yyyy-MM-dd HH\\:mm\\:ss.fff} ${level:uppercase=true:padding=-4:truncate=4} ${message} ${all-event-properties:separator= :includeScopeProperties=true}${onexception:${newline}${exception:format=tostring}}",
        };
    using var factory = CreateNLogFactory(target, NLog.LogLevel.Info);
    var logger = factory.GetLogger("comparison");
    action(logger);
    return sw.ToString();
}

string CaptureMel(bool json, Action<Microsoft.Extensions.Logging.ILogger> action)
{
    var oldOut = Console.Out;
    var oldErr = Console.Error;
    var sw = new StringWriter();
    Console.SetOut(sw);
    Console.SetError(sw);
    try
    {
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            if (json)
                b.AddJsonConsole(o =>
                {
                    o.IncludeScopes = true;
                    o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffZ";
                });
            else
                b.AddSimpleConsole(o =>
                {
                    o.SingleLine = false;
                    o.IncludeScopes = true;
                    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                });
        });
        var logger = factory.CreateLogger("Demo");
        action(logger);
        factory.Dispose(); // Flush background queue before restoring Console
    }
    finally
    {
        Console.SetOut(oldOut);
        Console.SetError(oldErr);
    }

    return sw.ToString();
}

string CaptureZLogger(bool json, Action<Microsoft.Extensions.Logging.ILogger> action)
{
    var ms = new MemoryStream();
    var factory = LoggerFactory.Create(b =>
    {
        b.SetMinimumLevel(MelLL.Information);
        if (json)
            b.AddZLoggerStream(ms, o =>
            {
                o.IncludeScopes = true;
                o.UseJsonFormatter();
            });
        else
            b.AddZLoggerStream(ms, o =>
            {
                o.IncludeScopes = true;
                o.UsePlainTextFormatter(formatter =>
                {
                    formatter.SetPrefixFormatter(
                        $"{0:yyyy-MM-dd HH:mm:ss.fff} {1:short} ",
                        (in ZLogger.MessageTemplate t, in LogInfo i) => t.Format(i.Timestamp, i.LogLevel));
                });
            });
    });
    var logger = factory.CreateLogger("comparison");
    action(logger);
    factory.Dispose();
    return Encoding.UTF8.GetString(ms.ToArray());
}

string CaptureLog4Net(bool json, Action<ILog> action)
{
    var sw = json
        ? l4NJsonSw
        : l4NConsoleSw;
    var log = json
        ? l4NJson
        : l4NConsole;
    sw.GetStringBuilder().Clear();
    action(log);
    return sw.ToString();
}


string CaptureZeroLog(Action<ZeroLog.Log> action)
{
    var sw = new StringWriter();
    var formatter = new ZeroLog.Formatting.DefaultFormatter(
        new ZeroLog.Formatting.PatternWriter(
            "%{date:yyyy-MM-dd HH:mm:ss.fff} %{level:pad} %message"));
    _ = ZeroLog.LogManager.Initialize(new ZeroLog.Configuration.ZeroLogConfiguration
    {
        AppendingStrategy = ZeroLog.Configuration.AppendingStrategy.Synchronous,
        RootLogger =
        {
            Level = ZeroLog.LogLevel.Info,
            Appenders =
            {
                new ZeroLog.Appenders.TextWriterAppender(sw)
                    { Formatter = formatter },
            },
        },
    });
    var log = ZeroLog.LogManager.GetLogger("comparison");
    action(log);
    ZeroLog.LogManager.Shutdown();
    return sw.ToString();
}

static NLog.LogFactory CreateNLogFactory(Target target, NLog.LogLevel minLevel)
{
    var config = new LoggingConfiguration();
    config.AddTarget(target);
    config.AddRule(minLevel, NLog.LogLevel.Fatal, target);
    var factory = new NLog.LogFactory { Configuration = config };
    return factory;
}

static void SetupLog4Net(Hierarchy hierarchy, string name, AppenderSkeleton appender)
{
    appender.ActivateOptions();
    var logger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger(name);
    logger.Level = log4net.Core.Level.Info;
    logger.Additivity = false;
    logger.AddAppender(appender);
}


internal sealed class SerilogStreamSink(ITextFormatter formatter, TextWriter writer) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        formatter.Format(logEvent, writer);
    }
}

[Target("ClipComparisonStream")]
internal sealed class NLogStreamTarget(TextWriter writer) : TargetWithLayout
{
    protected override void Write(NLog.LogEventInfo logEvent)
    {
        writer.Write(RenderLogEvent(Layout, logEvent));
    }
}

internal sealed class Log4NetStreamAppender(TextWriter writer) : AppenderSkeleton
{
    protected override void Append(log4net.Core.LoggingEvent loggingEvent)
    {
        writer.Write(RenderLoggingEvent(loggingEvent));
    }
}
