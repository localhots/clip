using System.Reflection;
using BenchmarkDotNet.Attributes;
using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;
using ZLogger;
using Clip.Extensions.Logging;
using ClipLL = Clip.LogLevel;
using MelLL = Microsoft.Extensions.Logging.LogLevel;

namespace Clip.Benchmarks;

//
// Serilog: format via ITextFormatter, write to TextWriter
//

internal sealed class SerilogStreamSink(ITextFormatter formatter, TextWriter writer) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        formatter.Format(logEvent, writer);
    }
}

//
// NLog: render Layout, write to TextWriter
//

[Target("ClipBenchStream")]
internal sealed class NLogStreamTarget(TextWriter writer) : TargetWithLayout
{
    protected override void Write(NLog.LogEventInfo logEvent)
    {
        writer.Write(RenderLogEvent(Layout, logEvent));
    }
}

//
// log4net: render layout event, write to TextWriter
//

internal sealed class Log4NetStreamAppender(TextWriter writer) : AppenderSkeleton
{
    protected override void Append(log4net.Core.LoggingEvent loggingEvent)
    {
        writer.Write(RenderLoggingEvent(loggingEvent));
    }
}

//
// Shared base
//
// Derived benchmark classes inherit GlobalSetup/GlobalCleanup and all logger
// fields. Each derived class is run as a separate `dotnet run` invocation, so
// an InProcess crash in one category does not abort the others.

public abstract class BenchmarkBase
{
    protected ILogger ClipConsole = null!;
    protected ILogger ClipJson = null!;
    protected ILogger ClipFiltered = null!;
    protected IZeroLogger ClipZeroConsole = null!;
    protected IZeroLogger ClipZeroJson = null!;
    protected IZeroLogger ClipZeroFiltered = null!;

    protected Serilog.ILogger SerilogConsole = null!;
    protected Serilog.ILogger SerilogJson = null!;
    protected Serilog.ILogger SerilogFiltered = null!;
    private Serilog.Core.Logger _serilogConsoleDispose = null!;
    private Serilog.Core.Logger _serilogJsonDispose = null!;
    private Serilog.Core.Logger _serilogFilteredDispose = null!;

    private NLog.LogFactory _nlogConsoleFactory = null!;
    private NLog.LogFactory _nlogJsonFactory = null!;
    private NLog.LogFactory _nlogFilteredFactory = null!;
    protected NLog.ILogger NlogConsole = null!;
    protected NLog.ILogger NlogJson = null!;
    protected NLog.ILogger NlogFiltered = null!;

    private Microsoft.Extensions.Logging.ILoggerFactory _melConsoleFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _melJsonFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _melFilteredFactory = null!;
    protected Microsoft.Extensions.Logging.ILogger MelConsole = null!;
    protected Microsoft.Extensions.Logging.ILogger MelJson = null!;
    protected Microsoft.Extensions.Logging.ILogger MelFiltered = null!;
    private TextWriter _savedConsoleOut = null!;
    private TextWriter _savedConsoleErr = null!;

    private Microsoft.Extensions.Logging.ILoggerFactory _zloggerConsoleFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _zloggerJsonFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _zloggerFilteredFactory = null!;
    protected Microsoft.Extensions.Logging.ILogger ZloggerConsole = null!;
    protected Microsoft.Extensions.Logging.ILogger ZloggerJson = null!;
    protected Microsoft.Extensions.Logging.ILogger ZloggerFiltered = null!;

    protected ILog Log4NetConsole = null!;
    protected ILog Log4NetFiltered = null!;

    //
    // ClipMEL (Clip via MEL adapter)
    //

    private Microsoft.Extensions.Logging.ILoggerFactory _clipMelConsoleFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _clipMelJsonFactory = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _clipMelFilteredFactory = null!;
    protected Microsoft.Extensions.Logging.ILogger ClipMelConsole = null!;
    protected Microsoft.Extensions.Logging.ILogger ClipMelJson = null!;
    protected Microsoft.Extensions.Logging.ILogger ClipMelFiltered = null!;
    private Logger _clipMelConsoleUnderlying = null!;
    private Logger _clipMelJsonUnderlying = null!;
    private Logger _clipMelFilteredUnderlying = null!;

    //
    // ZeroLog (synchronous mode for fair comparison)
    //

