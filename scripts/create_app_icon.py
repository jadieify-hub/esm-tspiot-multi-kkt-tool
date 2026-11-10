"""Render the approved 'body and receipt' icon from one geometry definition."""
from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parents[1] / "src/EsmTspiot.WinForms.Shared/Assets"
PETROL = "#245B73"
WHITE = "#FFFFFF"
SIZES = (16, 24, 32, 48, 64, 128, 256)
SHAPES = [
    ("rect", (0, 0, 32, 32), 6, PETROL),
    ("polygon", ((10, 5), (13, 7), (16, 5), (19, 7), (22, 5), (22, 18), (10, 18)), 0, WHITE),
    ("rect", (5, 14, 27, 27), 2, WHITE),
    ("rect", (9, 13, 23, 16), .5, PETROL),
    ("rect", (13, 9, 19, 11), .5, PETROL),
    ("rect", (9, 20, 23, 23), 1, PETROL),
]


def render(size):
    scale = size * 4 / 32
    canvas = Image.new("RGBA", (size * 4, size * 4))
    draw = ImageDraw.Draw(canvas)
    for kind, coords, radius, fill in SHAPES:
        if kind == "rect":
            draw.rounded_rectangle(tuple(v * scale for v in coords), radius=radius * scale, fill=fill)
        else:
            draw.polygon([(x * scale, y * scale) for x, y in coords], fill=fill)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    svg = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">']
    for kind, coords, radius, fill in SHAPES:
        if kind == "rect":
            x, y, right, bottom = coords
            svg.append(f'<rect x="{x}" y="{y}" width="{right-x}" height="{bottom-y}" rx="{radius}" fill="{fill}"/>')
        else:
            points = " ".join(f"{x},{y}" for x, y in coords)
            svg.append(f'<polygon points="{points}" fill="{fill}"/>')
    svg.append("</svg>")
    (ASSETS / "app.svg").write_text("\n".join(svg), encoding="utf-8")
    frames = [render(size) for size in SIZES]
    frames[-1].save(ASSETS / "app_icon_preview.png")
    frames[-1].save(ASSETS / "app.ico", sizes=[(size, size) for size in SIZES], append_images=frames[:-1])
    print(ASSETS / "app.ico")


if __name__ == "__main__":
    main()
