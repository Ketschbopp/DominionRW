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


def glyph_morale(d, ink):  # a sun with rays
    d.ellipse([46, 46, 82, 82], fill=ink)
    for i in range(10):
        a = i * math.pi / 5
        d.line([(64 + math.cos(a) * 24, 64 + math.sin(a) * 24), (64 + math.cos(a) * 36, 64 + math.sin(a) * 36)], fill=ink, width=5)


def glyph_counter(d, ink):  # a shield with a crossed-out speech bubble
    d.polygon([(34, 30), (94, 30), (94, 62), (64, 100), (34, 62)], outline=ink, width=6)
    d.rounded_rectangle([46, 44, 82, 66], radius=6, outline=ink, width=4)
    d.polygon([(54, 66), (50, 76), (62, 66)], fill=ink)
    d.line([(42, 80), (86, 38)], fill=ink, width=5)


def glyph_debunk(d, ink):  # a big X over a scrap of paper
    d.rectangle([38, 34, 90, 94], outline=ink, width=4)
    d.line([(36, 32), (92, 96)], fill=ink, width=9)
    d.line([(92, 32), (36, 96)], fill=ink, width=9)


def glyph_spread(d, ink):  # a megaphone with sound lines
    d.polygon([(30, 54), (58, 54), (88, 34), (88, 94), (58, 74), (30, 74)], fill=ink)
    d.rectangle([40, 74, 50, 92], fill=ink)
    for r in (12, 22):
        d.arc([88 - r, 64 - r, 88 + r + 8, 64 + r], 300, 60, fill=ink, width=4)


def glyph_suppress(d, ink):  # redacted lines of text
    for y, w in ((40, 52), (56, 44), (72, 54), (88, 36)):
        d.rectangle([38, y - 5, 38 + w, y + 5], fill=ink)
    d.line([(34, 98), (94, 30)], fill=ink, width=4)


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


# ---------------------------------------------------------------- statecraft desk (treaties, ballots, dossiers)

def parchment_sheet():
    """Treaty parchment: warmer and darker than newsprint, with a burnt vignette at the edges."""
    w, h = 1024, 1024
    img = paper(w, h, seed=33).convert("RGBA")
    px = img.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            dx, dy = (x - w / 2) / (w / 2), (y - h / 2) / (h / 2)
            v = min(1.0, (dx * dx + dy * dy) ** 1.6)
            k = 1.0 - 0.38 * v
            px[x, y] = (int(r * k * 1.02), int(g * k * 0.97), int(b * k * 0.84), 255)
    d = ImageDraw.Draw(img)
    rnd = random.Random(8)
    for _ in range(40):  # foxing: small brown age spots
        x, y, r = rnd.randrange(w), rnd.randrange(h), rnd.uniform(2, 6)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(120, 80, 40, rnd.randint(4, 10)))
    img = img.filter(ImageFilter.GaussianBlur(0.9))
    img.convert("RGB").save(OUT / "Parchment.png", optimize=True)


def wax_seal():
    """A blob of sealing wax with a ridged edge and a pressed ring. White; tinted with the faction's colour in code."""
    w = 128
    rnd = random.Random(41)
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = []
    for i in range(48):
        a = i / 48 * 2 * math.pi
        r = 56 + rnd.uniform(-4, 5) + (3 if i % 2 else 0)
        pts.append((64 + r * math.cos(a), 64 + r * math.sin(a)))
    d.polygon(pts, fill=(235, 235, 235, 255))
    d.ellipse([20, 20, 108, 108], fill=(255, 255, 255, 255))
    d.ellipse([24, 24, 104, 104], outline=(190, 190, 190, 255), width=4)
    d.arc([30, 30, 98, 98], 200, 300, fill=(255, 255, 255, 255), width=3)  # highlight
    img.filter(ImageFilter.GaussianBlur(0.7)).save(OUT / "WaxSeal.png")


def signature(n, seed):
    """A flourished, illegible cursive signature in white ink: looping strokes (a trochoid) with varying letter heights."""
    w, h = 512, 110
    rnd = random.Random(seed)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = []
    t, x0 = 0.0, 24.0
    heights = [rnd.uniform(10, 18) if rnd.random() < 0.7 else rnd.uniform(26, 40) for _ in range(40)]
    while True:
        letter = int(t / (2 * math.pi))
        if letter >= len(heights):
            break
        r = heights[letter]
        x = x0 + t * 4.2 - math.sin(t) * 9
        y = 74 - (1 - math.cos(t)) * r * 0.5
        if x > w - 60:
            break
        pts.append((x + (74 - y) * 0.45, y))  # slanted, as handwriting is
        t += 0.12
        if rnd.random() < 0.004:  # a pen lift between names
            x0 += 18
    for i in range(len(pts) - 1):
        if abs(pts[i + 1][0] - pts[i][0]) < 12:
            d.line([pts[i], pts[i + 1]], fill=WHITE, width=3)
    end_x = pts[-1][0] if pts else w / 2
    d.line([(26, 90), (end_x * 0.6, 95), (end_x + 30, 84)], fill=WHITE, width=3, joint="curve")
    img.filter(ImageFilter.GaussianBlur(0.5)).save(OUT / f"Signature_{n}.png")


