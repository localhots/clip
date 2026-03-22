# Clip — Benchmark Comparison

BenchmarkDotNet v0.15.8, macOS Tahoe 26.3.1 (25D2128) [Darwin 25.3.0]  
Apple M5, 1 CPU, 10 logical and 10 physical cores  
Run: 2026-03-22 20:36

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

This report puts Clip head-to-head against six established .NET loggers, all writing to `Stream.Null` so we measure pure formatting cost:

- **Serilog** — rich sink ecosystem and message templates. Allocates a `LogEvent` and boxes value types per call.
- **NLog** — layout renderers give surgical control over output. String-based rendering with per-call allocations.
- **MEL** (Microsoft.Extensions.Logging) — ships with ASP.NET Core. Virtual dispatch, provider iteration, background I/O thread.
- **MELSrcGen** — MEL with `[LoggerMessage]` source generation. Eliminates runtime template parsing and value-type boxing. Same MEL pipeline underneath — this is how Microsoft recommends using MEL in hot paths.
- **ZLogger** — Cysharp's high-performance logger built on MEL. Defers *all* formatting to a background thread — benchmarks only reflect enqueue cost.
- **log4net** — the port of Java's Log4j. No structured fields, pattern layouts all the way down.
- **ClipMEL** — Clip behind MEL's `ILogger` via `Clip.Extensions.Logging`. Shows MEL abstraction cost.
- **ZeroLog** — Abc-Arbitrage's zero-allocation logger. Builder API, synchronous mode — measures full formatting cost.

## Feature Matrix

| Feature | Clip | Serilog | NLog | MEL | ZLogger | Log4Net | ZeroLog |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Structured Fields | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ |
| Typed Fields | ✅ | — | — | — | — | — | ✅ |
| Zero-Alloc API | ✅ | — | — | — | ✅ | — | ✅ |
| Scoped Context | ✅ | ✅ | ✅ | ✅ | ✅ | — | — |
| Console Sink | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| JSON Sink | ✅ | ✅ | ✅ | ✅ | ✅ | — | — |
| Async / Background | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Message Templates | — | ✅ | ✅ | ✅ | ✅ | — | — |
| Source Generator | — | — | — | ✅ | ✅ | — | — |

---

## Filtered

Debug call at Info minimum level — measures the cost of checking the level and returning without doing any work.

```csharp
logger.Debug("This is filtered out");
```

![Filtered](tmp/charts/Filtered.svg)

| Logger | Mean | Allocated |
|--------|-----:|----------:|
| **Clip** | 0.0000 ns | - |
| **ClipZero** | 0.0000 ns | - |
| **ClipMEL** | 5.3697 ns | - |
| MEL | 5.4377 ns | - |
| MELSrcGen | 0.5981 ns | - |
| Serilog | 0.6247 ns | - |
| ZLogger | 2.5023 ns | - |
| NLog | 0.0000 ns | - |
| Log4Net | 3.5296 ns | - |
| ZeroLog | 0.4977 ns | - |

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

