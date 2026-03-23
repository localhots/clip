namespace Clip.OpenTelemetry;

/// <summary>Retry behavior for failed OTLP exports.</summary>
public enum RetryPolicy
{
    /// <summary>No retries. Failed batches are dropped immediately.</summary>
    None,

    /// <summary>
    /// Retry retryable errors with exponential backoff + jitter.
    /// Non-retryable errors (e.g. HTTP 400, gRPC InvalidArgument) are dropped immediately.
    /// </summary>
    ExponentialBackoff,
}
