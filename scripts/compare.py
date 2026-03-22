#!/usr/bin/env python3
"""Generate docs/COMPARE.md from BenchmarkDotNet artifact files.

Usage:
    python3 scripts/compare.py              # writes docs/COMPARE.md
    python3 scripts/compare.py --stdout      # prints to stdout
    python3 scripts/compare.py --dir path/   # custom artifacts dir
"""

import argparse
import re
import sys
from datetime import datetime
from pathlib import Path

# Allow importing from scripts/
sys.path.insert(0, str(Path(__file__).resolve().parent))
from metadata import (
    render_caveats,
    get_excludes,
    get_description,
    get_code_example,
    render_feature_matrix,
    parse_table,
    strip_prefix,
    LOGGER_ORDER,
)

ARTIFACTS_DIR = Path("tmp/BenchmarkDotNet.Artifacts/results")
OUTPUT_FILE = Path("docs/COMPARE.md")

CATEGORY_TITLES = {
    "NoFields": "No Fields",
    "FiveFields": "Five Fields",
    "WithException": "With Exception",
    "WithContext": "With Context",
}

CATEGORY_ORDER = ["NoFields", "FiveFields", "WithContext", "WithException"]

CLIP_NAMES = {"Clip", "ClipZero", "ClipMEL"}

_LOGGER_RANK = {name: i for i, name in enumerate(LOGGER_ORDER)}


def parse_env(text: str) -> list[str]:
    """Extract environment lines from between ``` fences."""
    blocks = text.split("```")
    if len(blocks) < 2:
        return []
    lines = [
        l.strip()
        for l in blocks[1].strip().splitlines()
        if l.strip() and l.strip() != "-" and not l.strip().startswith("[")
    ]
    return lines[:2]


def bold_clip(name: str) -> str:
    return f"**{name}**" if name in CLIP_NAMES else name


def fmt_alloc(alloc: str) -> str:
    return "-" if not alloc or alloc == "-" else alloc


def category_title(category: str) -> str:
    """Convert a BDN category like 'Json_WithContext' into 'With Context'."""
    suffix = category.rsplit("_", 1)[-1] if "_" in category else category
    return CATEGORY_TITLES.get(suffix, suffix)


def emit_filtered(rows: list[dict[str, str]]) -> list[str]:
    code = get_code_example("Filtered")
    out = [
        "",
        "## Filtered",
        "",
        get_description("Filtered"),
        "",
    ]
    if code:
        out += ["```csharp", code, "```", ""]
    out += _chart_ref("Filtered")
    out += [
        "| Logger | Mean | Allocated |",
        "|--------|-----:|----------:|",
    ]
    pending = []
    for r in rows:
        method = r.get("Method", "")
        if not method:
            continue
        name = strip_prefix(method, "Filtered")
        display = bold_clip(name)
        mean = r.get("Mean", "")
        alloc = fmt_alloc(r.get("Allocated", ""))
        pending.append((name, f"| {display} | {mean} | {alloc} |"))
    pending.sort(key=lambda e: _LOGGER_RANK.get(e[0], len(LOGGER_ORDER)))
    out.extend(row for _, row in pending)
    out += render_caveats("Filtered")
    return out


CHARTS_DIR = Path("tmp/charts")


def _chart_ref(cat: str) -> list[str]:
    """Return markdown image reference if chart exists."""
    chart = CHARTS_DIR / f"{cat}.svg"
    if chart.exists():
        return [f"![{cat}]({chart})", ""]
    return []


def _cat_sort_key(row: dict[str, str]) -> int:
    """Sort key for category ordering within a section."""
    cat = row.get("Categories", "")
    suffix = cat.rsplit("_", 1)[-1] if "_" in cat else cat
    try:
        return CATEGORY_ORDER.index(suffix)
    except ValueError:
        return len(CATEGORY_ORDER)


def emit_comparison(rows: list[dict[str, str]], section: str, desc: str) -> list[str]:
    rows = sorted(rows, key=_cat_sort_key)
    out = ["", f"## {section}", "", desc]

    current_cat = None
    excludes = set()
    pending: list[tuple[str, str]] = []  # (raw_name, formatted_row)

    def flush_cat():
        """Emit chart, then table, then caveats for the current category."""
        nonlocal pending
        if current_cat is None:
            return
        out.extend(_chart_ref(current_cat))
        out.extend(table_header)
        pending.sort(key=lambda e: _LOGGER_RANK.get(e[0], len(LOGGER_ORDER)))
        out.extend(row for _, row in pending)
        out.extend(render_caveats(current_cat))
        pending = []

    table_header: list[str] = []

    for r in rows:
        method = r.get("Method", "")
        cat = r.get("Categories", "")
        if not method:
            continue

        if cat != current_cat:
            flush_cat()
            current_cat = cat
            pending = []
            excludes = get_excludes(cat)
            title = f"{section}: {category_title(cat)}"
            sub_desc = get_description(cat.rsplit("_", 1)[-1] if "_" in cat else cat)
            sub_key = cat.rsplit("_", 1)[-1] if "_" in cat else cat
            sub_code = get_code_example(sub_key)
            out += ["", f"### {title}", ""]
            if sub_desc:
                out += [sub_desc, ""]
            if sub_code:
                out += ["```csharp", sub_code, "```", ""]
            table_header = [
                "| Logger | Mean | vs Clip | Allocated |",
                "|--------|-----:|--------:|----------:|",
            ]

        name = strip_prefix(method, cat)
        if name in excludes:
            continue
        display = bold_clip(name)
        mean = r.get("Mean", "")
        ratio = r.get("Ratio", "")
        alloc = fmt_alloc(r.get("Allocated", ""))
        pending.append((name, f"| {display} | {mean} | {ratio} | {alloc} |"))

    flush_cat()
    return out