def quill():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(54, 4), (60, 10), (22, 46), (14, 50), (18, 42)], fill=WHITE)
    d.line([(18, 46), (6, 58)], fill=WHITE, width=3)
    for i in range(6):
        d.line([(50 - i * 6, 10 + i * 6), (58 - i * 6, 4 + i * 6 + 8)], fill=(0, 0, 0, 0), width=1)
    img.save(OUT / "Quill.png")


def bookmark(filled):
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = []
    for i in range(10):
        r = 28 if i % 2 == 0 else 12
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((32 + r * math.cos(a), 34 + r * math.sin(a)))
    if filled:
        d.polygon(pts, fill=WHITE)
    else:
        d.polygon(pts, outline=WHITE, width=4)
    img.save(OUT / ("Star.png" if filled else "StarOutline.png"))


def scissors():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([6, 38, 24, 56], outline=WHITE, width=4)
    d.ellipse([40, 38, 58, 56], outline=WHITE, width=4)
    d.line([(20, 40), (46, 6)], fill=WHITE, width=5)
    d.line([(44, 40), (18, 6)], fill=WHITE, width=5)
    img.save(OUT / "Scissors.png")


def statecraft():
    parchment_sheet()
    wax_seal()
    for i in range(3):
        signature(i, 300 + i)
    quill()
    bookmark(True)
    bookmark(False)
    scissors()
    for i, (text, name) in enumerate([("SIGNED", "Signed"), ("REJECTED", "Rejected"), ("COUNTER-OFFER", "Counter"),
                                       ("RATIFIED", "Ratified"), ("VOID", "Void"), ("AYE", "Aye"), ("NAY", "Nay"),
                                       ("ABSTAIN", "Abstain"), ("CARRIED", "Carried"), ("DEFEATED", "Defeated"),
                                       ("TOP SECRET", "TopSecret"), ("SUCCESS", "Success"), ("FAILED", "Failed"),
                                       ("COMPROMISED", "Compromised"), ("RECALLED", "Recalled"), ("RENEWED", "Renewed"),
                                       ("PAID", "Paid"), ("CLIPPED", "Clipped"), ("DELIVERED", "Delivered")]):
        stamp(text, name, 400 + i)


# ---------------------------------------------------------------------------------------------------- rooms
# Backgrounds and fittings for the themed tabs: the telegraph office (Diplomacy), the state archive (Treaties),
# the war room (War) and the council chamber (Council). Tileable sheets are drawn with wrap-around noise.

def wrap_noise(w, h, cells, seed):
    """Smooth value noise in 0..1 that tiles seamlessly: a random grid repeated 3x3, upscaled, centre cut out."""
    import numpy as np
    rnd = np.random.default_rng(seed)
    cx, cy = cells if isinstance(cells, tuple) else (cells, cells)
    g = rnd.random((cy, cx))
    big = Image.fromarray((np.tile(g, (3, 3)) * 255).astype("uint8"), "L").resize((w * 3, h * 3), Image.BICUBIC)
    return np.asarray(big.crop((w, h, 2 * w, 2 * h)), dtype="float32") / 255.0


def save_rgb(arr, name):
    import numpy as np
    Image.fromarray(np.clip(arr, 0, 255).astype("uint8"), "RGB").save(OUT / f"{name}.png", optimize=True)


def wood():
    """Dark walnut panelling: long grain with knots of darker figure, seams between planks. Tiles both ways."""
    import numpy as np
    w = h = 512
    y = np.arange(h, dtype="float32")[:, None]
    x = np.arange(w, dtype="float32")[None, :]
    warp = wrap_noise(w, h, (4, 16), 51) * 26 + wrap_noise(w, h, (16, 64), 52) * 5
    grain = np.sin((y + warp) * 2 * np.pi / 512 * 38) * 0.5 + 0.5
    fine = wrap_noise(w, h, (512, 128), 53)
    tone = 0.62 + grain * 0.22 + fine * 0.12 + wrap_noise(w, h, (3, 6), 54) * 0.12
    seams = (np.mod(y, 128) < 2).astype("float32") * np.ones_like(x)
    tone = tone * (1 - seams * 0.45)
    base = np.array([92, 56, 32], dtype="float32")
    save_rgb(tone[..., None] * base, "Wood")


