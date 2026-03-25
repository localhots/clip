"""Shared benchmark metadata — descriptions, caveats, and exclusions.

Keyed by category pattern. Matching rules:
  - Exact match: key == category (e.g., "Console_NoFields")
  - Suffix match: category ends with "_" + key (e.g., "WithException"
    matches "Console_WithException" and "Json_WithException")
  - Prefix match: category starts with key + "_" (e.g., "Json"
    matches "Json_NoFields", "Json_FiveFields", etc.)

DESCRIPTIONS: one-line explanation of what each benchmark tests.
CAVEATS: per-logger fairness notes. Each entry is
  {"logger": "Name", "text": "..."} or {"text": "..."} for general notes.
EXCLUDES: loggers to filter out of specific categories.
"""

import re

# Short explanation of what each benchmark category measures.
# Keyed by section name or subcategory name (same matching rules).

DESCRIPTIONS = {
    "Filtered": {
        "text": (
            "Debug call at Info minimum level — measures the cost"
            " of checking the level and returning without doing"
            " any work."
        ),
        "code": 'logger.Debug("This is filtered out");',
    },
    "Console": {
        "text": (
            "Human-readable text output — the format most developers"
            " stare at during local development. Each logger formats"
            " a line with timestamp, level, message, and structured"
            " fields, then writes to `Stream.Null` so we measure"
            " pure formatting cost, not I/O."
            "\n\n"
            "Clip's console output:"
            "\n\n"
            "```\n"
            "2026-03-19 10:30:45.123 INFO Request handled"
            "                           Method=GET Path=/api/users Status=200\n"
            "2026-03-19 10:30:45.860 ERRO Connection failed"
            "                         Host=db.local Port=5432\n"
            "  System.InvalidOperationException: connection refused\n"
            "```"
            "\n\n"
            "This is where architectural choices really show."
            " Clip formats directly into a pooled UTF-8 byte buffer"
            " — one pass, no intermediate strings, no garbage."
            " Serilog and NLog allocate event objects and render"
            " through layers of abstractions."
            " MEL formats synchronously, then hands a string to a"
            " background thread for the actual write — you pay for"
            " formatting *and* the handoff."
            " ZLogger punts *everything* to a background thread,"
            " so its numbers here only show what it costs to drop"
            " a message on a queue — the real work happens later,"
            " off the clock."
        ),
    },
    "Json": {
        "text": (
            "Structured JSON — the format that actually goes to"
            " production log aggregators. Each logger serializes"
            " a JSON object with timestamp, level, message, and"
            " fields to `Stream.Null` so we measure serialization"
            " cost, not I/O."
            "\n\n"
            "Clip's JSON output:"
            "\n\n"
            "```json\n"
            "{\n"
            '  "ts": "2026-03-19T10:30:45.123Z",\n'
            '  "level": "info",\n'
            '  "msg": "Request handled",\n'
            '  "fields": {\n'
            '    "Method": "GET",\n'
            '    "Status": 200,\n'
            '    "Elapsed": 1.234,\n'
            '    "RequestId": "550e8400-e29b-41d4-a716-446655440000",\n'
            '    "Amount": 49.95\n'
            "  }\n"
            "}\n"
            "```"
            "\n\n"
            "Fields are typed — strings are quoted, numbers are bare,"
            " exceptions become nested `error` objects with `type`,"
            " `msg`, and `stack` fields. No toString() on everything."
            "\n\n"
            "JSON serialization is a harder test than console output."
            " You need proper escaping, correct numeric formatting,"
            " and structured nesting — not just string concatenation."
            " Clip writes JSON as raw UTF-8 bytes using a Utf8JsonWriter-style"
            " approach with SIMD string escaping."
            " Serilog wraps every value in its own property/value object"
            " hierarchy before serializing through a TextWriter."
            " NLog renders each attribute individually through its"
            " layout engine — strings all the way."
            " ZLogger defers serialization entirely to a background thread."
            " And log4net? It doesn't have a JSON formatter at all — it"
            " fakes it with a pattern string shaped like JSON. Structured"
            " fields don't even make it into the output."
        ),
    },
    "NoFields": {
        "text": "Message only, no structured fields attached.",
        "code": 'logger.Info("Request handled");',
    },
    "FiveFields": {
        "text": (
            "Message with five structured fields:"
            " string, int, double, Guid, and decimal."
        ),
        "code": (
            'logger.Info("Request handled", new {\n'
            '    Method = "GET",\n'
            "    Status = 200,\n"
            "    Elapsed = 1.234d,\n"
            '    RequestId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),\n'
            "    Amount = 49.95m,\n"
            "});"
        ),
    },
    "WithContext": {
        "text": (
            "Message inside a logging scope that adds"
            " two context fields, plus one call-site field."
        ),
        "code": (
            "using (logger.AddContext("
            'new { RequestId = "abc-123", UserId = 42 }))\n'
            "{\n"
            '    logger.Info("Processing", new { Step = "auth" });\n'
            "}"
        ),
    },
    "WithException": {
        "text": ("Message with an attached exception" " including a full stack trace."),
        "code": (
            'logger.Error("Connection failed", ex, new {\n'
            '    Host = "db.local",\n'
            "    Port = 5432,\n"
            "});"
        ),
    },
}
CAVEATS = {
    #
    # Filtered
    #
    "Filtered": [
        {
            "text": (
                "All loggers check the level and return immediately."
                " No message is formatted, no output is written."
            ),
        },
        {
            "logger": "Clip",
            "text": ("Single integer comparison, inlined by the JIT."),
        },
        {
            "logger": "Serilog",
            "text": (
                "Enum comparison against a mutable level switch."
                " Fast, but the indirection prevents inlining."
            ),
        },
        {
            "logger": "NLog",
            "text": ("Reads a cached boolean flag. Near-zero overhead."),
        },
        {
            "logger": "MEL",
            "text": (
                "Virtual dispatch through the `ILogger` interface,"
                " then iterates registered providers to check their"
                " levels."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated method checks `ILogger.IsEnabled`"
                " before doing any work — same dispatch cost as MEL."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Built on MEL, so pays the same interface-dispatch"
                " and provider-iteration cost."
            ),
        },
        {
            "logger": "Log4Net",
            "text": (
                "Walks a parent-child logger hierarchy to resolve"
                " the effective level."
            ),
        },
        {
            "logger": "ClipMEL",
            "text": (
                "Clip behind MEL's ILogger interface. The cost here"
                " is entirely MEL's own dispatch — MEL's global"
                " filter rejects the call before it ever reaches"
                " Clip's provider. Matches bare MEL."
            ),
        },
        {
            "logger": "ZeroLog",
            "text": (
                "Checks a cached level flag. Near-zero overhead."
                " Benchmarked via the concrete sealed `ZeroLog.Log`"
                " class — ZeroLog does not expose an interface,"
                " giving it a small dispatch advantage over loggers"
                " benchmarked through interfaces."
            ),
        },
    ],
    #
    # Console_NoFields
    #
    "Console_NoFields": [
        {
            "logger": "Clip",
            "text": (
                "Formats into a pooled byte buffer and writes UTF-8"
                " directly — no intermediate strings. Timestamp is"
                " cached so repeated calls within the same millisecond"
                " skip reformatting."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Allocates a log-event object and parses the message"
                " template per call. Output is rendered as strings via"
                " a TextWriter, not raw bytes."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Allocates a log-event struct per call. Output is"
                " produced by a chain of layout renderers writing"
                " strings."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Formats the message synchronously on the calling"
                " thread via SimpleConsoleFormatter, then enqueues"
                " the formatted string for background I/O. The"
                " benchmark captures the full formatting cost."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated `[LoggerMessage]` method — skips"
                " runtime template parsing. Same MEL pipeline"
                " (SimpleConsoleFormatter + background I/O) but"
                " the generated code is more efficient at the"
                " call site."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Enqueues the raw state to a background thread —"
                " formatting is fully deferred. The benchmark"
                " captures enqueue cost only."
            ),
        },
        {
            "logger": "Log4Net",
            "text": (
                "Synchronous like Clip. Allocates a log-event object"
                " and formats through a pattern layout to strings."
            ),
        },
        {
            "logger": "ClipMEL",
            "text": (
                "Clip's formatting engine behind MEL's ILogger."
                " Measures the cost of MEL's abstraction layer"
                " on top of Clip."
            ),
        },
        {
            "logger": "ZeroLog",
            "text": (
                "Abc-Arbitrage's zero-allocation logger. Running in"
                " synchronous mode so the benchmark measures full"
                " formatting cost, not just enqueue."
                " Benchmarked via the concrete sealed `ZeroLog.Log`"
                " class (no interface available), giving it a small"
                " dispatch advantage."
            ),
        },
    ],
    #
    # Console_FiveFields
    #
    "Console_FiveFields": [
        {
            "logger": "Clip",
            "text": (
                "Ergonomic tier allocates one anonymous object (40 B);"
                " fields extracted via compiled expression trees (cached"
                " per type). Zero-alloc tier passes fields as stack-allocated"
                " structs — no boxing, no heap allocation. Both write typed"
                " values into the same pooled byte buffer."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Each value is wrapped in a property object and"
                " value types are boxed. The template is parsed to"
                " match placeholders to arguments."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Value-type arguments are boxed. Layout renderers"
                " write each property as a string."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Formats synchronously, then enqueues for background"
                " I/O. Value-type arguments are boxed."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated — no template parsing, no boxing."
                " Strongly-typed parameters passed directly."
                " Same MEL formatting pipeline underneath."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Background thread — enqueue cost only."
                " Interpolated-string handlers avoid boxing but add"
                " struct construction overhead."
            ),
        },
        {
            "logger": "Log4Net",
            "text": (
                "Synchronous. Uses printf-style placeholders ({0})"
                " — no named structured fields. Arguments are boxed."
            ),
        },
        {
            "logger": "ClipMEL",
            "text": (
                "Same MEL template API as MEL, but formatting is"
                " handled by Clip's engine underneath."
            ),
        },
        {
            "logger": "ZeroLog",
            "text": (
                "Synchronous mode. Fields attached via builder API"
                " (AppendKeyValue). Zero heap allocation per call."
                " Benchmarked via concrete sealed class (no interface"
                " available)."
            ),
        },
    ],
    #
    # WithContext (applies to all *_WithContext charts)
    #
    "WithContext": [
        {
            "text": (
                "Log4Net and ZeroLog are excluded from context benchmarks"
                " — neither has a scoped-context API comparable to Serilog"
                " LogContext, NLog ScopeContext, or MEL BeginScope."
            ),
        },
    ],
    #
    # Console_WithContext
    #
    "Console_WithContext": [
        {
            "logger": "Clip",
            "text": (
                "Context stored in AsyncLocal<Field[]>. Ergonomic tier"
                " allocates an anonymous object for call-site fields;"
                " zero-alloc tier passes them as stack-allocated structs."
                " Context and call-site fields merged at write time."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "LogContext pushes properties via AsyncLocal."
                " Properties are merged into the event object at"
                " construction time."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "ScopeContext pushes properties via AsyncLocal."
                " Merged at layout render time."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Scope stored on the calling thread, formatted"
                " synchronously by SimpleConsoleFormatter."
                " Only the final I/O write is deferred."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated log call within MEL's BeginScope."
                " No template parsing or boxing for the call-site"
                " field. Same scope + formatting pipeline as MEL."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Scope stored on the calling thread, formatting"
                " deferred to a background thread. The benchmark"
                " only measures the calling thread."
            ),
        },
        {
            "logger": "ClipMEL",
            "text": (
                "Uses MEL's BeginScope, then delegates to Clip's" " formatting engine."
            ),
        },
    ],
    #
    # Console_WithException
    #
    "Console_WithException": [
        {
            "logger": "Clip",
            "text": (
                "Exception rendered synchronously into the same" " pooled byte buffer."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Exception rendered synchronously, appended as a"
                " string to the output."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Exception rendered synchronously via the layout."
                " Full stack trace appended as text after the message."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Exception formatted synchronously on the calling"
                " thread by SimpleConsoleFormatter (including"
                " exception.ToString()). Only the final I/O write"
                " is deferred to a background thread."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated — no template parsing or boxing."
                " Exception still formatted synchronously by"
                " SimpleConsoleFormatter."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Exception formatting deferred to a background"
                " thread. The benchmark only measures enqueue cost."
            ),
        },
        {
            "logger": "Log4Net",
            "text": (
                "Exception rendered synchronously via the layout"
                " pattern. Full stack trace appended as text."
            ),
        },
        {
            "logger": "ClipMEL",
            "text": (
                "Exception formatted by Clip's engine behind MEL's"
                " ILogger interface."
            ),
        },
        {
            "logger": "ZeroLog",
            "text": (
                "Synchronous mode. Exception attached via builder."
                " Zero heap allocation per call."
                " Benchmarked via concrete sealed class (no interface"
                " available)."
            ),
        },
        {
            "text": (
                "Exception benchmarks are not directly comparable"
                " across loggers — ZLogger defers formatting to a"
                " background thread while all others format"
                " synchronously."
            ),
        },
    ],
    #
    # JSON-specific (applies to all Json_* charts)
    #
    "Json": [
        {
            "text": (
                "Log4Net and ZeroLog are excluded from JSON"
                " benchmarks. Log4Net has no JSON formatter."
                " ZeroLog has no built-in JSON output mode."
            ),
        },
    ],
    #
    # Json_NoFields
    #
    "Json_NoFields": [
        {
            "logger": "Clip",
            "text": (
                "Builds JSON as raw UTF-8 bytes into a pooled buffer."
                " String values are escaped using SIMD."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Serializes through its own object model — each value"
                " is wrapped in a property object. Output goes through"
                " a TextWriter (strings, not raw bytes)."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Each JSON attribute is rendered individually through"
                " the layout engine. String-based output."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Uses JsonConsoleFormatter. Formats synchronously"
                " on the calling thread, then enqueues for"
                " background I/O."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated — no template parsing."
                " Same JsonConsoleFormatter pipeline as MEL."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Background thread — benchmark measures enqueue cost"
                " only. Has a real JSON formatter."
            ),
        },
    ],
    #
    # Json_FiveFields
    #
    "Json_FiveFields": [
        {
            "logger": "Clip",
            "text": (
                "Ergonomic tier allocates one anonymous object (40 B);"
                " fields extracted via expression trees. Zero-alloc tier"
                " passes stack-allocated structs directly. Both write typed"
                " JSON values with no boxing and no intermediate strings."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Each argument is wrapped in a property object then"
                " serialized. Value types are boxed."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Event properties are boxed and rendered through the"
                " layout engine as strings."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Uses JsonConsoleFormatter. Value types are boxed."
                " Formatted synchronously, then enqueued for"
                " background I/O."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated — no template parsing, no boxing."
                " Same JsonConsoleFormatter pipeline as MEL."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Background thread — enqueue cost only."
                " Interpolated-string handlers avoid boxing but add"
                " struct construction overhead."
            ),
        },
    ],
    #
    # Json_WithContext
    #
    "Json_WithContext": [
        {
            "logger": "Clip",
            "text": (
                "Ergonomic tier allocates an anonymous object for"
                " call-site fields; zero-alloc tier uses stack-allocated"
                " structs. Context and call-site fields merged at write"
                " time into the same pooled buffer."
            ),
        },
        {
            "logger": "Serilog",
            "text": (
                "Context properties merged into the event object"
                " and serialized through the object model."
            ),
        },
        {
            "logger": "NLog",
            "text": (
                "Scope properties merged and rendered through the" " layout engine."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Scope stored on the calling thread, formatted"
                " synchronously by JsonConsoleFormatter."
                " Only the final I/O write is deferred."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated log call within MEL's BeginScope."
                " Same JsonConsoleFormatter pipeline as MEL."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Scope stored on the calling thread, rendered on a"
                " background thread. The benchmark only measures the"
                " calling thread."
            ),
        },
    ],
    #
    # Json_WithException
    #
    "Json_WithException": [
        {
            "logger": "Clip",
            "text": (
                "Exception serialized as a structured JSON object"
                " synchronously into the pooled buffer."
            ),
        },
        {
            "logger": "Serilog",
            "text": ("Exception serialized as a string property" " synchronously."),
        },
        {
            "logger": "NLog",
            "text": (
                "Exception serialized as a JSON string attribute" " synchronously."
            ),
        },
        {
            "logger": "MEL",
            "text": (
                "Exception formatted synchronously by"
                " JsonConsoleFormatter. Only the final I/O"
                " write is deferred."
            ),
        },
        {
            "logger": "MELSrcGen",
            "text": (
                "Source-generated — no template parsing or boxing."
                " Exception still formatted synchronously by"
                " JsonConsoleFormatter."
            ),
        },
        {
            "logger": "ZLogger",
            "text": (
                "Exception formatting deferred to a background"
                " thread. The benchmark only measures enqueue cost."
            ),
        },
        {
            "text": (
                "Exception benchmarks are not directly comparable"
                " across loggers — ZLogger defers formatting to a"
                " background thread while all others format"
                " synchronously."
            ),
        },
    ],
}

