using Clip.Enrichers;

namespace Clip;

/// <summary>
/// Configures enrichers for a specific sink (or group of sinks) registered via
/// <see cref="SinkConfig.Enriched"/>. Works like <see cref="EnricherConfig"/> but
/// returns <c>this</c> for chaining within a lambda.
/// </summary>
public sealed class SinkEnricherConfig
{
    private readonly List<EnricherEntry> _enrichers = [];

    internal EnricherEntry[]? Build() => _enrichers.Count == 0 ? null : [.. _enrichers];

    /// <summary>
    /// Registers a custom <see cref="ILogEnricher"/> that runs on each log call
    /// at or above <paramref name="minLevel"/>.
    /// </summary>
    public SinkEnricherConfig With(ILogEnricher enricher, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(enricher, minLevel));
        return this;
    }

    /// <summary>Adds a constant string field.</summary>
    public SinkEnricherConfig Field(string key, string value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, int value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, long value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, bool value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, double value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, decimal value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }

    /// <inheritdoc cref="Field(string, string, LogLevel)"/>
    public SinkEnricherConfig Field(string key, Guid value, LogLevel minLevel = LogLevel.Trace)
    {
        _enrichers.Add(new EnricherEntry(new ConstantEnricher(new Field(key, value)), minLevel));
        return this;
    }
}
