using System.Buffers;
using System.Net.Http.Headers;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Export;

/// <summary>
/// Exports log batches over HTTP/protobuf (POST to <c>{endpoint}/v1/logs</c>).
/// </summary>
internal sealed class HttpExporter : IExporter
{
    private static readonly MediaTypeHeaderValue ProtobufContentType = new("application/x-protobuf");

    private readonly HttpClient _httpClient;
    private readonly Uri _exportUri;
    private readonly TimeSpan _timeout;

    internal HttpExporter(OtlpSinkOptions options)
    {
        _httpClient = new HttpClient();
        _timeout = options.ExportTimeout;

        foreach (var (key, value) in options.Headers)
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);

        var baseUri = options.Endpoint.TrimEnd('/');
        _exportUri = new Uri($"{baseUri}/v1/logs");
    }

    public async Task ExportAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        // Serialize to a pooled buffer instead of allocating via ToByteArray().
        var size = request.CalculateSize();
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var cos = new CodedOutputStream(buffer);
            request.WriteTo(cos);
            cos.Flush();
            using var content = new ByteArrayContent(buffer, 0, size);
            content.Headers.ContentType = ProtobufContentType;

            using var response = await _httpClient.PostAsync(_exportUri, content, cts.Token);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
