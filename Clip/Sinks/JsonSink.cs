using System.Text;
using System.Text.Json;
using Clip.Internal;

namespace Clip.Sinks;

/// <summary>
/// JSON Lines sink — one JSON object per log entry, newline-delimited. Writes to stderr by default.
/// Key names and timestamp format are configurable via <see cref="JsonFormatConfig"/>. Thread-safe.
/// </summary>
public sealed class JsonSink : ILogSink
{
    private readonly Stream _output;
    private readonly bool _ownsStream;
    private readonly byte[][] _labelBytes;
    private readonly LogBuffer _buffer;
    private readonly TimestampCache _tsCache;
    private readonly Lock _lock = new();
    private readonly byte[] _openTsPrefix;
    private readonly byte[] _levelPrefix;
    private readonly byte[] _msgPrefix;
    private readonly byte[]? _fieldsPrefix;
    private readonly byte[] _errorPrefix;
    private readonly int _maxInnerExceptionDepth;

    public JsonSink(JsonFormatConfig config, Stream? output = null)
    {
        _labelBytes =
        [
            config.GetLabelBytes(LogLevel.Trace),
            config.GetLabelBytes(LogLevel.Debug),
            config.GetLabelBytes(LogLevel.Info),
            config.GetLabelBytes(LogLevel.Warning),
            config.GetLabelBytes(LogLevel.Error),
            config.GetLabelBytes(LogLevel.Fatal),
        ];
        _ownsStream = output is null;
        _output = output ?? Console.OpenStandardError();
        _tsCache = new TimestampCache(config.TimestampFormat, config.CachePrecision);
        var esc = JsonFormatConfig.JsonEscape;
        _openTsPrefix = Encoding.UTF8.GetBytes($"{{\"{esc(config.TimestampKey)}\":\"");
        _levelPrefix = Encoding.UTF8.GetBytes($"\",\"{esc(config.LevelKey)}\":\"");
        _msgPrefix = Encoding.UTF8.GetBytes($"\",\"{esc(config.MessageKey)}\":");
        _fieldsPrefix = config.FieldsKey is not null
            ? Encoding.UTF8.GetBytes($",\"{esc(config.FieldsKey)}\":{{")
            : null;
        _errorPrefix = Encoding.UTF8.GetBytes($",\"{esc(config.ErrorKey)}\":");
        _maxInnerExceptionDepth = config.MaxInnerExceptionDepth;
        _buffer = new LogBuffer(config.MaxLogEntryBytes);
    }

    public JsonSink(Stream? output = null)
        : this(new JsonFormatConfig(), output)
    {
    }

    public void Write(DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        lock (_lock)
        {
            _buffer.Reset();

            _buffer.WriteBytes(_openTsPrefix);
            _tsCache.WriteTo(_buffer, timestamp);
            _buffer.WriteBytes(_levelPrefix);
            _buffer.WriteBytes(_labelBytes[(int)level]);
            _buffer.WriteBytes(_msgPrefix);
            _buffer.WriteJsonString(message);
            // Safe-point boundaries are at the outer-object level: positions where a
            // closing `,"truncated":true}\n` would produce valid JSON. Mark after each
            // complete top-level element.
            _buffer.MarkSafePoint();

            if (fields.Length > 0)
            {
                if (_fieldsPrefix is not null)
                {
                    _buffer.WriteBytes(_fieldsPrefix);
                    _buffer.WriteJsonString(fields[0].Key);
                    _buffer.WriteBytes(":"u8);
                    WriteFieldValue(_buffer, in fields[0]);
                    for (var i = 1; i < fields.Length; i++)
                    {
                        _buffer.WriteJsonFieldPrefix(fields[i].Key);
                        WriteFieldValue(_buffer, in fields[i]);
                    }
                    _buffer.WriteByte((byte)'}');
                    _buffer.MarkSafePoint();
                }
                else
                {
                    for (var i = 0; i < fields.Length; i++)
                    {
                        _buffer.WriteJsonFieldPrefix(fields[i].Key);
                        WriteFieldValue(_buffer, in fields[i]);
                        // Each field is a complete `,"k":v` at outer-object level —
                        // saturating mid-value rewinds to the previous field.
                        _buffer.MarkSafePoint();
                    }
                }
            }

            if (exception != null)
            {
                _buffer.WriteBytes(_errorPrefix);
                WriteException(_buffer, exception);
                _buffer.MarkSafePoint();
            }

            // If any write hit the size cap, the partial entry isn't a closed object.
            // Rewind to the last safe point and append a closing marker so we ship
            // everything up to the last complete element rather than throwing it away.
            if (_buffer.Saturated)
            {
                _buffer.RewindToSafePoint();
                _buffer.WriteMarker(",\"truncated\":true}\n"u8);
            }
            else
            {
                _buffer.WriteBytes("}\n"u8);
            }

            _output.Write(_buffer.WrittenSpan);
        }
    }

    private static void WriteFieldValue(LogBuffer buf, in Field f)
    {
        switch (f.Type)
        {
            case FieldType.Bool: buf.WriteBool(f.BoolValue); break;
            case FieldType.Int: buf.WriteLong(f.IntValue); break;
            case FieldType.Long: buf.WriteLong(f.LongValue); break;
            case FieldType.ULong: buf.WriteULong(unchecked((ulong)f.LongValue)); break;
            case FieldType.Float: buf.WriteFloat(f.FloatValue); break;
            case FieldType.Double: buf.WriteDouble(f.DoubleValue); break;
            case FieldType.DateTime:
                buf.WriteByte((byte)'"');
                buf.WriteDateTime(f.LongValue);
                buf.WriteByte((byte)'"');
                break;
            case FieldType.String:
                if (f.RefValue != null)
                    buf.WriteJsonString((string)f.RefValue);
                else
                    buf.WriteBytes("null"u8);
                break;
            case FieldType.Decimal: buf.WriteDecimal(f.DecimalValue); break;
            case FieldType.Guid:
                buf.WriteByte((byte)'"');
                buf.WriteGuid(f.GuidValue);
                buf.WriteByte((byte)'"');
                break;
            case FieldType.Object:
                buf.WriteBytes(JsonSerializer.SerializeToUtf8Bytes(f.RefValue));
                break;
        }
    }

    private void WriteException(LogBuffer buf, Exception ex, int depth = 0)
    {
        buf.WriteBytes("{\"type\":"u8);
        buf.WriteJsonString(ex.GetType().FullName ?? ex.GetType().Name);
        buf.WriteBytes(",\"msg\":"u8);
        buf.WriteJsonString(ex.Message);

        if (ex.Data.Count > 0)
        {
            buf.WriteBytes(",\"data\":{"u8);
            var first = true;
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
            {
                if (!first) buf.WriteByte((byte)',');
                first = false;
                buf.WriteJsonString(entry.Key.ToString() ?? "null");
                buf.WriteByte((byte)':');
                buf.WriteJsonString(entry.Value?.ToString() ?? "null");
            }

            buf.WriteByte((byte)'}');
        }

        var st = ex.StackTrace;
        if (st != null)
        {
            buf.WriteBytes(",\"stack\":"u8);
            buf.WriteJsonString(st);
        }

        if (ex.InnerException != null)
        {
            buf.WriteBytes(",\"inner\":"u8);
            if (depth + 1 >= _maxInnerExceptionDepth)
                buf.WriteBytes("{\"truncated\":true}"u8);
            else
                WriteException(buf, ex.InnerException, depth + 1);
        }

        buf.WriteByte((byte)'}');
    }


    public void Dispose()
    {
        if (_ownsStream) _output.Dispose();
    }
}
