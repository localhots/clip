using Clip.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using ClipLogLevel = Clip.LogLevel;

namespace Clip.Extensions.Logging.Tests;

public class MelIntegrationTests
{
    private static (ILoggerFactory factory, ListSink sink) CreateFactory(
        Action<ClipLoggerOptions>? configure = null)
    {
        var listSink = new ListSink();
        var clipLogger = Logger.Create(c => c
            .MinimumLevel(ClipLogLevel.Trace)
            .WriteTo.Sink(listSink));

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddClip(clipLogger);
            if (configure is not null) builder.Services.Configure(configure);
        });

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ILoggerFactory>(), listSink);
    }

    //
    // Level mapping
    //

    [Theory]
    [InlineData(MelLogLevel.Trace, "trace")]
    [InlineData(MelLogLevel.Debug, "debug")]
    [InlineData(MelLogLevel.Information, "info")]
    [InlineData(MelLogLevel.Warning, "warning")]
    [InlineData(MelLogLevel.Error, "error")]
    [InlineData(MelLogLevel.Critical, "fatal")]
    public void LevelMapping_AllMelLevels(MelLogLevel melLevel, string expectedClipName)
    {
        var clipLevel = LevelMapping.ToClip(melLevel);
        Assert.Equal(expectedClipName, clipLevel.ToString().ToLowerInvariant());
    }

    [Fact]
    public void IsEnabled_None_ReturnsFalse()
    {
        var (factory, _) = CreateFactory();
        var logger = factory.CreateLogger("Test");
        Assert.False(logger.IsEnabled(MelLogLevel.None));
    }

    //
    // Field extraction
    //

    [Fact]
    public void LogInformation_ExtractsNamedProperties()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("MyApp.Service");

        logger.LogInformation("Hello {Name}, age {Age}", "World", 42);

        var records = sink.Records;
        Assert.Single(records);
        Assert.Equal("Hello World, age 42", records[0].Message);
        Assert.Equal(ClipLogLevel.Info, records[0].Level);

        var fields = records[0].Fields;
        Assert.Contains(fields, f => f.Key == "SourceContext" && (string)f.RefValue! == "MyApp.Service");
        Assert.Contains(fields, f => f is { Key: "Name", RefValue: "World" });
        Assert.Contains(fields, f => f is { Key: "Age", Type: FieldType.Int, IntValue: 42 });
    }

    [Fact]
    public void LogWithEventId_IncludesEventIdFields()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");
        var eventId = new EventId(123, "MyEvent");

        logger.Log(MelLogLevel.Information, eventId, "test message");

        var records = sink.Records;
        Assert.Single(records);
        Assert.Contains(records[0].Fields, f => f is { Key: "EventId", IntValue: 123 });
        Assert.Contains(records[0].Fields, f =>
        {
            if (f.Key != "EventName") return false;
            return (string)f.RefValue! == "MyEvent";
        });
    }

    //
    // Exception logging
    //

    [Fact]
    public void LogError_IncludesException()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");
        var ex = new InvalidOperationException("boom");

        logger.LogError(ex, "Something failed");

        var records = sink.Records;
        Assert.Single(records);
        Assert.Equal(ClipLogLevel.Error, records[0].Level);
        Assert.Same(ex, records[0].Exception);
    }

    //
    // Category filtering
    //

    [Fact]
    public void CategoryFilter_HierarchicalMatching()
    {
        var map = new CategoryLevelMap(
            new Dictionary<string, ClipLogLevel>
            {
                ["Microsoft"] = ClipLogLevel.Warning,
                ["Microsoft.AspNetCore"] = ClipLogLevel.Error,
            },
            ClipLogLevel.Info);

        Assert.Equal(ClipLogLevel.Info, map.GetEffectiveLevel("MyApp.Service"));
        Assert.Equal(ClipLogLevel.Warning, map.GetEffectiveLevel("Microsoft.Extensions.Hosting"));
        Assert.Equal(ClipLogLevel.Error, map.GetEffectiveLevel("Microsoft.AspNetCore.Routing"));
    }

    [Fact]
    public void CategoryFilter_ExactMatch()
    {
        var map = new CategoryLevelMap(
            new Dictionary<string, ClipLogLevel>
            {
                ["MyApp"] = ClipLogLevel.Debug,
            },
            ClipLogLevel.Info);

        Assert.Equal(ClipLogLevel.Debug, map.GetEffectiveLevel("MyApp"));
    }

    [Fact]
    public void CategoryFilter_PartialNameDoesNotMatch()
    {
        var map = new CategoryLevelMap(
            new Dictionary<string, ClipLogLevel>
            {
                ["My"] = ClipLogLevel.Debug,
            },
            ClipLogLevel.Info);

        // "MyApp" does not start with "My." and is not exactly "My"
        Assert.Equal(ClipLogLevel.Info, map.GetEffectiveLevel("MyApp"));
    }

    //
    // Scope forwarding
    //

    [Fact]
    public void BeginScope_WithKvp_AddsContextFields()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("RequestId", "abc-123") }))
        {
            logger.LogInformation("Scoped message");
        }

        var records = sink.Records;
        Assert.Single(records);
        Assert.Contains(records[0].Fields, f => f.Key == "RequestId");
    }

    //
    // End-to-end with DI
    //

    [Fact]
    public void EndToEnd_ILoggerT_WithListSink()
    {
        var listSink = new ListSink();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddClip(opts =>
            {
                opts.ConfigureLogger = c => c
                    .MinimumLevel(ClipLogLevel.Trace)
                    .WriteTo.Sink(listSink);
            });
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<MelIntegrationTests>>();

        logger.LogInformation("Hello {Name}", "World");

        var records = listSink.Records;
        Assert.Single(records);
        Assert.Equal("Hello World", records[0].Message);
        Assert.Contains(records[0].Fields, f => f.Key == "Name" && f.RefValue is "World");
        Assert.Contains(records[0].Fields, f =>
            f.Key == "SourceContext" &&
            (string)f.RefValue! == typeof(MelIntegrationTests).FullName);
    }

    //
    // Category-level filtering via options
    //

    [Fact]
    public void CategoryLevelFiltering_ViaOptions()
    {
        var listSink = new ListSink();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddClip(opts =>
            {
                opts.ConfigureLogger = c => c
                    .MinimumLevel(ClipLogLevel.Trace)
                    .WriteTo.Sink(listSink);
                opts.DefaultLevel = ClipLogLevel.Warning;
                opts.CategoryLevels["MyApp"] = ClipLogLevel.Debug;
            });
        });

        var sp = services.BuildServiceProvider();

        // MyApp category should allow Debug
        var myAppLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("MyApp.Service");
        myAppLogger.LogDebug("should appear");
        Assert.Single(listSink.Records);

        listSink.Clear();

        // The other category should use Warning default
        var otherLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Other.Service");
        otherLogger.LogInformation("should be filtered");
        Assert.Empty(listSink.Records);

        otherLogger.LogWarning("should appear");
        Assert.Single(listSink.Records);
    }

    //
    // Scope state shape variants
    //

    [Fact]
    public void BeginScope_WithStringState_AddedAsScopeField()
    {
        // Non-KVP scope state takes the fallback path: a single "Scope" field is added
        // with the raw state as the value.
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        using (logger.BeginScope("Processing batch 5"))
            logger.LogInformation("inside scope");

        var fields = sink.Records[0].Fields;
        Assert.Contains(fields, f => f.Key == "Scope" && (string)f.RefValue! == "Processing batch 5");
    }

    [Fact]
    public void BeginScope_WithPocoState_AddedAsSingleScopeField()
    {
        // An anonymous object is not IReadOnlyList<KVP>, so it goes to the fallback path.
        // The whole POCO ends up as the value of a single "Scope" field — properties are
        // *not* expanded individually (that would require ad-hoc reflection on the hot path).
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        using (logger.BeginScope(new { RequestId = "abc" }))
            logger.LogInformation("inside scope");

        var fields = sink.Records[0].Fields;
        Assert.Contains(fields, f => f.Key == "Scope");
        Assert.DoesNotContain(fields, f => f.Key == "RequestId");
    }

    //
    // EventId edge cases
    //

    [Fact]
    public void EventId_ZeroIdWithName_EventNameStillEmitted()
    {
        // EventId and EventName are independent — a caller that supplies only Name should
        // still see the name in the output. The Id=0 case is only meaningful when *both*
        // Id and Name are unset (the implicit default from log-macro overloads that take
        // no EventId), which is tested via the no-EventId logger calls above.
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        logger.Log(MelLogLevel.Information, new EventId(0, "MyEvent"), "msg");

        var fields = sink.Records[0].Fields;
        Assert.DoesNotContain(fields, f => f.Key == "EventId");
        Assert.Contains(fields, f =>
        {
            if (f.Key != "EventName") return false;
            return (string)f.RefValue! == "MyEvent";
        });
    }

    [Fact]
    public void EventId_NonZeroIdWithoutName_OnlyEventIdEmitted()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        logger.Log(MelLogLevel.Information, new EventId(42), "msg");

        var fields = sink.Records[0].Fields;
        Assert.Contains(fields, f => f is { Key: "EventId", IntValue: 42 });
        Assert.DoesNotContain(fields, f => f.Key == "EventName");
    }

    [Fact]
    public void EventId_DefaultZeroIdNullName_NeitherFieldEmitted()
    {
        // The implicit default — no EventId argument at all. Neither field appears.
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        logger.LogInformation("msg");

        var fields = sink.Records[0].Fields;
        Assert.DoesNotContain(fields, f => f.Key == "EventId");
        Assert.DoesNotContain(fields, f => f.Key == "EventName");
    }

    //
    // External scope provider (ISupportExternalScope)
    //

    [Fact]
    public void ExternalScope_KvpFields_VisibleInLog()
    {
        // The host's LoggerFactory builds a unified IExternalScopeProvider and pushes it
        // into every ISupportExternalScope provider. Scopes pushed through the *factory*
        // (e.g. via factory.CreateLogger(...).BeginScope, or by other providers like
        // ASP.NET Core's HostingApplication for HTTP requests) must end up in Clip's fields.
        var listSink = new ListSink();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddClip(opts =>
            {
                opts.ConfigureLogger = c => c
                    .MinimumLevel(ClipLogLevel.Trace)
                    .WriteTo.Sink(listSink);
            });
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger("Test");

        // BeginScope on the factory-produced logger flows through the unified scope provider.
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("RequestId", "abc-123") }))
            logger.LogInformation("scoped");

        var fields = listSink.Records[0].Fields;
        Assert.Contains(fields, f => f.Key == "RequestId" && (string)f.RefValue! == "abc-123");
    }

    [Fact]
    public void ExternalScope_NonKvpState_AddedAsScopeField()
    {
        // Non-KVP scope states (POCOs, strings) collected via the external provider must
        // also surface — same fallback contract as ClipLogger.BeginScope.
        var listSink = new ListSink();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddClip(opts =>
            {
                opts.ConfigureLogger = c => c
                    .MinimumLevel(ClipLogLevel.Trace)
                    .WriteTo.Sink(listSink);
            });
        });

        var factory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger("Test");

        using (logger.BeginScope("processing-batch-5"))
            logger.LogInformation("scoped");

        var fields = listSink.Records[0].Fields;
        Assert.Contains(fields, f =>
            f.Key == "Scope" && (string)f.RefValue! == "processing-batch-5");
    }

    [Fact]
    public void NoExternalScopeProvider_DirectProviderUse_DoesNotCrash()
    {
        // Constructing the provider directly (without going through LoggerFactory) leaves
        // _scopeProvider null. The Log path must take the fast no-scope branch and not NRE.
        var listSink = new ListSink();
        var clip = Logger.Create(c => c
            .MinimumLevel(ClipLogLevel.Trace)
            .WriteTo.Sink(listSink));
        using var provider = new ClipLoggerProvider(clip);
        var logger = provider.CreateLogger("Test");

        var ex = Record.Exception(() => logger.LogInformation("no scope provider"));
        Assert.Null(ex);
        Assert.Single(listSink.Records);
    }

    //
    // Nested scopes
    //

    [Fact]
    public void BeginScope_NestedScopes_AllFieldsAppear()
    {
        var (factory, sink) = CreateFactory();
        var logger = factory.CreateLogger("Test");

        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("RequestId", "abc-123") }))
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("UserId", "user-42") }))
        {
            logger.LogInformation("Nested scoped message");
        }

        var records = sink.Records;
        Assert.Single(records);
        Assert.Contains(records[0].Fields, f => f.Key == "RequestId");
        Assert.Contains(records[0].Fields, f => f.Key == "UserId");
    }
}
