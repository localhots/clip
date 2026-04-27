using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clip.Extensions.Logging;

[ProviderAlias("Clip")]
public sealed class ClipLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly Logger _logger;
    private readonly CategoryLevelMap _levelMap;
    private readonly ConcurrentDictionary<string, ClipLogger> _loggers = new();
    private readonly bool _ownsLogger;
    private IExternalScopeProvider? _scopeProvider;

    public ClipLoggerProvider(IOptions<ClipLoggerOptions> options)
    {
        var opts = options.Value;
        _levelMap = new CategoryLevelMap(opts.CategoryLevels, opts.DefaultLevel);

        if (opts.ConfigureLogger is not null)
            _logger = Logger.Create(opts.ConfigureLogger);
        else
            _logger = Logger.Create(c => c
                .MinimumLevel(LogLevel.Trace)
                .WriteTo.Console());

        _ownsLogger = true;
    }

    public ClipLoggerProvider(Logger logger, ClipLoggerOptions? options = null)
    {
        _logger = logger;
        _ownsLogger = false;
        var opts = options ?? new ClipLoggerOptions();
        _levelMap = new CategoryLevelMap(opts.CategoryLevels, opts.DefaultLevel);
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name =>
        {
            var effectiveLevel = _levelMap.GetEffectiveLevel(name);
            return new ClipLogger(_logger, name, effectiveLevel, _scopeProvider);
        });
    }

    /// <summary>
    /// Receives the unified <see cref="IExternalScopeProvider"/> from the host's
    /// <see cref="ILoggerFactory"/>. Scopes pushed by other providers (e.g. ASP.NET Core's
    /// request scope, custom middleware) become visible in Clip log fields once this is set.
    /// </summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
        foreach (var logger in _loggers.Values)
            logger.SetScopeProvider(scopeProvider);
    }

    public void Dispose()
    {
        if (_ownsLogger)
            _logger.Dispose();
    }
}
