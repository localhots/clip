namespace Clip.Context;

/// <summary>
/// Returned by <see cref="Logger.SuppressLogging"/>. While the scope is live,
/// every <see cref="Logger"/> on the current async flow drops log calls before
/// any allocation, enricher, or sink invocation. Used by the OTLP sink to break
/// feedback loops where the export's own gRPC/HTTP traffic is instrumented and
/// re-enters Clip; also exposed for callers writing custom outbound sinks.
/// </summary>
public readonly struct SuppressionScope : IDisposable
{
    private readonly bool _previous;

    internal SuppressionScope(bool previous) => _previous = previous;

    public void Dispose() => LogSuppression.Restore(_previous);
}