# Loggers to exclude from specific categories.
# Keyed by category pattern (same matching rules as CAVEATS).
EXCLUDES = {
    "Json": ["Log4Net", "ZeroLog"],
    "WithContext": ["Log4Net", "ZeroLog"],
}

#
# Feature matrix
#

# Each feature maps to a short label shown in the column header.
# Each logger maps to a dict of feature -> value.
# Values: True / False / str (short note shown in the cell).
FEATURE_TABLES = [
    (
        "API & Data Model",
        [
            ("structured", "Structured Fields"),
            ("typed_fields", "Typed Fields"),
            ("zero_alloc", "Zero-Alloc API"),
            ("msg_templates", "Message Templates"),
            ("src_gen", "Source Generator"),
        ],
    ),
    (
        "Pipeline",
        [
            ("enrichers", "Enrichers"),
            ("level_gated_enrichers", "Level-Gated Enrichers"),
            ("filters", "Filters"),
            ("redactors", "Redactors"),
            ("scoped_ctx", "Scoped Context"),
        ],
    ),
    (
        "Output",
        [
            ("console", "Console Sink"),
            ("json", "JSON Sink"),
            ("file", "File Sink"),
            ("otlp", "OpenTelemetry / OTLP"),
        ],
    ),
    (
        "Architecture",
        [
            ("sync_default", "Sync-by-Default"),
            ("async", "Async / Background"),
            ("buffer_pooling", "Buffer Pooling"),
            ("zero_deps", "Zero Dependencies"),
            ("mel_adapter", "MEL Adapter"),
        ],
    ),
]

