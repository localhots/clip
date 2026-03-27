#!/usr/bin/env python3
"""Generate PDFs from benchmark markdown files.

Usage:
    python3 scripts/pdf.py

Requires: uv sync
    brew install pango  (macOS system dependency for weasyprint)
"""

import html as html_mod
import re
from pathlib import Path

import markdown
from pygments import highlight
from pygments.formatters import HtmlFormatter
from pygments.lexers import get_lexer_by_name, guess_lexer, TextLexer
from weasyprint import HTML

SCRIPT_DIR = Path(__file__).parent
ROOT_DIR = SCRIPT_DIR.parent
DOCS_DIR = ROOT_DIR / "docs"
PDF_DIR = ROOT_DIR / "tmp" / "pdf"

MD_FILES = ["COMPARE.md", "USAGE.md"]

_formatter = HtmlFormatter(style="default", nowrap=False)
_pygments_css = _formatter.get_style_defs(".highlight")

# Match <code class="language-xxx">...</code> inside <pre> tags
_CODE_BLOCK_RE = re.compile(
  r'<pre><code class="language-(\w+)">(.*?)</code></pre>',
  re.DOTALL,
)
# Match bare <pre><code>...</code></pre> (no language specified)
_CODE_BARE_RE = re.compile(
  r"<pre><code>(.*?)</code></pre>",
  re.DOTALL,
)

CSS = (
  """
@page {
    size: A4;
    margin: 18mm 16mm;
}
body {
    font-family: -apple-system, BlinkMacSystemFont, "Helvetica Neue",
                 Helvetica, Arial, sans-serif;
    font-size: 10pt;
    line-height: 1.5;
    color: #1a1a1a;
}
h1 {
    font-size: 22pt;
    border-bottom: 2px solid #2563eb;
    padding-bottom: 4pt;
    margin: 0 0 8pt 0;
}
h2 {
    font-size: 16pt;
    border-bottom: 1px solid #d1d5db;
    padding-bottom: 3pt;
    margin: 0 0 6pt 0;
    page-break-before: always;
    page-break-after: avoid;
}
h3 {
    font-size: 13pt;
    border-bottom: 1px solid #e5e7eb;
    padding-bottom: 2pt;
    margin: 0 0 4pt 0;
    page-break-before: always;
    page-break-after: avoid;
}
h1 + h2, h2 + h3 {
    page-break-before: avoid;
}
code {
    font-family: "SF Mono", Menlo, Consolas, "Courier New", monospace;
    font-size: 9pt;
    background: #f3f4f6;
    padding: 3px 3px;
    border-radius: 2px;
}
pre, .highlight pre {
    background: #f8f8f8;
    padding: 4px 10px;
    border-radius: 4px;
    font-size: 10pt;
    line-height: 1.35;
    page-break-inside: avoid;
    overflow-wrap: break-word;
    white-space: pre-wrap;
}
pre code {
    background: none;
    padding: 0;
}
.highlight {
    background: #f8f8f8;
    border-radius: 4px;
    page-break-inside: avoid;
}
blockquote {
    border-left: 3px solid #2563eb;
    margin: 4pt 0;
    padding: 2pt 10pt;
    color: #374151;
    background: #f9fafb;
    page-break-inside: avoid;
}
blockquote p {
    margin: 2pt 0;
}
img {
    max-width: 100%;
    height: auto;
    display: block;
    margin: 8pt auto;
    page-break-inside: avoid;
}
table {
    border-collapse: collapse;
    width: 100%;
    font-size: 9pt;
    margin: 8pt 0;
    page-break-inside: avoid;
}
th, td {
    border: 1px solid #d1d5db;
    padding: 4pt 6pt;
    text-align: left;
}
th {
    background: #f3f4f6;
    font-weight: 600;
}
hr {
    border: none;
    margin: 0;
    height: 0;
}
details {
    margin: 4pt 0;
}
summary {
    font-weight: 600;
    font-size: 10pt;
    margin-bottom: 4pt;
}
"""
  + _pygments_css
)


def _highlight_match(m):
  """Replace a <pre><code class="language-x"> block with Pygments HTML."""
  lang = m.group(1)
  code = m.group(2)
  code = html_mod.unescape(code)
  try:
    lexer = get_lexer_by_name(lang)
  except Exception:
    lexer = TextLexer()
  return highlight(code, lexer, _formatter)


def _highlight_bare(m):
  """Replace a bare <pre><code> block — guess the language."""
  code = m.group(1)
  code = html_mod.unescape(code)
  try:
    lexer = guess_lexer(code)
  except Exception:
    lexer = TextLexer()
  return highlight(code, lexer, _formatter)


def convert(md_path: Path, pdf_path: Path):
  text = md_path.read_text()
  html_body = markdown.markdown(
    text,
    extensions=["fenced_code", "tables"],
  )
  html_body = _CODE_BLOCK_RE.sub(_highlight_match, html_body)
  html_body = _CODE_BARE_RE.sub(_highlight_bare, html_body)

  full_html = (
    "<!DOCTYPE html><html><head>"
    '<meta charset="utf-8">'
    f"<style>{CSS}</style>"
    f"</head><body>{html_body}</body></html>"
  )
  HTML(
    string=full_html,
    base_url=str(ROOT_DIR),
  ).write_pdf(str(pdf_path))


def main():
  PDF_DIR.mkdir(parents=True, exist_ok=True)
  for name in MD_FILES:
    md_path = DOCS_DIR / name
    if not md_path.exists():
      print(f"  skip {name} (not found)")
      continue
    pdf_path = PDF_DIR / name.replace(".md", ".pdf")
    convert(md_path, pdf_path)
    print(f"  {pdf_path}")


if __name__ == "__main__":
  main()
