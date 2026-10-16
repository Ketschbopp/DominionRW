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


def delegation():
    """World map icon: a banner on a pole with the Dominion seal. White so the faction colour tints it."""
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(14, 4), (14, 60)], fill=(255, 255, 255, 255), width=4)
    d.polygon([(16, 6), (56, 6), (48, 20), (56, 34), (16, 34)], fill=(255, 255, 255, 255), outline=(40, 40, 40, 255))
    star(d, 32, 20, 9, 4, (60, 60, 60, 255))
    world = ROOT / "Textures" / "World"
    world.mkdir(parents=True, exist_ok=True)
    img.save(world / "Dominion_Delegation.png")


def warband():
    """World map icon: crossed swords over a shield. White so the faction colour tints it."""
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    white, dark = (255, 255, 255, 255), (40, 40, 40, 255)
    for (x0, y0, x1, y1) in ((8, 8, 56, 56), (56, 8, 8, 56)):
        d.line([(x0, y0), (x1, y1)], fill=dark, width=8)
        d.line([(x0, y0), (x1, y1)], fill=white, width=4)
    for (gx, gy) in ((14, 50), (50, 50)):  # hilts
        d.line([(gx - 7, gy - 7), (gx + 7, gy + 7)], fill=dark, width=5)
    d.polygon([(20, 22), (44, 22), (44, 38), (32, 50), (20, 38)], fill=white, outline=dark)
    d.line([(32, 24), (32, 46)], fill=dark, width=3)
    world = ROOT / "Textures" / "World"
    world.mkdir(parents=True, exist_ok=True)
    img.save(world / "Dominion_Warband.png")


def broadcast():
    img, d = base()
    d.line([(64, 120), (64, 50)], fill=INK, width=8)
    d.polygon([(40, 120), (88, 120), (64, 80)], outline=INK, fill=None)
    d.ellipse([54, 38, 74, 58], fill=SEAL)
    for r in (22, 38, 54):
        d.arc([64 - r, 48 - r, 64 + r, 48 + r], 220, 320, fill=INK, width=6)
    img.save(OUT / "Dominion_Broadcast.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    broadcast()
    delegation()
    warband()
    head_of_state()
    capital()
    diplomacy()
    print("wrote icons to", OUT)
