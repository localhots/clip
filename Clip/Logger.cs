using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Clip.Context;
using Clip.Enrichers;
using Clip.Fields;
using Clip.Sinks;

namespace Clip;

/// <summary>
/// The concrete logger. Implements both <see cref="ILogger"/> (ergonomic, reflection-based)
/// and <see cref="IZeroLogger"/> (zero-allocation, <see cref="Field"/>-based) interfaces.
/// Create via <see cref="Create"/>; register as a singleton.
/// </summary>
public sealed class Logger : ILogger, IZeroLogger
{
    private readonly SinkEntry[] _sinks;
    private readonly EnricherEntry[]? _enrichers;
    private readonly ILogRedactor[]? _redactors;
    private readonly ILogFilter[]? _filters;

    /// <summary>The global minimum log level. Calls below this level are no-ops.</summary>
    public LogLevel MinLevel { get; }

    private readonly struct SinkEntry(ILogSink sink, LogLevel minLevel, EnricherEntry[]? enrichers = null)
    {
        public readonly ILogSink Sink = sink;
        public readonly LogLevel MinLevel = minLevel;
        public readonly EnricherEntry[]? Enrichers = enrichers;
    }

    private Logger(LogLevel minLevel, SinkEntry[] sinks, EnricherEntry[]? enrichers, ILogRedactor[]? redactors,
        ILogFilter[]? filters)
    {
        MinLevel = minLevel;
        _sinks = sinks;
        _enrichers = enrichers;
        _redactors = redactors;
        _filters = filters;
    }

