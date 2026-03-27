# Clip — Benchmark Comparison

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm) (container)  
.NET SDK 9.0.312  
Run: 2026-03-27 00:48

Clip is a zero-dependency structured logging library for .NET 9. It formats directly into pooled UTF-8 byte buffers — no intermediate strings, no allocations on the hot path, no background-thread tricks to hide latency.

Clip ships two APIs that produce identical output: **Clip** (ergonomic — pass an anonymous object, fields extracted via compiled expression trees) and **ClipZero** (zero-alloc — pass `Field` structs on the stack, nothing touches the heap).

```csharp
// Ergonomic — one anonymous-object allocation, fields cached per type
logger.Info("Request handled",
    new { Method, Status, Elapsed, RequestId, Amount });

// Zero-alloc — stack-allocated structs, zero heap allocations
logger.Info("Request handled",
    new Field("Method", Method),
    new Field("Status", Status),
    new Field("Elapsed", Elapsed),
    new Field("RequestId", ReqId),
    new Field("Amount", Amount));
```

This report puts Clip head-to-head against eight other .NET loggers, all writing to `Stream.Null` so we measure pure formatting cost:

- **Serilog** — rich sink ecosystem and message templates. Allocates a `LogEvent` and boxes value types per call.
- **NLog** — layout renderers give surgical control over output. String-based rendering with per-call allocations.
- **MEL** (Microsoft.Extensions.Logging) — ships with ASP.NET Core. Virtual dispatch, provider iteration, background I/O thread.
- **MELSrcGen** — MEL with `[LoggerMessage]` source generation. Eliminates runtime template parsing and value-type boxing. Same MEL pipeline underneath — this is how Microsoft recommends using MEL in hot paths.
- **ZLogger** — Cysharp's high-performance logger built on MEL. Defers formatting to a background thread — under sustained load, backpressure adds cost on the calling thread.
- **log4net** — the port of Java's Log4j. No structured fields, pattern layouts all the way down.
- **ClipMEL** — Clip behind MEL's `ILogger` via `Clip.Extensions.Logging`. Shows MEL abstraction cost.
- **ZeroLog** — Abc-Arbitrage's zero-allocation logger. Builder API, synchronous mode — measures full formatting cost.

## Feature Matrix

| API & Data Model | Clip | Serilog | NLog | MEL | ZLogger | Log4Net | ZeroLog |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Structured Fields | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ |
| Typed Fields | ✅ | — | — | — | ✅ | — | ✅ |
| Zero-Alloc API | ✅ | — | — | — | ✅ | — | ✅ |
| Message Templates | — | ✅ | ✅ | ✅ | — | — | — |
| Source Generator | — | — | — | ✅ | ✅ | — | — |

| Pipeline | Clip | Serilog | NLog | MEL | ZLogger | Log4Net | ZeroLog |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Enrichers | ✅ | ✅ | ✅ | ✅ | — | — | — |
| Level-Gated Enrichers | ✅ | ✅ | — | — | — | — | — |
| Filters | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Redactors | ✅ | — | — | ✅ | — | — | — |
| Scoped Context | ✅ | ✅ | ✅ | ✅ | ✅ | — | — |

| Output | Clip | Serilog | NLog | MEL | ZLogger | Log4Net | ZeroLog |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Console Sink | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| JSON Sink | ✅ | ✅ | ✅ | ✅ | ✅ | — | — |
| File Sink | ✅ | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| OpenTelemetry / OTLP | ✅ | ✅ | ✅ | ✅ | — | — | — |

| Architecture | Clip | Serilog | NLog | MEL | ZLogger | Log4Net | ZeroLog |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Sync-by-Default | ✅ | ✅ | ✅ | — | — | ✅ | — |
| Async / Background | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ |
| Buffer Pooling | ✅ | — | ✅ | — | ✅ | — | ✅ |
| Zero Dependencies | ✅ | — | ✅ | — | — | — | — |
| MEL Adapter | ✅ | ✅ | ✅ | — | — | ✅ | — |