FEATURES = {
    "Clip": {
        "structured": True,
        "typed_fields": True,
        "zero_alloc": True,
        "msg_templates": False,
        "src_gen": False,
        "enrichers": True,
        "level_gated_enrichers": True,
        "filters": True,
        "redactors": True,
        "scoped_ctx": True,
        "console": True,
        "json": True,
        "file": True,
        "otlp": True,
        "sync_default": True,
        "async": True,
        "buffer_pooling": True,
        "zero_deps": True,
        "mel_adapter": True,
    },
    "Serilog": {
        "structured": True,
        "typed_fields": False,
        "zero_alloc": False,
        "msg_templates": True,
        "src_gen": False,
        "enrichers": True,
        "level_gated_enrichers": False,
        "filters": True,
        "redactors": False,
        "scoped_ctx": True,
        "console": True,
        "json": True,
        "file": True,
        "otlp": True,
        "sync_default": False,
        "async": True,
        "buffer_pooling": False,
        "zero_deps": False,
        "mel_adapter": True,
    },
    "NLog": {
        "structured": True,
        "typed_fields": False,
        "zero_alloc": False,
        "msg_templates": True,
        "src_gen": False,
        "enrichers": True,
        "level_gated_enrichers": False,
        "filters": True,
        "redactors": False,
        "scoped_ctx": True,
        "console": True,
        "json": True,
        "file": True,
        "otlp": True,
        "sync_default": False,
        "async": True,
        "buffer_pooling": False,
        "zero_deps": False,
        "mel_adapter": True,
    },
    "MEL": {
        "structured": True,
        "typed_fields": False,
        "zero_alloc": False,
        "msg_templates": True,
        "src_gen": True,
        "enrichers": False,
        "level_gated_enrichers": False,
        "filters": True,
        "redactors": False,
        "scoped_ctx": True,
        "console": True,
        "json": True,
        "file": False,
        "otlp": True,
        "sync_default": False,
        "async": True,
        "buffer_pooling": False,
        "zero_deps": False,
        "mel_adapter": False,
    },
    "ZLogger": {
        "structured": True,
        "typed_fields": False,
        "zero_alloc": True,
        "msg_templates": True,
        "src_gen": True,
        "enrichers": False,
        "level_gated_enrichers": False,
        "filters": True,
        "redactors": False,
        "scoped_ctx": True,
        "console": True,
        "json": True,
        "file": True,
        "otlp": False,
        "sync_default": False,
        "async": True,
        "buffer_pooling": True,
        "zero_deps": False,
        "mel_adapter": False,
    },
    "Log4Net": {
        "structured": False,
        "typed_fields": False,
        "zero_alloc": False,
        "msg_templates": False,
        "src_gen": False,
        "enrichers": True,
        "level_gated_enrichers": False,
        "filters": True,
        "redactors": False,
        "scoped_ctx": False,
        "console": True,
        "json": False,
        "file": True,
        "otlp": False,
        "sync_default": True,
        "async": True,
        "buffer_pooling": False,
        "zero_deps": False,
        "mel_adapter": True,
    },
    "ZeroLog": {
        "structured": True,
        "typed_fields": True,
        "zero_alloc": True,
        "msg_templates": False,
        "src_gen": False,
        "enrichers": False,
        "level_gated_enrichers": False,
        "filters": False,
        "redactors": False,
        "scoped_ctx": False,
        "console": True,
        "json": False,
        "file": False,
        "otlp": False,
        "sync_default": True,
        "async": True,
        "buffer_pooling": True,
        "zero_deps": False,
        "mel_adapter": False,
    },
}

