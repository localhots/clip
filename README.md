# Clip

Fast structured logging for .NET 9 / C# 13. Very fast. Very opinionated.

## Why

Most C# loggers make you pick between a convenient API and low overhead. The
familiar ones (Serilog, NLog, etc.) allocate 500–1500 bytes per call. Zero-alloc
alternatives are verbose.

Clip also skips the printf-style template strings that most loggers use.
Instead, messages are plain strings and structured data goes in typed fields
alongside them.

The **ergonomic interface** (`Clip.ILogger`) takes anonymous objects and costs
one small allocation (~40 bytes). The **zero-alloc interface**
(`Clip.IZeroLogger`) takes `Field` values on the stack and allocates nothing.
Same output, separate pipelines.

## Speed

Clip formats directly into pooled UTF-8 byte buffers. No intermediate strings,
no allocations on the hot path, no background threads hiding latency. Log calls
are synchronous by default, `BackgroundSink` can be used for async.

Filtered calls (below minimum level) are free — the level check is inlined and
nothing else runs. MEL's source-generated `[LoggerMessage]` variant gets
filtered calls down to ~0.6 ns, somehow still measurably slower than Clip.
That's the one scenario where source generation significantly outperforms
standard MEL.

The zero-alloc interface with five fields runs in ~138 ns with zero heap
allocations. The ergonomic interface runs in ~176 ns with a 72-byte Gen0
allocation. For context, MEL takes ~807 ns and allocates ~808 bytes for the
same work.

Full results in [docs/COMPARE.md](docs/COMPARE.md). To run the benchmarks:

- `make bench` takes ~40 minutes
- `make pdf` produces nice PDFs with charts and notes

## Design

**Fields, not templates.** Messages are plain strings. Structured data goes in
fields alongside the message, not interpolated into it. Sinks get typed field
data directly.

```csharp
// Serilog — template parsed at runtime, field position matters
Log.Information("User {UserId} logged in from {IP}", userId, ip);

// Clip — message is a constant, fields are named and typed
logger.Info("User logged in", new { UserId = userId, IP = ip });
```

**Zero dependencies.** Clip depends only on the .NET 9 runtime. No transitive
NuGet packages.

**Sinks own everything.** The logger is pure dispatch: check level, merge
fields, call sinks. It does no formatting, holds no locks, performs no I/O. Each
sink owns its own output pipeline. Failing sinks don't affect the others or the
caller.

## Requirements

- [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) or later

## Install

```bash
dotnet add package Clip
```

