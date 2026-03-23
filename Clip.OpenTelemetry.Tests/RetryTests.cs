using System.Net;
using Clip.OpenTelemetry.Export;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Tests;

public class RetryTests
{
    private static OtlpSinkOptions RetryOptions(RetryPolicy policy = RetryPolicy.ExponentialBackoff) => new()
    {
        BatchSize = 1,
        FlushInterval = TimeSpan.FromMilliseconds(50),
        RetryPolicy = policy,
        MaxRetries = 3,
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
        RetryMaxDelay = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public async Task Retry_SucceedsAfterTransientFailure()
    {
        var exporter = new CountingExporter(failCount: 2, retryable: true);

        using var sink = new OtlpSink(RetryOptions(), exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "retry me", ReadOnlySpan<Field>.Empty, null);

        await exporter.WaitForSuccessAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, sink.FailedExports);
        Assert.Equal(3, exporter.Attempts); // 2 failures + 1 success
    }

    [Fact]
    public async Task Retry_DropsOnNonRetryableError()
    {
        var exporter = new CountingExporter(failCount: 100, retryable: false);

        using var sink = new OtlpSink(RetryOptions(), exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "bad request", ReadOnlySpan<Field>.Empty, null);

        await Task.Delay(500);

        Assert.Equal(1, sink.FailedExports);
        Assert.Equal(1, exporter.Attempts); // No retry — dropped immediately.
    }

    [Fact]
    public async Task Retry_DropsAfterMaxRetries()
    {
        var exporter = new CountingExporter(failCount: 100, retryable: true);

        using var sink = new OtlpSink(RetryOptions(), exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "always fails", ReadOnlySpan<Field>.Empty, null);

        await Task.Delay(2000);

        Assert.Equal(1, sink.FailedExports);
        Assert.Equal(4, exporter.Attempts); // 1 initial + 3 retries
    }

    [Fact]
    public async Task Retry_None_DropsImmediately()
    {
        var exporter = new CountingExporter(failCount: 100, retryable: true);

        using var sink = new OtlpSink(RetryOptions(RetryPolicy.None), exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "no retry", ReadOnlySpan<Field>.Empty, null);

        await Task.Delay(500);

        Assert.Equal(1, sink.FailedExports);
        Assert.Equal(1, exporter.Attempts); // No retry even though error is retryable.
    }

    [Fact]
    public async Task Retry_BackoffDelayIncreases()
    {
        var exporter = new TimingExporter(failCount: 3);

        using var sink = new OtlpSink(RetryOptions(), exporter);
        sink.Write(DateTimeOffset.UtcNow, LogLevel.Info, "timing", ReadOnlySpan<Field>.Empty, null);

        await Task.Delay(2000);

        // 4 attempts: initial + 3 retries. Check that gaps between attempts grow.
        Assert.Equal(4, exporter.Timestamps.Count);
        var gaps = new List<double>();
        for (var i = 1; i < exporter.Timestamps.Count; i++)
            gaps.Add((exporter.Timestamps[i] - exporter.Timestamps[i - 1]).TotalMilliseconds);

        // Each gap should be >= the previous (exponential growth, allowing jitter variance).
        // With base=10ms: ~10ms, ~20ms, ~40ms. Just verify they're all positive and trending up.
        Assert.All(gaps, g => Assert.True(g > 0));
        Assert.True(gaps[^1] > gaps[0],
            $"Last gap ({gaps[^1]:F1}ms) should be larger than first ({gaps[0]:F1}ms)");
    }

    //
    // Test doubles
    //

    /// <summary>
    /// Exporter that fails a configurable number of times, then succeeds.
    /// </summary>
    private sealed class CountingExporter(int failCount, bool retryable) : IExporter
    {
        private int _attempts;
        private readonly SemaphoreSlim _success = new(0);

        public int Attempts => Volatile.Read(ref _attempts);

        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt <= failCount)
            {
                if (retryable)
                    throw new HttpRequestException("service unavailable", null, HttpStatusCode.ServiceUnavailable);
                else
                    throw new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
            }

            _success.Release();
            return Task.FromResult(new ExportLogsServiceResponse());
        }

        public async Task WaitForSuccessAsync(TimeSpan timeout)
        {
            if (!await _success.WaitAsync(timeout))
                throw new TimeoutException("Export never succeeded");
        }

        public void Dispose() => _success.Dispose();
    }

    /// <summary>
    /// Exporter that records timestamps of each attempt, always fails with retryable error.
    /// </summary>
    private sealed class TimingExporter(int failCount) : IExporter
    {
        private readonly Lock _lock = new();
        private readonly List<DateTimeOffset> _timestamps = [];

        public List<DateTimeOffset> Timestamps
        {
            get
            {
                lock (_lock)
                    return [.. _timestamps];
            }
        }

        public Task<ExportLogsServiceResponse> ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
        {
            lock (_lock)
                _timestamps.Add(DateTimeOffset.UtcNow);

            if (_timestamps.Count <= failCount)
                throw new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable);

            return Task.FromResult(new ExportLogsServiceResponse());
        }

        public void Dispose() { }
    }
}
