using System.Text;
using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class LoggerTests
{
    private static readonly JsonFormatConfig NestedConfig = new() { FieldsKey = "fields" };

    private static (Logger logger, MemoryStream ms) MakeJsonLogger(LogLevel minLevel = LogLevel.Trace)
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(minLevel)
            .WriteTo.Json(NestedConfig, ms));
        return (logger, ms);
    }

    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    [Fact]
    public void Info_WritesLog()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Info("hello");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("info", docs[0].RootElement.GetProperty("level").GetString());
        Assert.Equal("hello", docs[0].RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void AllLevels_WriteCorrectLevel()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Trace("t");
        logger.Debug("d");
        logger.Info("i");
        logger.Warning("w");
        logger.Error("e");

        var docs = ReadLines(ms);
        Assert.Equal(5, docs.Length);
        Assert.Equal("trace", docs[0].RootElement.GetProperty("level").GetString());
        Assert.Equal("debug", docs[1].RootElement.GetProperty("level").GetString());
        Assert.Equal("info", docs[2].RootElement.GetProperty("level").GetString());
        Assert.Equal("warning", docs[3].RootElement.GetProperty("level").GetString());
        Assert.Equal("error", docs[4].RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void MinLevel_FiltersLowPriorityLogs()
    {
        var (logger, ms) = MakeJsonLogger(LogLevel.Warning);
        logger.Trace("t");
        logger.Debug("d");
        logger.Info("i");
        logger.Warning("w");
        logger.Error("e");

        var docs = ReadLines(ms);
        Assert.Equal(2, docs.Length);
        Assert.Equal("warning", docs[0].RootElement.GetProperty("level").GetString());
        Assert.Equal("error", docs[1].RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void Info_WithObjectFields_ExtractsProperties()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Info("User logged in", new { UserId = 42, IP = "1.2.3.4" });

        var docs = ReadLines(ms);
        Assert.Single(docs);
        var fields = docs[0].RootElement.GetProperty("fields");
        Assert.Equal(42, fields.GetProperty("UserId").GetInt32());
        Assert.Equal("1.2.3.4", fields.GetProperty("IP").GetString());
    }

    [Fact]
    public void Info_ZeroAllocTier_WritesFields()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Info("msg", new Field("k", 1), new Field("v", "x"));

        var docs = ReadLines(ms);
        Assert.Single(docs);
        var fields = docs[0].RootElement.GetProperty("fields");
        Assert.Equal(1, fields.GetProperty("k").GetInt32());
        Assert.Equal("x", fields.GetProperty("v").GetString());
    }

    [Fact]
    public void Error_WithException_LogsErrorField()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Error("Failed", new InvalidOperationException("boom"));

        var docs = ReadLines(ms);
        Assert.Single(docs);
        var error = docs[0].RootElement.GetProperty("error");
        Assert.Equal(JsonValueKind.Object, error.ValueKind);
        Assert.Contains("InvalidOperationException", error.GetProperty("type").GetString());
        Assert.Equal("boom", error.GetProperty("msg").GetString());
    }

    [Fact]
    public void AddContext_AppendsContextFields()
    {
        var (logger, ms) = MakeJsonLogger();
        using (Logger.AddContext(new { RequestId = "abc-123" }))
        {
            logger.Info("Processing");
        }

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("abc-123", docs[0].RootElement.GetProperty("fields").GetProperty("RequestId").GetString());
    }

    [Fact]
    public void AddContext_Disposed_FieldsGone()
    {
        var (logger, ms) = MakeJsonLogger();
        using (Logger.AddContext(new { RequestId = "abc-123" }))
        {
            // Scope active
        }

        logger.Info("after");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        // No fields object at all when there are no fields, or RequestId not in it
        Assert.False(docs[0].RootElement.TryGetProperty("fields", out var fieldsEl)
                     && fieldsEl.TryGetProperty("RequestId", out _));
    }

    [Fact]
    public void AddContext_NestedScopes_MergeCorrectly()
    {
        var (logger, ms) = MakeJsonLogger();
        using (Logger.AddContext(new { A = 1 }))
        using (Logger.AddContext(new { B = 2 }))
        {
            logger.Info("inner");
        }

        var docs = ReadLines(ms);
        Assert.Single(docs);
        var fields = docs[0].RootElement.GetProperty("fields");
        Assert.Equal(1, fields.GetProperty("A").GetInt32());
        Assert.Equal(2, fields.GetProperty("B").GetInt32());
    }

    [Fact]
    public void AddContext_CallSiteOverridesContext()
    {
        var (logger, ms) = MakeJsonLogger();
        using (Logger.AddContext(new { X = 100 }))
        {
            logger.Info("msg", new { X = 999 });
        }

        var docs = ReadLines(ms);
        Assert.Single(docs);
        // Call-site X=999 wins over context X=100
        Assert.Equal(999, docs[0].RootElement.GetProperty("fields").GetProperty("X").GetInt32());
    }

    [Fact]
    public void AddContext_NestedOverwrite_NewFieldWins()
    {
        var (logger, ms) = MakeJsonLogger();
        using (Logger.AddContext(new { Key = "outer" }))
        using (Logger.AddContext(new { Key = "inner" }))
        {
            logger.Info("msg");
        }

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("inner", docs[0].RootElement.GetProperty("fields").GetProperty("Key").GetString());
    }

    [Fact]
    public void Create_DefaultSink_IsConsoleWhenNoneConfigured()
    {
        // When no sinks configured, should not throw
        var logger = Logger.Create(_ => { });
        Assert.NotNull(logger);
    }

    [Fact]
    public void Create_MultipleSinks_WritesToAll()
    {
        var ms1 = new MemoryStream();
        var ms2 = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(ms1)
            .WriteTo.Json(ms2));

        logger.Info("hello");

        Assert.True(ms1.Length > 0);
        Assert.True(ms2.Length > 0);
    }

    [Fact]
    public void Info_NullFields_NoContextFastPath()
    {
        var (logger, ms) = MakeJsonLogger();
        logger.Info("fast path"); // null fields, no context

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("fast path", docs[0].RootElement.GetProperty("msg").GetString());
    }


    [Fact]
    public void LogCall_WhenSinkThrows_DoesNotCrash()
    {
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink()));

        var ex = Record.Exception(() => logger.Info("should not crash"));
        Assert.Null(ex);
    }

    [Fact]
    public void LogCall_ZeroAllocTier_WhenSinkThrows_DoesNotCrash()
    {
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink()));

        var ex = Record.Exception(() => logger.Info("should not crash", new Field("k", 1)));
        Assert.Null(ex);
    }

    [Fact]
    public void WriteTo_WhenFirstSinkThrows_SecondSinkStillFires()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new ThrowingSink())
            .WriteTo.Json(ms));

        logger.Info("hello");
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void ConsoleSink_UserStream_NotDisposedOnSinkDispose()
    {
        var ms = new MemoryStream();
        var sink = new ConsoleSink(ms, colors: false);
        sink.Dispose();

        // Stream should still be usable after sink disposal
        var ex = Record.Exception(() => ms.WriteByte(0));
        Assert.Null(ex);
    }

    [Fact]
    public void ConsoleSink_DefaultStream_DisposedOnSinkDispose()
    {
        var sink = new ConsoleSink();
        var ex = Record.Exception(() => sink.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void JsonSink_UserStream_NotDisposedOnSinkDispose()
    {
        var ms = new MemoryStream();
        var sink = new JsonSink(ms);
        sink.Dispose();

        var ex = Record.Exception(() => ms.WriteByte(0));
        Assert.Null(ex);
    }


    [Fact]
    public void PerSinkLevel_FiltersCorrectly()
    {
        var msDebug = new MemoryStream();
        var msWarn = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(msDebug, minLevel: LogLevel.Debug)
            .WriteTo.Json(msWarn, minLevel: LogLevel.Warning));

        logger.Debug("d");
        logger.Warning("w");

        var debugDocs = ReadLines(msDebug);
        var warnDocs = ReadLines(msWarn);

        Assert.Equal(2, debugDocs.Length); // Debug sink sees both
        Assert.Single(warnDocs); // Warning sink sees only warning
        Assert.Equal("warning", warnDocs[0].RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void PerSinkLevel_GlobalLevelIsFloor()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Warning)
            .WriteTo.Json(ms, minLevel: LogLevel.Debug));

        logger.Debug("should be filtered by global level");

        Assert.Equal(0, ms.Length);
    }


    [Fact]
    public void BackgroundSink_FlushesOnDispose()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        for (var i = 0; i < 100; i++)
            logger.Info($"msg-{i}");

        logger.Dispose();

        var docs = ReadLines(ms);
        Assert.Equal(100, docs.Length);
    }

    [Fact]
    public void BackgroundSink_InnerSinkThrows_DoesNotCrashDrainLoop()
    {
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Sink(new ThrowingSink())));

        for (var i = 0; i < 10; i++)
            logger.Info($"msg-{i}");

        var ex = Record.Exception(() => logger.Dispose());
        Assert.Null(ex);
    }

    //
    // Post-dispose safety
    //

    [Fact]
    public void LogAfterDispose_ErgonomicTier_NoCrash()
    {
        var (logger, _) = MakeJsonLogger();
        logger.Dispose();

        var ex = Record.Exception(() => logger.Info("after dispose", new { K = 1 }));
        Assert.Null(ex);
    }

    [Fact]
    public void LogAfterDispose_ZeroAllocTier_NoCrash()
    {
        var (logger, _) = MakeJsonLogger();
        logger.Dispose();

        var ex = Record.Exception(() => logger.Info("after dispose", new Field("k", 1)));
        Assert.Null(ex);
    }

    [Fact]
    public void DoubleDispose_NoCrash()
    {
        var (logger, _) = MakeJsonLogger();

        var ex = Record.Exception(() =>
        {
            logger.Dispose();
            logger.Dispose();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void LogAfterDispose_WithContext_NoCrash()
    {
        var (logger, _) = MakeJsonLogger();
        using var _ = Logger.AddContext(new Field("ctx", "active"));
        logger.Dispose();

        var ex = Record.Exception(() => logger.Info("after dispose with context"));
        Assert.Null(ex);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            throw new InvalidOperationException("Sink exploded");
        }

        public void Dispose()
        {
        }
    }
}
