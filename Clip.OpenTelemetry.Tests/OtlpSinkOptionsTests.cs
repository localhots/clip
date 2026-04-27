namespace Clip.OpenTelemetry.Tests;

public class OtlpSinkOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new OtlpSinkOptions();

        Assert.Equal("http://localhost:4317", opts.Endpoint);
        Assert.Equal(OtlpProtocol.Grpc, opts.Protocol);
        Assert.Equal(512, opts.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.FlushInterval);
        Assert.Equal(2048, opts.QueueCapacity);
        Assert.Equal("unknown_service", opts.ServiceName);
        Assert.Null(opts.ServiceVersion);
        Assert.Empty(opts.Headers);
        Assert.Empty(opts.ResourceAttributes);
        Assert.Equal(TimeSpan.FromSeconds(10), opts.ExportTimeout);
    }

    [Fact]
    public void ApplyEnvironment_ReadsEndpoint()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel:4317");
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.Equal("http://otel:4317", opts.Endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
        }
    }

    [Fact]
    public void ApplyEnvironment_ReadsProtocol()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.Equal(OtlpProtocol.HttpProtobuf, opts.Protocol);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", null);
        }
    }

    [Fact]
    public void ApplyEnvironment_ReadsServiceName()
    {
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", "my-service");
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.Equal("my-service", opts.ServiceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", null);
        }
    }

    [Fact]
    public void ApplyEnvironment_ReadsHeaders()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", "Authorization=Bearer tok,X-Custom=val");
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.Equal("Bearer tok", opts.Headers["Authorization"]);
            Assert.Equal("val", opts.Headers["X-Custom"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", null);
        }
    }

    [Fact]
    public void ApplyEnvironment_DoesNotOverrideProgrammaticValues()
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel:4317");
        try
        {
            var opts = new OtlpSinkOptions { Endpoint = "http://custom:4317" };
            opts.ApplyEnvironment();
            // Programmatic value was non-default, so env var should not override.
            Assert.Equal("http://custom:4317", opts.Endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
        }
    }

    [Theory]
    [InlineData("Authorization=Bearer tok\r\nX-Injected: yes")]
    [InlineData("Authorization=Bearer tok\nX-Injected: yes")]
    [InlineData("X-Bad\r\nKey=value")]
    public void ApplyEnvironment_RejectsHeadersWithCrLf(string headerValue)
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", headerValue);
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.DoesNotContain(opts.Headers, kv =>
                kv.Key.Contains('\r') || kv.Key.Contains('\n') ||
                kv.Value.Contains('\r') || kv.Value.Contains('\n'));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", null);
        }
    }

    [Fact]
    public void ApplyEnvironment_RejectsResourceAttributesWithCrLf()
    {
        Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", "deployment=prod\r\nevil=injected");
        try
        {
            var opts = new OtlpSinkOptions();
            opts.ApplyEnvironment();
            Assert.DoesNotContain(opts.ResourceAttributes, kv =>
                kv.Key.Contains('\r') || kv.Key.Contains('\n') ||
                kv.Value.Contains('\r') || kv.Value.Contains('\n'));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", null);
        }
    }
}
