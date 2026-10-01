from __future__ import annotations

import html
import subprocess
import sys
from pathlib import Path

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml.ns import qn
from docx.table import Table
from docx.text.paragraph import Paragraph


INPUT = Path(r"C:\remote-OneWayVer2\outputs\C211201_유영선_보고서양식준수본_v2.docx")
OUT_DIR = Path(r"C:\remote-OneWayVer2\tmp\docx_yys\rendered_v2_fallback")
HTML_PATH = OUT_DIR / "preview.html"
PDF_PATH = OUT_DIR / "preview.pdf"
CHROME = Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe")
PDFTOPPM = Path(r"C:\Users\유영선\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\poppler\Library\bin\pdftoppm.exe")


def iter_blocks(doc):
    body = doc.element.body
    for child in body.iterchildren():
        if child.tag == qn("w:p"):
            yield Paragraph(child, doc)
        elif child.tag == qn("w:tbl"):
            yield Table(child, doc)


def pt(value, default=0.0):
    return value.pt if value is not None else default


def cm(value, default=0.0):
    return value.cm if value is not None else default


def paragraph_html(p: Paragraph) -> str:
    fmt = p.paragraph_format
    align_map = {
        WD_ALIGN_PARAGRAPH.CENTER: "center",
        WD_ALIGN_PARAGRAPH.RIGHT: "right",
        WD_ALIGN_PARAGRAPH.JUSTIFY: "justify",
    }
    styles = [
        f"margin-top:{pt(fmt.space_before):.2f}pt",
        f"margin-bottom:{pt(fmt.space_after):.2f}pt",
        f"margin-left:{cm(fmt.left_indent):.2f}cm",
        f"margin-right:{cm(fmt.right_indent):.2f}cm",
        f"text-indent:{cm(fmt.first_line_indent):.2f}cm",
        f"text-align:{align_map.get(p.alignment, 'left')}",
    ]
    if isinstance(fmt.line_spacing, float):
        styles.append(f"line-height:{fmt.line_spacing:.2f}")
    elif fmt.line_spacing is not None:
        styles.append(f"line-height:{pt(fmt.line_spacing):.2f}pt")

    runs = []
    for run in p.runs:
        size = run.font.size.pt if run.font.size else 10.5
        weight = "700" if run.bold else "400"
        text = html.escape(run.text).replace("\n", "<br>")
        runs.append(f'<span style="font-size:{size:.2f}pt;font-weight:{weight}">{text}</span>')
    content = "".join(runs) or "&nbsp;"
    page_break = ""
    if p._p.pPr is not None and p._p.pPr.sectPr is not None:
        section_types = p._p.pPr.sectPr.xpath("./w:type")
        value = section_types[0].get(qn("w:val")) if section_types else "nextPage"
        if value != "continuous":
            page_break = " page-break-after:always;"
    return f'<p style="{";".join(styles)};{page_break}">{content}</p>'


def table_html(table: Table, index: int) -> str:
    cls = f"table-{index}"
    rows = []
    for row in table.rows:
        cells = []
        i = 0
        while i < len(row.cells):
            cell = row.cells[i]
            span = 1
            while i + span < len(row.cells) and row.cells[i + span]._tc is cell._tc:
                span += 1
            paras = "".join(paragraph_html(p) for p in cell.paragraphs)
            colspan = f' colspan="{span}"' if span > 1 else ""
            cells.append(f"<td{colspan}>{paras}</td>")
            i += span
        rows.append("<tr>" + "".join(cells) + "</tr>")
    return f'<table class="{cls}">' + "".join(rows) + "</table>"


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for old in OUT_DIR.glob("page-*.png"):
        old.unlink()
    if PDF_PATH.exists():
        PDF_PATH.unlink()
    doc = Document(INPUT)
    parts = []
    table_index = 0
    for block in iter_blocks(doc):
        if isinstance(block, Paragraph):
            parts.append(paragraph_html(block))
        else:
            parts.append(table_html(block, table_index))
            table_index += 1

    css = """
    @page { size: A4 portrait; margin: 10mm 5mm 6mm 5mm; }
    * { box-sizing: border-box; }
    body { margin: 0; font-family: 'BatangChe', '바탕체', serif; font-size: 10pt; color: #000; }
    p { padding: 0; orphans: 2; widows: 2; }
    table { width: 100%; border-collapse: collapse; margin: 3pt 0 5pt 0; table-layout: fixed; page-break-inside: auto; }
    tr { page-break-inside: avoid; }
    td { border: 0.4pt solid #888; padding: 2.5pt 3pt; vertical-align: middle; overflow-wrap: anywhere; }
    td p { margin-top: 0 !important; margin-bottom: 0 !important; text-indent: 0 !important; }
    .table-0 td { text-align: center; }
    .table-1, .table-2 { font-size: 8pt; }
    .table-1 td:first-child, .table-2 td:first-child { width: 23%; }
    """
    content = "<!doctype html><html lang='ko'><head><meta charset='utf-8'><style>" + css + "</style></head><body>" + "".join(parts) + "</body></html>"
    HTML_PATH.write_text(content, encoding="utf-8")

    subprocess.run(
        [
            str(CHROME),
            "--headless=new",
            "--disable-gpu",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            f"--user-data-dir={OUT_DIR / 'chrome-profile'}",
            "--no-pdf-header-footer",
            f"--print-to-pdf={PDF_PATH}",
            HTML_PATH.as_uri(),
        ],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    subprocess.run(
        [str(PDFTOPPM), "-png", "-r", "150", str(PDF_PATH), str(OUT_DIR / "page")],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    print(PDF_PATH)
    for page in sorted(OUT_DIR.glob("page-*.png")):
        print(page)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise
