#!/usr/bin/env python3
"""Generate docs/USAGE.md from ComparisonDemo parseable output.

Parses the delimited output from Clip.ComparisonDemo (@@@ / --- markers)
and compiles it into a formatted markdown file showing code + output for
every logger across every benchmark scenario.

Usage:
    python3 scripts/usage.py tmp/raw/usage.txt
    dotnet run ... | python3 scripts/usage.py -
"""

import json
import re
import sys
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from metadata import DESCRIPTIONS

OUTPUT_FILE = Path("docs/USAGE.md")

SCENARIO_TITLES = {
    "NoFields": "No Fields",
    "FiveFields": "Five Fields",
    "WithContext": "With Context",
    "WithException": "With Exception",
}

SCENARIO_ORDER = ["NoFields", "FiveFields", "WithContext", "WithException"]


def parse(text: str) -> list[dict]:
    """Parse delimited output into a list of entry dicts."""
    entries = []
    for block in re.split(r"@@@ end @@@", text):
        header = re.search(r"@@@ scenario=(\w+) logger=(.+?) @@@", block)
        if not header:
            continue

        entry = {
            "scenario": header.group(1),
            "logger": header.group(2),
        }

        sections = re.split(r"^--- (\S+) ---$", block, flags=re.MULTILINE)
        for i in range(1, len(sections) - 1, 2):
            name = sections[i]
            content = sections[i + 1].strip()
            entry[name] = content

        entries.append(entry)

    return entries


def prettify_json(text: str) -> str:
    """Pretty-print JSON lines in captured output, leaving non-JSON lines as-is."""
    result = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("{"):
            try:
                result.append(json.dumps(json.loads(stripped), indent=2))
                continue
            except json.JSONDecodeError:
                pass
        result.append(line)
    return "\n".join(result)


def render(entries: list[dict]) -> str:
    lines = [
        "# Logger Usage Comparison",
        "",
        f"> Generated: {datetime.now():%Y-%m-%d %H:%M}",
        "",
        "Code examples and real output for each logger across the benchmark scenarios.",
        "Both text and JSON output shown where applicable.",
    ]

    by_scenario: dict[str, list[dict]] = {}
    for e in entries:
        by_scenario.setdefault(e["scenario"], []).append(e)

    for scenario in SCENARIO_ORDER:
        group = by_scenario.get(scenario, [])
        if not group:
            continue

        title = SCENARIO_TITLES.get(scenario, scenario)
        desc_entry = DESCRIPTIONS.get(scenario)
        desc = desc_entry["text"] if desc_entry else ""

        lines += ["", "---", "", f"## {title}", "", desc]

        for entry in group:
            logger = entry["logger"]
            lines += ["", f"### {logger}", ""]

            if "code" in entry:
                lines += ["```csharp", entry["code"], "```"]

            if "console" in entry:
                lines += ["", "```", entry["console"], "```"]

            if "json" in entry:
                lines += ["", "```json", prettify_json(entry["json"]), "```"]

    lines.append("")
    return "\n".join(lines) + "\n"


def main():
    if len(sys.argv) < 2:
        print("Usage: usage.py <input-file>  (or '-' for stdin)", file=sys.stderr)
        sys.exit(1)

    path = sys.argv[1]
    text = sys.stdin.read() if path == "-" else Path(path).read_text()

    entries = parse(text)
    if not entries:
        print("No entries found in input", file=sys.stderr)
        sys.exit(1)

    result = render(entries)
    OUTPUT_FILE.write_text(result)
    print(f"Written {OUTPUT_FILE} ({len(entries)} entries)")


if __name__ == "__main__":
    main()
