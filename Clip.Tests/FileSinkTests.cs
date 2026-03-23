using System.Text.Json;
using Clip.Sinks;

namespace Clip.Tests;

public class FileSinkTests : IDisposable
{
    private readonly string _dir;

    public FileSinkTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clip-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string LogPath(string name = "app.log")
    {
        return Path.Combine(_dir, name);
    }

    private static DateTimeOffset FixedTs => new(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Write_CreatesFileAndProducesValidJson()
    {
        var path = LogPath();
        using var sink = new FileSink(path);
        sink.Write(FixedTs, LogLevel.Info, "hello", [], null);

        var text = File.ReadAllText(path);
        var line = text.TrimEnd('\n');
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("hello", doc.RootElement.GetProperty("msg").GetString());
        Assert.Equal("info", doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void Write_AppendsMultipleLines()
    {
        var path = LogPath();
        using var sink = new FileSink(path);
        sink.Write(FixedTs, LogLevel.Info, "first", [], null);
        sink.Write(FixedTs, LogLevel.Info, "second", [], null);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
            // Must be valid JSON
            using (JsonDocument.Parse(line))
            {
            }
    }

    [Fact]
    public void Write_WithFields_IncludesFields()
    {
        var path = LogPath();
        using var sink = new FileSink(path, format: new JsonFormatConfig { FieldsKey = "fields" });
        sink.Write(FixedTs, LogLevel.Info, "msg",
            [new Field("user", "alice"), new Field("count", 42)], null);

        var text = File.ReadAllText(path).TrimEnd('\n');
        using var doc = JsonDocument.Parse(text);
        var fields = doc.RootElement.GetProperty("fields");
        Assert.Equal("alice", fields.GetProperty("user").GetString());
        Assert.Equal(42, fields.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Write_WithException_IncludesError()
    {
        var path = LogPath();
        using var sink = new FileSink(path);
        var ex = new InvalidOperationException("boom");
        sink.Write(FixedTs, LogLevel.Error, "failed", [], ex);

        var text = File.ReadAllText(path).TrimEnd('\n');
        using var doc = JsonDocument.Parse(text);
        var error = doc.RootElement.GetProperty("error");
        Assert.Contains("InvalidOperationException", error.GetProperty("type").GetString());
        Assert.Equal("boom", error.GetProperty("msg").GetString());
    }

    [Fact]
    public void Roll_WhenFileSizeExceeded_CreatesRolledFile()
    {
        var path = LogPath();
        // Tiny max size to force rolling
        using var sink = new FileSink(path, 100, 3);

        // Write enough to trigger at least one roll
        for (var i = 0; i < 10; i++)
            sink.Write(FixedTs, LogLevel.Info, $"message number {i}", [], null);

        // Current file should exist
        Assert.True(File.Exists(path));
        // At least one rolled file should exist
        var rolled1 = Path.Combine(_dir, "app.1.log");
        Assert.True(File.Exists(rolled1));
    }

    [Fact]
    public void Roll_RespectsMaxRetainedFiles()
    {
        var path = LogPath();
        using var sink = new FileSink(path, 50, 2);

        // Write many entries to force multiple rolls
        for (var i = 0; i < 20; i++)
            sink.Write(FixedTs, LogLevel.Info, $"message-{i:D4}", [], null);

        // Should have at most: current + 2 rolled = 3 files
        var logFiles = Directory.GetFiles(_dir, "app*");
        Assert.True(
            logFiles.Length <= 3,
            $"Expected at most 3 files, got {logFiles.Length}: {string.Join(", ", logFiles.Select(Path.GetFileName))}");

        // Rolled file 3 should not exist (maxRetained = 2)
        var rolled3 = Path.Combine(_dir, "app.3.log");
        Assert.False(File.Exists(rolled3));
    }

    [Fact]
    public void Roll_AllRolledFilesContainValidJson()
    {
        var path = LogPath();
        using var sink = new FileSink(path, 100, 5);

        for (var i = 0; i < 20; i++)
            sink.Write(FixedTs, LogLevel.Info, $"msg-{i}", [], null);

        foreach (var file in Directory.GetFiles(_dir, "app*"))
        {
            var lines = File.ReadAllLines(file)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            foreach (var line in lines)
                using (JsonDocument.Parse(line))
                {
                } // Must be valid
        }
    }

    [Fact]
    public void Write_ThreadSafe_ConcurrentWrites()
    {
        var path = LogPath();
        using var sink = new FileSink(path, 10 * 1024 * 1024);

        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 100, i =>
            sink.Write(FixedTs, LogLevel.Info, $"concurrent-{i}", [], null));

        var lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(100, lines.Length);
        foreach (var line in lines)
            using (JsonDocument.Parse(line))
            {
            }
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfNeeded()
    {
        var nested = Path.Combine(_dir, "sub", "dir", "app.log");
        using var sink = new FileSink(nested);
        sink.Write(FixedTs, LogLevel.Info, "test", [], null);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Constructor_ThrowsOnNullPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileSink(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidMaxFileSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSink(LogPath(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSink(LogPath(), -1));
    }

    [Fact]
    public void Dispose_FlushesAndClosesFile()
    {
        var path = LogPath();
        var sink = new FileSink(path);
        sink.Write(FixedTs, LogLevel.Info, "before dispose", [], null);
        sink.Dispose();

        // Should be readable after disposal
        var text = File.ReadAllText(path).TrimEnd('\n');
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("before dispose", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void Write_AppendsToExistingFile()
    {
        var path = LogPath();

        // First session
        using (var sink = new FileSink(path))
        {
            sink.Write(FixedTs, LogLevel.Info, "session1", [], null);
        }

        // Second session — should append
        using (var sink = new FileSink(path))
        {
            sink.Write(FixedTs, LogLevel.Info, "session2", [], null);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void SinkConfig_File_IntegratesWithLogger()
    {
        var path = LogPath();
        using var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Debug)
            .WriteTo.File(path, new JsonFormatConfig { FieldsKey = "fields" }));

        logger.Info("from logger", new { Key = "value" });

        var text = File.ReadAllText(path).TrimEnd('\n');
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("from logger", doc.RootElement.GetProperty("msg").GetString());
        Assert.Equal("value", doc.RootElement.GetProperty("fields").GetProperty("Key").GetString());
    }

    [Fact]
    public void Write_ThreadSafe_ConcurrentWritesWithRolling()
    {
        var path = LogPath();
        using var sink = new FileSink(path, 100, 5);

        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 100, i =>
            sink.Write(FixedTs, LogLevel.Info, $"concurrent-roll-{i}", [], null));

        // All surviving files should contain valid JSON (old files may be deleted by retention)
        var totalLines = 0;
        foreach (var file in Directory.GetFiles(_dir, "app*"))
        {
            var lines = File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            totalLines += lines.Length;
            foreach (var line in lines)
                using (JsonDocument.Parse(line))
                {
                }
        }

        Assert.True(totalLines > 0, "Expected at least some log entries");
        // Rolling happened (multiple files exist)
        Assert.True(Directory.GetFiles(_dir, "app*").Length > 1, "Expected rolling to have occurred");
    }

    [Fact]
    public void Write_OversizedEntry_DoesNotRollInfinitely()
    {
        var path = LogPath();
        // maxFileSize smaller than a single entry
        using var sink = new FileSink(path, 10, 5);

        // Should not throw or loop forever
        sink.Write(FixedTs, LogLevel.Info, "this message is much longer than 10 bytes", [], null);
        sink.Write(FixedTs, LogLevel.Info, "second entry", [], null);

        // Both entries should be written (across files)
        var totalLines = 0;
        foreach (var file in Directory.GetFiles(_dir, "app*"))
            totalLines += File.ReadAllLines(file).Count(l => !string.IsNullOrWhiteSpace(l));
        Assert.Equal(2, totalLines);
    }

    [Fact]
    public void Roll_UnlimitedRetention_KeepsAllFiles()
    {
        var path = LogPath();
        using var sink = new FileSink(path, 100, 0);

        for (var i = 0; i < 20; i++)
            sink.Write(FixedTs, LogLevel.Info, $"unlimited-{i}", [], null);

        // With maxRetainedFiles=0, all rolled files should be preserved
        var logFiles = Directory.GetFiles(_dir, "app*");
        Assert.True(logFiles.Length > 3, $"Expected many files with unlimited retention, got {logFiles.Length}");
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeMaxRetainedFiles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSink(LogPath(), maxRetainedFiles: -1));
    }

    [Fact]
    public void Write_AppendAcrossSessions_RollsOnCumulativeSize()
    {
        var path = LogPath();

        // Session 1: write close to the limit
        using (var sink = new FileSink(path, 200))
        {
            sink.Write(FixedTs, LogLevel.Info, "session1-filling-up-the-file", [], null);
        }

        var sizeAfterSession1 = new FileInfo(path).Length;

        // Session 2: a small write should trigger a roll based on cumulative size
        using (var sink = new FileSink(path, 200))
        {
            sink.Write(FixedTs, LogLevel.Info, "session2-triggers-roll-if-cumulative", [], null);
            sink.Write(FixedTs, LogLevel.Info, "session2-second", [], null);
        }

        // If cumulative tracking works, the file should have rolled
        if (sizeAfterSession1 > 100) // Session1 entry was large enough
        {
            var rolled1 = Path.Combine(_dir, "app.1.log");
            Assert.True(File.Exists(rolled1), "Expected roll based on cumulative size across sessions");
        }
    }
}
