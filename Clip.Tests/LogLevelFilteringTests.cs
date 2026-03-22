using System.Text;
using System.Text.Json;

namespace Clip.Tests;

/// <summary>
/// Log level filtering edge cases: IsEnabled boundaries, per-sink levels, global floor.
/// </summary>
public class LogLevelFilteringTests
{
    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    //
    // IsEnabled at all boundaries
    //

    [Theory]
    [InlineData(LogLevel.Trace, LogLevel.Trace, true)]
    [InlineData(LogLevel.Trace, LogLevel.Debug, true)]
    [InlineData(LogLevel.Trace, LogLevel.Info, true)]
    [InlineData(LogLevel.Trace, LogLevel.Warning, true)]
    [InlineData(LogLevel.Trace, LogLevel.Error, true)]
    [InlineData(LogLevel.Trace, LogLevel.Fatal, true)]
    [InlineData(LogLevel.Info, LogLevel.Trace, false)]
    [InlineData(LogLevel.Info, LogLevel.Debug, false)]
    [InlineData(LogLevel.Info, LogLevel.Info, true)]
    [InlineData(LogLevel.Info, LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, LogLevel.Warning, false)]
    [InlineData(LogLevel.Error, LogLevel.Error, true)]
    [InlineData(LogLevel.Fatal, LogLevel.Error, false)]
    [InlineData(LogLevel.Fatal, LogLevel.Fatal, true)]
    public void IsEnabled_AllBoundaries(LogLevel minLevel, LogLevel checkLevel, bool expected)
    {
        var logger = Logger.Create(c => c.MinimumLevel(minLevel).WriteTo.Null());
        Assert.Equal(expected, logger.IsEnabled(checkLevel));
    }

    //
    // Sink-level filtering
    //

    [Fact]
    public void PerSinkLevel_OnlySinkAboveThresholdReceives()
    {
        var msTrace = new MemoryStream();
        var msError = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(msTrace, LogLevel.Trace)
            .WriteTo.Json(msError, LogLevel.Error));

        logger.Trace("t");
        logger.Debug("d");
        logger.Info("i");
        logger.Warning("w");
        logger.Error("e");

        var traceDocs = ReadLines(msTrace);
        var errorDocs = ReadLines(msError);
        Assert.Equal(5, traceDocs.Length);
        Assert.Single(errorDocs);
        Assert.Equal("error", errorDocs[0].RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void GlobalMinLevel_IsFloor_SinkCannotGoLower()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Warning)
            .WriteTo.Json(ms, LogLevel.Trace)); // Sink wants trace, but global is warning

        logger.Trace("filtered");
        logger.Debug("filtered");
        logger.Info("filtered");
        logger.Warning("passes");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("warning", docs[0].RootElement.GetProperty("level").GetString());
    }

    //
    // Zero-alloc tier respects level filtering
    //

    [Fact]
    public void ZeroAllocTier_Filtered()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Error)
            .WriteTo.Json(ms));

        logger.Trace("filtered", new Field("k", 1));
        logger.Debug("filtered", new Field("k", 1));
        logger.Info("filtered", new Field("k", 1));
        logger.Warning("filtered", new Field("k", 1));
        logger.Error("passes", new Field("k", 1));

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("error", docs[0].RootElement.GetProperty("level").GetString());
    }

    //
    // MinLevel property
    //

    [Fact]
    public void MinLevel_ExposedCorrectly()
    {
        var logger = Logger.Create(c => c.MinimumLevel(LogLevel.Warning).WriteTo.Null());
        Assert.Equal(LogLevel.Warning, logger.MinLevel);
    }

    //
    // Default minimum level
    //

    [Fact]
    public void DefaultMinLevel_IsInfo()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c.WriteTo.Json(ms));
        // The default is Info, so Trace and Debug should be filtered
        logger.Trace("filtered");
        logger.Debug("filtered");
        logger.Info("passes");

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("info", docs[0].RootElement.GetProperty("level").GetString());
    }

    //
    // Log with dynamic level API
    //

    [Fact]
    public void Log_DynamicLevel_RespectsFiltering()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c.MinimumLevel(LogLevel.Warning).WriteTo.Json(ms));

        logger.Log(LogLevel.Info, "filtered", []);
        logger.Log(LogLevel.Warning, "passes", []);

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("warning", docs[0].RootElement.GetProperty("level").GetString());
    }
}
