using System.Text;

namespace Clip.Sinks;

/// <summary>
/// Formatting options for <see cref="JsonSink"/>: key names for timestamp, level, message,
/// fields, and error; timestamp format; and level label strings.
/// </summary>
public sealed class JsonFormatConfig
{
    private readonly byte[][] _labelBytes;
    private readonly IReadOnlyList<string> _levelLabels = ["trace", "debug", "info", "warning", "error", "fatal"];

    public string TimestampFormat { get; init; } = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    public TimeSpan CachePrecision { get; init; } = TimeSpan.FromMilliseconds(1);
    public string TimestampKey { get; init; } = "ts";
    public string LevelKey { get; init; } = "level";
    public string MessageKey { get; init; } = "msg";
    public string? FieldsKey { get; init; }
    public string ErrorKey { get; init; } = "error";

    /// <summary>
    /// Maximum <see cref="Exception.InnerException"/> chain depth to render. Beyond this,
    /// a truncation sentinel (<c>{"truncated":true}</c>) is emitted instead of recursing.
    /// Caps stack usage on pathologically deep chains. Default 32.
    /// </summary>
    public int MaxInnerExceptionDepth { get; init; } = 32;

    /// <summary>
    /// Maximum bytes a single rendered log entry may occupy. When a log call would push
    /// past this, the partial entry is discarded and a small fixed JSON object
    /// (<c>{"ts":..., "level":..., "msg":"&lt;log entry truncated&gt;", "truncated":true}</c>)
    /// is emitted instead — keeping the output line valid JSON. Default 4 MiB.
    /// </summary>
    public int MaxLogEntryBytes { get; init; } = 4 * 1024 * 1024;

    public IReadOnlyList<string> LevelLabels
    {
        get => _levelLabels;
        init
        {
            _levelLabels = value;
            _labelBytes = BuildLabelBytes(value);
        }
    }

    public JsonFormatConfig()
    {
        _labelBytes = BuildLabelBytes(_levelLabels);
    }

    internal byte[] GetLabelBytes(LogLevel level)
    {
        var idx = (int)level;
        return idx < _labelBytes.Length
            ? _labelBytes[idx]
            : "unknown"u8.ToArray();
    }

    /// <summary>
    /// Escapes a string for safe embedding inside a JSON string value.
    /// Returns the original string when no escaping is needed (zero-alloc common case).
    /// Uses the same escape rules as <see cref="Internal.LogBuffer.WriteJsonStringEscaped"/>.
    /// </summary>
    internal static string JsonEscape(string s)
    {
        // Fast path: scan for chars needing escape. Config keys are almost always
        // plain ASCII identifiers, so this returns the original string with no allocation.
        foreach (var c in s)
            if (c < 0x20 || c == '"' || c == '\\')
                return JsonEscapeSlow(s);
        return s;
    }

    private static string JsonEscapeSlow(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append(@"\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }

        return sb.ToString();
    }

    private static byte[][] BuildLabelBytes(IReadOnlyList<string> labels)
    {
        var result = new byte[labels.Count][];
        for (var i = 0; i < labels.Count; i++)
            result[i] = Encoding.UTF8.GetBytes(JsonEscape(labels[i]));
        return result;
    }
}
