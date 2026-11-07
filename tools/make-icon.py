"""Draws the dawsu! app icon: a white "d" and pink "!" in the wordmark's font (Archivo Expanded ExtraBold) on a dark
rounded square. Writes src/OsuHitsound.App/Assets/dawsu.ico (16-256 px) and dawsu-256.png.

Usage: python tools/make-icon.py   (needs Pillow; run from the repository root)
"""
from PIL import Image, ImageDraw, ImageFont
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, 'src', 'OsuHitsound.App', 'Assets')
FONT = os.path.join(ASSETS, 'Fonts', 'ArchivoExpanded-ExtraBold.ttf')
BACKGROUND, BORDER, WHITE, PINK = (27, 27, 30, 255), (255, 102, 170, 255), (244, 244, 246, 255), (255, 102, 170, 255)


def draw(size):
    """One icon size, drawn at 4x and scaled down so small sizes stay smooth."""
    s = size * 4
    image = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    radius = s * 0.22
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=radius, fill=BACKGROUND)
    if size >= 32:  # a thin pink edge, lost at the smallest sizes anyway
        width = max(4, s // 64)
        d.rounded_rectangle((width // 2, width // 2, s - 1 - width // 2, s - 1 - width // 2), radius=radius, outline=BORDER, width=width)
    font = ImageFont.truetype(FONT, int(s * 0.62))
    d_box = d.textbbox((0, 0), 'd', font=font)
    bang_box = d.textbbox((0, 0), '!', font=font)
    gap = s * 0.01
    width = (d_box[2] - d_box[0]) + gap + (bang_box[2] - bang_box[0])
    height = max(d_box[3], bang_box[3]) - min(d_box[1], bang_box[1])
    x = (s - width) / 2 - d_box[0]
    y = (s - height) / 2 - min(d_box[1], bang_box[1]) + s * 0.01
    d.text((x, y), 'd', font=font, fill=WHITE)
    d.text((x + (d_box[2] - d_box[0]) + gap + d_box[0] - bang_box[0], y), '!', font=font, fill=PINK)
    return image.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [draw(n) for n in sizes]
images[-1].save(os.path.join(ASSETS, 'dawsu-256.png'))
images[-1].save(os.path.join(ASSETS, 'dawsu.ico'), sizes=[(n, n) for n in sizes], append_images=images[:-1])
print('wrote', os.path.join(ASSETS, 'dawsu.ico'))
