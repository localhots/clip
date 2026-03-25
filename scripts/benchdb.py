#!/usr/bin/env python3
"""Benchmark database — import BDN results, show contents.

Usage:
    python3 scripts/benchdb.py import              # import from default artifacts dir
    python3 scripts/benchdb.py import --dir path/  # import from custom dir
    python3 scripts/benchdb.py show                # print DB summary
"""

import argparse
import csv
import json
import os
import subprocess
import sys
from datetime import datetime
from pathlib import Path

DB_PATH = Path("tmp/benchdb.json")
ARTIFACTS_DIR = Path("tmp/BenchmarkDotNet.Artifacts/results")

BENCH_CLASSES = ("FilteredBenchmarks", "ConsoleBenchmarks", "JsonBenchmarks")

# Columns to extract from BDN CSV per benchmark class.
FIELDS = {
    "Method",
    "Categories",
    "Mean",
    "Error",
    "StdDev",
    "Median",
    "Ratio",
    "Gen0",
    "Gen1",
    "Gen2",
    "Allocated",
}


def _git_sha() -> str:
    """Return short git SHA, or 'unknown'."""
    try:
        return subprocess.check_output(
            ["git", "rev-parse", "--short=7", "HEAD"],
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return "unknown"


def _parse_env_header(md_path: Path) -> str:
    """Extract the raw environment header from a BDN markdown report."""
    text = md_path.read_text()
    blocks = text.split("```")
    if len(blocks) < 2:
        return ""
    return blocks[1].strip()


def _parse_csv(csv_path: Path) -> list[dict[str, str]]:
    """Parse a BDN CSV file, extracting only the columns we care about."""
    rows = []
    with open(csv_path, newline="") as f:
        reader = csv.DictReader(f)
        for raw in reader:
            row = {}
            for key in FIELDS:
                val = raw.get(key, "").strip()
                if val:
                    row[key] = val
            if "Method" in row:
                rows.append(row)
    return rows


def _is_baseline(method: str, ratio: str) -> bool:
    """Detect if this method is the baseline.

    BDN sets Ratio=1.00 for baselines. For Filtered benchmarks where
    the baseline is 0 ns, BDN sets Ratio=? for all — fall back to
    checking if the method ends with _Clip (the [Benchmark(Baseline=true)]
    method in all benchmark classes).
    """
    if ratio in ("1.00", "1"):
        return True
    if ratio == "?" and method.endswith("_Clip"):
        return True
    return False


def import_cmd(
    artifacts_dir: Path, db_path: Path, logger_filter: list[str] | None = None
):
    """Import BDN CSV results into the database.

    If logger_filter is set, only import methods whose logger suffix
    (the part after the last '_') matches one of the given names.
    E.g., ['Clip', 'ClipZero'] imports only *_Clip and *_ClipZero methods.
    """
    if not artifacts_dir.exists():
        print(f"Artifacts directory not found: {artifacts_dir}", file=sys.stderr)
        sys.exit(1)

    # Load existing DB
    db: dict = {"environment": {}, "results": {}}
    if db_path.exists():
        with open(db_path) as f:
            db = json.load(f)

    sha = _git_sha()
    ts = datetime.now().isoformat(timespec="seconds")
    imported = 0

    for class_name in BENCH_CLASSES:
        csv_file = artifacts_dir / f"Clip.Benchmarks.{class_name}-report.csv"
        if not csv_file.exists():
            continue

        md_file = artifacts_dir / f"Clip.Benchmarks.{class_name}-report-github.md"
        if md_file.exists():
            header = _parse_env_header(md_file)
            if header:
                db["environment"]["raw_header"] = header

        rows = _parse_csv(csv_file)
        if not rows:
            continue

        class_data = db.setdefault("results", {}).setdefault(class_name, {})
        class_imported = 0

        for row in rows:
            method = row.pop("Method")

            # Apply logger filter if set
            if logger_filter:
                logger = method.rsplit("_", 1)[-1] if "_" in method else method
                if logger not in logger_filter:
                    continue

            ratio = row.pop("Ratio", "")
            baseline = _is_baseline(method, ratio)

            entry: dict = {
                "baseline": baseline,
                "timestamp": ts,
                "git_sha": sha,
            }

            # Map CSV fields to DB fields
            if "Categories" in row:
                entry["categories"] = row["Categories"]
            if "Mean" in row:
                entry["mean"] = row["Mean"]
            if "Error" in row:
                entry["error"] = row["Error"]
            if "StdDev" in row:
                entry["stddev"] = row["StdDev"]
            if "Median" in row:
                entry["median"] = row["Median"]
            if "Gen0" in row:
                entry["gen0"] = row["Gen0"]
            if "Gen1" in row:
                entry["gen1"] = row["Gen1"]
            if "Gen2" in row:
                entry["gen2"] = row["Gen2"]
            if "Allocated" in row:
                entry["allocated"] = row["Allocated"]

            class_data[method] = entry
            imported += 1
            class_imported += 1

        print(f"  {class_name}: {class_imported} methods")

    # Write atomically
    db_path.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = db_path.with_suffix(".tmp")
    with open(tmp_path, "w") as f:
        json.dump(db, f, indent=2)
        f.write("\n")
    os.replace(tmp_path, db_path)

    print(f"Imported {imported} results into {db_path}")


def show_cmd(db_path: Path):
    """Print a summary of the database contents."""
    if not db_path.exists():
        print(f"No database found at {db_path}")
        return

    with open(db_path) as f:
        db = json.load(f)

    env = db.get("environment", {}).get("raw_header", "")
    if env:
        first_line = env.splitlines()[0] if env else ""
        print(f"Environment: {first_line}")
    print()

    results = db.get("results", {})
    for class_name in BENCH_CLASSES:
        class_data = results.get(class_name, {})
        if not class_data:
            continue

        # Group by logger (suffix after last _)
        loggers: dict[str, list[str]] = {}
        timestamps: set[str] = set()
        for method, data in class_data.items():
            logger = method.rsplit("_", 1)[-1] if "_" in method else method
            loggers.setdefault(logger, []).append(method)
            timestamps.add(data.get("timestamp", "?"))

        ts_range = sorted(timestamps)
        ts_display = (
            ts_range[0] if len(ts_range) == 1 else f"{ts_range[0]} .. {ts_range[-1]}"
        )

        print(f"{class_name}: {len(class_data)} methods")
        print(f"  Loggers: {', '.join(sorted(loggers.keys()))}")
        print(f"  Updated: {ts_display}")
        print()


def main():
    parser = argparse.ArgumentParser(description="Benchmark database manager.")
    sub = parser.add_subparsers(dest="command")

    imp = sub.add_parser("import", help="Import BDN CSV results into the database")
    imp.add_argument(
        "--dir",
        type=Path,
        default=ARTIFACTS_DIR,
        help="Path to BDN artifacts directory",
    )
    imp.add_argument(
        "--db",
        type=Path,
        default=DB_PATH,
        help="Path to benchdb.json",
    )
    imp.add_argument(
        "--filter",
        nargs="+",
        metavar="LOGGER",
        help="Only import these loggers (e.g., --filter Clip ClipZero Serilog)",
    )

    show = sub.add_parser("show", help="Show database summary")
    show.add_argument(
        "--db",
        type=Path,
        default=DB_PATH,
        help="Path to benchdb.json",
    )

    args = parser.parse_args()

    if args.command == "import":
        import_cmd(args.dir, args.db, logger_filter=args.filter)
    elif args.command == "show":
        show_cmd(args.db)
    else:
        parser.print_help()
        sys.exit(1)


if __name__ == "__main__":
    main()
