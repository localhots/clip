namespace Clip.Filters;

/// <summary>
/// Filters fields by name (case-insensitive). Any field whose key matches is skipped.
/// </summary>
public sealed class FieldNameFilter(IEnumerable<string> fields) : ILogFilter
{
    private readonly HashSet<string> _fields = new(fields, StringComparer.OrdinalIgnoreCase);

    public bool ShouldSkip(string key) => _fields.Contains(key);
}
