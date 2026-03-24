namespace Clip.Redactors;

/// <summary>
/// Redacts fields by name. Any field whose name matches (ordinal, case-insensitive)
/// is replaced with the mask value.
/// </summary>
public sealed class FieldRedactor(IEnumerable<string> fields, string mask = "***") : ILogRedactor
{
    private readonly HashSet<string> _fields = new(fields, StringComparer.OrdinalIgnoreCase);

    public void Redact(ref Field field)
    {
        if (_fields.Contains(field.Key))
            field = new Field(field.Key, mask);
    }
}