def stone():
    """The archive's walls: dark ashlar blocks, mortar lines, a little grit. Tiles both ways."""
    import numpy as np
    w = h = 512
    y = np.arange(h)[:, None] * np.ones((1, w))
    x = np.ones((h, 1)) * np.arange(w)[None, :]
    row = (y // 64).astype(int)
    xs = x + (row % 2) * 64
    mortar = ((np.mod(y, 64) < 3) | (np.mod(xs, 128) < 3)).astype("float32")
    block = ((row * 7 + (xs // 128).astype(int) * 3) % 5) / 5.0
    tone = 0.75 + block * 0.12 + wrap_noise(w, h, 64, 61) * 0.18 + wrap_noise(w, h, 8, 62) * 0.1
    tone = tone * (1 - mortar * 0.55)
    save_rgb(tone[..., None] * np.array([62, 60, 58], dtype="float32"), "Stone")


def concrete():
    """The war room's bunker walls: cold grey concrete with stains and formwork lines. Tiles both ways."""
    import numpy as np
    w = h = 512
    tone = 0.7 + wrap_noise(w, h, 128, 71) * 0.12 + wrap_noise(w, h, 6, 72) * 0.2
    y = np.arange(h)[:, None] * np.ones((1, w))
    tone = tone * (1 - (np.mod(y, 256) < 2) * 0.3)
    save_rgb(tone[..., None] * np.array([46, 48, 50], dtype="float32"), "Concrete")


def cork():
    """The archive's corkboard: warm brown with dark and light granules. Tiles both ways."""
    import numpy as np
    w = h = 512
    rnd = np.random.default_rng(81)
    speck = rnd.random((h, w))
    tone = 0.8 + wrap_noise(w, h, 128, 82) * 0.2 + (speck > 0.93) * 0.18 - (speck < 0.07) * 0.3 + wrap_noise(w, h, 5, 83) * 0.1
    img = Image.fromarray(np.clip(tone[..., None] * np.array([168, 120, 76], dtype="float32"), 0, 255).astype("uint8"), "RGB")
    img.filter(ImageFilter.GaussianBlur(0.6)).save(OUT / "Cork.png", optimize=True)


def brass():
    """A brushed brass plate with a bevelled edge, for nameplates and switches. Stretched horizontally."""
    import numpy as np
    w, h = 256, 64
    rnd = np.random.default_rng(91)
    streaks = np.repeat(rnd.random((h, 1)), w, axis=1) * 0.5 + rnd.random((h, w)) * 0.08
    y = np.arange(h)[:, None] / h
    light = 1.05 - 0.35 * y + streaks * 0.12
    arr = light[..., None] * np.array([196, 152, 72], dtype="float32")
    arr[:2, :] = [240, 210, 140]
    arr[-2:, :] = [96, 70, 30]
    arr[:, :2] = [220, 180, 110]
    arr[:, -2:] = [110, 80, 36]
    save_rgb(arr, "Brass")


def knob():
    """A round brass key cap with a highlight, for telegraph keys."""
    s = 128
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([4, 8, s - 4, s - 2], fill=(60, 40, 18, 200))  # shadow
    d.ellipse([4, 4, s - 6, s - 6], fill=(150, 112, 48, 255))
    for i in range(18):
        k = i / 18
        c = (int(150 + 80 * k), int(112 + 72 * k), int(48 + 60 * k), 255)
        r = (s - 10) / 2 * (1 - k * 0.55)
        cx, cy = s / 2 - 1 - k * 8, s / 2 - 1 - k * 10
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c)
    img.filter(ImageFilter.GaussianBlur(0.8)).save(OUT / "Knob.png")


def dial():
    """A goodwill gauge: a cream face with a brass bezel and a scale from -100 (red) through 0 to +100 (green) over the
    upper half. The needle is drawn in code."""
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = s / 2
    d.ellipse([2, 2, s - 2, s - 2], fill=(150, 110, 50, 255))
    d.ellipse([12, 12, s - 12, s - 12], fill=(236, 226, 200, 255))
    zones = [(180, 225, (170, 50, 40)), (225, 260, (200, 140, 60)), (260, 280, (150, 150, 140)), (280, 315, (120, 160, 80)), (315, 360, (60, 130, 60))]
    for a0, a1, col in zones:
        d.arc([30, 30, s - 30, s - 30], a0, a1, fill=col + (255,), width=12)
    f = font(SERIF_BOLD, 18)
    for i in range(21):
        a = math.radians(180 + i * 9)
        r0, r1 = (c - 44, c - 26) if i % 5 == 0 else (c - 38, c - 28)
        d.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)), (c + r1 * math.cos(a), c + r1 * math.sin(a))], fill=(40, 32, 24, 255), width=3 if i % 5 == 0 else 1)
    for v, i in (("-100", 0), ("0", 10), ("+100", 20)):
        a = math.radians(180 + i * 9)
        d.text((c + (c - 64) * math.cos(a), c + (c - 64) * math.sin(a)), v, font=f, fill=(40, 32, 24, 255), anchor="mm")
    d.ellipse([c - 10, c - 10, c + 10, c + 10], fill=(90, 66, 30, 255))
    img.filter(ImageFilter.GaussianBlur(0.4)).save(OUT / "Dial.png")


