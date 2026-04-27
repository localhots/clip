using System.Buffers;
using System.Threading.Channels;

namespace Clip.Sinks;

/// <summary>
/// Decorator that offloads sink writes to a background thread via a bounded channel.
/// Log calls enqueue and return immediately. When the channel is full, the oldest entry is dropped.
/// On disposal, the drain loop is given up to 5 seconds to flush.
/// </summary>
public sealed class BackgroundSink(ILogSink inner, int capacity = 1024) : ILogSink
{
    private readonly Channel<LogEntry> _channel = Channel.CreateBounded<LogEntry>(
        new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    // Wired by Logger.Create after configure() runs, so OnInternalError() and Background()
    // can be called in either order. Until set, drain-loop failures are silently swallowed
    // (preserving previous behavior).
    private Action<Exception>? _onError;

    internal void SetErrorHandler(Action<Exception>? handler) => _onError = handler;

    private readonly Task _drainTask = Task.CompletedTask; // Safe default; overwritten by chained ctor
    private bool _disposed;

    // Dummy bool parameter disambiguate from the primary constructor so Create()
    // can chain to it and start the drain loop after _channel is initialized.
    // ReSharper disable once UnusedParameter.Local
    private BackgroundSink(ILogSink inner, int capacity, bool _) : this(inner, capacity)
    {
        _drainTask = Task.Run(DrainAsync);
    }

    internal static BackgroundSink Create(ILogSink inner, int capacity = 1024)
    {
        return new BackgroundSink(inner, capacity, true);
    }

    public void Write(DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        Field[] fieldArray;
        var fieldCount = fields.Length;
        if (fieldCount == 0)
        {
            fieldArray = [];
        }
        else
        {
            fieldArray = ArrayPool<Field>.Shared.Rent(fieldCount);
            fields.CopyTo(fieldArray);
        }

        var entry = new LogEntry(timestamp, level, message, fieldArray, fieldCount, exception);
        if (!_channel.Writer.TryWrite(entry) && fieldCount > 0)
            ArrayPool<Field>.Shared.Return(fieldArray, true);
    }

    private async Task DrainAsync()
    {
        var reader = _channel.Reader;
        while (await reader.WaitToReadAsync())
            while (reader.TryRead(out var entry))
                try
                {
                    inner.Write(entry.Timestamp, entry.Level, entry.Message,
                        entry.Fields.AsSpan(0, entry.FieldCount), entry.Exception);
                }
                catch (Exception ex)
                {
                    // Inner sink failure must not crash the drain loop
                    var handler = _onError;
                    if (handler != null)
                    {
                        try { handler(ex); }
                        catch { /* handler must not crash the drain loop */ }
                    }
                }
                finally
                {
                    if (entry.FieldCount > 0)
                        ArrayPool<Field>.Shared.Return(entry.Fields, true);
                }
    }

    public void Dispose()
    {
        // Idempotent: skip everything on second call so we don't double-dispose a custom
        // inner sink whose Dispose isn't safe to call twice.
        if (_disposed) return;
        _disposed = true;

        // TryComplete (vs Complete) so a double-Dispose doesn't fault even before the
        // _disposed guard — second call sees the channel already completed and returns false.
        _channel.Writer.TryComplete();

        // If the drain finished, dispose the inner sink. If it didn't, the drain task is
        // still inside inner.Write and disposing now would race (FileStream freed mid-write,
        // half-formed JSON line on disk). Leak the inner sink instead — better an orphaned
        // file handle than corrupted output.
        if (_drainTask.Wait(TimeSpan.FromSeconds(5)))
            inner.Dispose();
    }

    private readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Message,
        Field[] Fields,
        int FieldCount,
        Exception? Exception);
}
