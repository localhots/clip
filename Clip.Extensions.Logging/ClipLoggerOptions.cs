using ClipLogLevel = Clip.LogLevel;

namespace Clip.Extensions.Logging;

public sealed class ClipLoggerOptions
{
    public Action<LoggerConfig>? ConfigureLogger { get; set; }
    public ClipLogLevel DefaultLevel { get; set; } = ClipLogLevel.Info;
    public Dictionary<string, ClipLogLevel> CategoryLevels { get; } = new();
}