def ticker_tape():
    """Telegraph tape: an off-white paper strip with a row of feed holes. Tiles horizontally."""
    import numpy as np
    w, h = 256, 36
    rnd = np.random.default_rng(101)
    tone = 0.92 + rnd.random((h, w)) * 0.06
    arr = tone[..., None] * np.array([240, 232, 210], dtype="float32")
    img = Image.fromarray(arr.astype("uint8"), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img)
    for x in range(8, w, 16):
        d.ellipse([x - 2, 3, x + 2, 7], fill=(60, 44, 28, 255))
    d.line([(0, 0), (w, 0)], fill=(170, 160, 140, 255))
    d.line([(0, h - 1), (w, h - 1)], fill=(150, 140, 120, 255))
    img.save(OUT / "TickerTape.png")


def shelf():
    """An archive shelf seen from the front: a plank's lip with a highlight and the shadow under it. Tiles horizontally."""
    import numpy as np
    w, h = 256, 40
    y = np.arange(h)[:, None] * np.ones((1, w))
    grain = wrap_noise(w, h, (32, 4), 111)
    tone = 0.75 + grain * 0.2
    tone = np.where(y < 3, 1.25, tone)
    tone = np.where(y > h - 12, 0.5 - (y - (h - 12)) / 12 * 0.35, tone)
    save_rgb(tone[..., None] * np.array([110, 70, 40], dtype="float32"), "Shelf")


def scroll_roll():
    """A rolled parchment scroll lying on its side: a lit cylinder with spiral ends. Ribbon drawn separately."""
    import numpy as np
    w, h = 256, 64
    y = (np.arange(h)[:, None] / (h - 1)) * np.ones((1, w))
    light = 0.55 + 0.6 * np.sin(np.clip(y, 0, 1) * np.pi) ** 0.6 - (y > 0.6) * (y - 0.6) * 0.6
    noise = wrap_noise(w, h, (64, 8), 121) * 0.08
    arr = (light + noise)[..., None] * np.array([214, 192, 148], dtype="float32")
    alpha = np.full((h, w), 255, dtype="uint8")
    img = Image.fromarray(np.dstack([np.clip(arr, 0, 255).astype("uint8"), alpha]), "RGBA")
    d = ImageDraw.Draw(img)
    for x0 in (0, w - 16):  # spiral ends
        d.ellipse([x0, 0, x0 + 16, h - 1], fill=(198, 172, 124, 255), outline=(120, 92, 56, 255), width=2)
        for r in (5, 10):
            d.arc([x0 + 8 - r * 0.4, h / 2 - r * 1.4, x0 + 8 + r * 0.4, h / 2 + r * 1.4], 0, 300, fill=(120, 92, 56, 255), width=1)
    img.save(OUT / "Scroll.png")


def ribbon(frayed):
    """A ribbon tied round a scroll with two tails. White; tinted with the treaty's colour in code."""
    w, h = 48, 96
    rnd = random.Random(131 if frayed else 130)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([14, 0, 34, 40], fill=WHITE)
    d.ellipse([8, 28, 40, 48], fill=(225, 225, 225, 255))  # the knot
    d.polygon([(18, 42), (24, 44), (14, 92), (4, 86)], fill=WHITE)
    d.polygon([(30, 42), (24, 44), (34, 92), (44, 86)], fill=WHITE)
    if frayed:
        for _ in range(40):
            x, y = rnd.randrange(w), rnd.randrange(50, h)
            d.line([(x, y), (x + rnd.randint(-3, 3), y + rnd.randint(3, 8))], fill=(0, 0, 0, 0), width=2)
        for _ in range(6):
            y = rnd.randrange(4, 38)
            d.line([(14, y), (14 + rnd.randint(3, 8), y + 2)], fill=(0, 0, 0, 0), width=2)
    img.save(OUT / ("RibbonFrayed.png" if frayed else "Ribbon.png"))


