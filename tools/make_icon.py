"""把 icon.png 转成 Windows 应用图标（多尺寸 .ico）。

源图是方形黑底，圆角外也是纯黑。这里补一层圆角遮罩让四角透明，
再按每个尺寸单独缩放，保证小尺寸下线条不糊。
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "icon.png"
ICON_DIR = ROOT / "src" / "StockTicker" / "Resources"
ICO_SIZES = [256, 128, 64, 48, 32, 24, 20, 16]

# 源图圆角半径占画布的比例，按目测微调。
CORNER_RATIO = 0.208


def load_source() -> Image.Image:
    image = Image.open(SOURCE).convert("RGBA")
    size = min(image.size)
    if image.size[0] != image.size[1]:
        left = (image.width - size) // 2
        top = (image.height - size) // 2
        image = image.crop((left, top, left + size, top + size))
    return image


def apply_round_corners(image: Image.Image) -> Image.Image:
    """把圆角外的纯黑区域变成透明。"""
    size = image.width
    mask = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size * 4 - 1, size * 4 - 1],
        radius=int(size * 4 * CORNER_RATIO),
        fill=255,
    )
    mask = mask.resize((size, size), Image.LANCZOS)

    result = image.copy()
    result.putalpha(mask)
    return result


def downscale(image: Image.Image, size: int) -> Image.Image:
    """先降采样再轻微锐化，小尺寸下 K 线和猫的轮廓更利落。"""
    small = image.resize((size, size), Image.LANCZOS)
    if size <= 48:
        small = small.filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=2))
    return small


def main() -> int:
    if not SOURCE.exists():
        print(f"找不到源图：{SOURCE}")
        return 1

    ICON_DIR.mkdir(parents=True, exist_ok=True)
    master = apply_round_corners(load_source())

    master.resize((512, 512), Image.LANCZOS).save(ICON_DIR / "icon-preview.png")

    ico_base = downscale(master, 256)
    ico_base.save(
        ICON_DIR / "app.ico",
        format="ICO",
        sizes=[(size, size) for size in ICO_SIZES],
    )

    print(f"generated: {ICON_DIR / 'app.ico'}")
    print(f"generated: {ICON_DIR / 'icon-preview.png'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
