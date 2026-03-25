"""Read the benchmark database and return rows in parse_table() format.

This module is the shared reader for benchdb.json. Both chart.py and
compare.py import from here instead of reading BDN artifact files.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from metadata import parse_mean_ns

DEFAULT_DB_PATH = Path("tmp/benchdb.json")


def load_db(path: Path = DEFAULT_DB_PATH) -> dict:
  """Load and return the benchmark database."""
  if not path.exists():
    print(f"Benchmark database not found: {path}", file=sys.stderr)
    print("Run 'make bench' or 'scripts/benchdb.py import' first.", file=sys.stderr)
    sys.exit(1)
  with open(path) as f:
    return json.load(f)


def get_environment(db: dict) -> list[str]:
  """Return environment lines for the COMPARE.md header."""
  raw = db.get("environment", {}).get("raw_header", "")
  if not raw:
    return []
  lines = [
    l.strip()
    for l in raw.splitlines()
    if l.strip() and l.strip() != "-" and not l.strip().startswith("[")
  ]
  return lines[:2]


def _compute_ratios(methods: dict) -> dict[str, str]:
  """Compute ratio strings for all methods within one category.

  Returns a dict mapping method name -> ratio string (e.g., "3.77").
  The baseline method gets "1.00". If baseline mean is 0, all get "?".
  """
  # Find baseline
  baseline_name = None
  baseline_ns = None
  for name, data in methods.items():
    if data.get("baseline"):
      baseline_name = name
      baseline_ns = parse_mean_ns(data.get("mean", ""))
      break

  if baseline_name is None or baseline_ns is None or baseline_ns == 0:
    return {name: "?" for name in methods}

  ratios = {}
  for name, data in methods.items():
    if name == baseline_name:
      ratios[name] = "1.00"
    else:
      mean_ns = parse_mean_ns(data.get("mean", ""))
      if mean_ns is not None:
        ratios[name] = f"{mean_ns / baseline_ns:.2f}"
      else:
        ratios[name] = "?"
  return ratios


def _gen_display(val: str) -> str:
  """Format a Gen column value: '0.0000' -> '-', else keep as-is."""
  if not val or val == "0.0000":
    return "-"
  return val


def load_class_rows(class_name: str, db: dict) -> list[dict[str, str]]:
  """Load rows for a benchmark class, with recomputed ratios.

  Returns rows in the same format as metadata.parse_table(): a list
  of dicts with keys like Method, Categories, Mean, Ratio, Allocated, etc.
  """
  class_data = db.get("results", {}).get(class_name, {})
  if not class_data:
    return []

  # Group by category for ratio computation
  by_cat: dict[str, dict[str, dict]] = {}
  for method_name, data in class_data.items():
    cat = data.get("categories", "")
    by_cat.setdefault(cat, {})[method_name] = data

  # Compute ratios per category
  all_ratios: dict[str, str] = {}
  for cat, cat_methods in by_cat.items():
    all_ratios.update(_compute_ratios(cat_methods))

  # Build rows
  rows = []
  for method_name, data in class_data.items():
    row: dict[str, str] = {"Method": method_name}

    cat = data.get("categories", "")
    if cat:
      row["Categories"] = cat

    row["Mean"] = data.get("mean", "")
    row["Error"] = data.get("error", "")
    row["StdDev"] = data.get("stddev", "")

    if "median" in data:
      row["Median"] = data["median"]

    row["Ratio"] = all_ratios.get(method_name, "?")

    if "gen0" in data:
      row["Gen0"] = _gen_display(data["gen0"])
    if "gen1" in data:
      row["Gen1"] = _gen_display(data["gen1"])
    if "gen2" in data:
      row["Gen2"] = _gen_display(data["gen2"])

    alloc = data.get("allocated", "-")
    row["Allocated"] = "-" if alloc in ("-", "0 B", "") else alloc

    rows.append(row)

  return rows
