using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Clip.Internal;

internal sealed class LogBuffer
{
    private byte[] _buf = ArrayPool<byte>.Shared.Rent(InitialSize);
    private int _pos;
    private const int InitialSize = 1024;
    private const int MaxRetainSize = 10 * 1024;

    // SIMD-accelerated: chars 0x00-0x1F plus " and \
    private static readonly SearchValues<char> JsonEscapeChars =
        SearchValues.Create(
            "\"\\\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\e\u001c\u001d\u001e\u001f");

    // C0 controls + DEL minus tab/CR/LF — used to sanitize user-supplied strings in
    // multiline contexts (message body, exception message, stack trace) before they reach
    // a terminal. Without this an attacker-controlled value can inject ANSI escapes
    // (clear screen, rewrite previous line, fake log entries).
    private static readonly SearchValues<char> ControlCharsMultiline =
        SearchValues.Create(
            "\0\a\b\v\f\e");

    // Same as above plus CR and LF — used for single-line contexts like field values and
    // Exception.Data entries, where a newline would forge a fake log line.
    private static readonly SearchValues<char> ControlCharsSingleLine =
        SearchValues.Create(
            "\0\a\b\n\v\f\r\e");

    public ReadOnlySpan<byte> WrittenSpan => _buf.AsSpan(0, _pos);

    public void Reset()
    {
        if (_buf.Length > MaxRetainSize)
        {
            ArrayPool<byte>.Shared.Return(_buf);
            _buf = ArrayPool<byte>.Shared.Rent(InitialSize);
        }

        _pos = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte b)
    {
        if (_pos + 1 > _buf.Length) Grow(1);
        _buf[_pos++] = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        if (_pos + data.Length > _buf.Length) Grow(data.Length);
        data.CopyTo(_buf.AsSpan(_pos));
        _pos += data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePadding(int count)
    {
        if (_pos + count > _buf.Length) Grow(count);
        _buf.AsSpan(_pos, count).Fill((byte)' ');
        _pos += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string s)
    {
        var needed = s.Length * 3; // Worst-case: each char may encode to 3 UTF-8 bytes
        if (_pos + needed > _buf.Length) Grow(needed);
        // Scalar ASCII fast path: avoids UTF8.GetBytes overhead for short ASCII strings
        if (s.Length <= 24)
        {
            int i;
            for (i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c > 0x7F) break;
                _buf[_pos + i] = (byte)c;
            }

            if (i == s.Length)
            {
                _pos += s.Length;
                return;
            }
        }

        _pos += Encoding.UTF8.GetBytes(s.AsSpan(), _buf.AsSpan(_pos));
    }

    /// <summary>
    /// Writes a UTF-8 encoded string with C0 control characters and DEL stripped.
    /// When <paramref name="allowMultiline"/> is true, CR and LF are preserved (use this
    /// for the message body, exception message, and stack trace); otherwise they are
    /// stripped (use this for field values and Exception.Data, which logically occupy a
    /// single line — a newline there forges a fake log entry).
    /// </summary>
    public void WriteSanitized(string s, bool allowMultiline)
    {
        var bad = allowMultiline
            ? ControlCharsMultiline
            : ControlCharsSingleLine;
        var span = s.AsSpan();

        // Fast path: nothing to strip — fall through to the existing WriteString path,
        // which keeps its scalar ASCII shortcut.
        var idx = span.IndexOfAny(bad);
        if (idx < 0)
        {
            WriteString(s);
            return;
        }

        if (idx > 0) WriteSpan(span[..idx]);
        span = span[(idx + 1)..];
        while (span.Length > 0)
        {
            idx = span.IndexOfAny(bad);
            if (idx < 0)
            {
                WriteSpan(span);
                return;
            }

            if (idx > 0) WriteSpan(span[..idx]);
            span = span[(idx + 1)..];
        }
    }

