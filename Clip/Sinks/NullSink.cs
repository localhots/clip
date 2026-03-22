namespace Clip.Sinks;

/// <summary>No-op sink that discards all entries. Useful for testing and benchmarks.</summary>
public sealed class NullSink : ILogSink
{
    public void Write(
        DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    { }

    public void Dispose() { }
}
