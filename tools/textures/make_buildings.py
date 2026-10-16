"""Generates building textures (top-down, transparent background, 128 px per cell).

Run from the repo root:  python3 tools/textures/make_buildings.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures" / "Things" / "Building"
OUTLINE = (30, 26, 22, 255)


def shaded_rect(d, box, fill, outline=OUTLINE, width=3, highlight=18):
    """Rectangle with a dark outline and a lighter top edge, RimWorld style."""
    d.rectangle(box, fill=fill, outline=outline, width=width)
    x0, y0, x1, y1 = box
    hl = tuple(min(255, c + highlight) for c in fill[:3]) + (255,)
    d.rectangle([x0 + width, y0 + width, x1 - width, y0 + width + 4], fill=hl)


def radio_receiver():
    s = 128
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # desk
    shaded_rect(d, [8, 30, 120, 112], (122, 86, 52, 255))
    for y in (52, 74, 96):
        d.line([(12, y), (116, y)], fill=(104, 72, 42, 255), width=1)
    # radio set
    shaded_rect(d, [22, 36, 104, 78], (70, 84, 70, 255), highlight=22)
    d.rectangle([30, 44, 70, 62], fill=(196, 170, 96, 255), outline=OUTLINE, width=2)   # dial window
    for x in range(34, 68, 6):
        d.line([(x, 46), (x, 52)], fill=(80, 60, 30, 255), width=1)
    d.line([(50, 45), (54, 61)], fill=(170, 30, 30, 255), width=2)                    # needle
    for cx in (82, 96):                                                                 # knobs
        d.ellipse([cx - 6, 46, cx + 6, 58], fill=(40, 40, 40, 255), outline=OUTLINE, width=2)
        d.line([(cx, 47), (cx, 52)], fill=(200, 200, 200, 255), width=2)
    d.rectangle([30, 66, 96, 72], fill=(50, 60, 50, 255))                               # speaker grille
    for x in range(32, 96, 4):
        d.line([(x, 66), (x, 72)], fill=(35, 40, 35, 255), width=1)
    # antenna
    d.line([(100, 38), (118, 6)], fill=(170, 170, 175, 255), width=3)
    d.ellipse([114, 2, 122, 10], fill=(200, 60, 50, 255), outline=OUTLINE, width=1)
    # headset and notebook
    d.arc([24, 84, 52, 108], 180, 360, fill=(30, 30, 30, 255), width=4)
    d.ellipse([20, 94, 30, 106], fill=(45, 45, 45, 255), outline=OUTLINE)
    d.ellipse([46, 94, 56, 106], fill=(45, 45, 45, 255), outline=OUTLINE)
    d.rectangle([70, 86, 104, 108], fill=(232, 222, 196, 255), outline=OUTLINE, width=2)
    for y in (92, 97, 102):
        d.line([(74, y), (100, y)], fill=(120, 120, 140, 255), width=1)
    # soft shadow under everything
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([12, 36, 124, 118], fill=(0, 0, 0, 70))
    shadow = shadow.filter(ImageFilter.GaussianBlur(3))
    out = Image.alpha_composite(shadow, img)
    out.save(OUT / "Dominion_RadioReceiver.png", optimize=True)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    radio_receiver()
    print("wrote", OUT / "Dominion_RadioReceiver.png")
