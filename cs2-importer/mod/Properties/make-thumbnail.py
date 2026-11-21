"""Draws Thumbnail.png (512 x 512) for Paradox Mods: a CS1 road grid on the left turning into a CS2 one on the right.

    python cs2-importer/mod/Properties/make-thumbnail.py

Pure Pillow, fonts from C:\\Windows\\Fonts (Segoe UI / Bahnschrift). Deterministic: rerun after changing it.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

S = 512
HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = r"C:\Windows\Fonts"


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


img = Image.new("RGB", (S, S), (18, 32, 52))
d = ImageDraw.Draw(img)

# background: vertical gradient, darker at the bottom
for y in range(S):
    t = y / S
    d.line([(0, y), (S, y)], fill=(int(22 - 10 * t), int(44 - 18 * t), int(70 - 26 * t)))

# map card with a road grid: left half CS1 green, right half CS2 teal
card = (40, 40, 472, 330)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle(card, 28, fill=255)
layer = Image.new("RGB", (S, S))
ld = ImageDraw.Draw(layer)
for x in range(card[0], card[2]):
    t = (x - card[0]) / (card[2] - card[0])
    k = 1 / (1 + math.exp(-(t - 0.5) * 14))  # soft blend in the middle
    c1, c2 = (96, 142, 78), (52, 120, 128)
    ld.line([(x, card[1]), (x, card[3])], fill=tuple(int(a + (b - a) * k) for a, b in zip(c1, c2)))
# roads: a curved highway, a grid and a roundabout
road = (232, 232, 226)
for i, x in enumerate(range(90, 460, 62)):
    ld.line([(x, 40), (x + 18, 330)], fill=road, width=7 if i % 3 == 0 else 4)
for y in range(78, 330, 56):
    ld.line([(40, y), (472, y - 14)], fill=road, width=4)
ld.arc([150, 120, 260, 230], 0, 360, fill=road, width=8)
pts = [(40 + i * 6, 300 - 140 * math.sin(i / 72 * math.pi) - i * 0.6) for i in range(73)]
ld.line(pts, fill=(250, 206, 90), width=11, joint="curve")
ld.line(pts, fill=(60, 60, 60), width=3, joint="curve")
img.paste(layer, (0, 0), mask)
shadow = Image.new("L", (S, S), 0)
ImageDraw.Draw(shadow).rounded_rectangle(card, 28, outline=255, width=3)
img.paste((255, 255, 255), (0, 0), shadow.filter(ImageFilter.GaussianBlur(0.6)))

# badges: CS1 → CS2
def badge(cx, text, fill):
    d.rounded_rectangle([cx - 62, 270, cx + 62, 342], 18, fill=fill, outline=(255, 255, 255), width=4)
    f = font("bahnschrift.ttf", 46)
    w = d.textlength(text, font=f)
    d.text((cx - w / 2, 279), text, font=f, fill=(255, 255, 255))

badge(150, "CS1", (64, 112, 52))
badge(362, "CS2", (28, 96, 112))
d.polygon([(226, 290), (276, 290), (276, 276), (300, 306), (276, 336), (276, 322), (226, 322)], fill=(250, 206, 90))

# title
f1 = font("segoeuib.ttf", 50)
f2 = font("segoeui.ttf", 25)
t1, t2 = "City Importer", "Roads, terrain, zones, transit"
d.text(((S - d.textlength(t1, font=f1)) / 2, 368), t1, font=f1, fill=(255, 255, 255))
d.text(((S - d.textlength(t2, font=f2)) / 2, 432), t2, font=f2, fill=(190, 210, 228))

img.save(os.path.join(HERE, "Thumbnail.png"), optimize=True)
print("wrote", os.path.join(HERE, "Thumbnail.png"))
