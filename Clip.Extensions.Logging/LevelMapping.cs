using System.Runtime.CompilerServices;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using ClipLogLevel = Clip.LogLevel;

namespace Clip.Extensions.Logging;

internal static class LevelMapping
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ClipLogLevel ToClip(MelLogLevel level)
    {
        return level switch
        {
            MelLogLevel.Trace => ClipLogLevel.Trace,
            MelLogLevel.Debug => ClipLogLevel.Debug,
            MelLogLevel.Information => ClipLogLevel.Info,
            MelLogLevel.Warning => ClipLogLevel.Warning,
            MelLogLevel.Error => ClipLogLevel.Error,
            MelLogLevel.Critical => ClipLogLevel.Fatal,
            _ => ClipLogLevel.Trace,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MelLogLevel ToMel(ClipLogLevel level)
    {
        return level switch
        {
            ClipLogLevel.Trace => MelLogLevel.Trace,
            ClipLogLevel.Debug => MelLogLevel.Debug,
            ClipLogLevel.Info => MelLogLevel.Information,
            ClipLogLevel.Warning => MelLogLevel.Warning,
            ClipLogLevel.Error => MelLogLevel.Error,
            ClipLogLevel.Fatal => MelLogLevel.Critical,
            _ => MelLogLevel.None,
        };
    }

    public static ClipLogLevel ParseClipLevel(string levelName)
    {
        return levelName switch
        {
            "Trace" => ClipLogLevel.Trace,
            "Debug" => ClipLogLevel.Debug,
            "Information" => ClipLogLevel.Info,
            "Warning" => ClipLogLevel.Warning,
            "Error" => ClipLogLevel.Error,
            "Critical" => ClipLogLevel.Fatal,
            _ => throw new ArgumentException($"Unknown log level: {levelName}", nameof(levelName)),
        };
    }
}
