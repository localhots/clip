using Clip.Internal;

namespace Clip.Sinks;

/// <summary>
/// Human-readable sink with ANSI color support. Writes to stderr by default.
/// Configurable via <see cref="ConsoleFormatConfig"/> (timestamp format, colors, level labels, message width).
/// Thread-safe.
/// </summary>
public sealed class ConsoleSink(ConsoleFormatConfig config, Stream? output = null) : ILogSink
{
    private readonly Stream _output = output ?? Console.OpenStandardError();
    private readonly bool _ownsStream = output is null;

    private readonly byte[][] _labelBytes =
    [
        config.GetLabelBytes(LogLevel.Trace),
        config.GetLabelBytes(LogLevel.Debug),
        config.GetLabelBytes(LogLevel.Info),
        config.GetLabelBytes(LogLevel.Warning),
        config.GetLabelBytes(LogLevel.Error),
        config.GetLabelBytes(LogLevel.Fatal),
    ];

    private readonly bool _colors = config.Colors;
    private readonly int _minMessageWidth = config.MinMessageWidth;
    private readonly bool _sanitize = config.SanitizeControlCharacters;
    private readonly int _maxInnerExceptionDepth = config.MaxInnerExceptionDepth;
    private readonly LogBuffer _buffer = new();
    private readonly TimestampCache _tsCache = new(config.TimestampFormat, config.CachePrecision);
    private readonly Lock _lock = new();

    public ConsoleSink(Stream? output = null, bool colors = true)
        : this(new ConsoleFormatConfig { Colors = colors }, output)
    {
    }

    public void Write(DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        lock (_lock)
        {
            _buffer.Reset();

            // Timestamp
            _tsCache.WriteTo(_buffer, timestamp);
            _buffer.WriteByte((byte)' ');

            // Level (colored)
            if (_colors) _buffer.WriteBytes(LevelColor(level));
            _buffer.WriteBytes(_labelBytes[(int)level]);
            if (_colors) _buffer.WriteBytes("\e[0m"u8);
            _buffer.WriteByte((byte)' ');

            // Message (bold)
            if (_colors) _buffer.WriteBytes("\e[1m"u8);
            WriteUserText(_buffer, message, allowMultiline: true);
            if (_colors) _buffer.WriteBytes("\e[0m"u8);

            // Fields
            if (fields.Length > 0)
            {
                var pad = _minMessageWidth - message.Length;
                if (pad > 0) _buffer.WritePadding(pad);
                _buffer.WriteBytes("  "u8);
                WriteFieldsSorted(_buffer, level, fields);
            }

            // Exception
            if (exception != null)
            {
                _buffer.WriteByte((byte)'\n');
                _buffer.WriteBytes("  "u8);
                WriteException(_buffer, exception);
            }

            _buffer.WriteByte((byte)'\n');
            _output.Write(_buffer.WrittenSpan);
        }
    }

    private void WriteFieldsSorted(LogBuffer buf, LogLevel level, ReadOnlySpan<Field> fields)
    {
        const int stackLimit = 64;
        var count = fields.Length;

        // Sort indices instead of fields to avoid copying 32-byte structs.
        // stackalloc keeps this zero-alloc for typical field counts.
        var indices = count <= stackLimit
            ? stackalloc int[count]
            : new int[count]; // Rare fallback — allocates

        for (var i = 0; i < count; i++) indices[i] = i;

        // Insertion sort: O(n²) but optimal for small n (typically <10 fields),
        // branch-friendly, and works directly on stackalloc'd memory.
        for (var i = 1; i < indices.Length; i++)
            for (var j = i; j > 0; j--)
            {
                if (string.CompareOrdinal(fields[indices[j]].Key, fields[indices[j - 1]].Key) >= 0) break;
                (indices[j], indices[j - 1]) = (indices[j - 1], indices[j]);
            }

        for (var i = 0; i < indices.Length; i++)
        {
            if (i > 0) buf.WriteByte((byte)' ');
            ref readonly var f = ref fields[indices[i]];

            if (_colors)
            {
                buf.WriteBytes(LevelColor(level));
                buf.WriteString(f.Key);
                buf.WriteBytes("\e[0m"u8);
                buf.WriteByte((byte)'=');
            }
            else
            {
                buf.WriteTextFieldPrefix(f.Key);
            }

            WriteFieldValue(buf, in f);
        }
    }

    private void WriteUserText(LogBuffer buf, string s, bool allowMultiline)
    {
        if (_sanitize)
            buf.WriteSanitized(s, allowMultiline);
        else
            buf.WriteString(s);
    }

    private void WriteFieldValue(LogBuffer buf, in Field f)
    {
        switch (f.Type)
        {
            case FieldType.Bool: buf.WriteBool(f.BoolValue); break;
            case FieldType.Int: buf.WriteLong(f.IntValue); break;
            case FieldType.Long: buf.WriteLong(f.LongValue); break;
            case FieldType.ULong: buf.WriteULong(unchecked((ulong)f.LongValue)); break;
            case FieldType.Float: buf.WriteFloat(f.FloatValue); break;
            case FieldType.Double: buf.WriteDouble(f.DoubleValue); break;
            case FieldType.DateTime: buf.WriteDateTime(f.LongValue); break;
            case FieldType.String: WriteUserText(buf, (string?)f.RefValue ?? "null", allowMultiline: false); break;
            case FieldType.Decimal: buf.WriteDecimal(f.DecimalValue); break;
            case FieldType.Guid: buf.WriteGuid(f.GuidValue); break;
            case FieldType.Object:
                if (f.RefValue is IUtf8SpanFormattable fmt)
                    buf.WriteUtf8Formattable(fmt);
                else
                    WriteUserText(buf, f.RefValue?.ToString() ?? "null", allowMultiline: false);
                break;
        }
    }

    private void WriteException(LogBuffer buf, Exception ex, int depth = 0)
    {
        buf.WriteString(ex.GetType().FullName ?? ex.GetType().Name);
        buf.WriteBytes(": "u8);
        WriteUserText(buf, ex.Message, allowMultiline: true);

        if (ex.Data.Count > 0)
        {
            buf.WriteBytes("\n  Data:"u8);
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
            {
                buf.WriteBytes("\n    "u8);
                WriteUserText(buf, entry.Key.ToString() ?? "null", allowMultiline: false);
                buf.WriteBytes(" = "u8);
                WriteUserText(buf, entry.Value?.ToString() ?? "null", allowMultiline: false);
            }
        }

        if (ex.InnerException != null)
        {
            if (depth + 1 >= _maxInnerExceptionDepth)
                buf.WriteBytes("\n ---> ... (inner exceptions truncated)"u8);
            else
            {
                buf.WriteBytes("\n ---> "u8);
                WriteException(buf, ex.InnerException, depth + 1);
                buf.WriteBytes("\n   --- End of inner exception stack trace ---"u8);
            }
        }

        var st = ex.StackTrace;
        if (st != null)
        {
            buf.WriteByte((byte)'\n');
            WriteUserText(buf, st, allowMultiline: true);
        }
    }

    private static ReadOnlySpan<byte> LevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "\e[37m"u8,
            LogLevel.Debug => "\e[37m"u8,
            LogLevel.Info => "\e[36m"u8,
            LogLevel.Warning => "\e[33m"u8,
            LogLevel.Error => "\e[31m"u8,
            LogLevel.Fatal => "\e[38;5;255m\e[48;5;88m"u8,
            _ => ReadOnlySpan<byte>.Empty,
        };
    }


    public void Dispose()
    {
        if (_ownsStream) _output.Dispose();
    }
}