    // ZeroLog.Log is a struct with reference fields — CLR zero-inits, but the
    // compiler can't prove non-nullability without an explicit assignment.
#pragma warning disable CS8618
    protected ZeroLog.Log ZeroLogConsole;
    protected ZeroLog.Log ZeroLogFiltered;
#pragma warning restore CS8618
    // Roots the session to prevent GC; shutdown is via LogManager.Shutdown()
    // ReSharper disable once NotAccessedField.Local
    private IDisposable? _zeroLogSession;

    //
    // Pipeline benchmarks (enricher / filter / redactor)
    //

    protected ILogger ClipEnriched = null!;
    protected ILogger ClipFieldFiltered = null!;
    protected ILogger ClipRedacted = null!;
    protected ILogger ClipFullPipeline = null!;
    protected IZeroLogger ClipZeroEnriched = null!;
    protected IZeroLogger ClipZeroFieldFiltered = null!;
    protected IZeroLogger ClipZeroRedacted = null!;
    protected IZeroLogger ClipZeroFullPipeline = null!;
    protected Serilog.ILogger SerilogEnriched = null!;
    private Serilog.Core.Logger _serilogEnrichedDispose = null!;
    protected NLog.ILogger NlogEnriched = null!;
    private NLog.LogFactory _nlogEnrichedFactory = null!;

    protected Exception SampleException = null!;

    //
    // Shared constants — every logger uses the same values, so benchmarks
    // are comparable and nothing is "cheated" via compile-time inlining.
    //

    protected const string Method = "GET";
    protected const int Status = 200;
    protected const double Elapsed = 1.234;
    protected static readonly Guid ReqId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
    protected const decimal Amount = 49.95m;
    protected const string Host = "db.local";
    protected const int Port = 5432;
    protected const string RequestId = "abc-123";
    protected const int UserId = 42;
    protected const string Step = "auth";

    [GlobalSetup]
    public void Setup()
    {
        var nullWriter = TextWriter.Synchronized(
            new StreamWriter(Stream.Null) { AutoFlush = true });

        var clipConsole = Logger.Create(c => c.MinimumLevel(ClipLL.Info).WriteTo.Console(Stream.Null, false));
        var clipJson = Logger.Create(c => c.MinimumLevel(ClipLL.Info).WriteTo.Json(Stream.Null));
        var clipFiltered = Logger.Create(c => c.MinimumLevel(ClipLL.Info).WriteTo.Null());
        ClipConsole = clipConsole;
        ClipJson = clipJson;
        ClipFiltered = clipFiltered;
        ClipZeroConsole = clipConsole;
        ClipZeroJson = clipJson;
        ClipZeroFiltered = clipFiltered;

        _serilogConsoleDispose = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SerilogStreamSink(
                new MessageTemplateTextFormatter(
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u4} {Message:lj} {Properties:j}{NewLine}{Exception}"),
                nullWriter))
            .CreateLogger();
        SerilogConsole = _serilogConsoleDispose;

