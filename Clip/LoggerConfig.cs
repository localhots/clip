using Clip.Redactors;
using Clip.Sinks;
using System.Text.RegularExpressions;

namespace Clip;

/// <summary>
/// Root configuration object for building a <see cref="Logger"/>. Provides fluent access
/// to sink, enricher, and redactor configuration via <see cref="WriteTo"/>,
/// <see cref="Enrich"/>, and <see cref="Redact"/>.
/// </summary>
/// <example>
/// <code>
/// var logger = Logger.Create(config => config
///     .MinimumLevel(LogLevel.Debug)
///     .Enrich.Field("app", "my-service")
///     .Enrich.With(new HttpRequestEnricher(), minLevel: LogLevel.Warning)
///     .Redact.Fields("password", "token")
///     .WriteTo.Console()
///     .WriteTo.Json(output: File.OpenWrite("app.log")));
/// </code>
/// </example>
public sealed class LoggerConfig
{
    internal LogLevel MinLevel { get; private set; } = LogLevel.Info;

    /// <summary>Configures where log entries are written.</summary>
    public SinkConfig WriteTo { get; }

    /// <summary>Configures fields that are automatically added to log entries.</summary>
    public EnricherConfig Enrich { get; }

    /// <summary>Configures redaction rules applied to field values before they reach sinks.</summary>
    public RedactorConfig Redact { get; }

    /// <summary>Configures field filters that prevent matching fields from reaching sinks.</summary>
    public FilterConfig Filter { get; }

    public LoggerConfig()
    {
        WriteTo = new SinkConfig(this);
        Enrich = new EnricherConfig(this);
        Redact = new RedactorConfig(this);
        Filter = new FilterConfig(this);
    }

    /// <summary>
    /// Sets the global minimum log level. Log calls below this level are skipped
    /// before any work is done (zero overhead). Defaults to <see cref="LogLevel.Info"/>.
    /// </summary>
    /// <param name="level">The minimum level at which log entries are processed.</param>
    public LoggerConfig MinimumLevel(LogLevel level)
    {
        MinLevel = level;
        return this;
    }
}

/// <summary>
/// Configures enrichers that automatically add fields to log entries.
/// Enricher fields have the lowest priority — context fields and call-site fields
/// override them on key collision.
/// </summary>
/// <remarks>
/// Each enricher can be level-gated via <c>minLevel</c>: it only fires when the log entry's
/// level is at or above the threshold. Use this to attach verbose diagnostic data
/// (HTTP headers, request bodies) only to warnings and errors, keeping routine logs clean.
/// </remarks>
public sealed class EnricherConfig
{
    private readonly LoggerConfig _parent;
    private readonly List<EnricherEntry> _enrichers = [];

    internal EnricherConfig(LoggerConfig parent) => _parent = parent;

    internal EnricherEntry[]? Build() => _enrichers.Count == 0 ? null : [.. _enrichers];

    /// <summary>
    /// Registers a custom <see cref="ILogEnricher"/> that runs on each log call
    /// at or above <paramref name="minLevel"/>.
    /// </summary>
    /// <param name="enricher">The enricher instance. Must be thread-safe.</param>
    /// <param name="minLevel">
    /// Minimum log level at which this enricher fires. Defaults to <see cref="LogLevel.Trace"/> (always).
    /// Set to <see cref="LogLevel.Warning"/> to enrich only warnings, errors, and fatals.
    /// </param>
    public LoggerConfig With(ILogEnricher enricher, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(enricher, minLevel));
        return _parent;
    }

    /// <summary>
    /// Adds a constant string field to every log entry at or above <paramref name="minLevel"/>.
    /// </summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The field value.</param>
    /// <param name="minLevel">
    /// Minimum log level at which this field is included. Defaults to <see cref="LogLevel.Trace"/> (always).
    /// </param>
    public LoggerConfig Field(string key, string value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, int value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, long value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, bool value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, double value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, decimal value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public LoggerConfig Field(string key, Guid value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return _parent;
    }

    private sealed class ConstantEnricher(Field field) : ILogEnricher
    {
        public void Enrich(List<Field> target) => target.Add(field);
    }
}

/// <summary>
/// Configures redactors that scrub sensitive values from fields before they reach sinks.
/// Redactors run after all fields (enricher + context + call-site) are merged and deduplicated.
/// </summary>
public sealed class RedactorConfig
{
    private readonly LoggerConfig _parent;
    private readonly List<ILogRedactor> _redactors = [];

    internal RedactorConfig(LoggerConfig parent) => _parent = parent;

    internal ILogRedactor[]? Build() => _redactors.Count == 0 ? null : [.. _redactors];

