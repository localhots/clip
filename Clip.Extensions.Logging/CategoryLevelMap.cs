using ClipLogLevel = Clip.LogLevel;

namespace Clip.Extensions.Logging;

internal sealed class CategoryLevelMap
{
    private readonly (string Prefix, ClipLogLevel Level)[] _rules;
    private readonly ClipLogLevel _defaultLevel;

    public CategoryLevelMap(Dictionary<string, ClipLogLevel> categoryLevels, ClipLogLevel defaultLevel)
    {
        _defaultLevel = defaultLevel;

        // Sort longest-first for greedy prefix matching
        _rules = categoryLevels
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToArray();
    }

    public ClipLogLevel GetEffectiveLevel(string categoryName)
    {
        foreach (var (prefix, level) in _rules)
            if (categoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                // Must match exactly or at a namespace boundary
                if (categoryName.Length == prefix.Length || categoryName[prefix.Length] == '.')
                    return level;

        return _defaultLevel;
    }
}
