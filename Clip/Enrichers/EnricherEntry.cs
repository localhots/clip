namespace Clip.Enrichers;

internal readonly struct EnricherEntry(ILogEnricher enricher, LogLevel minLevel)
{
    public readonly ILogEnricher Enricher = enricher;
    public readonly LogLevel MinLevel = minLevel;
}