_FEATURE_LOGGER_ORDER = [
    "Clip",
    "Serilog",
    "NLog",
    "MEL",
    "ZLogger",
    "Log4Net",
    "ZeroLog",
]


def render_feature_matrix():
    """Render the feature matrix as grouped markdown tables.

    Returns a list of strings (one per line).
    """
    loggers = [l for l in _FEATURE_LOGGER_ORDER if l in FEATURES]

    lines = ["", "## Feature Matrix", ""]

    for group_title, labels in FEATURE_TABLES:
        header = "| " + group_title + " | " + " | ".join(loggers) + " |"
        sep = "|---------|" + "|".join(":---:" for _ in loggers) + "|"
        lines += [header, sep]

        for key, label in labels:
            cells = []
            for logger in loggers:
                val = FEATURES.get(logger, {}).get(key, False)
                if val is True:
                    cells.append("\u2705")
                elif val is False:
                    cells.append("\u2014")
                else:
                    cells.append(str(val))
            lines.append(f"| {label} | " + " | ".join(cells) + " |")

        lines.append("")

    return lines


LOGGER_ORDER = [
    "Clip",
    "ClipZero",
    "ClipMEL",
    "MEL",
    "MELSrcGen",
    "Serilog",
    "ZLogger",
    "NLog",
    "Log4Net",
    "ZeroLog",
]