    /// <summary>Registers a custom <see cref="ILogRedactor"/>. Must be thread-safe.</summary>
    /// <param name="redactor">The redactor instance.</param>
    public LoggerConfig With(ILogRedactor redactor)
    {
        _redactors.Add(redactor);
        return _parent;
    }

    /// <summary>
    /// Redacts a single field by key (case-insensitive). The value is replaced with
    /// <paramref name="replacement"/>.
    /// </summary>
    /// <param name="key">The field name to redact.</param>
    /// <param name="replacement">The replacement string for the redacted value.</param>
    public LoggerConfig Field(string key, string replacement = "***")
    {
        _redactors.Add(new FieldRedactor([key], replacement));
        return _parent;
    }

    /// <summary>
    /// Redacts fields whose key matches any of the specified names (case-insensitive).
    /// Matching field values are replaced with <c>"***"</c>.
    /// </summary>
    /// <param name="keys">Field names to redact (e.g. <c>"password"</c>, <c>"token"</c>).</param>
    public LoggerConfig Fields(params string[] keys)
    {
        _redactors.Add(new FieldRedactor(keys));
        return _parent;
    }

    /// <summary>
    /// Redacts string field values that match the given regex pattern.
    /// Matching substrings are replaced with <paramref name="replacement"/>.
    /// </summary>
    /// <param name="pattern">A regular expression pattern to match against string field values.</param>
    /// <param name="replacement">The replacement string for matched substrings.</param>
    public LoggerConfig Pattern(string pattern, string replacement = "***")
    {
        _redactors.Add(new PatternRedactor(pattern, replacement));
        return _parent;
    }

    /// <inheritdoc cref="Pattern(string, string)"/>
    public LoggerConfig Pattern(Regex pattern, string replacement = "***")
    {
        _redactors.Add(new PatternRedactor(pattern, replacement));
        return _parent;
    }
}

/// <summary>
/// Configures where log entries are written. Multiple sinks can be registered — each
/// receives every log entry that meets its <c>minLevel</c> threshold.
/// If no sinks are configured, defaults to a colored console sink at <see cref="LogLevel.Trace"/>.
/// </summary>
public sealed class SinkConfig
{
    private readonly LoggerConfig _parent;
    private readonly List<(ILogSink Sink, LogLevel MinLevel)> _sinks = [];

    internal SinkConfig(LoggerConfig parent)
    {
        _parent = parent;
    }

    internal (ILogSink Sink, LogLevel MinLevel)[] Build()
    {
        if (_sinks.Count == 0)
            _sinks.Add((new ConsoleSink(), LogLevel.Trace));
        return [.. _sinks];
    }

