using System.Text;

namespace Clip.Sinks;

/// <summary>
/// Formatting options for <see cref="ConsoleSink"/>: timestamp format, ANSI colors,
/// level label text, and minimum message column width for aligned output.
/// </summary>
public sealed class ConsoleFormatConfig
{
    private readonly byte[][] _labelBytes;
    private readonly int _minMessageWidth = 40;
    private readonly IReadOnlyList<string> _levelLabels = ["TRAC", "DEBU", "INFO", "WARN", "ERRO", "FATA"];

    public string TimestampFormat { get; init; } = "yyyy-MM-dd HH:mm:ss.fff";
    public TimeSpan CachePrecision { get; init; } = TimeSpan.FromMilliseconds(1);
    public bool Colors { get; init; } = true;

    /// <summary>
    /// When true (default), strips C0 control characters and DEL from user-supplied
    /// strings (message body, field values, exception data) before writing them, to
    /// prevent ANSI/terminal-control injection from attacker-influenced log values.
    /// Tab is always preserved; CR/LF is preserved in messages and stack traces and
    /// stripped from field values and <see cref="Exception.Data"/> entries.
    /// </summary>
    public bool SanitizeControlCharacters { get; init; } = true;

    /// <summary>
    /// Maximum <see cref="Exception.InnerException"/> chain depth to render. Beyond this,
    /// a truncation sentinel is emitted instead of recursing. Caps stack usage on
    /// pathologically deep chains (which are easy to construct from deserialized data).
    /// Default 32.
    /// </summary>
    public int MaxInnerExceptionDepth { get; init; } = 32;

    public int MinMessageWidth
    {
        get => _minMessageWidth;
        init => _minMessageWidth = Math.Max(0, value);
    }

    public IReadOnlyList<string> LevelLabels
    {
        get => _levelLabels;
        init
        {
            _levelLabels = value;
            _labelBytes = BuildLabelBytes(value);
        }
    }

    public ConsoleFormatConfig()
    {
        _labelBytes = BuildLabelBytes(_levelLabels);
    }

    internal byte[] GetLabelBytes(LogLevel level)
    {
        var idx = (int)level;
        return idx < _labelBytes.Length
            ? _labelBytes[idx]
            : "????"u8.ToArray();
    }

    private static byte[][] BuildLabelBytes(IReadOnlyList<string> labels)
    {
        var result = new byte[labels.Count][];
        for (var i = 0; i < labels.Count; i++)
            result[i] = Encoding.UTF8.GetBytes(labels[i]);
        return result;
    }
}
