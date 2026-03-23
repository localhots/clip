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

    /// <summary>
    /// Applies <c>OTEL_EXPORTER_OTLP_*</c> environment variable overrides to any property
    /// that still holds its default value.
    /// </summary>
    internal OtlpSinkOptions ApplyEnvironment()
    {
        if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is { Length: > 0 } ep
            && Endpoint == "http://localhost:4317")
            Endpoint = ep;

        if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") is { Length: > 0 } proto
            && Protocol == OtlpProtocol.Grpc)
        {
            Protocol = proto switch
            {
                "grpc" => OtlpProtocol.Grpc,
                "http/protobuf" => OtlpProtocol.HttpProtobuf,
                _ => Protocol,
            };
        }

        if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS") is { Length: > 0 } hdrs
            && Headers.Count == 0)
        {
            foreach (var pair in hdrs.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx > 0)
                    Headers[pair[..eqIdx].Trim()] = pair[(eqIdx + 1)..].Trim();
            }
        }

        if (Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") is { Length: > 0 } svc
            && ServiceName == "unknown_service")
            ServiceName = svc;

        return this;
    }
}
