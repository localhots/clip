namespace Clip.Sinks;

/// <summary>
/// Rolling file sink that writes JSON Lines with automatic size-based rotation.
/// Rolled files are suffixed with <c>.1</c>, <c>.2</c>, etc. Thread-safe (serialized by inner <see cref="JsonSink"/>).
/// </summary>
public sealed class FileSink : ILogSink
{
    private readonly JsonSink _jsonSink;
    private readonly RollingFileStream _rollingStream;

    /// <summary>
    /// Creates a file sink that writes JSON Lines to rolling log files.
    /// </summary>
    /// <param name="path">Base file path (e.g. "/var/log/app.log").</param>
    /// <param name="maxFileSize">
    /// Maximum bytes before rolling (soft limit — a single entry may exceed this). Default 10 MB.
    /// </param>
    /// <param name="maxRetainedFiles">
    /// Maximum number of rolled files to retain. 0 = unlimited. Default 7.
    /// </param>
    /// <param name="format">Optional JSON format settings. Defaults to <see cref="JsonFormatConfig"/> defaults.</param>
    public FileSink(
        string path,
        long maxFileSize = 10 * 1024 * 1024,
        int maxRetainedFiles = 7,
        JsonFormatConfig? format = null)
    {
        _rollingStream = new RollingFileStream(path, maxFileSize, maxRetainedFiles);
        _jsonSink = new JsonSink(format ?? new JsonFormatConfig(), _rollingStream);
    }

    public void Write(DateTimeOffset timestamp, LogLevel level, string message,
        ReadOnlySpan<Field> fields, Exception? exception)
    {
        _jsonSink.Write(timestamp, level, message, fields, exception);
    }

    public void Dispose()
    {
        _jsonSink.Dispose();
        _rollingStream.Dispose();
    }

    /// <summary>
    /// A stream wrapper that handles size-based file rolling transparently.
    /// Thread safety: all calls are serialized by JsonSink._lock. This class is not independently thread-safe.
    /// </summary>
    private sealed class RollingFileStream : Stream
    {
        private readonly string _directory;
        private readonly string _filePrefix;
        private readonly string _fileExtension;
        private readonly long _maxFileSize;
        private readonly int _maxRetainedFiles;

        private FileStream? _inner;
        private long _currentSize;
        private string _currentPath;

        public RollingFileStream(string path, long maxFileSize, int maxRetainedFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxFileSize, 0);
            ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedFiles);

            var fullPath = Path.GetFullPath(path);
            _directory = Path.GetDirectoryName(fullPath)!;
            _fileExtension = Path.GetExtension(fullPath);
            _filePrefix = Path.GetFileNameWithoutExtension(fullPath);
            _maxFileSize = maxFileSize;
            _maxRetainedFiles = maxRetainedFiles;
            _currentPath = "";

            Directory.CreateDirectory(_directory);
            OpenFile();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_inner is null)
                return; // Sink is in a failed state after a Roll() error

            if (_currentSize + buffer.Length > _maxFileSize && _currentSize > 0)
                Roll();

            if (_inner is null)
                return; // Roll failed to reopen

            _inner.Write(buffer);
            _inner.Flush();
            _currentSize += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        private void OpenFile()
        {
            _currentPath = Path.Combine(_directory, $"{_filePrefix}{_fileExtension}");
            _inner = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _currentSize = _inner.Length;
        }

        private void Roll()
        {
            _inner?.Dispose();
            _inner = null;

            try
            {
                if (_maxRetainedFiles > 0)
                {
                    // Bounded: delete oldest, shift others up
                    var oldest = RolledPath(_maxRetainedFiles);
                    if (File.Exists(oldest))
                        try
                        {
                            File.Delete(oldest);
                        }
                        catch
                        {
                            // Best-effort cleanup
                        }

                    for (var i = _maxRetainedFiles - 1; i >= 1; i--)
                    {
                        var src = RolledPath(i);
                        if (File.Exists(src))
                            try
                            {
                                File.Move(src, RolledPath(i + 1), true);
                            }
                            catch
                            {
                                // Best-effort
                            }
                    }
                }
                else
                {
                    // Unlimited: find the highest existing index and shift all up
                    var highest = 0;
                    while (File.Exists(RolledPath(highest + 1))) highest++;

                    for (var i = highest; i >= 1; i--)
                        try
                        {
                            File.Move(RolledPath(i), RolledPath(i + 1), true);
                        }
                        catch
                        {
                            // Best-effort
                        }
                }

                if (File.Exists(_currentPath))
                    File.Move(_currentPath, RolledPath(1), true);
            }
            catch
            {
                // Rolling failed — still try to reopen so the sink stays alive
            }

            try
            {
                OpenFile();
            }
            catch
            {
                // _inner remains null — Write will no-op until the next roll attempt
            }
        }

        private string RolledPath(int index)
        {
            return Path.Combine(_directory, $"{_filePrefix}.{index}{_fileExtension}");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner?.Dispose();
            base.Dispose(disposing);
        }

        // Required Stream overrides (write-only stream)
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _inner?.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