        _serilogJsonDispose = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SerilogStreamSink(new JsonFormatter(), nullWriter))
            .CreateLogger();
        SerilogJson = _serilogJsonDispose;

        _serilogFilteredDispose = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new SerilogStreamSink(new JsonFormatter(), TextWriter.Null))
            .CreateLogger();
        SerilogFiltered = _serilogFilteredDispose;

        _nlogConsoleFactory = CreateNLogFactory(
            new NLogStreamTarget(nullWriter)
            {
                Name = "console",
                Layout =
                    @"${date:format=yyyy-MM-dd HH\:mm\:ss.fff} ${level:uppercase=true:padding=-4:truncate=4} ${message} ${all-event-properties:separator= :includeScopeProperties=true}${onexception:${newline}${exception:format=tostring}}",
            },
            NLog.LogLevel.Info);
        NlogConsole = _nlogConsoleFactory.GetLogger("benchmark");

        _nlogJsonFactory = CreateNLogFactory(
            new NLogStreamTarget(nullWriter)
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
            },
            NLog.LogLevel.Info);
        NlogJson = _nlogJsonFactory.GetLogger("benchmark");

        _nlogFilteredFactory = CreateNLogFactory(new NullTarget("filtered"), NLog.LogLevel.Info);
        NlogFiltered = _nlogFilteredFactory.GetLogger("benchmark");

        // Uses standard AddSimpleConsole. SimpleConsoleFormatter formats
        // synchronously on the calling thread; the formatted string is then
        // enqueued to a background thread for Console.Write I/O only.
        // Redirect Console to null since the background writer targets Console.Out.
        _savedConsoleOut = Console.Out;
        _savedConsoleErr = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);

        _melConsoleFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.IncludeScopes = true;
                o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            });
        });
        MelConsole = _melConsoleFactory.CreateLogger("benchmark");

        _melJsonFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffZ";
            });
        });
        MelJson = _melJsonFactory.CreateLogger("benchmark");

        _melFilteredFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddSimpleConsole(o => o.SingleLine = true);
        });
        MelFiltered = _melFilteredFactory.CreateLogger("benchmark");

        _zloggerConsoleFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddZLoggerStream(Stream.Null, options =>
            {
                options.IncludeScopes = true;
                options.UsePlainTextFormatter(formatter =>
                {
                    formatter.SetPrefixFormatter(
                        $"{0:yyyy-MM-dd HH:mm:ss.fff} {1:short} ",
                        (in ZLogger.MessageTemplate t, in LogInfo i) => t.Format(i.Timestamp, i.LogLevel));
                });
            });
        });
        ZloggerConsole = _zloggerConsoleFactory.CreateLogger("comparison");

        _zloggerJsonFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddZLoggerStream(Stream.Null, options =>
            {
                options.IncludeScopes = true;
                options.UseJsonFormatter();
            });
        });
        ZloggerJson = _zloggerJsonFactory.CreateLogger("comparison");

        _zloggerFilteredFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddZLoggerStream(Stream.Null);
        });
        ZloggerFiltered = _zloggerFilteredFactory.CreateLogger("comparison");

        var asm = Assembly.GetExecutingAssembly();
        var hierarchy = (Hierarchy)LogManager.GetRepository(asm);
        hierarchy.Root.Level = log4net.Core.Level.Debug;

        SetupLog4NetLogger(hierarchy, "console",
            new Log4NetStreamAppender(nullWriter)
            {
                Name = "l4n-console",
                Layout = new log4net.Layout.PatternLayout(
                    "%date{yyyy-MM-dd HH:mm:ss.fff} %-5level %message%newline%exception"),
            });
        SetupLog4NetLogger(hierarchy, "json",
            new Log4NetStreamAppender(nullWriter)
            {
                Name = "l4n-json",
                Layout = new log4net.Layout.PatternLayout(
                    "{\"ts\":\"%utcdate{ISO8601}\",\"level\":\"%level\",\"msg\":\"%message\"}%newline"),
            });
        SetupLog4NetLogger(hierarchy, "filtered",
            new Log4NetStreamAppender(TextWriter.Null)
            {
                Name = "l4n-filtered",
                Layout = new log4net.Layout.SimpleLayout(),
            });

        hierarchy.Configured = true;
        Log4NetConsole = LogManager.GetLogger(asm, "console");
        Log4NetFiltered = LogManager.GetLogger(asm, "filtered");

        _clipMelConsoleUnderlying = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info).WriteTo.Console(Stream.Null, false));
        _clipMelConsoleFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddClip(_clipMelConsoleUnderlying);
        });
        ClipMelConsole = _clipMelConsoleFactory.CreateLogger("benchmark");

        _clipMelJsonUnderlying = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info).WriteTo.Json(Stream.Null));
        _clipMelJsonFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddClip(_clipMelJsonUnderlying);
        });
        ClipMelJson = _clipMelJsonFactory.CreateLogger("benchmark");

        _clipMelFilteredUnderlying = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info).WriteTo.Null());
        _clipMelFilteredFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(MelLL.Information);
            b.AddClip(_clipMelFilteredUnderlying);
        });
        ClipMelFiltered = _clipMelFilteredFactory.CreateLogger("benchmark");

        //
        // Pipeline loggers (enricher / filter / redactor)
        //

        var clipEnriched = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info)
            .Enrich.Field("app", "benchmark")
            .WriteTo.Console(Stream.Null, false));
        ClipEnriched = clipEnriched;
        ClipZeroEnriched = clipEnriched;

        var clipFieldFiltered = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info)
            .Filter.Fields("password")
            .WriteTo.Console(Stream.Null, false));
        ClipFieldFiltered = clipFieldFiltered;
        ClipZeroFieldFiltered = clipFieldFiltered;

        var clipRedacted = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info)
            .Redact.Fields("Token")
            .WriteTo.Console(Stream.Null, false));
        ClipRedacted = clipRedacted;
        ClipZeroRedacted = clipRedacted;

        var clipFullPipeline = Logger.Create(c => c
            .MinimumLevel(ClipLL.Info)
            .Enrich.Field("app", "benchmark")
            .Filter.Fields("password")
            .Redact.Fields("Token")
            .WriteTo.Console(Stream.Null, false));
        ClipFullPipeline = clipFullPipeline;
        ClipZeroFullPipeline = clipFullPipeline;

        _serilogEnrichedDispose = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("app", "benchmark")
            .WriteTo.Sink(new SerilogStreamSink(
                new MessageTemplateTextFormatter(
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u4} {Message:lj} {Properties:j}{NewLine}{Exception}"),
                nullWriter))
            .CreateLogger();
        SerilogEnriched = _serilogEnrichedDispose;

        _nlogEnrichedFactory = CreateNLogFactory(
            new NLogStreamTarget(nullWriter)
            {
                Name = "enriched",
                Layout =
                    @"${date:format=yyyy-MM-dd HH\:mm\:ss.fff} ${level:uppercase=true:padding=-4:truncate=4} ${message} app=${gdc:item=app} ${all-event-properties:separator= :includeScopeProperties=true}${onexception:${newline}${exception:format=tostring}}",
            },
            NLog.LogLevel.Info);
        NLog.GlobalDiagnosticsContext.Set("app", "benchmark");
        NlogEnriched = _nlogEnrichedFactory.GetLogger("benchmark");

        var zeroLogFormatter = new ZeroLog.Formatting.DefaultFormatter(
            new ZeroLog.Formatting.PatternWriter(
                "%{date:yyyy-MM-dd HH:mm:ss.fff} %{level:pad} %message"));
        _zeroLogSession = ZeroLog.LogManager.Initialize(new ZeroLog.Configuration.ZeroLogConfiguration
        {
            AppendingStrategy = ZeroLog.Configuration.AppendingStrategy.Synchronous,
            RootLogger =
            {
                Level = ZeroLog.LogLevel.Info,
                Appenders =
                {
                    new ZeroLog.Appenders.TextWriterAppender(nullWriter)
                        { Formatter = zeroLogFormatter },
                },
            },
        });
        ZeroLogConsole = ZeroLog.LogManager.GetLogger("console");
        ZeroLogFiltered = ZeroLog.LogManager.GetLogger("filtered");

        try
        {
            throw new InvalidOperationException("simulated database error");
        }
        catch (Exception ex)
        {
            SampleException = ex;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ClipConsole.Dispose();
        ClipJson.Dispose();
        ClipFiltered.Dispose();
        _serilogConsoleDispose.Dispose();
        _serilogJsonDispose.Dispose();
        _serilogFilteredDispose.Dispose();
        _nlogConsoleFactory.Dispose();
        _nlogJsonFactory.Dispose();
        _nlogFilteredFactory.Dispose();
        _melConsoleFactory.Dispose();
        _melJsonFactory.Dispose();
        _melFilteredFactory.Dispose();
        _clipMelConsoleFactory.Dispose();
        _clipMelJsonFactory.Dispose();
        _clipMelFilteredFactory.Dispose();
        _clipMelConsoleUnderlying.Dispose();
        _clipMelJsonUnderlying.Dispose();
        _clipMelFilteredUnderlying.Dispose();
        ClipEnriched.Dispose();
        ClipFieldFiltered.Dispose();
        ClipRedacted.Dispose();
        ClipFullPipeline.Dispose();
        _serilogEnrichedDispose.Dispose();
        _nlogEnrichedFactory.Dispose();
        Console.SetOut(_savedConsoleOut);
        Console.SetError(_savedConsoleErr);
        _zloggerConsoleFactory.Dispose();
        _zloggerJsonFactory.Dispose();
        _zloggerFilteredFactory.Dispose();
        ZeroLog.LogManager.Shutdown();
        LogManager.GetRepository(Assembly.GetExecutingAssembly()).Shutdown();
    }

    private static NLog.LogFactory CreateNLogFactory(Target target, NLog.LogLevel minLevel)
    {
        var config = new LoggingConfiguration();
        config.AddTarget(target);
        config.AddRule(minLevel, NLog.LogLevel.Fatal, target);
        var factory = new NLog.LogFactory();
        factory.Configuration = config;
        return factory;
    }

    private static void SetupLog4NetLogger(Hierarchy hierarchy, string name, AppenderSkeleton appender)
    {
        appender.ActivateOptions();
        var logger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger(name);
        logger.Level = log4net.Core.Level.Info;
        logger.Additivity = false;
        logger.AddAppender(appender);
    }
}
