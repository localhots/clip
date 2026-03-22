namespace Clip.Redactors;

/// <summary>
/// Redacts fields by name. Any field whose name matches (ordinal, case-insensitive)
/// is replaced with the mask value.
/// </summary>
public sealed class FieldRedactor(IEnumerable<string> fields, string mask = "***") : ILogRedactor
{
    private readonly HashSet<string> _fields = new(fields, StringComparer.OrdinalIgnoreCase);

    public void Redact(Span<Field> fields)
    {
        for (var i = 0; i < fields.Length; i++)
            if (_fields.Contains(fields[i].Key))
                fields[i] = new Field(fields[i].Key, mask);
    }
}
