namespace Clip;

/// <summary>
/// Adds fields to every log entry. Enricher fields have the lowest priority:
/// context fields and call-site fields override them on key collision.
/// Implementations must be thread-safe.
/// </summary>
public interface ILogEnricher
{
    void Enrich(List<Field> target);
}