Packages are published to [NuGet.org](https://www.nuget.org/packages/Clip) and
[GitHub Packages](https://github.com/localhots?tab=packages&repo_name=clip).

## Quick Start

```csharp
using Clip;

var logger = Logger.Create(c => c
    .MinimumLevel(LogLevel.Debug)
    .WriteTo.Console());

logger.Info("Server started", new { Port = 8080, Env = "production" });
```

```
2024-01-15 09:30:00.123 INFO Server started                           Env=production Port=8080
```

## Interfaces

**Ergonomic** — pass anonymous objects. One Gen0 allocation per call (~40 bytes).
Fields extracted via compiled expression trees, cached per type.

```csharp
logger.Info("Request handled", new { Method = "GET", Path = "/api/users", Status = 200 });
```

**Zero-alloc** — pass `Field` values directly. Zero heap allocations.

```csharp
logger.Info("Request handled",
    new Field("Method", "GET"),
    new Field("Path", "/api/users"),
    new Field("Status", 200));
```

The compiler selects the interface via `[OverloadResolutionPriority]`. Through
`ILogger`, only the ergonomic interface is visible. On the concrete `Logger`
class, both are available.

See [docs/USAGE.md](docs/USAGE.md) for more examples and output samples.

## Sinks

### Console

ANSI-colored, human-readable output to stderr. Padded messages, sorted fields.

```csharp
var logger = Logger.Create(c => c.WriteTo.Console());
```

```
2024-01-15 09:30:00.123 INFO Starting server                          host=localhost port=8080
2024-01-15 09:30:00.456 WARN High memory usage                        threshold=80 used=85
2024-01-15 09:30:01.789 ERRO Connection failed                        host=db.local
```

### JSON

JSON Lines format via `Utf8JsonWriter`. Field values map directly to typed JSON
methods — no boxing, no intermediate strings.

```csharp
var logger = Logger.Create(c => c.WriteTo.Json(stream));
```

```json
{
  "ts": "2024-01-15T09:30:00.123Z",
  "level": "info",
  "msg": "Starting server",
  "fields": {
    "host": "localhost",
    "port": 8080
  }
}
```

### Multiple Sinks

```csharp
var logger = Logger.Create(c => c
    .MinimumLevel(LogLevel.Debug)
    .WriteTo.Console()
    .WriteTo.Json());
```

Each sink can have its own minimum level.

### Background Sink

Wraps any sink with a bounded channel. The log call enqueues and returns
immediately; a background task drains the queue.

```csharp
var logger = Logger.Create(c => c
    .WriteTo.Background(b => b.Json(), capacity: 4096));
```

On dispose, the channel is drained so no messages are lost.

### OpenTelemetry (OTLP)

Export structured logs to any OpenTelemetry-compatible backend (Jaeger, Grafana,
Datadog, etc.) via gRPC or HTTP/protobuf. Separate package with minimal
dependencies — no OpenTelemetry SDK required.

```bash
dotnet add package Clip.OpenTelemetry
```

```csharp
using Clip.OpenTelemetry;

var logger = Logger.Create(c => c
    .WriteTo.Otlp(opts => {
        opts.Endpoint = "http://collector:4317";
        opts.ServiceName = "my-service";
    }));
```

Logs are batched internally and exported on a background thread. Clip fields map
directly to OTLP attributes, log levels map to OTLP severity numbers. Supports
`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`,
`OTEL_EXPORTER_OTLP_HEADERS`, and `OTEL_SERVICE_NAME` environment variables.

To use HTTP/protobuf instead of gRPC:

```csharp
opts.Protocol = OtlpProtocol.HttpProtobuf;
opts.Endpoint = "http://collector:4318";
```

### Custom Sinks

Implement `ILogSink`:

```csharp
public interface ILogSink : IDisposable
{
    void Write(DateTimeOffset timestamp, LogLevel level, string message,
               ReadOnlySpan<Field> fields, Exception? exception);
}
```

Register with `.WriteTo.Sink(mySink)`. Note that `ReadOnlySpan<Field>` cannot be
captured — process or copy field data synchronously.

## Enrichers

Add fields to every log entry automatically. Good for things like app name,
hostname, or environment.

```csharp
var logger = Logger.Create(c => c
    .Enrich.Field("app", "my-service")
    .Enrich.With(new MyEnricher())
    .WriteTo.Console());
```

Enrichers can be level-gated — attach verbose data only to warnings and errors:

```csharp
c.Enrich.With(new RequestBodyEnricher(), minLevel: LogLevel.Warning)
```

Enricher fields have the lowest priority — context and call-site fields override
them on key collision.

## Filters

Exclude fields entirely — filtered fields never reach redactors or sinks.

```csharp
var logger = Logger.Create(c => c
    .Filter.Fields("_internal", "debug_trace")
    .Filter.Pattern(@"^temp_")
    .WriteTo.Console());
```

Custom filters implement `ILogFilter`:

```csharp
public class PrefixFilter(string prefix) : ILogFilter
{
    public bool ShouldSkip(string key) => key.StartsWith(prefix);
}

// Register: .Filter.With(new PrefixFilter("_"))
```

## Redactors

Scrub sensitive values before they reach any sink. Runs after all fields are
merged. Unlike filters (which remove fields), redactors replace values — the
field key remains visible.

```csharp
var logger = Logger.Create(c => c
    .Redact.Fields("password", "token")
    .Redact.Pattern(@"\d{4}-\d{4}-\d{4}-(\d{4})", "****-****-****-$1")
    .WriteTo.Console());
```

## Context Scopes

Attach fields to all log calls within a scope. Async-safe via `AsyncLocal`.

```csharp
using (logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
{
    logger.Info("Processing");      // includes RequestId + UserId
    logger.Info("Done");            // same context
}
logger.Info("Outside");             // context gone
```

Scopes nest. Inner fields override outer fields with the same key.

## Log Levels

`Trace` · `Debug` · `Info` · `Warning` · `Error` · `Fatal`

```csharp
logger.Trace("detailed diagnostics");
logger.Debug("internal state", new { Queue = 12 });
logger.Info("normal operation");
logger.Warning("something unusual", new { Retries = 3 });
logger.Error("operation failed", exception, new { Code = 500 });
logger.Fatal("unrecoverable");
```

Filtered calls (below minimum level) are effectively free — the level check is
inlined and the rest of the method is never entered.

## MEL Adapter

Drop Clip into an existing `Microsoft.Extensions.Logging` setup via the
`Clip.Extensions.Logging` package:

```csharp
builder.Logging.AddClip(options => {
    options.MinimumLevel = LogLevel.Debug;
});
```

Or pass an existing `Logger` instance:

```csharp
builder.Logging.AddClip(myLogger);
```

## Analyzers

Clip ships with Roslyn analyzers that catch common mistakes at compile time.
Install alongside Clip:

```bash
dotnet add package Clip.Analyzers
```

| ID      | Severity | Description                                                        | Code Fix                |
|---------|----------|--------------------------------------------------------------------|-------------------------|
| CLIP001 | Error    | Invalid fields argument — primitives, strings, arrays not accepted | Wrap in anonymous type  |
| CLIP002 | Warning  | Message contains `{Placeholder}` template syntax                   | Move to fields          |
| CLIP003 | Warning  | `AddContext` return value not disposed                             | Add `using`             |
| CLIP004 | Info     | Exception not passed to `Error` in catch block                     | Add exception parameter |
| CLIP005 | Warning  | Unreachable code after `Fatal`                                     | —                       |
| CLIP006 | Warning  | Interpolated string in log message                                 | Extract to fields       |
| CLIP007 | Info     | Exception wrapped in fields anonymous type                         | Use `Error` overload    |
| CLIP008 | Info     | Empty or whitespace log message                                    | —                       |
| CLIP009 | Info     | Log message starts with lowercase                                  | Capitalize              |

## Build & Test

```bash
make help          # show all targets
make check         # build + test
make bench         # run benchmark suite (~40 minutes)
make pdf           # generate charts + PDFs
make demo          # run demo app
```

## License

[MIT](LICENSE)