    private void WriteSpan(ReadOnlySpan<char> s)
    {
        var needed = s.Length * 3;
        if (_pos + needed > _buf.Length) Grow(needed);
        _pos += Encoding.UTF8.GetBytes(s, _buf.AsSpan(_pos));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLong(long v)
    {
        if (_pos + 20 > _buf.Length) Grow(20);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w);
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double v)
    {
        if (_pos + 32 > _buf.Length) Grow(32);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w, new StandardFormat('G'));
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float v)
    {
        if (_pos + 16 > _buf.Length) Grow(16);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w, new StandardFormat('G'));
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTime(long utcTicks)
    {
        // 28 chars for "O" format: "2024-06-15T10:30:00.0000000Z"
        if (_pos + 28 > _buf.Length) Grow(28);
        var dt = new DateTime(utcTicks, DateTimeKind.Utc);
        Utf8Formatter.TryFormat(dt, _buf.AsSpan(_pos), out var w, new StandardFormat('O'));
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteULong(ulong v)
    {
        if (_pos + 20 > _buf.Length) Grow(20);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w);
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDecimal(decimal v)
    {
        if (_pos + 31 > _buf.Length) Grow(31);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w);
        _pos += w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid v)
    {
        if (_pos + 36 > _buf.Length) Grow(36);
        Utf8Formatter.TryFormat(v, _buf.AsSpan(_pos), out var w, new StandardFormat('D'));
        _pos += w;
    }

    public void WriteUtf8Formattable(IUtf8SpanFormattable value)
    {
        if (_pos + 64 > _buf.Length) Grow(64);
        if (value.TryFormat(_buf.AsSpan(_pos), out var w, default, CultureInfo.InvariantCulture))
        {
            _pos += w;
            return;
        }

        Grow(512);
        if (value.TryFormat(_buf.AsSpan(_pos), out w, default, CultureInfo.InvariantCulture))
        {
            _pos += w;
            return;
        }

        WriteString(value.ToString() ?? "");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(bool v)
    {
        WriteBytes(v
            ? "true"u8
            : "false"u8);
    }

    /// <summary>Writes key= in one capacity check (Console text field prefix).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTextFieldPrefix(string key)
    {
        var needed = key.Length * 3 + 1; // key + '='
        if (_pos + needed > _buf.Length) Grow(needed);
        // Scalar ASCII fast path for field keys (always C# identifiers)
        int i;
        for (i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (c > 0x7E) break;
            _buf[_pos + i] = (byte)c;
        }

        if (i == key.Length)
        {
            _pos += key.Length;
            _buf[_pos++] = (byte)'=';
            return;
        }

        _pos += Encoding.UTF8.GetBytes(key.AsSpan(), _buf.AsSpan(_pos));
        _buf[_pos++] = (byte)'=';
    }

    /// <summary>Writes ',"key":' — comma, quoted JSON key, and colon in one capacity check.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteJsonFieldPrefix(string key)
    {
        var needed = key.Length * 3 + 4;
        if (_pos + needed > _buf.Length) Grow(needed);
        // Speculative write: writes comma and opening quote before verifying the key is
        // pure ASCII. If the check fails, _pos hasn't advanced, so the fallback overwrites.
        _buf[_pos] = (byte)',';
        _buf[_pos + 1] = (byte)'"';
        // Scalar ASCII check+copy: field keys are C# identifiers (short, ASCII, no escaping).
        // Uses 0x7E upper bound (not 0x7F) to also exclude the DEL control character.
        int i;
        for (i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (c > 0x7E || c < 0x20 || c == '"' || c == '\\') break;
            _buf[_pos + 2 + i] = (byte)c;
        }

        if (i == key.Length)
        {
            _pos += key.Length + 2;
            _buf[_pos++] = (byte)'"';
            _buf[_pos++] = (byte)':';
            return;
        }

        // Fallback: non-ASCII or needs escaping (rare for field keys).
        // Restarts from _pos, overwriting the speculative bytes above.
        WriteByte((byte)',');
        WriteJsonString(key);
        WriteByte((byte)':');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteJsonString(string s)
    {
        // Short ASCII fast path: scalar check+copy avoids ContainsAny + UTF8.GetBytes overhead
        if (s.Length <= 16)
        {
            var needed = s.Length + 2; // ASCII: 1 byte per char + 2 quotes
            if (_pos + needed > _buf.Length) Grow(s.Length * 3 + 2);
            _buf[_pos] = (byte)'"';
            int i;
            for (i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c > 0x7E || c < 0x20 || c == '"' || c == '\\') break;
                _buf[_pos + 1 + i] = (byte)c;
            }

            if (i == s.Length)
            {
                _pos += s.Length + 1;
                _buf[_pos++] = (byte)'"';
                return;
            }
        }

        WriteJsonStringSlow(s);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteJsonStringSlow(string s)
    {
        var span = s.AsSpan();
        if (span.ContainsAny(JsonEscapeChars))
        {
            WriteByte((byte)'"');
            WriteJsonStringEscaped(span);
            WriteByte((byte)'"');
            return;
        }

        var needed = s.Length * 3 + 2;
        if (_pos + needed > _buf.Length) Grow(needed);
        _buf[_pos++] = (byte)'"';
        _pos += Encoding.UTF8.GetBytes(span, _buf.AsSpan(_pos));
        _buf[_pos++] = (byte)'"';
    }

    private void WriteJsonStringEscaped(ReadOnlySpan<char> remaining)
    {
        while (remaining.Length > 0)
        {
            var idx = remaining.IndexOfAny(JsonEscapeChars);
            switch (idx)
            {
                case < 0:
                    {
                        var n = remaining.Length * 3;
                        if (_pos + n > _buf.Length) Grow(n);
                        _pos += Encoding.UTF8.GetBytes(remaining, _buf.AsSpan(_pos));
                        return;
                    }
                case > 0:
                    {
                        var n = idx * 3;
                        if (_pos + n > _buf.Length) Grow(n);
                        _pos += Encoding.UTF8.GetBytes(remaining[..idx], _buf.AsSpan(_pos));
                        break;
                    }
            }

            if (_pos + 6 > _buf.Length) Grow(6);
            _buf[_pos++] = (byte)'\\';
            switch (remaining[idx])
            {
                case '"': _buf[_pos++] = (byte)'"'; break;
                case '\\': _buf[_pos++] = (byte)'\\'; break;
                case '\n': _buf[_pos++] = (byte)'n'; break;
                case '\r': _buf[_pos++] = (byte)'r'; break;
                case '\t': _buf[_pos++] = (byte)'t'; break;
                case '\b': _buf[_pos++] = (byte)'b'; break;
                case '\f': _buf[_pos++] = (byte)'f'; break;
                default:
                    var c = remaining[idx];
                    _buf[_pos++] = (byte)'u';
                    _buf[_pos++] = (byte)'0';
                    _buf[_pos++] = (byte)'0';
                    _buf[_pos++] = HexDigit(c >> 4);
                    _buf[_pos++] = HexDigit(c & 0xF);
                    break;
            }

            remaining = remaining[(idx + 1)..];
        }
    }

    private static byte HexDigit(int v)
    {
        return (byte)(v < 10
            ? '0' + v
            : 'a' + v - 10);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int needed)
    {
        var newSize = Math.Max(_buf.Length * 2, _pos + needed);
        var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
        _buf.AsSpan(0, _pos).CopyTo(newBuf);
        ArrayPool<byte>.Shared.Return(_buf);
        _buf = newBuf;
    }
}
