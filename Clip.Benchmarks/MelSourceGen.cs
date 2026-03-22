using Microsoft.Extensions.Logging;
using MelLL = Microsoft.Extensions.Logging.LogLevel;

namespace Clip.Benchmarks;

//
// Source-generated MEL log methods
//
// These use [LoggerMessage] to eliminate runtime template parsing,
// boxing, and params-array allocation. This is how Microsoft recommends
// using MEL in high-performance paths (.NET 6+).
//

internal static partial class MelSourceGen
{
    [LoggerMessage(Level = MelLL.Information, Message = "Request handled")]
    public static partial void LogRequestHandled(Microsoft.Extensions.Logging.ILogger logger);

    [LoggerMessage(Level = MelLL.Information,
        Message = "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}")]
    public static partial void LogRequestHandledFields(
        Microsoft.Extensions.Logging.ILogger logger, string method, int status,
        double elapsed, Guid requestId, decimal amount);

    [LoggerMessage(Level = MelLL.Error,
        Message = "Connection failed {Host} {Port}")]
    public static partial void LogConnectionFailed(
        Microsoft.Extensions.Logging.ILogger logger, Exception exception, string host, int port);

    [LoggerMessage(Level = MelLL.Information, Message = "Processing {Step}")]
    public static partial void LogProcessing(Microsoft.Extensions.Logging.ILogger logger, string step);

    [LoggerMessage(Level = MelLL.Debug, Message = "This is filtered out")]
    public static partial void LogFiltered(Microsoft.Extensions.Logging.ILogger logger);
}
