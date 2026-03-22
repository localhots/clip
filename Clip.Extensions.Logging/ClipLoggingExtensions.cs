using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Clip.Extensions.Logging;

public static class ClipLoggingExtensions
{
    public static ILoggingBuilder AddClip(this ILoggingBuilder builder, Action<ClipLoggerOptions>? configure = null)
    {
        builder.AddConfiguration();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<ClipLoggerOptions>, ClipLoggerOptionsSetup>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, ClipLoggerProvider>());

        if (configure is not null)
            builder.Services.Configure(configure);

        return builder;
    }

    public static ILoggingBuilder AddClip(this ILoggingBuilder builder, Logger logger)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider>(new ClipLoggerProvider(logger)));
        return builder;
    }
}