UNIT_NS = {"ns": 1, "μs": 1_000, "us": 1_000, "ms": 1_000_000, "s": 1_000_000_000}


def parse_mean_ns(value: str) -> float | None:
    """Convert a BDN mean string like '27.22 ns' to nanoseconds."""
    m = re.match(r"([0-9.,]+)\s*(ns|μs|us|ms|s)", value.strip())
    if not m:
        return None
    num = float(m.group(1).replace(",", ""))
    return num * UNIT_NS[m.group(2)]


def parse_table(text: str) -> list[dict[str, str]]:
    """Parse a BDN markdown table into a list of row dicts."""
    lines = [l for l in text.splitlines() if l.startswith("|")]
    if len(lines) < 3:
        return []

    headers = [h.strip() for h in lines[0].split("|")[1:-1]]
    rows = []
    for line in lines[2:]:  # skip header + separator
        cells = [c.strip() for c in line.split("|")[1:-1]]
        if not any(cells):
            continue
        rows.append(dict(zip(headers, cells)))
    return rows


def strip_prefix(method: str, category: str) -> str:
    """Strip the category-derived prefix from a method name.

    BDN method names follow the pattern <Suffix>_<Logger>, where <Suffix>
    matches the end of the category name. E.g. in category 'Json_WithContext',
    method 'WithContext_Clip' -> 'Clip'.
    """
    cat_suffix = category.rsplit("_", 1)[-1] if "_" in category else category
    prefix = f"{cat_suffix}_"
    if method.startswith(prefix):
        return method[len(prefix) :]
    # Also try the full category as prefix (for single-word categories)
    if method.startswith(f"{category}_"):
        return method[len(category) + 1 :]
    return method


