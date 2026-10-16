"""Generates About/Preview.png and About/ModIcon.png in Dominion's paper-and-stamp style.

Run from the repo root:  python3 tools/textures/make_about.py
Needs Pillow. Fonts: DejaVu / Liberation (common on Linux); falls back to Pillow's default font.
"""
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
PAPER = (226, 214, 186)
INK = (46, 38, 30)
SEAL = (150, 28, 30)


def font(names, size):
    for n in names:
        for d in ("/usr/share/fonts/truetype/liberation", "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/truetype/freefont"):
            p = Path(d) / n
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


SERIF_BOLD = ["LiberationSerif-Bold.ttf", "DejaVuSerif-Bold.ttf", "FreeSerifBold.ttf"]
SERIF = ["LiberationSerif-Regular.ttf", "DejaVuSerif.ttf", "FreeSerif.ttf"]
MONO = ["LiberationMono-Regular.ttf", "DejaVuSansMono.ttf"]


def paper(w, h, seed=7):
    """Aged paper: base colour, fibre noise, darker edges."""
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), PAPER)
    px = img.load()
    for y in range(h):
        for x in range(w):
            n = rnd.randint(-9, 9)
            dx = min(x, w - 1 - x) / (w * 0.5)
            dy = min(y, h - 1 - y) / (h * 0.5)
            edge = (1 - min(dx, dy)) ** 3 * 38
            r, g, b = PAPER
            px[x, y] = (int(r + n - edge), int(g + n - edge * 1.05), int(b + n - edge * 1.2))
    return img.filter(ImageFilter.GaussianBlur(0.6))


def seal(size, text="DOMINION"):
    """Round ink stamp with ring text and a star, slightly faded."""
    s = size * 2
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = s / 2
    col = SEAL + (225,)
    d.ellipse([s * 0.04, s * 0.04, s * 0.96, s * 0.96], outline=col, width=int(s * 0.035))
    d.ellipse([s * 0.17, s * 0.17, s * 0.83, s * 0.83], outline=col, width=int(s * 0.018))
    # star
    pts = []
    for i in range(10):
        r = s * (0.22 if i % 2 == 0 else 0.09)
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((c + r * math.cos(a), c + r * math.sin(a)))
    d.polygon(pts, fill=col)
    # ring text
    f = font(SERIF_BOLD, int(s * 0.085))
    label = f" {text} · RIM COUNCIL · "
    radius = s * 0.395
    step = 2 * math.pi / len(label)
    for i, ch in enumerate(label):
        a = -math.pi / 2 + i * step
        glyph = Image.new("RGBA", (int(s * 0.12), int(s * 0.12)), (0, 0, 0, 0))
        ImageDraw.Draw(glyph).text((glyph.width / 2, glyph.height / 2), ch, font=f, fill=col, anchor="mm")
        glyph = glyph.rotate(-math.degrees(a) - 90, resample=Image.BICUBIC)
        img.alpha_composite(glyph, (int(c + radius * math.cos(a) - glyph.width / 2), int(c + radius * math.sin(a) - glyph.height / 2)))
    # fade: knock out random specks so it reads as a rubber stamp
    rnd = random.Random(3)
    a = img.getchannel("A").load()
    for _ in range(s * s // 9):
        x, y = rnd.randrange(s), rnd.randrange(s)
        a[x, y] = int(a[x, y] * rnd.uniform(0.2, 0.8))
    return img.resize((size, size), Image.LANCZOS)


def preview():
    w, h = 1280, 720
    img = paper(w, h).convert("RGBA")
    d = ImageDraw.Draw(img)
    # ruled header like an official form
    d.line([(80, 120), (w - 80, 120)], fill=INK, width=2)
    d.line([(80, 126), (w - 80, 126)], fill=INK, width=1)
    d.text((80, 70), "FILE No. D-001  ·  CLASSIFICATION: PUBLIC", font=font(MONO, 26), fill=INK)
    d.text((80, 170), "DOMINION", font=font(SERIF_BOLD, 168), fill=INK)
    d.text((86, 365), "Diplomacy · Espionage · War · The Rim Council", font=font(SERIF, 40), fill=INK)
    lines = [
        "Every faction a state. Every event a headline.",
        "Summits, decrees, coups, embargoes, warbands, vassals.",
        "Unite the rim, by treaty, by pressure or by force.",
    ]
    f = font(MONO, 26)
    for i, t in enumerate(lines):
        d.text((86, 470 + i * 46), t, font=f, fill=INK)
    d.line([(80, h - 90), (w - 80, h - 90)], fill=INK, width=1)
    d.text((80, h - 75), "RimWorld 1.7", font=font(MONO, 24), fill=INK)
    st = seal(250).rotate(-12, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(st, (w - st.width - 40, h - st.height - 100))
    img.convert("RGB").save(ROOT / "About" / "Preview.png", optimize=True)


def icon():
    s = 256
    img = paper(s, s, seed=11).convert("RGBA")
    st = seal(232)
    img.alpha_composite(st, ((s - st.width) // 2, (s - st.height) // 2))
    img.convert("RGB").save(ROOT / "About" / "ModIcon.png", optimize=True)


if __name__ == "__main__":
    preview()
    icon()
    print("wrote About/Preview.png and About/ModIcon.png")
