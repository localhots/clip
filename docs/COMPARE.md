# Clip — Benchmark Comparison

BenchmarkDotNet v0.15.8, macOS Tahoe 26.3.1 (25D2128) [Darwin 25.3.0]  
Apple M5, 1 CPU, 10 logical and 10 physical cores  
Run: 2026-03-25 01:18

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
| **ClipMEL** | 5.4696 ns | - |
| MEL | 5.4600 ns | - |
| MELSrcGen | 0.6146 ns | - |
| Serilog | 0.6935 ns | - |
| ZLogger | 2.6475 ns | - |
| NLog | 0.0000 ns | - |
| Log4Net | 3.5433 ns | - |
| ZeroLog | 0.7324 ns | - |

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
| **Clip** | 26.04 ns | 1.00 | - |
| **ClipZero** | 26.52 ns | 1.02 | - |
| **ClipMEL** | 59.44 ns | 2.28 | 64 B |
| MEL | 2,294.36 ns | 88.11 | 352 B |
| MELSrcGen | 494.62 ns | 18.99 | 368 B |
| Serilog | 284.00 ns | 10.91 | 416 B |
| ZLogger | 367.74 ns | 14.12 | - |
| NLog | 1,156.53 ns | 44.41 | 304 B |
| Log4Net | 184.09 ns | 7.07 | 392 B |
| ZeroLog | 114.48 ns | 4.40 | - |

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
| **Clip** | 193.72 ns | 1.00 | 72 B |
| **ClipZero** | 140.21 ns | 0.72 | - |
| **ClipMEL** | 2,766.03 ns | 14.28 | 608 B |
| MEL | 868.40 ns | 4.48 | 808 B |
| MELSrcGen | 4,087.45 ns | 21.10 | 904 B |
| Serilog | 729.60 ns | 3.77 | 1216 B |
| ZLogger | 585.98 ns | 3.02 | 158 B |
| NLog | 637.83 ns | 3.29 | 1368 B |
| Log4Net | 333.88 ns | 1.72 | 888 B |
| ZeroLog | 289.96 ns | 1.50 | - |

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
| **Clip** | 133.47 ns | 1.00 | 232 B |
| **ClipZero** | 116.73 ns | 0.87 | 176 B |
| **ClipMEL** | 244.80 ns | 1.83 | 576 B |
| MEL | 724.17 ns | 5.43 | 792 B |
| MELSrcGen | 708.85 ns | 5.31 | 808 B |
| Serilog | 693.98 ns | 5.20 | 1344 B |
| ZLogger | 526.75 ns | 3.95 | 200 B |
| NLog | 459.28 ns | 3.44 | 1288 B |

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
| **Clip** | 1,732.46 ns | 1.00 | 2384 B |
| **ClipZero** | 1,737.73 ns | 1.00 | 2352 B |
| **ClipMEL** | 1,881.48 ns | 1.09 | 2648 B |
| MEL | 3,449.25 ns | 1.99 | 4024 B |
| MELSrcGen | 3,457.03 ns | 2.00 | 4016 B |
| Serilog | 2,187.86 ns | 1.26 | 3864 B |
| ZLogger | 892.56 ns | 0.52 | 1387 B |
| NLog | 2,241.79 ns | 1.29 | 4040 B |
| Log4Net | 2,076.18 ns | 1.20 | 4448 B |
| ZeroLog | 1,990.62 ns | 1.15 | 2736 B |

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
| **Clip** | 27.95 ns | 1.00 | - |
| **ClipZero** | 28.06 ns | 1.00 | - |
| MEL | 1,172.90 ns | 41.96 | 784 B |
| MELSrcGen | 1,183.08 ns | 42.33 | 752 B |
| Serilog | 274.67 ns | 9.83 | 608 B |
| ZLogger | 385.23 ns | 13.78 | - |
| NLog | 151.55 ns | 5.42 | 288 B |

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
| **Clip** | 189.17 ns | 1.00 | 72 B |
| **ClipZero** | 132.34 ns | 0.70 | - |
| MEL | 1,993.93 ns | 10.54 | 1824 B |
| MELSrcGen | 2,010.63 ns | 10.63 | 2272 B |
| Serilog | 1,010.30 ns | 5.34 | 1408 B |
| ZLogger | 359.52 ns | 1.90 | - |
| NLog | 868.95 ns | 4.59 | 1384 B |

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
| **Clip** | 164.02 ns | 1.00 | 232 B |
| **ClipZero** | 159.55 ns | 0.97 | 176 B |
| MEL | 1,579.35 ns | 9.63 | 1440 B |
| MELSrcGen | 2,316.03 ns | 14.12 | 1432 B |
| Serilog | 784.72 ns | 4.78 | 1432 B |
| ZLogger | 1,270.86 ns | 7.75 | 280 B |
| NLog | 534.41 ns | 3.26 | 1288 B |

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
| **Clip** | 2,627.45 ns | 1.00 | 2384 B |
| **ClipZero** | 2,602.91 ns | 0.99 | 2352 B |
| MEL | 5,075.81 ns | 1.93 | 4264 B |
| MELSrcGen | 5,009.14 ns | 1.91 | 4272 B |
| Serilog | 3,606.83 ns | 1.37 | 3664 B |
| ZLogger | 657.39 ns | 0.25 | 1376 B |
| NLog | 3,707.23 ns | 1.41 | 4336 B |

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
