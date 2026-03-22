using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Clip.Extensions.Logging;

internal sealed class ClipLoggerOptionsSetup(IConfiguration? configuration = null)
    : IConfigureOptions<ClipLoggerOptions>
{
    public void Configure(ClipLoggerOptions options)
    {
        if (configuration is null) return;

        var section = configuration.GetSection("Logging:Clip:LogLevel");
        if (!section.Exists()) return;

        foreach (var child in section.GetChildren())
        {
            if (child.Value is null) continue;

            var level = LevelMapping.ParseClipLevel(child.Value);

            if (child.Key == "Default")
                options.DefaultLevel = level;
            else
                options.CategoryLevels[child.Key] = level;
        }
    }
}
