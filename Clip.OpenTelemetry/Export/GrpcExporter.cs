using Grpc.Core;
using Grpc.Net.Client;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Export;

/// <summary>
/// Exports log batches over gRPC using <see cref="GrpcChannel"/>.
/// </summary>
internal sealed class GrpcExporter : IExporter
{
    private readonly GrpcChannel _channel;
    private readonly LogsService.LogsServiceClient _client;
    private readonly Metadata _headers;
    private readonly TimeSpan _timeout;

    internal GrpcExporter(OtlpSinkOptions options)
    {
        _channel = GrpcChannel.ForAddress(options.Endpoint);
        _client = new LogsService.LogsServiceClient(_channel);
        _timeout = options.ExportTimeout;

        _headers = [];
        foreach (var (key, value) in options.Headers)
            _headers.Add(key, value);
    }

    public async Task ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var callOptions = new CallOptions(_headers, cancellationToken: cts.Token);
        using var call = _client.ExportAsync(request, callOptions);
        await call.ResponseAsync;
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
