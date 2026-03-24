namespace Clip.Filters;

/// <summary>
/// Filters fields by name (case-insensitive). Any field whose key matches is skipped.
/// </summary>
public sealed class FieldNameFilter(IEnumerable<string> fields) : ILogFieldFilter
{
    private readonly HashSet<string> _fields = new(fields, StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> Keys => _fields;

    public bool ShouldSkip(string key) => _fields.Contains(key);
}