def _matches(key, category):
    """Check if a caveat key matches a category name."""
    if key == category:
        return True
    if category.endswith("_" + key):
        return True
    # Prefix match for sink-wide notes (e.g., "Json" matches "Json_*")
    if category.startswith(key + "_"):
        return True
    return False


def get_description(key):
    """Look up a description text by exact key."""
    entry = DESCRIPTIONS.get(key)
    return entry["text"] if entry else None


def get_code_example(key):
    """Look up a Clip code example by exact key."""
    entry = DESCRIPTIONS.get(key)
    return entry.get("code") if entry else None


def get_excludes(category):
    """Return set of logger names to exclude for a category."""
    result = set()
    for key, names in EXCLUDES.items():
        if _matches(key, category):
            result.update(names)
    return result


def get_caveats(category):
    """Collect all caveat entries that match a category."""
    result = []
    for key, entries in CAVEATS.items():
        if _matches(key, category):
            result.extend(entries)
    return result


_CAVEAT_LOGGER_ORDER = [
    "Clip",
    "ClipZero",
    "ClipMEL",
    "MEL",
    "MELSrcGen",
    "Serilog",
    "ZLogger",
    "NLog",
    "Log4Net",
    "ZeroLog",
]
_CAVEAT_RANK = {name: i for i, name in enumerate(_CAVEAT_LOGGER_ORDER)}


def render_caveats(category):
    """Render matching caveats as separate markdown blockquotes.

    Returns a list of strings (one per line), or empty list if no
    caveats match. Each entry becomes its own blockquote block,
    separated by a blank line for visual distinction.
    Automatically filters out caveats for excluded loggers.
    Logger-specific caveats are sorted by LOGGER_ORDER; general
    (no-logger) caveats appear at the end.
    """
    excludes = get_excludes(category)
    entries = [e for e in get_caveats(category) if e.get("logger") not in excludes]
    if not entries:
        return []

    # Sort: logger-specific entries by LOGGER_ORDER, general entries last
    entries.sort(
        key=lambda e: (
            _CAVEAT_RANK.get(e.get("logger", ""), len(_CAVEAT_LOGGER_ORDER)),
            0 if e.get("logger") else 1,
        )
    )

    lines = []
    for i, entry in enumerate(entries):
        logger = entry.get("logger")
        text = entry["text"]
        if i > 0:
            # HTML comment forces markdown to create separate <blockquote>s
            lines += ["", "<!-- -->"]
        lines.append("")
        if logger:
            lines.append(f"> **{logger}:** {text}")
        else:
            lines.append(f"> {text}")

    return lines
