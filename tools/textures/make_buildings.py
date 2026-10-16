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


def leadership_desk():
    """2x2 desk, Graphic_Multi with masks: red in the mask = tinted by the desk's material."""
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    mask = Image.new("RGBA", (s, s), (0, 0, 0, 255))
    d, m = ImageDraw.Draw(img), ImageDraw.Draw(mask)
    body = [14, 40, 242, 214]
    # desk body: light grey so the material colour comes through
    shaded_rect(d, body, (214, 214, 214, 255), width=4, highlight=24)
    m.rectangle(body, fill=(255, 0, 0, 255))
    d.rectangle([24, 50, 232, 204], outline=(150, 150, 150, 255), width=3)        # inlaid border
    for y in range(64, 200, 18):                                                 # grain
        d.line([(28, y), (228, y + 3)], fill=(185, 185, 185, 255), width=2)
    d.rectangle([14, 196, 242, 214], fill=(170, 170, 170, 255), outline=OUTLINE, width=3)  # front edge
    # untinted details (mask stays black there)
    def detail(box, fill, shape="rect"):
        (d.ellipse if shape == "ellipse" else d.rectangle)(box, fill=fill, outline=OUTLINE, width=2)
        (m.ellipse if shape == "ellipse" else m.rectangle)(box, fill=(0, 0, 0, 255))
    detail([92, 96, 164, 164], (236, 228, 204, 255))                              # document
    for y in range(106, 156, 8):
        d.line([(100, y), (156, y)], fill=(110, 110, 130, 255), width=2)
    detail([136, 140, 160, 160], (170, 30, 30, 255), "ellipse")                   # wax seal
    detail([44, 70, 74, 100], (40, 40, 60, 255), "ellipse")                       # inkwell
    d.line([(64, 74), (86, 52)], fill=(240, 240, 240, 255), width=3)              # quill
    detail([186, 62, 222, 98], (60, 90, 60, 255), "ellipse")                      # lamp shade
    detail([198, 74, 210, 86], (250, 230, 140, 255), "ellipse")
    detail([40, 150, 80, 186], (110, 40, 40, 255))                                # book stack
    detail([44, 140, 84, 172], (40, 70, 110, 255))
    detail([108, 186, 148, 198], (200, 170, 70, 255))                             # nameplate
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([20, 48, 250, 224], fill=(0, 0, 0, 80))
    shadow = shadow.filter(ImageFilter.GaussianBlur(4))
    south = Image.alpha_composite(shadow, img)
    for rot, angle in (("south", 0), ("north", 180), ("east", 90)):
        south.rotate(angle).save(OUT / f"Dominion_LeadershipDesk_{rot}.png", optimize=True)
        mask.rotate(angle).save(OUT / f"Dominion_LeadershipDesk_{rot}m.png", optimize=True)


def broadcast_center():
    """2x2 studio: desk with mixing console and microphone, transmitter mast at the back."""
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    shaded_rect(d, [16, 96, 240, 236], (92, 96, 104, 255), width=4)                  # studio desk
    shaded_rect(d, [40, 112, 216, 176], (52, 56, 62, 255), width=3, highlight=20)    # mixing console
    for i, x in enumerate(range(52, 208, 16)):                                       # faders
        d.rectangle([x, 120, x + 6, 168], fill=(30, 30, 34, 255))
        y = 128 + (i * 13) % 32
        d.rectangle([x - 3, y, x + 9, y + 8], fill=(220, 220, 220, 255), outline=OUTLINE)
    for x, col in ((60, (200, 60, 50, 255)), (84, (60, 200, 80, 255)), (108, (230, 200, 60, 255))):
        d.ellipse([x, 186, x + 14, 200], fill=col, outline=OUTLINE)                  # lamps
    d.line([(180, 196), (196, 164)], fill=(40, 40, 40, 255), width=4)                # mic arm
    d.ellipse([186, 152, 210, 176], fill=(70, 70, 76, 255), outline=OUTLINE, width=2)
    # transmitter mast
    shaded_rect(d, [92, 20, 164, 92], (140, 140, 148, 255), width=3)
    d.line([(128, 26), (128, 4)], fill=(190, 190, 196, 255), width=6)
    for r in (18, 30):
        d.arc([128 - r, 8 - r // 2, 128 + r, 8 + r], 200, 340, fill=(220, 80, 70, 255), width=3)
    d.rectangle([104, 40, 152, 76], fill=(60, 64, 70, 255), outline=OUTLINE, width=2)
    for y in range(46, 74, 6):
        d.line([(110, y), (146, y)], fill=(110, 200, 120, 255), width=2)
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([22, 28, 248, 246], fill=(0, 0, 0, 70))
    shadow = shadow.filter(ImageFilter.GaussianBlur(4))
    Image.alpha_composite(shadow, img).save(OUT / "Dominion_BroadcastCenter.png", optimize=True)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    broadcast_center()
    radio_receiver()
    leadership_desk()
    print("wrote building textures to", OUT)
