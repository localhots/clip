namespace Clip.Sinks;

/// <summary>
/// Receives formatted log entries. Implementations must be thread-safe — multiple threads
/// may call <see cref="Write"/> concurrently. The <c>fields</c> span is valid
/// only for the duration of the call; copy if you need to store it.
/// </summary>
public interface ILogSink : IDisposable
{
    /// <summary>Writes a single log entry to the output destination.</summary>
    /// <param name="timestamp">UTC timestamp of the log entry.</param>
    /// <param name="level">Severity level.</param>
    /// <param name="message">Plain-text message (not a template).</param>
    /// <param name="fields">Structured fields. Valid only during this call.</param>
    /// <param name="exception">Optional exception attached to the entry.</param>
    void Write(
        DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception);
}
