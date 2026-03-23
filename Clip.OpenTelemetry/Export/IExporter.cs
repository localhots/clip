using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Clip.OpenTelemetry.Export;

/// <summary>
/// Protocol-specific OTLP exporter. Implementations handle serialization
/// and transport (gRPC or HTTP/protobuf).
/// </summary>
internal interface IExporter : IDisposable
{
    Task ExportAsync(ExportLogsServiceRequest request, CancellationToken ct);
}
