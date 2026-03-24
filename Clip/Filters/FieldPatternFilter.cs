using System.Text.RegularExpressions;

namespace Clip.Filters;

/// <summary>
/// Filters fields whose key matches a regex pattern. Any field whose key matches is skipped.
/// </summary>
public sealed class FieldPatternFilter : ILogFieldFilter
{
    private readonly Regex _pattern;

    public FieldPatternFilter(Regex pattern)
    {
        _pattern = pattern;
    }

    public FieldPatternFilter(string pattern)
    {
        _pattern = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    }

    public bool ShouldSkip(string key)
    {
        try
        {
            return _pattern.IsMatch(key);
        }
        catch
        {
            // Timeout or error — don't skip (safe default)
            return false;
        }
    }
}
