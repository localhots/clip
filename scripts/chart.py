#!/usr/bin/env python3
"""Generate comparison bar charts from current BenchmarkDotNet artifacts.

Reads BDN markdown report files and produces horizontal bar charts
as SVG files — no matplotlib or numpy required.

Usage:
    python3 scripts/chart.py
    python3 scripts/chart.py --dir path/to/artifacts
"""

import argparse
import html
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from metadata import get_excludes, parse_table, strip_prefix, LOGGER_ORDER

ARTIFACTS_DIR = Path("tmp/BenchmarkDotNet.Artifacts/results")
CHARTS_DIR = Path("tmp/charts")

LOGGER_COLORS = {
    "Clip": "#0077b6",
    "ClipZero": "#00b4d8",
    "ClipMEL": "#90e0ef",
    "Serilog": "#e63946",
    "NLog": "#f4a261",
    "MEL": "#7b2d8b",
    "MELSrcGen": "#b185c9",
    "ZLogger": "#2a9d8f",
    "Log4Net": "#8d99ae",
    "ZeroLog": "#e76f51",
}
DEFAULT_COLOR = "#adb5bd"

UNIT_NS = {"ns": 1, "μs": 1_000, "us": 1_000, "ms": 1_000_000, "s": 1_000_000_000}

#
# Layout constants
#

CHART_WIDTH = 600
BAR_HEIGHT = 24
BAR_GAP = 4
BAR_RADIUS = 2
LABEL_WIDTH = 90
LABEL_PAD = 8
VALUE_PAD = 6
RIGHT_MARGIN = 20
INSIDE_LABEL_THRESHOLD = 0.45
FONT_FAMILY = "system-ui, -apple-system, sans-serif"
LABEL_FONT_SIZE = 12
VALUE_FONT_SIZE = 11
BG_COLOR = "white"
LABEL_COLOR = "#374151"
VALUE_COLOR = "#374151"
VALUE_COLOR_INSIDE = "white"


def parse_mean_ns(value: str) -> float | None:
    """Convert a BDN mean string like '27.22 ns' to nanoseconds."""
    m = re.match(r"([0-9.,]+)\s*(ns|μs|us|ms|s)", value.strip())
    if not m:
        return None
    num = float(m.group(1).replace(",", ""))
    return num * UNIT_NS[m.group(2)]


def parse_alloc(value: str) -> str:
    """Return a short allocation label, or '' for zero/missing."""
    s = value.strip().rstrip("B").strip()
    if not s or s == "-":
        return ""
    return value.strip()


def _fmt_label(val: float, alloc: str) -> str:
    """Format a value label like '1,234 ns  (40 B)'."""
    time_label = f"{val:,.0f} ns" if val >= 10 else f"{val:.2f} ns"
    if alloc:
        return f"{time_label}  ({alloc})"
    return time_label


