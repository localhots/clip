using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ClipLogLevel = Clip.LogLevel;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Clip.Extensions.Logging;

internal sealed class ClipLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly Logger _inner;
    private readonly string _categoryName;
    private readonly MelLogLevel _effectiveMelLevel;

    internal ClipLogger(Logger inner, string categoryName, ClipLogLevel effectiveLevel)
    {
        _inner = inner;
        _categoryName = categoryName;

        // Precompute the effective minimum as a MelLogLevel so IsEnabled
        // becomes a single integer comparison — no enum conversion, no
        // virtual call into inner.IsEnabled on the filtered hot path.
        // Take the stricter (higher) of the category level and the
        // inner logger's actual minimum.
        var innerMin = inner.MinLevel;
        var effectiveClip = effectiveLevel > innerMin
            ? effectiveLevel
            : innerMin;
        _effectiveMelLevel = LevelMapping.ToMel(effectiveClip);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(MelLogLevel logLevel)
    {
        return logLevel != MelLogLevel.None && (uint)logLevel >= (uint)_effectiveMelLevel;
    }

    public void Log<TState>(
        MelLogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var clipLevel = LevelMapping.ToClip(logLevel);
        var stateFields = MelFieldAdapter.ExtractFields(_categoryName, state, eventId);

        _inner.Log(clipLevel, message, stateFields, exception);
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            var fields = new Field[kvps.Count];
            for (var i = 0; i < kvps.Count; i++)
                fields[i] = MelFieldAdapter.CreateFieldFromKvp(kvps[i].Key, kvps[i].Value);
            return Logger.AddContext(fields);
        }

        return Logger.AddContext(new Field("Scope", state));
    }
}