![Console_NoFields](tmp/charts/Console_NoFields.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 26.30 ns | 1.00 | - |
| **ClipZero** | 26.40 ns | 1.00 | - |
| **ClipMEL** | 57.91 ns | 2.20 | 64 B |
| MEL | 405.21 ns | 15.41 | 352 B |
| MELSrcGen | 389.97 ns | 14.83 | 368 B |
| Serilog | 299.05 ns | 11.37 | 416 B |
| ZLogger | 349.56 ns | 13.29 | - |
| NLog | 161.47 ns | 6.14 | 304 B |
| Log4Net | 194.18 ns | 7.38 | 392 B |
| ZeroLog | 114.74 ns | 4.36 | - |

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

> **ZLogger:** Enqueues the raw state to a background thread — formatting is fully deferred. The benchmark captures enqueue cost only.

<!-- -->

> **NLog:** Allocates a log-event struct per call. Output is produced by a chain of layout renderers writing strings.

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

![Console_FiveFields](tmp/charts/Console_FiveFields.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 175.75 ns | 1.00 | 72 B |
| **ClipZero** | 138.08 ns | 0.79 | - |
| **ClipMEL** | 418.58 ns | 2.38 | 608 B |
| MEL | 806.98 ns | 4.59 | 808 B |
| MELSrcGen | 898.26 ns | 5.11 | 904 B |
| Serilog | 704.78 ns | 4.01 | 1216 B |
| ZLogger | 623.43 ns | 3.55 | 138 B |
| NLog | 633.25 ns | 3.60 | 1368 B |
| Log4Net | 340.33 ns | 1.94 | 888 B |
| ZeroLog | 282.34 ns | 1.61 | - |

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

> **ZLogger:** Background thread — enqueue cost only. Interpolated-string handlers avoid boxing but add struct construction overhead.

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

![Console_WithContext](tmp/charts/Console_WithContext.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 126.03 ns | 1.00 | 232 B |
| **ClipZero** | 104.10 ns | 0.83 | 176 B |
| **ClipMEL** | 286.71 ns | 2.27 | 576 B |
| MEL | 584.66 ns | 4.64 | 792 B |
| MELSrcGen | 685.46 ns | 5.44 | 808 B |
| Serilog | 684.58 ns | 5.43 | 1344 B |
| ZLogger | 789.16 ns | 6.26 | 200 B |
| NLog | 461.57 ns | 3.66 | 1288 B |

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

![Console_WithException](tmp/charts/Console_WithException.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 2,144.00 ns | 1.00 | 2384 B |
| **ClipZero** | 2,098.69 ns | 0.98 | 2352 B |
| **ClipMEL** | 1,975.69 ns | 0.92 | 2648 B |
| MEL | 3,938.81 ns | 1.84 | 4024 B |
| MELSrcGen | 3,539.77 ns | 1.65 | 4016 B |
| Serilog | 2,710.93 ns | 1.26 | 3864 B |
| ZLogger | 824.57 ns | 0.38 | 1387 B |
| NLog | 3,396.36 ns | 1.58 | 4040 B |
| Log4Net | 2,087.16 ns | 0.97 | 4448 B |
| ZeroLog | 2,032.87 ns | 0.95 | 2736 B |

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

> **ZLogger:** Exception formatting deferred to a background thread. The benchmark only measures enqueue cost.

<!-- -->

> **NLog:** Exception rendered synchronously via the layout. Full stack trace appended as text after the message.

<!-- -->

> **Log4Net:** Exception rendered synchronously via the layout pattern. Full stack trace appended as text.

<!-- -->

> **ZeroLog:** Synchronous mode. Exception attached via builder. Zero heap allocation per call. Benchmarked via concrete sealed class (no interface available).

<!-- -->

> Exception benchmarks are not directly comparable across loggers — ZLogger defers formatting to a background thread while all others format synchronously.

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

![Json_NoFields](tmp/charts/Json_NoFields.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 28.14 ns | 1.00 | - |
| **ClipZero** | 27.87 ns | 0.99 | - |
| MEL | 1,009.09 ns | 35.86 | 784 B |
| MELSrcGen | 1,321.03 ns | 46.95 | 752 B |
| Serilog | 305.46 ns | 10.86 | 608 B |
| ZLogger | 352.61 ns | 12.53 | - |
| NLog | 176.40 ns | 6.27 | 288 B |

> **Clip:** Builds JSON as raw UTF-8 bytes into a pooled buffer. String values are escaped using SIMD.

<!-- -->

> **MEL:** Uses JsonConsoleFormatter. Formats synchronously on the calling thread, then enqueues for background I/O.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing. Same JsonConsoleFormatter pipeline as MEL.

<!-- -->

> **Serilog:** Serializes through its own object model — each value is wrapped in a property object. Output goes through a TextWriter (strings, not raw bytes).

<!-- -->

> **ZLogger:** Background thread — benchmark measures enqueue cost only. Has a real JSON formatter.

<!-- -->

> **NLog:** Each JSON attribute is rendered individually through the layout engine. String-based output.

<!-- -->

> Log4Net and ZeroLog are excluded from JSON benchmarks. Log4Net has no JSON formatter. ZeroLog has no built-in JSON output mode.

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

![Json_FiveFields](tmp/charts/Json_FiveFields.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 184.54 ns | 1.00 | 72 B |
| **ClipZero** | 134.37 ns | 0.73 | - |
| MEL | 2,361.75 ns | 12.81 | 1824 B |
| MELSrcGen | 2,299.33 ns | 12.47 | 2272 B |
| Serilog | 1,062.69 ns | 5.76 | 1408 B |
| ZLogger | 739.94 ns | 4.01 | 572 B |
| NLog | 901.82 ns | 4.89 | 1384 B |

> **Clip:** Ergonomic tier allocates one anonymous object (40 B); fields extracted via expression trees. Zero-alloc tier passes stack-allocated structs directly. Both write typed JSON values with no boxing and no intermediate strings.

<!-- -->

> **MEL:** Uses JsonConsoleFormatter. Value types are boxed. Formatted synchronously, then enqueued for background I/O.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing, no boxing. Same JsonConsoleFormatter pipeline as MEL.

<!-- -->

> **Serilog:** Each argument is wrapped in a property object then serialized. Value types are boxed.

<!-- -->

> **ZLogger:** Background thread — enqueue cost only. Interpolated-string handlers avoid boxing but add struct construction overhead.

<!-- -->

> **NLog:** Event properties are boxed and rendered through the layout engine as strings.

<!-- -->

> Log4Net and ZeroLog are excluded from JSON benchmarks. Log4Net has no JSON formatter. ZeroLog has no built-in JSON output mode.

### JSON: With Context

Message inside a logging scope that adds two context fields, plus one call-site field.

```csharp
using (logger.AddContext(new { RequestId = "abc-123", UserId = 42 }))
{
    logger.Info("Processing", new { Step = "auth" });
}
```

![Json_WithContext](tmp/charts/Json_WithContext.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 169.09 ns | 1.00 | 232 B |
| **ClipZero** | 119.05 ns | 0.71 | 176 B |
| MEL | 1,492.97 ns | 8.84 | 1440 B |
| MELSrcGen | 1,499.23 ns | 8.88 | 1432 B |
| Serilog | 865.88 ns | 5.13 | 1432 B |
| ZLogger | 1,620.16 ns | 9.60 | 284 B |
| NLog | 605.28 ns | 3.59 | 1288 B |

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

<!-- -->

> Log4Net and ZeroLog are excluded from JSON benchmarks. Log4Net has no JSON formatter. ZeroLog has no built-in JSON output mode.

### JSON: With Exception

Message with an attached exception including a full stack trace.

```csharp
logger.Error("Connection failed", ex, new {
    Host = "db.local",
    Port = 5432,
});
```

![Json_WithException](tmp/charts/Json_WithException.svg)

| Logger | Mean | vs Clip | Allocated |
|--------|-----:|--------:|----------:|
| **Clip** | 2,097.42 ns | 1.01 | 2384 B |
| **ClipZero** | 1,945.10 ns | 0.93 | 2352 B |
| MEL | 4,262.38 ns | 2.04 | 4265 B |
| MELSrcGen | 4,745.30 ns | 2.27 | 4273 B |
| Serilog | 2,858.13 ns | 1.37 | 3665 B |
| ZLogger | 544.04 ns | 0.26 | 212 B |
| NLog | 2,944.03 ns | 1.41 | 4336 B |

> **Clip:** Exception serialized as a structured JSON object synchronously into the pooled buffer.

<!-- -->

> **MEL:** Exception formatted synchronously by JsonConsoleFormatter. Only the final I/O write is deferred.

<!-- -->

> **MELSrcGen:** Source-generated — no template parsing or boxing. Exception still formatted synchronously by JsonConsoleFormatter.

<!-- -->

> **Serilog:** Exception serialized as a string property synchronously.

<!-- -->

> **ZLogger:** Exception formatting deferred to a background thread. The benchmark only measures enqueue cost.

<!-- -->

> **NLog:** Exception serialized as a JSON string attribute synchronously.

<!-- -->

> Log4Net and ZeroLog are excluded from JSON benchmarks. Log4Net has no JSON formatter. ZeroLog has no built-in JSON output mode.

<!-- -->

> Exception benchmarks are not directly comparable across loggers — ZLogger defers formatting to a background thread while all others format synchronously.
