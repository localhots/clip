namespace Clip.Sinks;

/// <summary>A captured log entry with fields copied to an array.</summary>
public sealed record LogRecord(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message,
    Field[] Fields,
    Exception? Exception);

/// <summary>
/// In-memory sink that stores entries as <see cref="LogRecord"/> objects.
/// Useful for testing — assert against <see cref="Records"/> after logging. Thread-safe.
/// </summary>
public sealed class ListSink : ILogSink
{
    private readonly Lock _lock = new();
    private readonly List<LogRecord> _records = [];

    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_lock) return [.. _records];
        }
    }

    public void Write(
        DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        var record = new LogRecord(timestamp, level, message, fields.ToArray(), exception);
        lock (_lock) _records.Add(record);
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
    }

    public void Dispose() { }
}
