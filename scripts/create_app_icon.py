from __future__ import annotations

import sys
from collections import deque
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ASSET_DIR = ROOT / "src" / "EsmTspiot.WinForms.Shared" / "Assets"
ICO_PATH = ASSET_DIR / "app.ico"
PREVIEW_PATH = ASSET_DIR / "app_icon_preview.png"


def is_background(pixel: tuple[int, int, int, int]) -> bool:
    r, g, b, a = pixel
    if a == 0:
        return True
    return r >= 226 and g >= 226 and b >= 226 and max(r, g, b) - min(r, g, b) <= 22


def remove_edge_background(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()
    visited: set[tuple[int, int]] = set()
    queue: deque[tuple[int, int]] = deque()

    for x in range(width):
        queue.append((x, 0))
        queue.append((x, height - 1))
    for y in range(height):
        queue.append((0, y))
        queue.append((width - 1, y))

    while queue:
        x, y = queue.popleft()
        if (x, y) in visited or x < 0 or y < 0 or x >= width or y >= height:
            continue
        visited.add((x, y))
        if not is_background(pixels[x, y]):
            continue

        r, g, b, a = pixels[x, y]
        pixels[x, y] = (r, g, b, 0)
        queue.append((x + 1, y))
        queue.append((x - 1, y))
        queue.append((x, y + 1))
        queue.append((x, y - 1))

    return rgba


def crop_to_square(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        return image

    left, top, right, bottom = bbox
    margin = int(max(right - left, bottom - top) * 0.06)
    left = max(0, left - margin)
    top = max(0, top - margin)
    right = min(image.width, right + margin)
    bottom = min(image.height, bottom + margin)

    cropped = image.crop((left, top, right, bottom))
    side = max(cropped.width, cropped.height)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(cropped, ((side - cropped.width) // 2, (side - cropped.height) // 2))
    return canvas


def normalize_remaining_light_background(image: Image.Image) -> Image.Image:
    rgba = image.copy()
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            r, g, b, a = pixels[x, y]
            if a > 0 and is_background((r, g, b, a)):
                pixels[x, y] = (255, 255, 255, a)
    return rgba


def main() -> None:
    if len(sys.argv) < 2:
        raise SystemExit("Usage: python scripts/create_app_icon.py <source-png>")

    source = Path(sys.argv[1])
    if not source.exists():
        raise FileNotFoundError(source)

    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    image = Image.open(source)
    icon = crop_to_square(normalize_remaining_light_background(remove_edge_background(image)))
    icon.save(PREVIEW_PATH)
    icon.save(ICO_PATH, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    print(ICO_PATH)
    print(PREVIEW_PATH)


if __name__ == "__main__":
    main()
