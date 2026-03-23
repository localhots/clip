using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Clip.OpenTelemetry.Tests;

/// <summary>
/// Shared OTEL collector container for all integration tests.
/// Started once, reused across all tests in the collection.
/// </summary>
public sealed class OtlpCollectorFixture : IAsyncLifetime
{
    private const string CollectorImage = "otel/opentelemetry-collector:0.120.0";

    private const string CollectorConfig = """
                                           receivers:
                                             otlp:
                                               protocols:
                                                 grpc:
                                                   endpoint: 0.0.0.0:4317
                                                 http:
                                                   endpoint: 0.0.0.0:4318
                                           exporters:
                                             debug:
                                               verbosity: detailed
                                           service:
                                             pipelines:
                                               logs:
                                                 receivers: [otlp]
                                                 exporters: [debug]
                                           """;

    private IContainer _container = null!;

    public string GrpcEndpoint => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(4317)}";
    public string HttpEndpoint => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(4318)}";

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage(CollectorImage)
            .WithPortBinding(4317, true)
            .WithPortBinding(4318, true)
            .WithEnvironment("COLLECTOR_CONFIG", CollectorConfig)
            .WithCommand("--config=env:COLLECTOR_CONFIG")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready"))
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Polls collector output until <paramref name="expected"/> appears or timeout.
    /// </summary>
    public async Task<string> WaitForOutputAsync(string expected, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var logs = await _container.GetLogsAsync();
            var output = logs.Stdout + logs.Stderr;
            if (output.Contains(expected))
                return output;
            await Task.Delay(50);
        }

        var final = await _container.GetLogsAsync();
        return final.Stdout + final.Stderr;
    }
}

[CollectionDefinition("OtlpCollector")]
public class OtlpCollectorCollection : ICollectionFixture<OtlpCollectorFixture>;

/// <summary>
/// Integration tests against a real OpenTelemetry Collector.
/// All tests share one container via <see cref="OtlpCollectorFixture"/>.
/// Assertions verify the collector's debug exporter output format:
///   -> key: Type(value)
///   Body: Str(message)
///   SeverityText: LEVEL
/// </summary>
[Collection("OtlpCollector")]
public class OtlpCollectorTests(OtlpCollectorFixture collector)
{
    [Fact]
    public async Task Grpc_LogsArriveWithCorrectValues()
    {
        using var sink = new OtlpSink(new OtlpSinkOptions
        {
            Endpoint = collector.GrpcEndpoint,
            Protocol = OtlpProtocol.Grpc,
            ServiceName = "grpc-test",
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(100),
        });

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Hello from gRPC",
            [new Field("env", "production"), new Field("count", 42)], null);

        var output = await collector.WaitForOutputAsync("Hello from gRPC");

        Assert.Contains("Body: Str(Hello from gRPC)", output);
        Assert.Contains("SeverityText: INFO", output);
        Assert.Contains("service.name: Str(grpc-test)", output);
        Assert.Contains("-> env: Str(production)", output);
        Assert.Contains("-> count: Int(42)", output);
    }

    [Fact]
    public async Task Http_LogsArriveWithCorrectValues()
    {
        using var sink = new OtlpSink(new OtlpSinkOptions
        {
            Endpoint = collector.HttpEndpoint,
            Protocol = OtlpProtocol.HttpProtobuf,
            ServiceName = "http-test",
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(100),
        });

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Warning, "Hello from HTTP",
            [new Field("host", "db.internal"), new Field("port", 5432)], null);

        var output = await collector.WaitForOutputAsync("Hello from HTTP");

        Assert.Contains("Body: Str(Hello from HTTP)", output);
        Assert.Contains("SeverityText: WARN", output);
        Assert.Contains("service.name: Str(http-test)", output);
        Assert.Contains("-> host: Str(db.internal)", output);
        Assert.Contains("-> port: Int(5432)", output);
    }

    [Fact]
    public async Task Grpc_ExceptionAttributesWithCorrectValues()
    {
        using var sink = new OtlpSink(new OtlpSinkOptions
        {
            Endpoint = collector.GrpcEndpoint,
            Protocol = OtlpProtocol.Grpc,
            ServiceName = "exception-test",
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(100),
        });

        var ex = new InvalidOperationException("something broke");
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Error, "Operation failed",
            ReadOnlySpan<Field>.Empty, ex);

        var output = await collector.WaitForOutputAsync("Operation failed");

        Assert.Contains("Body: Str(Operation failed)", output);
        Assert.Contains("SeverityText: ERROR", output);
        Assert.Contains("-> exception.type: Str(System.InvalidOperationException)", output);
        Assert.Contains("-> exception.message: Str(something broke)", output);
        Assert.Contains("-> exception.stacktrace: Str(System.InvalidOperationException: something broke)", output);
    }

    [Fact]
    public async Task Grpc_BatchedLogsAllArrive()
    {
        using var sink = new OtlpSink(new OtlpSinkOptions
        {
            Endpoint = collector.GrpcEndpoint,
            Protocol = OtlpProtocol.Grpc,
            ServiceName = "batch-test",
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(200),
        });

        for (var i = 0; i < 10; i++)
            sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, $"Batch message {i}",
                [new Field("index", i)], null);

        var output = await collector.WaitForOutputAsync("Batch message 9");

        for (var i = 0; i < 10; i++)
        {
            Assert.Contains($"Body: Str(Batch message {i})", output);
            Assert.Contains($"-> index: Int({i})", output);
        }
    }

    [Fact]
    public async Task Grpc_AllFieldTypesWithCorrectValues()
    {
        using var sink = new OtlpSink(new OtlpSinkOptions
        {
            Endpoint = collector.GrpcEndpoint,
            Protocol = OtlpProtocol.Grpc,
            ServiceName = "fieldtype-test",
            ServiceVersion = "2.0.0",
            BatchSize = 1,
            FlushInterval = TimeSpan.FromMilliseconds(100),
        });

        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "Field types test",
        [
            new Field("bool_field", true),
            new Field("int_field", 42),
            new Field("long_field", 123456789L),
            new Field("double_field", 3.14),
            new Field("string_field", "hello world"),
        ], null);

        var output = await collector.WaitForOutputAsync("Field types test");

        Assert.Contains("service.name: Str(fieldtype-test)", output);
        Assert.Contains("service.version: Str(2.0.0)", output);
        Assert.Contains("-> bool_field: Bool(true)", output);
        Assert.Contains("-> int_field: Int(42)", output);
        Assert.Contains("-> long_field: Int(123456789)", output);
        Assert.Contains("-> double_field: Double(3.14", output);
        Assert.Contains("-> string_field: Str(hello world)", output);
    }
}
