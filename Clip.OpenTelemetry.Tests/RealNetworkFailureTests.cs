using Clip.OpenTelemetry.Export;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Tests;

/// <summary>
/// Exercises the actual transport-level failure paths against unreachable endpoints.
/// The unit-level RetryTests use a fake exporter and verify the retry policy mechanism;
/// these tests verify the *exception types* that real transports actually throw, which is
/// what RetryClassifier dispatches on. A regression that swaps real network errors for a
/// type RetryClassifier doesn't recognize would silently drop transient failures.
/// </summary>
public class RealNetworkFailureTests
{
    // 127.0.0.1:1 is reserved (TCPMUX) and reliably refuses connections in test
    // environments without going to the network.
    private const string UnreachableGrpc = "http://127.0.0.1:1";
    private const string UnreachableHttp = "http://127.0.0.1:1";

    [Fact]
    public async Task GrpcExporter_ConnectionRefused_ThrowsRetryableException()
    {
        var options = new OtlpSinkOptions
        {
            Endpoint = UnreachableGrpc,
            Protocol = OtlpProtocol.Grpc,
            ExportTimeout = TimeSpan.FromSeconds(2),
        };

        using var exporter = new GrpcExporter(options);
        var ex = await Record.ExceptionAsync(() =>
            exporter.ExportAsync(new ExportLogsServiceRequest(), CancellationToken.None));

        Assert.NotNull(ex);
        Assert.IsType<RpcException>(ex);
        // Whatever the exact code, RetryClassifier must recognize it as retryable.
        // (Unavailable for connection refused, DeadlineExceeded if ExportTimeout fires first.)
        Assert.True(RetryClassifier.IsRetryable(ex),
            $"Connection-refused RpcException with code {((RpcException)ex).StatusCode} must be classified retryable.");
    }

    [Fact]
    public async Task HttpExporter_ConnectionRefused_ThrowsRetryableException()
    {
        var options = new OtlpSinkOptions
        {
            Endpoint = UnreachableHttp,
            Protocol = OtlpProtocol.HttpProtobuf,
            ExportTimeout = TimeSpan.FromSeconds(2),
        };

        using var exporter = new HttpExporter(options);
        var ex = await Record.ExceptionAsync(() =>
            exporter.ExportAsync(new ExportLogsServiceRequest(), CancellationToken.None));

        Assert.NotNull(ex);
        // HttpClient surfaces network-level failures as HttpRequestException with no
        // StatusCode (since no response was received). RetryClassifier's transport-error
        // branch is precisely this case.
        Assert.IsType<HttpRequestException>(ex);
        Assert.Null(((HttpRequestException)ex).StatusCode);
        Assert.True(RetryClassifier.IsRetryable(ex),
            "HttpRequestException with null StatusCode must be classified retryable.");
    }

    [Fact]
    public async Task GrpcExporter_DnsResolutionFailure_ThrowsRetryableException()
    {
        // .invalid is RFC 6761 reserved — guaranteed never to resolve.
        var options = new OtlpSinkOptions
        {
            Endpoint = "http://nonexistent-host.invalid:4317",
            Protocol = OtlpProtocol.Grpc,
            ExportTimeout = TimeSpan.FromSeconds(2),
        };

        using var exporter = new GrpcExporter(options);
        var ex = await Record.ExceptionAsync(() =>
            exporter.ExportAsync(new ExportLogsServiceRequest(), CancellationToken.None));

        Assert.NotNull(ex);
        Assert.IsType<RpcException>(ex);
        Assert.True(RetryClassifier.IsRetryable(ex),
            $"DNS-failure RpcException with code {((RpcException)ex).StatusCode} must be classified retryable.");
    }

    [Fact]
    public async Task HttpExporter_DnsResolutionFailure_ThrowsRetryableException()
    {
        var options = new OtlpSinkOptions
        {
            Endpoint = "http://nonexistent-host.invalid:4318",
            Protocol = OtlpProtocol.HttpProtobuf,
            ExportTimeout = TimeSpan.FromSeconds(2),
        };

        using var exporter = new HttpExporter(options);
        var ex = await Record.ExceptionAsync(() =>
            exporter.ExportAsync(new ExportLogsServiceRequest(), CancellationToken.None));

        Assert.NotNull(ex);
        Assert.IsType<HttpRequestException>(ex);
        Assert.True(RetryClassifier.IsRetryable(ex),
            "DNS-failure HttpRequestException must be classified retryable.");
    }
}
