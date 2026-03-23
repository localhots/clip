using System.Net;
using Grpc.Core;

namespace Clip.OpenTelemetry.Export;

/// <summary>
/// Classifies export exceptions as retryable or non-retryable per the OTLP spec.
/// </summary>
internal static class RetryClassifier
{
    /// <summary>
    /// Returns <c>true</c> if the exception represents a transient failure
    /// that should be retried per the OTLP specification.
    /// </summary>
    internal static bool IsRetryable(Exception ex)
    {
        return ex switch
        {
            // OTLP spec: HTTP 429, 502, 503, 504 are retryable.
            HttpRequestException
            {
                StatusCode: HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout,
            } => true,

            // OTLP spec: these gRPC codes are retryable.
            // Note: Cancelled is excluded — it indicates client-initiated cancellation (e.g. disposal),
            // not a transient server error.
            RpcException rpc => rpc.StatusCode is
                StatusCode.Unavailable or
                StatusCode.DeadlineExceeded or
                StatusCode.Aborted or
                StatusCode.OutOfRange or
                StatusCode.DataLoss or
                StatusCode.ResourceExhausted,

            // Network-level failures (DNS, connection refused) — no HTTP status code.
            HttpRequestException { StatusCode: null } => true,

            _ => false,
        };
    }
}
