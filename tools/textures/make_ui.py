"""Generates Dominion's interface art: paper, newspaper masthead, rubber stamps, rules and category icons.

Run from the repo root:  python3 tools/textures/make_ui.py
Needs Pillow. Uses the same paper and fonts as make_about.py.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_about import INK, SEAL, SERIF, SERIF_BOLD, MONO, font, paper, seal  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures" / "UI" / "Dominion"
WHITE = (255, 255, 255, 255)


def grunge(img, seed, amount=7, strength=(0.25, 0.85)):
    """Knock random specks out of the alpha channel so ink looks hand-stamped."""
    rnd = random.Random(seed)
    a = img.getchannel("A").load()
    w, h = img.size
    for _ in range(w * h // amount):
        x, y = rnd.randrange(w), rnd.randrange(h)
        a[x, y] = int(a[x, y] * rnd.uniform(*strength))
    return img


def paper_sheet():
    """Newsprint: warm paper with fibres, faint fold lines and darker edges. Stretched over panels."""
    w, h = 1024, 1024
    img = paper(w, h, seed=21).convert("RGBA")
    d = ImageDraw.Draw(img)
    rnd = random.Random(5)
    for _ in range(260):  # faint fibres; the sheet is stretched over large panels, so keep them subtle
        x, y = rnd.randrange(w), rnd.randrange(h)
        l = rnd.randint(2, 5)
        a = rnd.uniform(0, math.pi)
        d.line([(x, y), (x + math.cos(a) * l, y + math.sin(a) * l)], fill=(150, 130, 100, 22), width=1)
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    img.convert("RGB").save(OUT / "Paper.png", optimize=True)


def masthead():
    """Newspaper title in a heavy serif, with ornamental rules. Ink only (transparent), tinted in code."""
    w, h = 1024, 150
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    size = 92
    f = font(["Caladea-Bold.ttf", "DejaVuSerif-Bold.ttf", "LiberationSerif-Bold.ttf"], size)
    while f.getlength("The Rimward Courier") > w - 200 and size > 30:  # leave room for the ornaments
        size -= 2
        f = font(["Caladea-Bold.ttf", "DejaVuSerif-Bold.ttf", "LiberationSerif-Bold.ttf"], size)
    d.text((w / 2, 72), "The Rimward Courier", font=f, fill=WHITE, anchor="mm")
    for y, wd in ((8, 4), (16, 1), (h - 14, 1), (h - 6, 4)):
        d.line([(10, y), (w - 10, y)], fill=WHITE, width=wd)
    for x in (52, w - 52):
        star(d, x, 72, 22, 9)
    grunge(img, 9, amount=14, strength=(0.5, 0.95)).save(OUT / "Masthead.png")


def banner_title():
    """'DOMINION' for the tab header, in the same seal ink."""
    w, h = 640, 120
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = font(["Caladea-Bold.ttf", "DejaVuSerif-Bold.ttf", "LiberationSerif-Bold.ttf"], 96)
    d.text((w / 2, h / 2 + 4), "DOMINION", font=f, fill=WHITE, anchor="mm")
    grunge(img, 4, amount=16, strength=(0.55, 0.95)).save(OUT / "Title.png")


def stamp(text, name, seed, wide=1.0):
    """A worn rubber stamp: double-bordered box with the word inside. White, tinted (red, green, amber) in code."""
    f = font(["Caladea-Bold.ttf", "DejaVuSerif-Bold.ttf"], 64)
    tw = int(f.getlength(text))
    w, h = int(max(tw + 70, 260) * wide), 110
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([4, 4, w - 5, h - 5], radius=12, outline=WHITE, width=7)
    d.rounded_rectangle([16, 16, w - 17, h - 17], radius=6, outline=WHITE, width=2)
    d.text((w / 2, h / 2 + 2), text, font=f, fill=WHITE, anchor="mm")
    img = grunge(img, seed, amount=5, strength=(0.1, 0.9))
    img.filter(ImageFilter.GaussianBlur(0.4)).save(OUT / f"Stamp_{name}.png")


def rule():
    """Ornamental divider: a line with a diamond in the middle."""
    w, h = 512, 24
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = h // 2
    d.line([(4, c), (w / 2 - 22, c)], fill=WHITE, width=2)
    d.line([(w / 2 + 22, c), (w - 4, c)], fill=WHITE, width=2)
    d.polygon([(w / 2, c - 9), (w / 2 + 14, c), (w / 2, c + 9), (w / 2 - 14, c)], fill=WHITE)
    for x in (w / 2 - 30, w / 2 + 30):
        d.ellipse([x - 3, c - 3, x + 3, c + 3], fill=WHITE)
    img.save(OUT / "Rule.png")


def seal_small():
    """The Dominion seal, for the header."""
    seal(128).save(OUT / "Seal.png")


def pin():
    """A red push-pin for unread clippings."""
    img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([10, 4, 38, 32], fill=(185, 40, 34, 255), outline=(90, 18, 14, 255), width=2)
    d.ellipse([16, 9, 24, 17], fill=(240, 150, 140, 255))
    d.line([(24, 32), (24, 46)], fill=(90, 90, 90, 255), width=3)
    img.save(OUT / "Pin.png")


def round_stamp(name, glyph, seed):
    """A circular rubber stamp with a pictogram, used as a gizmo icon. Drawn in red ink (gizmos cannot be tinted per draw)."""
    w = 128
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ink = (150, 32, 28, 255)
    d.ellipse([4, 4, w - 5, w - 5], outline=ink, width=8)
    d.ellipse([18, 18, w - 19, w - 19], outline=ink, width=3)
    glyph(d, ink)
    img = grunge(img, seed, amount=5, strength=(0.15, 0.9))
    img.filter(ImageFilter.GaussianBlur(0.4)).save(OUT / f"Gizmo_{name}.png")


def glyph_advisory(d, ink):  # a microphone with radio waves
    d.rounded_rectangle([52, 32, 76, 72], radius=12, fill=ink)
    d.arc([42, 46, 86, 84], 0, 180, fill=ink, width=5)
    d.line([(64, 84), (64, 96)], fill=ink, width=5)
    d.line([(50, 97), (78, 97)], fill=ink, width=5)
    for r in (30, 40):
        d.arc([64 - r - 8, 52 - r, 64 + r + 8, 52 + r], 150, 210, fill=ink, width=4)
        d.arc([64 - r - 8, 52 - r, 64 + r + 8, 52 + r], 330, 30, fill=ink, width=4)


def glyph_investigate(d, ink):  # a magnifying glass over a scrap of newsprint
    for y in (44, 56, 68, 80):
        d.line([(34, y), (70, y)], fill=ink, width=3)
    d.ellipse([46, 34, 86, 74], outline=ink, width=7)
    d.line([(80, 68), (98, 90)], fill=ink, width=10)


# ---------------------------------------------------------------- category icons (64 px, white glyphs)

def icon(name, draw):
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw(ImageDraw.Draw(img))
    grunge(img, hash(name) & 0xFFFF, amount=11, strength=(0.45, 0.95)).save(OUT / f"Cat_{name}.png")


def star(d, cx, cy, r_out, r_in):
    pts = []
    for i in range(10):
        r = r_out if i % 2 == 0 else r_in
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon(pts, fill=WHITE)


def cat_general(d):
    star(d, 32, 33, 26, 11)


def cat_diplomacy(d):  # a sealed scroll
    d.rounded_rectangle([10, 12, 54, 50], radius=6, outline=WHITE, width=4)
    for y in (22, 30, 38):
        d.line([(18, y), (46, y)], fill=WHITE, width=3)
    d.ellipse([38, 38, 56, 56], fill=WHITE)


def cat_war(d):  # crossed swords
    for (a, b) in (((8, 8), (54, 54)), ((56, 8), (10, 54))):
        d.line([a, b], fill=WHITE, width=6)
    d.line([(6, 46), (18, 58)], fill=WHITE, width=6)
    d.line([(46, 58), (58, 46)], fill=WHITE, width=6)


def cat_military(d):  # shield
    d.polygon([(10, 8), (54, 8), (54, 30), (32, 58), (10, 30)], outline=WHITE, width=5)
    d.line([(32, 12), (32, 50)], fill=WHITE, width=4)
    d.line([(14, 26), (50, 26)], fill=WHITE, width=4)


def cat_economy(d):  # coin stack
    for i, y in enumerate((44, 34, 24)):
        d.ellipse([12, y - 8, 52, y + 8], outline=WHITE, width=4)
    d.text((32, 16), "$", fill=WHITE, anchor="mm", font=font(["DejaVuSans-Bold.ttf"], 18))


def cat_politics(d):  # crown
    d.polygon([(8, 50), (8, 18), (20, 32), (32, 10), (44, 32), (56, 18), (56, 50)], fill=WHITE)
    d.rectangle([8, 50, 56, 56], fill=WHITE)


def cat_espionage(d):  # eye
    d.ellipse([4, 18, 60, 46], outline=WHITE, width=5)
    d.ellipse([22, 22, 42, 42], fill=WHITE)


def cat_disaster(d):  # flame
    d.polygon([(32, 4), (48, 26), (50, 42), (40, 56), (24, 56), (14, 42), (18, 28), (26, 36)], fill=WHITE)


def cat_culture(d):  # musical note
    d.ellipse([10, 40, 28, 56], fill=WHITE)
    d.ellipse([36, 34, 54, 50], fill=WHITE)
    d.line([(26, 48), (26, 12)], fill=WHITE, width=5)
    d.line([(52, 42), (52, 6)], fill=WHITE, width=5)
    d.polygon([(26, 12), (52, 6), (52, 16), (26, 22)], fill=WHITE)


def cat_council(d):  # gavel
    d.rounded_rectangle([8, 10, 40, 26], radius=4, fill=WHITE)
    d.line([(24, 24), (44, 50)], fill=WHITE, width=6)
    d.rectangle([30, 52, 60, 58], fill=WHITE)


def icons():
    for name, fn in [("General", cat_general), ("Diplomacy", cat_diplomacy), ("War", cat_war), ("Military", cat_military),
                     ("Economy", cat_economy), ("Politics", cat_politics), ("Espionage", cat_espionage),
                     ("Disaster", cat_disaster), ("Culture", cat_culture), ("Council", cat_council)]:
        icon(name, fn)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    paper_sheet()
    masthead()
    banner_title()
    stamp("CONFIRMED", "Confirmed", 11)
    stamp("RUMOUR", "Rumor", 12)
    stamp("DISPROVED", "Disproved", 13)
    stamp("STOP PRESS", "StopPress", 14)
    stamp("CLASSIFIED", "Classified", 15)
    stamp("INVESTIGATE", "Investigate", 16)
    stamp("UNDER INQUIRY", "Inquiry", 17)
    stamp("VERIFIED", "Verified", 18)
    stamp("ADVISORY", "Advisory", 19)
    stamp("IN FORCE", "InForce", 20)
    stamp("QUEUED", "Queued", 21)
    round_stamp("Advisory", glyph_advisory, 22)
    round_stamp("Investigate", glyph_investigate, 23)
    rule()
    seal_small()
    pin()
    icons()
    print("wrote UI art to", OUT)
