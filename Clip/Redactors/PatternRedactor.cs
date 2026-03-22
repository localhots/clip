using System.Text.RegularExpressions;

namespace Clip.Redactors;

/// <summary>
/// Redacts string field values that match a regex pattern.
/// Non-string fields are not inspected. Accepts a pre-compiled Regex
/// (including [GeneratedRegex] source generators) for the best performance.
/// </summary>
public sealed class PatternRedactor : ILogRedactor
{
    private readonly Regex _pattern;
    private readonly string _replacement;

    public PatternRedactor(Regex pattern, string replacement = "***")
    {
        _pattern = pattern;
        _replacement = replacement;
    }

    public PatternRedactor(string pattern, string replacement = "***")
    {
        _pattern = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        _replacement = replacement;
    }

    public void Redact(Span<Field> fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (fields[i].Type != FieldType.String) continue;
            var value = (string?)fields[i].RefValue;
            if (value is null) continue;
            var replaced = _pattern.Replace(value, _replacement);
            if (!ReferenceEquals(replaced, value))
                fields[i] = new Field(fields[i].Key, replaced);
        }
    }
}
