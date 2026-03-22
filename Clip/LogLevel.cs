namespace Clip;

/// <summary>
/// Log severity levels, ordered from least to most severe.
/// Used for global minimum level, per-sink filtering, and enricher gating.
/// </summary>
public enum LogLevel : byte
{
    /// <summary>Fine-grained diagnostics. Typically disabled in production.</summary>
    Trace,
    /// <summary>Diagnostic information useful during development.</summary>
    Debug,
    /// <summary>Normal operational events (request handled, job completed).</summary>
    Info,
    /// <summary>Unexpected but recoverable situations that deserve attention.</summary>
    Warning,
    /// <summary>Failures that prevented an operation from completing.</summary>
    Error,
    /// <summary>Unrecoverable errors. Logging at this level flushes sinks and terminates the process.</summary>
    Fatal,
}
