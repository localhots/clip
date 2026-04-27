namespace Clip.OpenTelemetry;

/// <summary>
/// Configuration for the OTLP log exporter sink.
/// Programmatic values take precedence; unset properties fall back to
/// <c>OTEL_EXPORTER_OTLP_*</c> environment variables, then defaults.
/// </summary>
public sealed class OtlpSinkOptions
{
    /// <summary>OTLP collector endpoint. Default: <c>http://localhost:4317</c> (gRPC).</summary>
    public string Endpoint { get; set; } = "http://localhost:4317";

    /// <summary>Transport protocol. Default: <see cref="OtlpProtocol.Grpc"/>.</summary>
    public OtlpProtocol Protocol { get; set; } = OtlpProtocol.Grpc;

    /// <summary>Maximum number of log records per export batch. Default: 512.</summary>
    public int BatchSize { get; set; } = 512;

    /// <summary>Maximum time between flushes. Default: 5 seconds.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Bounded queue capacity. When full, oldest entries are dropped. Default: 2048.</summary>
    public int QueueCapacity { get; set; } = 2048;

    /// <summary>Additional headers sent with every export request (e.g. auth tokens).</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>OpenTelemetry service name resource attribute. Default: <c>unknown_service</c>.</summary>
    public string ServiceName { get; set; } = "unknown_service";

    /// <summary>Optional service version resource attribute.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Additional OTLP resource attributes.</summary>
    public Dictionary<string, string> ResourceAttributes { get; set; } = new();

    /// <summary>Timeout for each export request. Default: 10 seconds.</summary>
    public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Retry behavior for failed exports. Default: <see cref="OpenTelemetry.RetryPolicy.None"/>.</summary>
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.None;

    /// <summary>Maximum number of retry attempts per batch. Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for exponential backoff. Default: 500ms.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum delay between retries. Default: 5 seconds.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Applies <c>OTEL_EXPORTER_OTLP_*</c> environment variable overrides to any property
    /// that still holds its default value.
    /// </summary>
    internal OtlpSinkOptions ApplyEnvironment()
    {
        if (Env("OTEL_EXPORTER_OTLP_ENDPOINT") is { } ep && Endpoint == "http://localhost:4317")
            Endpoint = ep;

        if (Env("OTEL_EXPORTER_OTLP_PROTOCOL") is { } proto && Protocol == OtlpProtocol.Grpc)
            Protocol = proto switch
            {
                "grpc" => OtlpProtocol.Grpc,
                "http/protobuf" => OtlpProtocol.HttpProtobuf,
                _ => throw new NotSupportedException(
                    $"Unsupported OTLP protocol: '{proto}'. Supported values: 'grpc', 'http/protobuf'."),
            };

        if (Env("OTEL_EXPORTER_OTLP_HEADERS") is { } headers && Headers.Count == 0)
            ParseKeyValuePairs(headers, Headers);

        if (Env("OTEL_SERVICE_NAME") is { } serviceName && ServiceName == "unknown_service")
            ServiceName = serviceName;

        if (Env("OTEL_RESOURCE_ATTRIBUTES") is { } resAttrs && ResourceAttributes.Count == 0)
            ParseKeyValuePairs(resAttrs, ResourceAttributes);

        return this;
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is { Length: > 0 }
            ? value
            : null;
    }

    internal static void ParseKeyValuePairs(string input, Dictionary<string, string> target)
    {
        foreach (var pair in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0)
                target[pair[..eqIdx].Trim()] = pair[(eqIdx + 1)..].Trim();
        }
    }
}
