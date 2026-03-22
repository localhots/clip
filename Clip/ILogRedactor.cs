namespace Clip;

/// <summary>
/// Inspects the final merged field list and redacts sensitive values.
/// Runs after all fields (enricher + context + call-site) are merged and deduplicated.
/// To redact a field, replace it at its index: fields[i] = new Field(key, masked).
/// Implementations must be thread-safe.
/// </summary>
public interface ILogRedactor
{
    void Redact(Span<Field> fields);
}
