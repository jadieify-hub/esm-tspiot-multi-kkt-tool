from __future__ import annotations

import html
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "INSTRUCTION_FOR_DUMMIES.md"
OUTPUT_DIR = ROOT / "output" / "pdf"
OUTPUT = OUTPUT_DIR / "Instruction-MultiKKT.pdf"


def register_fonts() -> tuple[str, str]:
    fonts_dir = Path("C:/Windows/Fonts")
    regular = fonts_dir / "arial.ttf"
    bold = fonts_dir / "arialbd.ttf"

    if regular.exists():
        pdfmetrics.registerFont(TTFont("AppArial", str(regular)))
    else:
        raise FileNotFoundError("Arial font was not found at C:/Windows/Fonts/arial.ttf")

    if bold.exists():
        pdfmetrics.registerFont(TTFont("AppArialBold", str(bold)))
    else:
        pdfmetrics.registerFont(TTFont("AppArialBold", str(regular)))

    return "AppArial", "AppArialBold"


def build_styles(font_name: str, bold_font_name: str):
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "TitleRu",
            parent=base["Title"],
            fontName=bold_font_name,
            fontSize=19,
            leading=23,
            alignment=TA_CENTER,
            spaceAfter=8 * mm,
        ),
        "h2": ParagraphStyle(
            "Heading2Ru",
            parent=base["Heading2"],
            fontName=bold_font_name,
            fontSize=13,
            leading=16,
            textColor=colors.HexColor("#17365D"),
            spaceBefore=5 * mm,
            spaceAfter=2.5 * mm,
        ),
        "h3": ParagraphStyle(
            "Heading3Ru",
            parent=base["Heading3"],
            fontName=bold_font_name,
            fontSize=11,
            leading=14,
            textColor=colors.HexColor("#333333"),
            spaceBefore=3 * mm,
            spaceAfter=1.5 * mm,
        ),
        "body": ParagraphStyle(
            "BodyRu",
            parent=base["BodyText"],
            fontName=font_name,
            fontSize=10,
            leading=13.5,
            alignment=TA_LEFT,
            spaceAfter=1.7 * mm,
        ),
        "bullet": ParagraphStyle(
            "BulletRu",
            parent=base["BodyText"],
            fontName=font_name,
            fontSize=10,
            leading=13.5,
            leftIndent=6 * mm,
            firstLineIndent=-3 * mm,
            bulletIndent=2 * mm,
            spaceAfter=1.2 * mm,
        ),
        "code": ParagraphStyle(
            "CodeRu",
            parent=base["BodyText"],
            fontName=font_name,
            fontSize=9,
            leading=12,
            backColor=colors.HexColor("#F3F5F7"),
            borderColor=colors.HexColor("#D6DADE"),
            borderWidth=0.5,
            borderPadding=4,
            leftIndent=2 * mm,
            rightIndent=2 * mm,
            spaceBefore=1 * mm,
            spaceAfter=2.5 * mm,
            wordWrap="CJK",
        ),
        "note": ParagraphStyle(
            "NoteRu",
            parent=base["BodyText"],
            fontName=bold_font_name,
            fontSize=10,
            leading=13.5,
            textColor=colors.HexColor("#7A2E0E"),
            backColor=colors.HexColor("#FFF4E5"),
            borderColor=colors.HexColor("#F2C078"),
            borderWidth=0.5,
            borderPadding=5,
            spaceBefore=2 * mm,
            spaceAfter=3 * mm,
        ),
    }


def inline_text(text: str) -> str:
    normalized = (
        text.replace("`", "")
        .replace("—", "-")
        .replace("–", "-")
        .replace("‑", "-")
    )
    return html.escape(normalized)


def paragraph(text: str, style: ParagraphStyle) -> Paragraph:
    return Paragraph(inline_text(text), style)


def parse_markdown(markdown: str, styles):
    flowables = []
    in_code = False
    code_lines: list[str] = []

    def flush_code():
        if not code_lines:
            return
        escaped = "<br/>".join(html.escape(line) for line in code_lines)
        flowables.append(Paragraph(escaped, styles["code"]))
        code_lines.clear()

    for raw_line in markdown.splitlines():
        line = raw_line.rstrip()

        if line.strip().startswith("```"):
            if in_code:
                flush_code()
                in_code = False
            else:
                in_code = True
            continue

        if in_code:
            code_lines.append(line)
            continue

        stripped = line.strip()
        if not stripped:
            flowables.append(Spacer(1, 0.6 * mm))
            continue

        if stripped.startswith("# "):
            flowables.append(paragraph(stripped[2:].strip(), styles["title"]))
        elif stripped.startswith("## "):
            flowables.append(paragraph(stripped[3:].strip(), styles["h2"]))
        elif stripped.startswith("### "):
            flowables.append(paragraph(stripped[4:].strip(), styles["h3"]))
        elif stripped.startswith("- "):
            flowables.append(Paragraph(inline_text(stripped[2:].strip()), styles["bullet"], bulletText="•"))
        elif stripped.lower().startswith("важно:"):
            flowables.append(paragraph(stripped, styles["note"]))
        else:
            flowables.append(paragraph(stripped, styles["body"]))

    if in_code:
        flush_code()

    return flowables


def draw_footer(canvas, doc):
    canvas.saveState()
    font_name = "AppArial"
    canvas.setFont(font_name, 8)
    canvas.setFillColor(colors.HexColor("#666666"))
    footer = f"Инструкция: управление ККТ в ЕСМ/ТС ПИоТ    Стр. {doc.page}"
    canvas.drawCentredString(A4[0] / 2, 10 * mm, footer)
    canvas.restoreState()


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    font_name, bold_font_name = register_fonts()
    styles = build_styles(font_name, bold_font_name)

    markdown = SOURCE.read_text(encoding="utf-8")
    story = parse_markdown(markdown, styles)

    doc = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=A4,
        rightMargin=16 * mm,
        leftMargin=16 * mm,
        topMargin=16 * mm,
        bottomMargin=17 * mm,
        title="Инструкция для чайников: управление ККТ в ЕСМ/ТС ПИоТ",
        author="Руслан Керусов",
        subject="Издатель и владелец: KRS",
    )
    doc.build(story, onFirstPage=draw_footer, onLaterPages=draw_footer)
    print(str(OUTPUT))


if __name__ == "__main__":
    main()
