using System.Collections;
using System.Runtime.InteropServices;
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
    private readonly int _maxCollectionItems = config.MaxCollectionItems;
    private readonly int _maxCollectionDepth = config.MaxCollectionDepth;
    private readonly LogBuffer _buffer = new(config.MaxLogEntryBytes);
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
            // Mark each completed section so a saturation later in the entry still
            // ships everything up to the previous boundary. MarkSafePoint is a no-op
            // once saturated, so a partial section can never advance the safe point.
            _buffer.MarkSafePoint();

            // Fields
            if (fields.Length > 0)
            {
                var pad = _minMessageWidth - message.Length;
                if (pad > 0) _buffer.WritePadding(pad);
                _buffer.WriteBytes("  "u8);
                WriteFieldsSorted(_buffer, level, fields);
                _buffer.MarkSafePoint();
            }

            // Exception
            if (exception != null)
            {
                _buffer.WriteByte((byte)'\n');
                _buffer.WriteBytes("  "u8);
                WriteException(_buffer, exception);
                _buffer.MarkSafePoint();
            }

            _buffer.WriteByte((byte)'\n');

            // If any write hit the size cap, append `<truncated>\n` to the partial line.
            // Console output is plain text — mid-line truncation is readable, no rewind
            // of content needed, just a clean termination.
            if (_buffer.Saturated)
            {
                _buffer.RewindToSafePoint();
                _buffer.WriteMarker(" <truncated>\n"u8);
            }

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
            buf.MarkSafePoint();
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
            case FieldType.Object: WriteObjectValue(buf, f.RefValue, depth: 0); break;
        }
    }

    //
    // Object rendering
    //

    /// <summary>
    /// Renders a boxed field value. Collections are expanded (<c>[a, b, c]</c> for
    /// sequences, <c>{k=v, k=v}</c> for dictionaries) rather than falling through to
    /// <c>ToString()</c>, which for most collection types yields only the type name.
    /// </summary>
    private void WriteObjectValue(LogBuffer buf, object? value, int depth)
    {
        switch (value)
        {
            case null: buf.WriteBytes("null"u8); return;
            case string s: WriteUserText(buf, s, allowMultiline: false); return;
            // Covers every primitive numeric type plus DateTime/DateTimeOffset/TimeSpan/Guid:
            // formats straight into the buffer with no intermediate string.
            case IUtf8SpanFormattable fmt: buf.WriteUtf8Formattable(fmt); return;
            case IDictionary dict: WriteDictionary(buf, dict, depth); return;
            case IEnumerable seq: WriteSequence(buf, seq, depth); return;
            default: WriteUserText(buf, value.ToString() ?? "null", allowMultiline: false); return;
        }
    }

    private void WriteSequence(LogBuffer buf, IEnumerable seq, int depth)
    {
        if (depth >= _maxCollectionDepth)
        {
            buf.WriteBytes("[...]"u8);
            return;
        }

        // Typed fast paths: iterate the backing storage directly, so neither the elements
        // nor an enumerator are allocated. CollectionsMarshal.AsSpan is O(1) and safe here
        // because nothing in this call path mutates the list.
        switch (seq)
        {
            case string[] a: WriteStringSpan(buf, a); return;
            case List<string> l: WriteStringSpan(buf, CollectionsMarshal.AsSpan(l)); return;
            case int[] a: WriteFormattableSpan<int>(buf, a); return;
            case List<int> l: WriteFormattableSpan<int>(buf, CollectionsMarshal.AsSpan(l)); return;
            case long[] a: WriteFormattableSpan<long>(buf, a); return;
            case List<long> l: WriteFormattableSpan<long>(buf, CollectionsMarshal.AsSpan(l)); return;
            case double[] a: WriteFormattableSpan<double>(buf, a); return;
            case List<double> l: WriteFormattableSpan<double>(buf, CollectionsMarshal.AsSpan(l)); return;
        }

        buf.WriteByte((byte)'[');

        // Indexed path for anything else list-shaped (other arrays, other List<T>,
        // Collection<T>, ...): no enumerator allocation, though value-type elements box.
        if (seq is IList list)
        {
            var shown = Math.Min(list.Count, _maxCollectionItems);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0) buf.WriteBytes(", "u8);
                WriteObjectValue(buf, list[i], depth + 1);
                if (buf.Saturated) break;
            }

            WriteOverflow(buf, list.Count - shown, shown);
        }
        else
        {
            var n = 0;
            foreach (var item in seq)
            {
                // Also the guard that keeps an unbounded (lazy, possibly infinite) sequence
                // from spinning forever once the buffer has stopped accepting writes.
                if (n == _maxCollectionItems)
                {
                    if (n > 0) buf.WriteBytes(", "u8);
                    buf.WriteBytes("..."u8);
                    break;
                }

                if (n > 0) buf.WriteBytes(", "u8);
                WriteObjectValue(buf, item, depth + 1);
                n++;
                if (buf.Saturated) break;
            }
        }

        buf.WriteByte((byte)']');
    }

    private void WriteDictionary(LogBuffer buf, IDictionary dict, int depth)
    {
        if (depth >= _maxCollectionDepth)
        {
            buf.WriteBytes("{...}"u8);
            return;
        }

        buf.WriteByte((byte)'{');
        var n = 0;
        foreach (DictionaryEntry entry in dict)
        {
            if (n == _maxCollectionItems)
            {
                if (n > 0) buf.WriteBytes(", "u8);
                buf.WriteBytes("..."u8);
                break;
            }

            if (n > 0) buf.WriteBytes(", "u8);
            WriteObjectValue(buf, entry.Key, depth + 1);
            buf.WriteByte((byte)'=');
            WriteObjectValue(buf, entry.Value, depth + 1);
            n++;
            if (buf.Saturated) break;
        }

        buf.WriteByte((byte)'}');
    }

    private void WriteStringSpan(LogBuffer buf, ReadOnlySpan<string> items)
    {
        buf.WriteByte((byte)'[');
        var shown = Math.Min(items.Length, _maxCollectionItems);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) buf.WriteBytes(", "u8);
            if (items[i] is null) buf.WriteBytes("null"u8);
            else WriteUserText(buf, items[i], allowMultiline: false);
            if (buf.Saturated) break;
        }

        WriteOverflow(buf, items.Length - shown, shown);
        buf.WriteByte((byte)']');
    }

    private void WriteFormattableSpan<T>(LogBuffer buf, ReadOnlySpan<T> items)
        where T : IUtf8SpanFormattable
    {
        buf.WriteByte((byte)'[');
        var shown = Math.Min(items.Length, _maxCollectionItems);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) buf.WriteBytes(", "u8);
            buf.WriteFormattable(items[i]);
            if (buf.Saturated) break;
        }

        WriteOverflow(buf, items.Length - shown, shown);
        buf.WriteByte((byte)']');
    }

    /// <summary>Emits <c>, ... (N more)</c> when a counted collection was clipped.</summary>
    private static void WriteOverflow(LogBuffer buf, int remaining, int shown)
    {
        if (remaining <= 0) return;
        if (shown > 0) buf.WriteBytes(", "u8);
        buf.WriteBytes("... ("u8);
        buf.WriteLong(remaining);
        buf.WriteBytes(" more)"u8);
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
