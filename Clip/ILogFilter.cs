namespace Clip;

/// <summary>
/// Determines whether a field should be skipped by name. Filtered fields are never
/// added to the field list — they never reach redactors or sinks.
/// Implementations must be thread-safe.
/// </summary>
public interface ILogFilter
{
    /// <summary>Returns <c>true</c> if a field with this key should be excluded from the log entry.</summary>
    bool ShouldSkip(string key);
}
