namespace Clip;

/// <summary>
/// Redacts a single field value. Called for each field after filtering and deduplication.
/// To redact, assign a new value: <c>field = new Field(field.Key, "***")</c>.
/// If the field should not be redacted, leave it unchanged.
/// Implementations must be thread-safe.
/// </summary>
public interface ILogRedactor
{
    void Redact(ref Field field);
}