def find_report(artifacts: Path, name: str) -> Path | None:
    p = artifacts / f"Clip.Benchmarks.{name}-report-github.md"
    return p if p.exists() else None


def generate(artifacts: Path) -> str:
    lines: list[str] = ["# Clip — Benchmark Comparison", ""]

    # Environment header from first available report
    for name in ("FilteredBenchmarks", "ConsoleBenchmarks", "JsonBenchmarks"):
        report = find_report(artifacts, name)
        if report:
            env = parse_env(report.read_text())
            env.append(f"Run: {datetime.now():%Y-%m-%d %H:%M}")
            lines.append("  \n".join(env))
            break

    lines += [
        "",
        "Clip is a zero-dependency structured logging library for .NET 9."
        " It formats directly into pooled UTF-8 byte buffers — no intermediate"
        " strings, no allocations on the hot path, no background-thread tricks"
        " to hide latency.",
        "",
        "Clip ships two APIs that produce identical output:"
        " **Clip** (ergonomic — pass an anonymous object, fields extracted"
        " via compiled expression trees) and **ClipZero** (zero-alloc —"
        " pass `Field` structs on the stack, nothing touches the heap).",
        "",
        "```csharp",
        "// Ergonomic — one anonymous-object allocation, fields cached per type",
        'logger.Info("Request handled",',
        "    new { Method, Status, Elapsed, RequestId, Amount });",
        "",
        "// Zero-alloc — stack-allocated structs, zero heap allocations",
        'logger.Info("Request handled",',
        '    new Field("Method", Method),',
        '    new Field("Status", Status),',
        '    new Field("Elapsed", Elapsed),',
        '    new Field("RequestId", ReqId),',
        '    new Field("Amount", Amount));',
        "```",
        "",
        "This report puts Clip head-to-head against six established .NET loggers,"
        " all writing to `Stream.Null` so we measure pure formatting cost:",
        "",
        "- **Serilog** — rich sink ecosystem and message templates."
        " Allocates a `LogEvent` and boxes value types per call.",
        "- **NLog** — layout renderers give surgical control over output."
        " String-based rendering with per-call allocations.",
        "- **MEL** (Microsoft.Extensions.Logging) — ships with ASP.NET Core."
        " Virtual dispatch, provider iteration, background I/O thread.",
        "- **MELSrcGen** — MEL with `[LoggerMessage]` source generation."
        " Eliminates runtime template parsing and value-type boxing."
        " Same MEL pipeline underneath — this is how Microsoft recommends"
        " using MEL in hot paths.",
        "- **ZLogger** — Cysharp's high-performance logger built on MEL."
        " Defers *all* formatting to a background thread — benchmarks"
        " only reflect enqueue cost.",
        "- **log4net** — the port of Java's Log4j."
        " No structured fields, pattern layouts all the way down.",
        "- **ClipMEL** — Clip behind MEL's `ILogger` via"
        " `Clip.Extensions.Logging`. Shows MEL abstraction cost.",
        "- **ZeroLog** — Abc-Arbitrage's zero-allocation logger."
        " Builder API, synchronous mode — measures full formatting cost.",
    ]

    lines += render_feature_matrix()

    lines += [
        "",
        "---",
    ]

    # Filtered
    report = find_report(artifacts, "FilteredBenchmarks")
    if report:
        lines += emit_filtered(parse_table(report.read_text()))

    lines += ["", "---"]

    # Console
    report = find_report(artifacts, "ConsoleBenchmarks")
    if report:
        lines += emit_comparison(
            parse_table(report.read_text()),
            "Console",
            get_description("Console"),
        )

    lines += ["", "---"]

    # JSON
    report = find_report(artifacts, "JsonBenchmarks")
    if report:
        lines += emit_comparison(
            parse_table(report.read_text()),
            "JSON",
            get_description("Json"),
        )

    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(
        description="Generate docs/COMPARE.md from BDN artifacts."
    )
    parser.add_argument(
        "--dir",
        type=Path,
        default=ARTIFACTS_DIR,
        help="Path to BDN artifacts directory",
    )
    parser.add_argument(
        "--stdout", action="store_true", help="Print to stdout instead of file"
    )
    args = parser.parse_args()
    artifacts = args.dir
    to_stdout = args.stdout

    if not artifacts.exists():
        print(f"Artifacts directory not found: {artifacts}", file=sys.stderr)
        sys.exit(1)

    result = generate(artifacts)

    if to_stdout:
        print(result, end="")
    else:
        OUTPUT_FILE.write_text(result)
        print(f"Written {OUTPUT_FILE}")


if __name__ == "__main__":
    main()