    /// <summary>
    /// Creates a new <see cref="Logger"/> from a fluent configuration lambda.
    /// </summary>
    /// <param name="configure">
    /// A delegate that configures sinks, enrichers, redactors, and minimum level
    /// via the <see cref="LoggerConfig"/> builder.
    /// </param>
    /// <example>
    /// <code>
    /// var logger = Logger.Create(config => config
    ///     .MinimumLevel(LogLevel.Debug)
    ///     .Enrich.Field("app", "my-service")
    ///     .WriteTo.Console());
    /// </code>
    /// </example>
    public static Logger Create(Action<LoggerConfig> configure)
    {
        var config = new LoggerConfig();
        configure(config);
        var raw = config.WriteTo.Build();
        var sinks = new SinkEntry[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            sinks[i] = new SinkEntry(raw[i].Sink, raw[i].MinLevel, raw[i].Enrichers);
        return new Logger(config.MinLevel, sinks, config.Enrich.Build(), config.Redact.Build(),
            config.Filter.Build());
    }


    //
    // Public dynamic-level API for adapters (e.g., MEL integration)
    //

    /// <summary>Returns <c>true</c> if the given level passes the global minimum level filter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level)
    {
        return MinLevel <= level;
    }

    /// <summary>Logs at a dynamic level. Used by framework adapters (e.g., MEL integration).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log(LogLevel level, string message, ReadOnlySpan<Field> fields, Exception? exception = null)
    {
        LogZeroAlloc(level, message, fields, exception);
    }

    IDisposable ILogger.AddContext(object fields)
    {
        return AddContext(fields);
    }

    IDisposable IZeroLogger.AddContext(params ReadOnlySpan<Field> fields)
    {
        return AddContext(fields);
    }

    public static ContextScope AddContext(object fields)
    {
        var list = FieldListPool.Rent();
        try
        {
            FieldExtractor.ExtractInto(fields, list);
            return LogScope.Push(CollectionsMarshal.AsSpan(list));
        }
        finally
        {
            FieldListPool.Return(list);
        }
    }

    [OverloadResolutionPriority(1)]
    public static ContextScope AddContext(params ReadOnlySpan<Field> fields)
    {
        return LogScope.Push(fields);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Trace(string message, object? fields = null)
    {
        LogErgonomic(LogLevel.Trace, message, fields, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string message, object? fields = null)
    {
        LogErgonomic(LogLevel.Debug, message, fields, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(string message, object? fields = null)
    {
        LogErgonomic(LogLevel.Info, message, fields, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(string message, object? fields = null)
    {
        LogErgonomic(LogLevel.Warning, message, fields, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message, object? fields = null)
    {
        LogErgonomic(LogLevel.Error, message, fields, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message, Exception exception, object? fields = null)
    {
        LogErgonomic(LogLevel.Error, message, fields, exception);
    }

    public void Fatal(string message, object? fields = null)
    {
        // Fatal always logs — never filtered
        var list = FieldListPool.Rent();
        try
        {
            ApplyEnrichers(list, LogLevel.Fatal);
            LogScope.CopyCurrentTo(list);
            if (fields != null) FieldExtractor.ExtractInto(fields, list);
            var span = CollectionsMarshal.AsSpan(list);
            var count = ProcessFields(span);
            WriteTo(LogLevel.Fatal, message, span[..count], null);
        }
        finally
        {
            FieldListPool.Return(list);
        }

        Dispose(); // Flush sinks
        Environment.Exit(1);
    }


    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Trace(string message, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Trace, message, fields, null);
    }

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string message, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Debug, message, fields, null);
    }

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(string message, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Info, message, fields, null);
    }

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(string message, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Warning, message, fields, null);
    }

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Error, message, fields, null);
    }

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message, Exception exception, params ReadOnlySpan<Field> fields)
    {
        LogZeroAlloc(LogLevel.Error, message, fields, exception);
    }

    //
    // Inlining strategy: each public log method (Trace, Debug, etc.) is AggressiveInlining,
    // so the level check becomes a single comparison at the call site — the most common
    // outcome (filtered out) has zero call overhead. The Impl methods are NoInlining to
    // prevent the JIT from bloating call sites with the full field-merge/write logic.
    //

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogErgonomic(LogLevel level, string message, object? fields, Exception? exception)
    {
        if (MinLevel > level) return;
        LogErgonomicImpl(level, message, fields, exception);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogErgonomicImpl(LogLevel level, string message, object? fields, Exception? exception)
    {
        try
        {
            if (fields == null && !LogScope.HasCurrent && _enrichers == null && _redactors == null
                && _filters == null)
            {
                WriteTo(level, message, ReadOnlySpan<Field>.Empty, exception);
                return;
            }

            var list = FieldListPool.Rent();
            try
            {
                ApplyEnrichers(list, level);
                LogScope.CopyCurrentTo(list);
                if (fields != null) FieldExtractor.ExtractInto(fields, list);
                var span = CollectionsMarshal.AsSpan(list);
                var count = ProcessFields(span);
                WriteTo(level, message, span[..count], exception);
            }
            finally
            {
                FieldListPool.Return(list);
            }
        }
        catch
        {
            // A log call must never crash the application.
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogZeroAlloc(LogLevel level, string message, ReadOnlySpan<Field> fields, Exception? exception)
    {
        if (MinLevel > level) return;
        LogZeroAllocImpl(level, message, fields, exception);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LogZeroAllocImpl(LogLevel level, string message, ReadOnlySpan<Field> fields, Exception? exception)
    {
        try
        {
            if (!LogScope.HasCurrent && _enrichers == null && _redactors == null && _filters == null)
            {
                WriteTo(level, message, fields, exception);
                return;
            }

            var list = FieldListPool.Rent();
            try
            {
                ApplyEnrichers(list, level);
                LogScope.CopyCurrentTo(list);
                foreach (var f in fields) list.Add(f);
                var span = CollectionsMarshal.AsSpan(list);
                var count = ProcessFields(span);
                WriteTo(level, message, span[..count], exception);
            }
            finally
            {
                FieldListPool.Return(list);
            }
        }
        catch
        {
            // A log call must never crash the application.
        }
    }

    private void ApplyEnrichers(List<Field> target, LogLevel level)
    {
        if (_enrichers == null) return;
        foreach (ref readonly var entry in _enrichers.AsSpan())
        {
            if (entry.MinLevel > level) continue;
            try
            {
                entry.Enricher.Enrich(target);
            }
            catch
            {
                // Enricher failure must not crash the pipeline
            }
        }
    }

    //
    // Single-pass field processing: filter, deduplicate, redact
    //

    private int ProcessFields(Span<Field> fields)
    {
        var write = 0;
        for (var i = 0; i < fields.Length; i++)
        {
            var key = fields[i].Key;
            if (IsFiltered(key)) continue;
            if (HasLaterDuplicate(fields, i)) continue;

            fields[write] = fields[i];
            ApplyRedactors(ref fields[write]);
            write++;
        }
        return write;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFiltered(string key)
    {
        if (_filters == null) return false;
        foreach (var filter in _filters)
            try
            {
                if (filter.ShouldSkip(key)) return true;
            }
            catch
            {
                // Filter failure must not crash the pipeline
            }
        return false;
    }

    private static bool HasLaterDuplicate(Span<Field> fields, int index)
    {
        var key = fields[index].Key;
        for (var j = index + 1; j < fields.Length; j++)
            if (fields[j].Key == key) return true;
        return false;
    }

    private void ApplyRedactors(ref Field field)
    {
        if (_redactors == null) return;
        foreach (var redactor in _redactors)
            try
            {
                redactor.Redact(ref field);
            }
            catch
            {
                // Redactor failure must not crash the pipeline
            }
    }

    private void WriteTo(LogLevel level, string message, ReadOnlySpan<Field> fields, Exception? exception)
    {
        var ts = DateTimeOffset.UtcNow;
        foreach (ref readonly var entry in _sinks.AsSpan())
        {
            if (entry.MinLevel > level) continue;
            try
            {
                if (entry.Enrichers == null)
                    entry.Sink.Write(ts, level, message, fields, exception);
                else
                    WriteWithEnrichers(ts, level, message, fields, exception, in entry);
            }
            catch
            {
                // Sink failure must not affect other sinks
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteWithEnrichers(DateTimeOffset ts, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception, in SinkEntry entry)
    {
        var list = FieldListPool.Rent();
        try
        {
            foreach (var f in fields) list.Add(f);

            foreach (ref readonly var e in entry.Enrichers.AsSpan())
            {
                if (e.MinLevel > level) continue;
                try { e.Enricher.Enrich(list); }
                catch { }
            }

            var span = CollectionsMarshal.AsSpan(list);
            var count = ProcessFields(span);
            entry.Sink.Write(ts, level, message, span[..count], exception);
        }
        finally { FieldListPool.Return(list); }
    }

    public void Dispose()
    {
        foreach (ref readonly var entry in _sinks.AsSpan())
            try
            {
                entry.Sink.Dispose();
            }
            catch
            {
                // Ignored
            }
    }
}
