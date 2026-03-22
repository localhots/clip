using System.Globalization;
using System.Runtime.CompilerServices;

namespace Clip.Internal;

internal sealed class TimestampCache(string format, TimeSpan precision)
{
    private readonly long _precisionTicks = precision.Ticks;
    private long _lastTicks;
    private readonly byte[] _cached = new byte[64];
    private int _cachedLen;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTo(LogBuffer buffer, DateTimeOffset timestamp)
    {
        var ticks = timestamp.UtcTicks;
        if (_cachedLen > 0 && (ticks - _lastTicks) < _precisionTicks)
        {
            buffer.WriteBytes(_cached.AsSpan(0, _cachedLen));
            return;
        }
        FormatAndWrite(buffer, timestamp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FormatAndWrite(LogBuffer buffer, DateTimeOffset timestamp)
    {
        _lastTicks = timestamp.UtcTicks;
        // Formats as UtcDateTime (not DateTimeOffset) so JSON timestamps emit "Z" suffix
        // rather than "+00:00". InvariantCulture is required — macOS uses different
        // decimal separators without it.
        timestamp.UtcDateTime.TryFormat(_cached, out _cachedLen, format, CultureInfo.InvariantCulture);
        buffer.WriteBytes(_cached.AsSpan(0, _cachedLen));
    }
}