---

## Filtered

Debug call at Info minimum level — measures the cost of checking the level and returning without doing any work.

```csharp
logger.Debug("This is filtered out");
```

![Filtered](docs/charts/Filtered.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | Allocated |
|--------|-----:|------:|-------:|----------:|
| **Clip** | 0.0000 ns | 0.0000 ns | 0.0000 ns | - |
| **ClipZero** | 0.0000 ns | 0.0000 ns | 0.0000 ns | - |
| **ClipMEL** | 5.1697 ns | 0.0136 ns | 0.0273 ns | - |
| MEL | 4.8919 ns | 0.0074 ns | 0.0145 ns | - |
| MELSrcGen | 0.4775 ns | 0.0002 ns | 0.0004 ns | - |
| Serilog | 0.5219 ns | 0.0057 ns | 0.0113 ns | - |
| ZLogger | 3.0037 ns | 0.0013 ns | 0.0026 ns | - |
| NLog | 0.0000 ns | 0.0000 ns | 0.0000 ns | - |
| Log4Net | 2.9125 ns | 0.0004 ns | 0.0008 ns | - |
| ZeroLog | 0.3273 ns | 0.0055 ns | 0.0112 ns | - |

</details>

> **Clip:** Single integer comparison, inlined by the JIT.

<!-- -->

> **ClipMEL:** Clip behind MEL's ILogger interface. The cost here is entirely MEL's own dispatch — MEL's global filter rejects the call before it ever reaches Clip's provider. Matches bare MEL.

<!-- -->

> **MEL:** Virtual dispatch through the `ILogger` interface, then iterates registered providers to check their levels.

<!-- -->

> **MELSrcGen:** Source-generated method checks `ILogger.IsEnabled` before doing any work — same dispatch cost as MEL.

<!-- -->

> **Serilog:** Enum comparison against a mutable level switch. Fast, but the indirection prevents inlining.

<!-- -->

> **ZLogger:** Built on MEL, so pays the same interface-dispatch and provider-iteration cost.

<!-- -->

> **NLog:** Reads a cached boolean flag. Near-zero overhead.

<!-- -->

> **Log4Net:** Walks a parent-child logger hierarchy to resolve the effective level.

<!-- -->

> **ZeroLog:** Checks a cached level flag. Near-zero overhead. Benchmarked via the concrete sealed `ZeroLog.Log` class — ZeroLog does not expose an interface, giving it a small dispatch advantage over loggers benchmarked through interfaces.

<!-- -->

> All loggers check the level and return immediately. No message is formatted, no output is written.

---

## Console

Human-readable text output — the format most developers stare at during local development. Each logger formats a line with timestamp, level, message, and structured fields, then writes to `Stream.Null` so we measure pure formatting cost, not I/O.

Clip's console output:

```
2026-03-19 10:30:45.123 INFO Request handled                           Method=GET Path=/api/users Status=200
2026-03-19 10:30:45.860 ERRO Connection failed                         Host=db.local Port=5432
  System.InvalidOperationException: connection refused
```

This is where architectural choices really show. Clip formats directly into a pooled UTF-8 byte buffer — one pass, no intermediate strings, no garbage. Serilog and NLog allocate event objects and render through layers of abstractions. MEL formats synchronously, then hands a string to a background thread for the actual write — you pay for formatting *and* the handoff. ZLogger punts *everything* to a background thread, so its numbers here only show what it costs to drop a message on a queue — the real work happens later, off the clock.

### Console: No Fields

Message only, no structured fields attached.

```csharp
logger.Info("Request handled");
```

![Console_NoFields](docs/charts/Console_NoFields.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 26.42 ns | 0.020 ns | 0.040 ns | 1.00 | - |
| **ClipZero** | 26.44 ns | 0.020 ns | 0.039 ns | 1.00 | - |
| **ClipMEL** | 57.54 ns | 0.042 ns | 0.084 ns | 2.18 | 64 B |
| MEL | 372.86 ns | 5.612 ns | 11.337 ns | 14.11 | 304 B |
| MELSrcGen | 375.11 ns | 8.219 ns | 16.603 ns | 14.20 | 320 B |
| Serilog | 276.33 ns | 0.473 ns | 0.945 ns | 10.46 | 416 B |
| ZLogger | 341.99 ns | 0.410 ns | 0.810 ns | 12.94 | - |
| NLog | 143.87 ns | 0.256 ns | 0.518 ns | 5.45 | 304 B |
| Log4Net | 181.89 ns | 0.243 ns | 0.491 ns | 6.88 | 392 B |
| ZeroLog | 113.13 ns | 0.133 ns | 0.266 ns | 4.28 | - |

</details>

> **Clip:** Formats into a pooled byte buffer and writes UTF-8 directly — no intermediate strings. Timestamp is cached so repeated calls within the same millisecond skip reformatting.

<!-- -->

> **ClipMEL:** Clip's formatting engine behind MEL's ILogger. Measures the cost of MEL's abstraction layer on top of Clip.

<!-- -->

> **MEL:** Formats the message synchronously on the calling thread via SimpleConsoleFormatter, then enqueues the formatted string for background I/O. The benchmark captures the full formatting cost.

<!-- -->

> **MELSrcGen:** Source-generated `[LoggerMessage]` method — skips runtime template parsing. Same MEL pipeline (SimpleConsoleFormatter + background I/O) but the generated code is more efficient at the call site.

<!-- -->

> **Serilog:** Allocates a log-event object and parses the message template per call. Output is rendered as strings via a TextWriter, not raw bytes.

<!-- -->

> **ZLogger:** Enqueues the raw state to a background thread — formatting is fully deferred. Under sustained load the background queue fills up, adding backpressure to the calling thread.

<!-- -->

> **NLog:** Allocates a log-event object per call. Output is produced by a chain of layout renderers writing strings.

<!-- -->

> **Log4Net:** Synchronous like Clip. Allocates a log-event object and formats through a pattern layout to strings.

<!-- -->

> **ZeroLog:** Abc-Arbitrage's zero-allocation logger. Running in synchronous mode so the benchmark measures full formatting cost, not just enqueue. Benchmarked via the concrete sealed `ZeroLog.Log` class (no interface available), giving it a small dispatch advantage.

### Console: Five Fields

Message with five structured fields: string, int, double, Guid, and decimal.

```csharp
logger.Info("Request handled", new {
    Method = "GET",
    Status = 200,
    Elapsed = 1.234d,
    RequestId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
    Amount = 49.95m,
});
```

![Console_FiveFields](docs/charts/Console_FiveFields.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 191.20 ns | 0.284 ns | 0.573 ns | 1.00 | 72 B |
| **ClipZero** | 140.63 ns | 0.121 ns | 0.241 ns | 0.74 | - |
| **ClipMEL** | 390.26 ns | 0.448 ns | 0.906 ns | 2.04 | 608 B |
| MEL | 886.08 ns | 3.898 ns | 7.875 ns | 4.63 | 760 B |
| MELSrcGen | 783.41 ns | 5.874 ns | 11.865 ns | 4.10 | 856 B |
| Serilog | 715.36 ns | 2.004 ns | 4.049 ns | 3.74 | 1216 B |
| ZLogger | 500.95 ns | 0.478 ns | 0.955 ns | 2.62 | - |
| NLog | 615.36 ns | 1.444 ns | 2.916 ns | 3.22 | 1368 B |
| Log4Net | 318.79 ns | 0.334 ns | 0.660 ns | 1.67 | 888 B |
| ZeroLog | 278.05 ns | 0.178 ns | 0.356 ns | 1.45 | - |

</details>

> **Clip:** Ergonomic tier allocates one anonymous object (40 B); fields extracted via compiled expression trees (cached per type). Zero-alloc tier passes fields as stack-allocated structs — no boxing, no heap allocation. Both write typed values into the same pooled byte buffer.

<!-- -->

> **ClipMEL:** Same MEL template API as MEL, but formatting is handled by Clip's engine underneath.

<!-- -->

> **MEL:** Formats synchronously, then enqueues for background I/O. Value-type arguments are boxed.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing, no boxing. Strongly-typed parameters passed directly. Same MEL formatting pipeline underneath.

<!-- -->

> **Serilog:** Each value is wrapped in a property object and value types are boxed. The template is parsed to match placeholders to arguments.

<!-- -->

> **ZLogger:** Background thread — formatting deferred. Interpolated-string handlers avoid boxing but add struct construction overhead. Under sustained load, backpressure from a full queue increases calling-thread cost.

<!-- -->

> **NLog:** Value-type arguments are boxed. Layout renderers write each property as a string.

<!-- -->

> **Log4Net:** Synchronous. Uses printf-style placeholders ({0}) — no named structured fields. Arguments are boxed.

<!-- -->

> **ZeroLog:** Synchronous mode. Fields attached via builder API (AppendKeyValue). Zero heap allocation per call. Benchmarked via concrete sealed class (no interface available).

### Console: With Context

Message inside a logging scope that adds two context fields, plus one call-site field.

```csharp
using (logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
{
    logger.Info("Processing", new { Step = "auth" });
}
```

![Console_WithContext](docs/charts/Console_WithContext.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 125.06 ns | 0.070 ns | 0.136 ns | 1.00 | 232 B |
| **ClipZero** | 107.27 ns | 0.463 ns | 0.924 ns | 0.86 | 176 B |
| **ClipMEL** | 231.44 ns | 0.538 ns | 1.037 ns | 1.85 | 576 B |
| MEL | 557.60 ns | 10.829 ns | 21.875 ns | 4.46 | 744 B |
| MELSrcGen | 540.52 ns | 11.744 ns | 23.723 ns | 4.32 | 760 B |
| Serilog | 648.46 ns | 2.207 ns | 4.458 ns | 5.19 | 1344 B |
| ZLogger | 517.91 ns | 5.842 ns | 11.801 ns | 4.14 | 200 B |
| NLog | 422.64 ns | 0.709 ns | 1.431 ns | 3.38 | 1288 B |

</details>

> **Clip:** Context stored in AsyncLocal<Field[]>. Ergonomic tier allocates an anonymous object for call-site fields; zero-alloc tier passes them as stack-allocated structs. Context and call-site fields merged at write time.

<!-- -->

> **ClipMEL:** Uses MEL's BeginScope, then delegates to Clip's formatting engine.

<!-- -->

> **MEL:** Scope stored on the calling thread, formatted synchronously by SimpleConsoleFormatter. Only the final I/O write is deferred.

<!-- -->

> **MELSrcGen:** Source-generated log call within MEL's BeginScope. No template parsing or boxing for the call-site field. Same scope + formatting pipeline as MEL.

<!-- -->

> **Serilog:** LogContext pushes properties via AsyncLocal. Properties are merged into the event object at construction time.

<!-- -->

> **ZLogger:** Scope stored on the calling thread, formatting deferred to a background thread. The benchmark only measures the calling thread.

<!-- -->

> **NLog:** ScopeContext pushes properties via AsyncLocal. Merged at layout render time.

<!-- -->

> Log4Net and ZeroLog are excluded from context benchmarks — neither has a scoped-context API comparable to Serilog LogContext, NLog ScopeContext, or MEL BeginScope.

### Console: With Exception

Message with an attached exception including a full stack trace.

```csharp
logger.Error("Connection failed", ex, new {
    Host = "db.local",
    Port = 5432,
});
```

![Console_WithException](docs/charts/Console_WithException.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 1,719.24 ns | 1.907 ns | 3.852 ns | 1.00 | 2480 B |
| **ClipZero** | 1,704.84 ns | 1.200 ns | 2.396 ns | 0.99 | 2448 B |
| **ClipMEL** | 1,799.20 ns | 1.795 ns | 3.458 ns | 1.05 | 2744 B |
| MEL | 3,401.88 ns | 41.472 ns | 83.775 ns | 1.98 | 4064 B |
| MELSrcGen | 3,140.83 ns | 24.142 ns | 48.768 ns | 1.83 | 4064 B |
| Serilog | 2,152.17 ns | 2.143 ns | 4.231 ns | 1.25 | 3960 B |
| ZLogger | 1,988.66 ns | 232.719 ns | 448.371 ns | 1.16 | 449 B |
| NLog | 2,180.38 ns | 2.377 ns | 4.748 ns | 1.27 | 4136 B |
| Log4Net | 1,954.66 ns | 1.415 ns | 2.726 ns | 1.14 | 4544 B |
| ZeroLog | 1,913.81 ns | 3.963 ns | 7.915 ns | 1.11 | 2832 B |

</details>

> **Clip:** Exception rendered synchronously into the same pooled byte buffer.

<!-- -->

> **ClipMEL:** Exception formatted by Clip's engine behind MEL's ILogger interface.

<!-- -->

> **MEL:** Exception formatted synchronously on the calling thread by SimpleConsoleFormatter (including exception.ToString()). Only the final I/O write is deferred to a background thread.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing or boxing. Exception still formatted synchronously by SimpleConsoleFormatter.

<!-- -->

> **Serilog:** Exception rendered synchronously, appended as a string to the output.

<!-- -->

> **ZLogger:** Exception formatting deferred to a background thread. Under sustained load, backpressure causes high variance and inflated mean times.

<!-- -->

> **NLog:** Exception rendered synchronously via the layout. Full stack trace appended as text after the message.

<!-- -->

> **Log4Net:** Exception rendered synchronously via the layout pattern. Full stack trace appended as text.

<!-- -->

> **ZeroLog:** Synchronous mode. Exception attached via builder. Zero heap allocation per call. Benchmarked via concrete sealed class (no interface available).

<!-- -->

> Exception benchmarks are not directly comparable across loggers — ZLogger defers formatting to a background thread (with backpressure under sustained load) while all others format synchronously.

---

## JSON

Structured JSON — the format that actually goes to production log aggregators. Each logger serializes a JSON object with timestamp, level, message, and fields to `Stream.Null` so we measure serialization cost, not I/O.

Clip's JSON output:

```json
{
  "ts": "2026-03-19T10:30:45.123Z",
  "level": "info",
  "msg": "Request handled",
  "fields": {
    "Method": "GET",
    "Status": 200,
    "Elapsed": 1.234,
    "RequestId": "550e8400-e29b-41d4-a716-446655440000",
    "Amount": 49.95
  }
}
```

Fields are typed — strings are quoted, numbers are bare, exceptions become nested `error` objects with `type`, `msg`, and `stack` fields. No toString() on everything.

JSON serialization is a harder test than console output. You need proper escaping, correct numeric formatting, and structured nesting — not just string concatenation. Clip writes JSON as raw UTF-8 bytes using a Utf8JsonWriter-style approach with SIMD string escaping. Serilog wraps every value in its own property/value object hierarchy before serializing through a TextWriter. NLog renders each attribute individually through its layout engine — strings all the way. ZLogger defers serialization entirely to a background thread. And log4net? It doesn't have a JSON formatter at all — it fakes it with a pattern string shaped like JSON. Structured fields don't even make it into the output.

### JSON: No Fields

Message only, no structured fields attached.

```csharp
logger.Info("Request handled");
```

![Json_NoFields](docs/charts/Json_NoFields.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 26.68 ns | 0.011 ns | 0.020 ns | 1.00 | - |
| **ClipZero** | 27.01 ns | 0.007 ns | 0.014 ns | 1.01 | - |
| **ClipMEL** | 61.61 ns | 0.144 ns | 0.274 ns | 2.31 | 64 B |
| MEL | 837.89 ns | 3.834 ns | 7.656 ns | 31.41 | 784 B |
| MELSrcGen | 802.90 ns | 9.295 ns | 18.777 ns | 30.09 | 752 B |
| Serilog | 247.25 ns | 0.292 ns | 0.589 ns | 9.27 | 608 B |
| ZLogger | 325.55 ns | 7.446 ns | 15.042 ns | 12.20 | - |
| NLog | 138.79 ns | 0.238 ns | 0.476 ns | 5.20 | 291 B |

</details>

> **Clip:** Builds JSON as raw UTF-8 bytes into a pooled buffer. String values are escaped using SIMD.

<!-- -->

> **MEL:** Uses JsonConsoleFormatter. Formats synchronously on the calling thread, then enqueues for background I/O.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing. Same JsonConsoleFormatter pipeline as MEL.

<!-- -->

> **Serilog:** Serializes through its own object model — each value is wrapped in a property object. Output goes through a TextWriter (strings, not raw bytes).

<!-- -->

> **ZLogger:** Background thread — formatting deferred. Has a real JSON formatter. Under sustained load, backpressure from a full queue increases calling-thread cost.

<!-- -->

> **NLog:** Each JSON attribute is rendered individually through the layout engine. String-based output.

### JSON: Five Fields

Message with five structured fields: string, int, double, Guid, and decimal.

```csharp
logger.Info("Request handled", new {
    Method = "GET",
    Status = 200,
    Elapsed = 1.234d,
    RequestId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
    Amount = 49.95m,
});
```

![Json_FiveFields](docs/charts/Json_FiveFields.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 176.20 ns | 0.262 ns | 0.529 ns | 1.00 | 72 B |
| **ClipZero** | 129.18 ns | 0.092 ns | 0.185 ns | 0.73 | - |
| **ClipMEL** | 451.20 ns | 0.459 ns | 0.862 ns | 2.56 | 704 B |
| MEL | 1,924.97 ns | 4.528 ns | 9.043 ns | 10.92 | 1824 B |
| MELSrcGen | 1,931.31 ns | 14.522 ns | 28.323 ns | 10.96 | 2272 B |
| Serilog | 911.76 ns | 0.723 ns | 1.323 ns | 5.17 | 1408 B |
| ZLogger | 1,154.44 ns | 86.505 ns | 170.752 ns | 6.55 | 12 B |
| NLog | 789.35 ns | 0.932 ns | 1.882 ns | 4.48 | 1387 B |

</details>

> **Clip:** Ergonomic tier allocates one anonymous object (40 B); fields extracted via expression trees. Zero-alloc tier passes stack-allocated structs directly. Both write typed JSON values with no boxing and no intermediate strings.

<!-- -->

> **MEL:** Uses JsonConsoleFormatter. Value types are boxed. Formatted synchronously, then enqueued for background I/O.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing, no boxing. Same JsonConsoleFormatter pipeline as MEL.

<!-- -->

> **Serilog:** Each argument is wrapped in a property object then serialized. Value types are boxed.

<!-- -->

> **ZLogger:** Background thread — formatting deferred. Interpolated-string handlers avoid boxing but add struct construction overhead. Under sustained load, backpressure from a full queue increases calling-thread cost.

<!-- -->

> **NLog:** Event properties are boxed and rendered through the layout engine as strings.

### JSON: With Context

Message inside a logging scope that adds two context fields, plus one call-site field.

```csharp
using (logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
{
    logger.Info("Processing", new { Step = "auth" });
}
```

![Json_WithContext](docs/charts/Json_WithContext.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 123.08 ns | 0.101 ns | 0.203 ns | 1.00 | 232 B |
| **ClipZero** | 105.47 ns | 0.145 ns | 0.293 ns | 0.86 | 176 B |
| **ClipMEL** | 222.10 ns | 1.266 ns | 2.409 ns | 1.80 | 576 B |
| MEL | 1,310.36 ns | 47.158 ns | 95.262 ns | 10.65 | 1440 B |
| MELSrcGen | 1,316.70 ns | 31.368 ns | 63.366 ns | 10.70 | 1432 B |
| Serilog | 677.96 ns | 0.433 ns | 0.854 ns | 5.51 | 1432 B |
| ZLogger | 583.22 ns | 9.744 ns | 19.683 ns | 4.74 | 200 B |
| NLog | 441.53 ns | 0.435 ns | 0.827 ns | 3.59 | 1291 B |

</details>

> **Clip:** Ergonomic tier allocates an anonymous object for call-site fields; zero-alloc tier uses stack-allocated structs. Context and call-site fields merged at write time into the same pooled buffer.

<!-- -->

> **MEL:** Scope stored on the calling thread, formatted synchronously by JsonConsoleFormatter. Only the final I/O write is deferred.

<!-- -->

> **MELSrcGen:** Source-generated log call within MEL's BeginScope. Same JsonConsoleFormatter pipeline as MEL.

<!-- -->

> **Serilog:** Context properties merged into the event object and serialized through the object model.

<!-- -->

> **ZLogger:** Scope stored on the calling thread, rendered on a background thread. The benchmark only measures the calling thread.

<!-- -->

> **NLog:** Scope properties merged and rendered through the layout engine.

<!-- -->

> Log4Net and ZeroLog are excluded from context benchmarks — neither has a scoped-context API comparable to Serilog LogContext, NLog ScopeContext, or MEL BeginScope.

### JSON: With Exception

Message with an attached exception including a full stack trace.

```csharp
logger.Error("Connection failed", ex, new {
    Host = "db.local",
    Port = 5432,
});
```

![Json_WithException](docs/charts/Json_WithException.svg)

<details>
<summary>Benchmark data</summary>

| Logger | Mean | Error | StdDev | vs Clip | Allocated |
|--------|-----:|------:|-------:|--------:|----------:|
| **Clip** | 1,713.26 ns | 1.670 ns | 3.373 ns | 1.00 | 2480 B |
| **ClipZero** | 1,676.58 ns | 1.412 ns | 2.852 ns | 0.98 | 2448 B |
| **ClipMEL** | 1,771.99 ns | 2.577 ns | 4.841 ns | 1.03 | 2464 B |
| MEL | 3,751.33 ns | 12.818 ns | 24.388 ns | 2.19 | 4360 B |
| MELSrcGen | 3,795.16 ns | 11.699 ns | 21.974 ns | 2.22 | 4368 B |
| Serilog | 2,394.40 ns | 2.720 ns | 5.306 ns | 1.40 | 3760 B |
| ZLogger | 4,991.11 ns | 1,105.786 ns | 2,208.375 ns | 2.91 | 459 B |
| NLog | 2,345.66 ns | 2.618 ns | 5.288 ns | 1.37 | 4435 B |

</details>

> **Clip:** Exception serialized as a structured JSON object synchronously into the pooled buffer.

<!-- -->

> **MEL:** Exception formatted synchronously by JsonConsoleFormatter. Only the final I/O write is deferred.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing or boxing. Exception still formatted synchronously by JsonConsoleFormatter.

<!-- -->

> **Serilog:** Exception serialized as a string property synchronously.

<!-- -->

> **ZLogger:** Exception formatting deferred to a background thread. Under sustained load, backpressure causes high variance and inflated mean times.

<!-- -->

> **NLog:** Exception serialized as a JSON string attribute synchronously.

<!-- -->

> Exception benchmarks are not directly comparable across loggers — ZLogger defers formatting to a background thread (with backpressure under sustained load) while all others format synchronously.

<!-- -->

> Log4Net and ZeroLog are excluded from JSON benchmarks. Log4Net has no JSON formatter. ZeroLog has no built-in JSON output mode.
