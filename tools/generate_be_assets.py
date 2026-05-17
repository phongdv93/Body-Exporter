"""Regenerate BodyExporter BE logo PNGs, .ico, and website og assets (tight crop, high-res)."""

from __future__ import annotations

import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
WEB_STATIC = ROOT / "website" / "static"

BG = (31, 90, 165, 255)
BORDER = (22, 64, 116, 255)
PAD_RATIO = 0.035  # minimal margin — favicon fills the square
RADIUS_RATIO = 0.22
SIZES = (16, 20, 32, 40, 48, 64, 96, 128, 256, 512)


def _rounded_rect(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], radius: int, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def _font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    size = max(8, size)
    for name in ("segoeuib.ttf", "arialbd.ttf", "Arial Bold.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size=size)
        except OSError:
            continue
    return ImageFont.load_default()


def render_icon(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    pad = max(1, int(round(size * PAD_RATIO)))
    inner = (pad, pad, size - pad - 1, size - pad - 1)
    radius = max(2, int(round((size - 2 * pad) * RADIUS_RATIO)))
    border_w = max(1, size // 40)
    _rounded_rect(draw, inner, radius, fill=BG, outline=BORDER, width=border_w)

    font_px = int((size - 2 * pad) * 0.52)
    font = _font(font_px)
    text = "BE"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    cx = (size - tw) // 2 - bbox[0]
    cy = (size - th) // 2 - bbox[1]
    draw.text((cx, cy), text, fill=(255, 255, 255, 255), font=font)
    return img


def write_ico(png_by_size: dict[int, Image.Image], out_path: Path) -> None:
    entries: list[tuple[int, bytes]] = []
    for size in sorted(png_by_size.keys()):
        import io

        buf = io.BytesIO()
        png_by_size[size].save(buf, format="PNG")
        entries.append((size, buf.getvalue()))

    count = len(entries)
    header = struct.pack("<HHH", 0, 1, count)
    dir_size = 6 + 16 * count
    offset = dir_size
    directory = bytearray()
    image_data = bytearray()
    for size, data in entries:
        w = 0 if size >= 256 else size
        h = 0 if size >= 256 else size
        directory.extend(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset))
        image_data.extend(data)
        offset += len(data)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(header + bytes(directory) + bytes(image_data))


def render_og_card(logo: Image.Image) -> Image.Image:
    w, h = 1200, 630
    card = Image.new("RGBA", (w, h), (10, 14, 20, 255))
    target = int(min(w, h) * 0.62)
    logo_scaled = logo.resize((target, target), Image.Resampling.LANCZOS)
    x = (w - target) // 2
    y = (h - target) // 2
    card.paste(logo_scaled, (x, y), logo_scaled)
    return card


def main() -> int:
    png_by_size: dict[int, Image.Image] = {}
    for size in SIZES:
        png_by_size[size] = render_icon(size)

    ASSETS.mkdir(parents=True, exist_ok=True)
    WEB_STATIC.mkdir(parents=True, exist_ok=True)

    ico_path = ASSETS / "BodyExporter.ico"
    write_ico(png_by_size, ico_path)
    print(f"Wrote {ico_path}")

    for size, im in png_by_size.items():
        preview = ASSETS / f"BodyExporter_{size}.png"
        im.save(preview, "PNG")
    print(f"Wrote PNG previews under {ASSETS}")

    favicon_src = png_by_size[256]
    favicon_src.save(WEB_STATIC / "favicon.ico", format="ICO", sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
    # Pillow ICO may pad — also write multi-size via our writer
    write_ico({k: png_by_size[k] for k in (16, 32, 48, 64, 128, 256) if k in png_by_size}, WEB_STATIC / "favicon.ico")
    print(f"Wrote {WEB_STATIC / 'favicon.ico'}")

    logo512 = png_by_size[512]
    logo512.save(WEB_STATIC / "og.png", "PNG")
    render_og_card(logo512).save(WEB_STATIC / "og-card.png", "PNG")
    print(f"Wrote {WEB_STATIC / 'og.png'} and og-card.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())
