using System.Net;
using System.Net.Sockets;
using Clip.OpenTelemetry.Export;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Tests;

/// <summary>
/// Verifies the HTTP exporter caps response body size, so a malicious or
/// compromised collector cannot OOM the host with a multi-GB response body
/// inside the export timeout window.
/// </summary>
public class HttpResponseSizeCapTests
{
    [Fact]
    public async Task HttpExporter_RejectsOversizedResponseBody()
    {
        using var server = new OversizedResponseServer(bodyBytes: 8 * 1024 * 1024);
        server.Start();

        var options = new OtlpSinkOptions
        {
            Endpoint = server.BaseUrl,
            Protocol = OtlpProtocol.HttpProtobuf,
            ExportTimeout = TimeSpan.FromSeconds(5),
        };

        using var exporter = new HttpExporter(options);
        var request = new ExportLogsServiceRequest();

        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => exporter.ExportAsync(request, CancellationToken.None));
    }

    /// <summary>
    /// Loopback HTTP server that accepts a POST and replies with a body larger
    /// than the exporter's configured cap. Uses HttpListener — no extra deps.
    /// </summary>
    private sealed class OversizedResponseServer(int bodyBytes) : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly int _port = FindFreePort();
        private CancellationTokenSource? _cts;
        private Task? _loop;

        public string BaseUrl => $"http://127.0.0.1:{_port}";

        public void Start()
        {
            _listener.Prefixes.Add($"{BaseUrl}/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => ServeAsync(_cts.Token));
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            var payload = new byte[bodyBytes];
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/x-protobuf";
                ctx.Response.ContentLength64 = payload.Length;
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(payload, ct);
                    ctx.Response.Close();
                }
                catch
                {
                    // Client may abort the read once the cap trips — expected.
                }
            }
        }

        private static int FindFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { }
            try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts?.Dispose();
            ((IDisposable)_listener).Dispose();
        }
    }
}
