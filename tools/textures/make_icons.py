"""Generates command (gizmo) icons, 128x128, in the ink-stamp style.

Run from the repo root:  python3 tools/textures/make_icons.py
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures" / "UI" / "Commands"
INK = (230, 220, 190, 255)
SEAL = (170, 34, 34, 255)


def base():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def star(d, cx, cy, r_out, r_in, fill):
    pts = []
    for i in range(10):
        r = r_out if i % 2 == 0 else r_in
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon(pts, fill=fill)


def head_of_state():
    img, d = base()
    # crown
    d.polygon([(20, 92), (24, 40), (46, 66), (64, 30), (82, 66), (104, 40), (108, 92)], fill=(214, 172, 60, 255), outline=(60, 40, 10, 255))
    d.rectangle([20, 92, 108, 108], fill=(214, 172, 60, 255), outline=(60, 40, 10, 255))
    for cx in (40, 64, 88):
        d.ellipse([cx - 6, 94, cx + 6, 106], fill=SEAL)
    img.save(OUT / "Dominion_HeadOfState.png")


def capital():
    img, d = base()
    d.ellipse([10, 10, 118, 118], outline=INK, width=8)
    star(d, 64, 66, 42, 17, INK)
    img.save(OUT / "Dominion_Capital.png")


def diplomacy():
    img, d = base()
    d.rectangle([22, 14, 98, 112], fill=(236, 228, 204, 255), outline=(60, 50, 40, 255), width=3)   # treaty paper
    for y in range(30, 80, 10):
        d.line([(32, y), (88, y)], fill=(110, 110, 130, 255), width=3)
    d.ellipse([62, 72, 112, 122], fill=SEAL, outline=(90, 10, 10, 255), width=3)                    # seal
    star(d, 87, 97, 15, 6, (240, 200, 200, 255))
    img.save(OUT / "Dominion_Diplomacy.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    head_of_state()
    capital()
    diplomacy()
    print("wrote icons to", OUT)
