using Clip.OpenTelemetry.Mapping;
using SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber;

namespace Clip.OpenTelemetry.Tests;

public class SeverityMappingTests
{
    [Theory]
    [InlineData(LogLevel.Trace, 1, "TRACE")]
    [InlineData(LogLevel.Debug, 5, "DEBUG")]
    [InlineData(LogLevel.Info, 9, "INFO")]
    [InlineData(LogLevel.Warning, 13, "WARN")]
    [InlineData(LogLevel.Error, 17, "ERROR")]
    [InlineData(LogLevel.Fatal, 21, "FATAL")]
    public void AllLevels_MapCorrectly(LogLevel clipLevel, int expectedNumber, string expectedText)
    {
        var (number, text) = FieldMapper.ToSeverity(clipLevel);

        Assert.Equal((SeverityNumber)expectedNumber, number);
        Assert.Equal(expectedText, text);
    }
}
