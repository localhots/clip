namespace Clip.OpenTelemetry;

/// <summary>The OTLP transport protocol to use for exporting logs.</summary>
public enum OtlpProtocol
{
    /// <summary>gRPC transport (default OTLP port 4317).</summary>
    Grpc,

    /// <summary>HTTP/protobuf transport (default OTLP port 4318).</summary>
    HttpProtobuf,
}