def folder():
    """A manila war dossier with a tab on top. In its own colour; a faction strip is drawn on it in code."""
    import numpy as np
    w, h = 256, 300
    tone = 0.9 + wrap_noise(w, h, 64, 141) * 0.08
    arr = tone[..., None] * np.array([214, 184, 124], dtype="float32")
    img = Image.fromarray(np.dstack([np.clip(arr, 0, 255).astype("uint8"), np.zeros((h, w), dtype="uint8")]), "RGBA")
    mask = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([0, 22, w - 1, h - 1], 6, fill=255)
    md.rounded_rectangle([12, 0, 110, 40], 6, fill=255)
    img.putalpha(mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 22, w - 1, h - 1], 6, outline=(140, 110, 60, 255), width=2)
    d.line([(14, 22), (108, 22)], fill=(214, 184, 124, 255), width=3)
    img.save(OUT / "Folder.png")


def glow():
    """A soft round glow, white, for lamp light. Tinted in code."""
    s = 256
    img = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    px = img.load()
    for y in range(s):
        for x in range(s):
            r = math.hypot(x - s / 2, y - s / 2) / (s / 2)
            px[x, y] = (255, 255, 255, int(255 * max(0.0, 1 - r) ** 2))
    img.save(OUT / "Glow.png")


def seat():
    """A council chair seen from above: a round seat with a curved back. White; tinted by the member's lean in code."""
    s = 64
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([10, 16, 54, 58], fill=WHITE)
    d.arc([4, 6, 60, 62], 200, 340, fill=(200, 200, 200, 255), width=9)
    d.ellipse([18, 26, 46, 50], fill=(235, 235, 235, 255))
    img.filter(ImageFilter.GaussianBlur(0.5)).save(OUT / "Seat.png")


def banner():
    """A hanging motion banner with a swallowtail and fold shading. White; tinted in code. The pole is drawn in code."""
    w, h = 128, 256
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(8, 0), (w - 8, 0), (w - 8, h - 4), (w / 2, h - 48), (8, h - 4)], fill=WHITE)
    for x in (36, 64, 92):  # folds
        d.line([(x, 4), (x, h - 60)], fill=(225, 225, 225, 255), width=6)
    d.line([(14, 10), (w - 14, 10)], fill=(200, 200, 200, 255), width=2)
    d.polygon([(8, 0), (w - 8, 0), (w - 8, h - 4), (w / 2, h - 48), (8, h - 4)], outline=(190, 190, 190, 255), width=2)
    img.filter(ImageFilter.GaussianBlur(0.4)).save(OUT / "Banner.png")


def rooms():
    wood()
    stone()
    concrete()
    cork()
    brass()
    knob()
    dial()
    ticker_tape()
    shelf()
    scroll_roll()
    ribbon(False)
    ribbon(True)
    folder()
    glow()
    seat()
    banner()


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    paper_sheet()
    masthead()
    banner_title()
    stamp("CONFIRMED", "Confirmed", 11)
    stamp("RUMOUR", "Rumor", 12)
    stamp("DISPROVED", "Disproved", 13)
    stamp("FRONT PAGE", "FrontPage", 14)
    stamp("CLASSIFIED", "Classified", 15)
    stamp("INVESTIGATE", "Investigate", 16)
    stamp("UNDER INQUIRY", "Inquiry", 17)
    stamp("VERIFIED", "Verified", 18)
    stamp("ADVISORY", "Advisory", 19)
    stamp("IN FORCE", "InForce", 20)
    stamp("QUEUED", "Queued", 21)
    round_stamp("Advisory", glyph_advisory, 22)
    round_stamp("Investigate", glyph_investigate, 23)
    stamp("RESPOND", "Respond", 24)
    round_stamp("Morale", glyph_morale, 25)
    round_stamp("Counter", glyph_counter, 26)
    round_stamp("Debunk", glyph_debunk, 27)
    round_stamp("Spread", glyph_spread, 28)
    round_stamp("Suppress", glyph_suppress, 29)
    rule()
    seal_small()
    pin()
    icons()
    statecraft()
    rooms()
    print("wrote UI art to", OUT)
