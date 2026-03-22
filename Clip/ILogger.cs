namespace Clip;

/// <summary>
/// Ergonomic structured logger. Fields are passed as anonymous objects and
/// extracted via reflection (cached). Suitable for most application code
/// where convenience outweighs allocation cost.
/// </summary>
/// <remarks>
/// Register as a singleton in DI. For hot paths where zero allocation matters,
/// inject <see cref="IZeroLogger"/> instead.
/// </remarks>
/// <example>
/// <code>
/// logger.Info("Request handled", new { Method = "GET", Path = "/api", Status = 200 });
///
/// using (logger.AddContext(new { RequestId = "abc-123" }))
///     logger.Info("Processing step", new { Step = "validate" });
/// </code>
/// </example>
public interface ILogger : IDisposable
{
    /// <summary>
    /// Pushes context fields onto the current async scope. All log calls within
    /// the returned scope will include these fields. Dispose the scope to remove them.
    /// </summary>
    /// <param name="fields">
    /// An anonymous object (or any object) whose public properties become key-value fields.
    /// </param>
    /// <returns>A disposable scope. Use with <c>using</c> to auto-remove context on exit.</returns>
    IDisposable AddContext(object fields);

    /// <summary>Logs a message at <see cref="LogLevel.Trace"/> level.</summary>
    /// <param name="message">A plain-text message (not a template). Fields carry structured data separately.</param>
    /// <param name="fields">Optional anonymous object whose properties become structured fields.</param>
    void Trace(string message, object? fields = null);

    /// <summary>Logs a message at <see cref="LogLevel.Debug"/> level.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Debug(string message, object? fields = null);

    /// <summary>Logs a message at <see cref="LogLevel.Info"/> level.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Info(string message, object? fields = null);

    /// <summary>Logs a message at <see cref="LogLevel.Warning"/> level.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Warning(string message, object? fields = null);

    /// <summary>Logs a message at <see cref="LogLevel.Error"/> level.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Error(string message, object? fields = null);

    /// <summary>Logs a message with an exception at <see cref="LogLevel.Error"/> level.</summary>
    /// <param name="message">A plain-text message describing what failed.</param>
    /// <param name="exception">The exception to attach to the log entry.</param>
    /// <param name="fields">Optional anonymous object whose properties become structured fields.</param>
    void Error(string message, Exception exception, object? fields = null);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Fatal"/> level, flushes all sinks,
    /// and terminates the process with exit code 1.
    /// </summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Fatal(string message, object? fields = null);
}

/// <summary>
/// Zero-allocation structured logger. Fields are passed as <see cref="Field"/>
/// structs via <c>params ReadOnlySpan&lt;Field&gt;</c>, avoiding all reflection
/// and heap allocation at the call site.
/// </summary>
/// <remarks>
/// Use this interface in hot paths where allocation budget is critical.
/// For typical application code, prefer <see cref="ILogger"/> for convenience.
/// Both interfaces are implemented by <see cref="Logger"/>.
/// </remarks>
/// <example>
/// <code>
/// logger.Info("Request handled",
///     new Field("Method", "GET"),
///     new Field("Path", "/api"),
///     new Field("Status", 200));
///
/// using (logger.AddContext(new Field("RequestId", "abc-123")))
///     logger.Info("Processing step", new Field("Step", "validate"));
/// </code>
/// </example>
public interface IZeroLogger : IDisposable
{
    /// <summary>
    /// Pushes context fields onto the current async scope. All log calls within
    /// the returned scope will include these fields. Dispose the scope to remove them.
    /// </summary>
    /// <param name="fields">Structured fields to attach to every log entry in this scope.</param>
    /// <returns>A disposable scope. Use with <c>using</c> to auto-remove context on exit.</returns>
    IDisposable AddContext(params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message at <see cref="LogLevel.Trace"/> level with zero allocation.</summary>
    /// <param name="message">A plain-text message (not a template). Fields carry structured data separately.</param>
    /// <param name="fields">Structured fields to attach to this log entry.</param>
    void Trace(string message, params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message at <see cref="LogLevel.Debug"/> level with zero allocation.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Debug(string message, params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message at <see cref="LogLevel.Info"/> level with zero allocation.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Info(string message, params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message at <see cref="LogLevel.Warning"/> level with zero allocation.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Warning(string message, params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message at <see cref="LogLevel.Error"/> level with zero allocation.</summary>
    /// <inheritdoc cref="Trace" path="/param"/>
    void Error(string message, params ReadOnlySpan<Field> fields);

    /// <summary>Logs a message with an exception at <see cref="LogLevel.Error"/> level with zero allocation.</summary>
    /// <param name="message">A plain-text message describing what failed.</param>
    /// <param name="exception">The exception to attach to the log entry.</param>
    /// <param name="fields">Structured fields to attach to this log entry.</param>
    void Error(string message, Exception exception, params ReadOnlySpan<Field> fields);
}
