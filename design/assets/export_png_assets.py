"""Export deterministic PC Steward PNG assets and compose the prototype share card."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


BRAND = "#1F62D1"
WHITE = "#FFFFFF"
PALE_BLUE = "#B9D2FF"
WINDOWS_ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def draw_mark(size: int) -> Image.Image:
    scale = size / 64
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    def box(values: tuple[float, float, float, float]) -> tuple[int, int, int, int]:
        return tuple(round(value * scale) for value in values)

    draw.rounded_rectangle(
        box((0, 0, 64, 64)),
        radius=round(15 * scale),
        fill=BRAND,
    )

    loop_box = box((14, 14, 50, 50))
    loop_width = max(1, round(5 * scale))
    draw.arc(loop_box, start=28, end=328, fill=WHITE, width=loop_width)
    cap_radius = loop_width / 2
    for x, y in ((48, 40), (46.5, 22.5)):
        draw.ellipse(
            (
                round((x * scale) - cap_radius),
                round((y * scale) - cap_radius),
                round((x * scale) + cap_radius),
                round((y * scale) + cap_radius),
            ),
            fill=WHITE,
        )

    draw.rounded_rectangle(box((23, 21, 30, 28)), radius=max(1, round(2 * scale)), fill=WHITE)
    draw.rounded_rectangle(box((34, 21, 41, 28)), radius=max(1, round(2 * scale)), fill=PALE_BLUE)
    draw.rounded_rectangle(box((23, 32, 41, 39)), radius=max(1, round(2 * scale)), fill=WHITE)
    draw.ellipse(box((44, 15, 51, 22)), fill=PALE_BLUE)
    return canvas


def export_windows_icon(output_path: Path) -> None:
    """Export a multi-resolution ICO from the selected square mark."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    draw_mark(256).save(
        output_path,
        format="ICO",
        sizes=[(size, size) for size in WINDOWS_ICON_SIZES],
    )


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    font_name = "segoeuib.ttf" if bold else "segoeui.ttf"
    candidates = (
        Path("C:/Windows/Fonts") / font_name,
        Path("C:/Windows/Fonts/segoeuisb.ttf"),
        Path("C:/Windows/Fonts/arial.ttf"),
    )
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default(size=size)


def cover(source: Image.Image, width: int, height: int) -> Image.Image:
    source_ratio = source.width / source.height
    target_ratio = width / height
    if source_ratio > target_ratio:
        crop_width = round(source.height * target_ratio)
        left = (source.width - crop_width) // 2
        crop = source.crop((left, 0, left + crop_width, source.height))
    else:
        crop_height = round(source.width / target_ratio)
        top = (source.height - crop_height) // 2
        crop = source.crop((0, top, source.width, top + crop_height))
    return crop.resize((width, height), Image.Resampling.LANCZOS)


def compose_share_card(background_path: Path, output_path: Path) -> None:
    source = Image.open(background_path).convert("RGB")
    card = cover(source, 1200, 630).convert("RGBA")

    overlay = Image.new("RGBA", card.size, (0, 0, 0, 0))
    overlay_pixels = overlay.load()
    for x in range(card.width):
        if x <= 650:
            alpha = round(118 * (1 - (x / 650)) ** 1.4)
        else:
            alpha = 0
        for y in range(card.height):
            overlay_pixels[x, y] = (2, 10, 24, alpha)
    card = Image.alpha_composite(card, overlay)

    draw = ImageDraw.Draw(card)
    mark = draw_mark(72)
    card.alpha_composite(mark, (76, 64))

    eyebrow_font = load_font(17, bold=True)
    title_font = load_font(62, bold=True)
    statement_font = load_font(34, bold=False)
    detail_font = load_font(20, bold=False)
    footer_font = load_font(17, bold=False)

    draw.text((76, 162), "INTERACTIVE DESIGN PROTOTYPE", font=eyebrow_font, fill=PALE_BLUE)
    draw.text((76, 198), "PC Steward", font=title_font, fill=WHITE)
    draw.text((76, 285), "Evidence before action.", font=statement_font, fill=WHITE)
    draw.text((76, 342), "Local-first Windows software care", font=detail_font, fill="#D7E5FF")

    draw.rounded_rectangle((76, 492, 508, 544), radius=26, fill=(8, 24, 48, 205), outline=(90, 145, 234, 135), width=1)
    draw.text((100, 507), "Windows 11  ·  Account-free  ·  Trusted sources", font=footer_font, fill=PALE_BLUE)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    card.convert("RGB").save(output_path, format="PNG", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--background", type=Path, required=True)
    parser.add_argument("--asset-output", type=Path, required=True)
    parser.add_argument("--public-output", type=Path, required=True)
    args = parser.parse_args()

    args.asset_output.mkdir(parents=True, exist_ok=True)
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        draw_mark(size).save(args.asset_output / f"logo-mark-{size}.png", format="PNG", optimize=True)
    export_windows_icon(args.asset_output.parent / "ico" / "pc-steward.ico")

    share_card = args.asset_output / "pc-steward-social-card.png"
    compose_share_card(args.background, share_card)
    args.public_output.parent.mkdir(parents=True, exist_ok=True)
    Image.open(share_card).save(args.public_output, format="PNG", optimize=True)


if __name__ == "__main__":
    main()
