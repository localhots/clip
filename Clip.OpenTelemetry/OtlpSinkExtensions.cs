namespace Clip.OpenTelemetry;

/// <summary>
/// Extension methods for registering the OTLP sink on <see cref="SinkConfig"/>.
/// </summary>
public static class OtlpSinkExtensions
{
    /// <summary>
    /// Adds an OTLP log exporter sink that sends structured log data to an
    /// OpenTelemetry collector via gRPC or HTTP/protobuf.
    /// </summary>
    /// <param name="sinks">The sink configuration builder.</param>
    /// <param name="configure">Optional action to configure sink options.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    /// <example>
    /// <code>
    /// var logger = Logger.Create(config =&gt; config
    ///     .WriteTo.Otlp(opts =&gt; {
    ///         opts.Endpoint = "http://collector:4317";
    ///         opts.ServiceName = "my-service";
    ///     }));
    /// </code>
    /// </example>
    public static LoggerConfig Otlp(
        this SinkConfig sinks,
        Action<OtlpSinkOptions>? configure = null,
        LogLevel minLevel = LogLevel.Trace)
    {
        var options = new OtlpSinkOptions();
        configure?.Invoke(options);
        return sinks.Sink(new OtlpSink(options), minLevel);
    }
}