    /// <summary>Adds a human-readable console sink with ANSI colors.</summary>
    /// <param name="output">Target stream. Defaults to stderr.</param>
    /// <param name="colors">Whether to emit ANSI color codes.</param>
    /// <param name="minLevel">Minimum level for this sink. Entries below this level are not written.</param>
    public LoggerConfig Console(Stream? output = null, bool colors = true, LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new ConsoleSink(output, colors), minLevel));
        return _parent;
    }

    /// <summary>Adds a human-readable console sink with custom formatting.</summary>
    /// <param name="config">Format settings (timestamp format, colors, level labels, message width).</param>
    /// <param name="output">Target stream. Defaults to stderr.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig Console(ConsoleFormatConfig config, Stream? output = null, LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new ConsoleSink(config, output), minLevel));
        return _parent;
    }

    /// <summary>Adds a JSON Lines sink (one JSON object per log entry, newline-delimited).</summary>
    /// <param name="output">Target stream. Defaults to stderr.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig Json(Stream? output = null, LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new JsonSink(output), minLevel));
        return _parent;
    }

    /// <summary>Adds a JSON Lines sink with custom key names and timestamp format.</summary>
    /// <param name="config">Format settings (key names for timestamp, level, message, fields, error).</param>
    /// <param name="output">Target stream. Defaults to stderr.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig Json(JsonFormatConfig config, Stream? output = null, LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new JsonSink(config, output), minLevel));
        return _parent;
    }

    /// <summary>Adds a no-op sink that discards all entries. Useful for testing and benchmarks.</summary>
    public LoggerConfig Null()
    {
        _sinks.Add((new NullSink(), LogLevel.Trace));
        return _parent;
    }

    /// <summary>
    /// Adds a rolling file sink that writes JSON Lines with automatic size-based rotation.
    /// </summary>
    /// <param name="path">Path to the log file. Rolled files are suffixed with <c>.1</c>, <c>.2</c>, etc.</param>
    /// <param name="maxFileSize">Soft size limit per file in bytes. Defaults to 10 MB.</param>
    /// <param name="maxRetainedFiles">Maximum number of rolled files to keep. 0 for unlimited.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig File(
        string path,
        long maxFileSize = 10 * 1024 * 1024,
        int maxRetainedFiles = 7,
        LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new FileSink(path, maxFileSize, maxRetainedFiles), minLevel));
        return _parent;
    }

    /// <summary>
    /// Adds a rolling file sink with custom JSON format settings.
    /// </summary>
    /// <param name="path">Path to the log file.</param>
    /// <param name="config">JSON format settings for the file output.</param>
    /// <param name="maxFileSize">Soft size limit per file in bytes. Defaults to 10 MB.</param>
    /// <param name="maxRetainedFiles">Maximum number of rolled files to keep. 0 for unlimited.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig File(
        string path,
        JsonFormatConfig config,
        long maxFileSize = 10 * 1024 * 1024,
        int maxRetainedFiles = 7,
        LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((new FileSink(path, maxFileSize, maxRetainedFiles, config), minLevel));
        return _parent;
    }

    /// <summary>Registers a custom <see cref="ILogSink"/> implementation.</summary>
    /// <param name="sink">The sink instance. Must be thread-safe.</param>
    /// <param name="minLevel">Minimum level for this sink.</param>
    public LoggerConfig Sink(ILogSink sink, LogLevel minLevel = LogLevel.Trace)
    {
        _sinks.Add((sink, minLevel));
        return _parent;
    }

    /// <summary>
    /// Wraps inner sinks in a background processing layer. Log calls enqueue entries into
    /// a bounded channel and return immediately; a dedicated drain task writes to the inner
    /// sinks off the calling thread.
    /// </summary>
    /// <remarks>
    /// When the channel is full, the oldest entry is dropped (bounded, non-blocking).
    /// On dispose, the drain task is given up to 5 seconds to flush remaining entries.
    /// </remarks>
    /// <param name="configure">Configures the inner sinks that will receive entries on the background thread.</param>
    /// <param name="capacity">Maximum number of entries the channel can hold before dropping.</param>
    /// <param name="minLevel">Minimum level applied as a floor to all inner sinks.</param>
    public LoggerConfig Background(
        Action<SinkConfig> configure,
        int capacity = 1024,
        LogLevel minLevel = LogLevel.Trace)
    {
        var inner = new SinkConfig(_parent);
        configure(inner);
        foreach (var (sink, sinkLevel) in inner.Build())
            _sinks.Add((BackgroundSink.Create(sink, capacity), sinkLevel > minLevel ? sinkLevel : minLevel));
        return _parent;
    }
}

/// <summary>
/// Configures field filters that skip matching fields entirely — filtered fields never
/// reach redactors or sinks.
/// </summary>
public sealed class FilterConfig
{
    private readonly LoggerConfig _parent;
    private readonly List<ILogFieldFilter> _filters = [];

    internal FilterConfig(LoggerConfig parent) => _parent = parent;

    internal (HashSet<string>? Names, ILogFieldFilter[]? Custom) Build()
    {
        if (_filters.Count == 0) return (null, null);

        HashSet<string>? names = null;
        List<ILogFieldFilter>? custom = null;

        foreach (var filter in _filters)
        {
            if (filter is Filters.FieldNameFilter nf)
            {
                names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in nf.Keys) names.Add(key);
            }
            else
            {
                custom ??= [];
                custom.Add(filter);
            }
        }

        return (names, custom?.Count > 0 ? [.. custom] : null);
    }

    /// <summary>Registers a custom <see cref="ILogFieldFilter"/>. Must be thread-safe.</summary>
    /// <param name="filter">The filter instance.</param>
    public LoggerConfig With(ILogFieldFilter filter)
    {
        _filters.Add(filter);
        return _parent;
    }

    /// <summary>
    /// Filters fields whose key matches any of the specified names (case-insensitive).
    /// Matching fields are skipped during collection and never reach redactors or sinks.
    /// </summary>
    /// <param name="keys">Field names to filter.</param>
    public LoggerConfig Fields(params string[] keys)
    {
        _filters.Add(new Filters.FieldNameFilter(keys));
        return _parent;
    }

    /// <summary>
    /// Filters fields whose key matches the given regex pattern.
    /// </summary>
    /// <param name="pattern">A regular expression pattern to match against field keys.</param>
    public LoggerConfig Pattern(string pattern)
    {
        _filters.Add(new Filters.FieldPatternFilter(pattern));
        return _parent;
    }

    /// <inheritdoc cref="Pattern(string)"/>
    public LoggerConfig Pattern(Regex pattern)
    {
        _filters.Add(new Filters.FieldPatternFilter(pattern));
        return _parent;
    }
}