def make_chart(names: list[str], values: list[float], allocs: list[str], path: Path):
    """Generate a horizontal bar chart as SVG."""
    n = len(names)
    if n == 0:
        return

    max_val = max(values)
    bar_area_width = CHART_WIDTH - LABEL_WIDTH - RIGHT_MARGIN
    total_height = n * (BAR_HEIGHT + BAR_GAP) + BAR_GAP

    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{CHART_WIDTH}" height="{total_height}"'
        f' viewBox="0 0 {CHART_WIDTH} {total_height}" font-family="{FONT_FAMILY}">',
        f'<rect width="{CHART_WIDTH}" height="{total_height}" fill="{BG_COLOR}"/>',
    ]

    for i, (name, val, alloc) in enumerate(zip(names, values, allocs)):
        color = LOGGER_COLORS.get(name, DEFAULT_COLOR)
        y = BAR_GAP + i * (BAR_HEIGHT + BAR_GAP)
        bar_width = (val / max_val) * bar_area_width if max_val > 0 else 0
        if bar_width < 3:
            bar_width = 3
        bar_x = LABEL_WIDTH
        label = _fmt_label(val, alloc)
        esc_name = html.escape(name)
        esc_label = html.escape(label)
        cy = y + BAR_HEIGHT / 2

        # Logger name (right-aligned before the bar)
        lines.append(
            f'<text x="{LABEL_WIDTH - LABEL_PAD}" y="{cy}" '
            f'text-anchor="end" dominant-baseline="central" '
            f'font-size="{LABEL_FONT_SIZE}" fill="{LABEL_COLOR}">{esc_name}</text>'
        )

        # Bar
        lines.append(
            f'<rect x="{bar_x}" y="{y}" width="{bar_width:.1f}" height="{BAR_HEIGHT}" '
            f'rx="{BAR_RADIUS}" fill="{color}"/>'
        )

        # Value label — inside the bar (white) if bar is wide enough, else outside (dark)
        if bar_width > bar_area_width * INSIDE_LABEL_THRESHOLD:
            lines.append(
                f'<text x="{bar_x + bar_width - VALUE_PAD}" y="{cy}" '
                f'text-anchor="end" dominant-baseline="central" '
                f'font-size="{VALUE_FONT_SIZE}" fill="{VALUE_COLOR_INSIDE}">{esc_label}</text>'
            )
        else:
            lines.append(
                f'<text x="{bar_x + bar_width + VALUE_PAD}" y="{cy}" '
                f'text-anchor="start" dominant-baseline="central" '
                f'font-size="{VALUE_FONT_SIZE}" fill="{VALUE_COLOR}">{esc_label}</text>'
            )

    lines.append("</svg>")

    path.write_text("\n".join(lines))
    print(f"  {path}")


def process_report(artifacts: Path, bench_name: str):
    """Parse one BDN report and generate charts per category."""
    report = artifacts / f"Clip.Benchmarks.{bench_name}-report-github.md"
    if not report.exists():
        return

    rows = parse_table(report.read_text())
    if not rows:
        return

    # Group rows by category
    by_cat: dict[str, list[tuple[str, float, str]]] = {}
    for r in rows:
        method = r.get("Method", "")
        cat = r.get("Categories", "")
        mean_str = r.get("Mean", "")
        alloc_str = r.get("Allocated", "")
        if not method or not mean_str:
            continue

        # Filtered benchmarks have no Categories column;
        # only treat as "Filtered" when processing that specific report.
        if not cat:
            if bench_name == "FilteredBenchmarks":
                cat = "Filtered"
            else:
                continue

        name = strip_prefix(method, cat)
        mean = parse_mean_ns(mean_str)
        if mean is None:
            continue

        alloc = parse_alloc(alloc_str)
        by_cat.setdefault(cat, []).append((name, mean, alloc))

    for cat, entries in by_cat.items():
        excludes = get_excludes(cat)
        entries = [(n, v, a) for n, v, a in entries if n not in excludes]
        order = {name: i for i, name in enumerate(LOGGER_ORDER)}
        entries.sort(key=lambda e: order.get(e[0], len(LOGGER_ORDER)))
        names = [e[0] for e in entries]
        values = [e[1] for e in entries]
        allocs = [e[2] for e in entries]
        chart_path = CHARTS_DIR / f"{cat}.svg"
        make_chart(names, values, allocs, chart_path)


def main():
    parser = argparse.ArgumentParser(
        description="Generate comparison bar charts from BDN artifacts."
    )
    parser.add_argument(
        "--dir",
        type=Path,
        default=ARTIFACTS_DIR,
        help="Path to BDN artifacts directory",
    )
    args = parser.parse_args()
    artifacts = args.dir

    if not artifacts.exists():
        print(f"Artifacts directory not found: {artifacts}", file=sys.stderr)
        sys.exit(1)

    CHARTS_DIR.mkdir(parents=True, exist_ok=True)

    # Clear old charts
    for old in CHARTS_DIR.glob("*.svg"):
        old.unlink()
    for old in CHARTS_DIR.glob("*.png"):
        old.unlink()

    print("Generating comparison charts...")
    for bench in ("FilteredBenchmarks", "ConsoleBenchmarks", "JsonBenchmarks"):
        process_report(artifacts, bench)

    print("Done.")


if __name__ == "__main__":
    main()
