"""Procedural RimWorld-style texture generator for Medical Experimentation.
Top-down style, transparent background, muted palette to match vanilla.
Run: python tools/gen_textures.py
Extended per-milestone; each function writes one PNG under 1.6/Textures (or Textures/).
"""
import os
from PIL import Image, ImageDraw, ImageFilter

def add_outline(img, px=5, color=(0, 0, 0, 255)):
    """Add a bold solid outline (silhouette halo) around the opaque shape, RimWorld style."""
    alpha = img.split()[3]
    dil = alpha.filter(ImageFilter.MaxFilter(px * 2 + 1))
    mask = dil.point(lambda a: 255 if a > 12 else 0)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    black = Image.new("RGBA", img.size, color)
    out = Image.composite(black, out, mask)
    out.alpha_composite(img)
    return out

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "Textures")

def out(path):
    full = os.path.join(TEX, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    return full

def save(img, path):
    p = out(path)
    img.save(p)
    print("wrote", os.path.relpath(p, ROOT))

# ---- shared helpers -------------------------------------------------------

def rounded(draw, box, r, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)

# ---- buildings ------------------------------------------------------------

def experimentation_bench():
    W, H = 256, 128
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # steel frame (top-down bench)
    rounded(d, (8, 30, W-8, H-12), 6, (70, 73, 78, 255), outline=(40, 42, 46, 255), width=3)
    # work surface
    rounded(d, (16, 38, W-16, H-22), 4, (96, 100, 106, 255), outline=(55, 58, 62, 255), width=2)
    # shelf / backsplash with monitor
    rounded(d, (14, 12, W-14, 38), 3, (52, 55, 60, 255), outline=(35, 37, 40, 255), width=2)
    rounded(d, (24, 16, 70, 34), 2, (60, 120, 140, 255), outline=(30, 50, 60, 255), width=1)  # screen
    # glassware on the surface (vials / flasks) in muted clinical colors
    vials = [(96, (120, 180, 200)), (122, (170, 120, 190)), (148, (190, 170, 110)), (174, (150, 190, 150))]
    for x, col in vials:
        d.rectangle((x, 52, x+12, 92), fill=(210, 215, 220, 90), outline=(160, 165, 170, 160))
        d.rectangle((x+1, 70, x+11, 92), fill=col + (200,))
        d.rectangle((x+2, 48, x+10, 54), fill=(150, 155, 160, 200))  # stopper
    # a tray / mortar on the right
    d.ellipse((196, 60, 236, 88), fill=(120, 124, 130, 255), outline=(70, 72, 76, 255), width=2)
    d.ellipse((204, 64, 228, 82), fill=(80, 83, 88, 255))
    save(add_outline(img, px=5), os.path.join("Things", "Building", "ExperimentationBench.png"))

# ---- items ----------------------------------------------------------------

def compound_vial():
    """A vial. Kept near-white so RimWorld's per-def <color> tints the liquid."""
    W, H = 128, 128
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # cork
    d.rectangle((54, 16, 74, 30), fill=(150, 120, 90, 255), outline=(90, 70, 50, 255))
    # neck
    d.rectangle((58, 28, 70, 42), fill=(225, 230, 235, 255), outline=(170, 175, 180, 255))
    # body (rounded flask) - light so it tints
    d.rounded_rectangle((40, 40, 88, 104), radius=18, fill=(235, 240, 245, 255), outline=(160, 165, 172, 255), width=3)
    # liquid fill (the part that should read as the compound color; keep light)
    d.rounded_rectangle((46, 64, 82, 100), radius=12, fill=(245, 245, 245, 255))
    # highlight
    d.ellipse((50, 48, 60, 66), fill=(255, 255, 255, 180))
    save(img, os.path.join("Things", "Item", "Compound.png"))

def cmd_new_experiment():
    W, H = 64, 64
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # erlenmeyer flask
    d.polygon([(28, 12), (36, 12), (36, 28), (50, 52), (14, 52), (28, 28)], fill=(210, 220, 225, 255), outline=(60, 65, 70, 255))
    d.rectangle((26, 8, 38, 14), fill=(120, 125, 130, 255))
    d.line((20, 46, 44, 46), fill=(90, 160, 180, 255), width=6)  # liquid
    # plus
    d.rectangle((46, 30, 58, 36), fill=(120, 200, 140, 255))
    d.rectangle((49, 27, 55, 39), fill=(120, 200, 140, 255))
    save(img, os.path.join("UI", "Commands", "ME_NewExperiment.png"))

def chemical_dispersal():
    """1x1 top-down canister emitter with vents."""
    W, H = 128, 128
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((22, 22, 106, 106), fill=(70, 78, 70, 255), outline=(40, 46, 40, 255), width=4)
    d.ellipse((36, 36, 92, 92), fill=(96, 110, 90, 255), outline=(55, 62, 52, 255), width=3)
    # central nozzle
    d.ellipse((54, 54, 74, 74), fill=(60, 66, 58, 255), outline=(35, 40, 34, 255), width=2)
    # vent slots around
    for ang in range(0, 360, 45):
        import math
        rad = math.radians(ang)
        cx, cy = 64 + 30 * math.cos(rad), 64 + 30 * math.sin(rad)
        d.ellipse((cx - 4, cy - 4, cx + 4, cy + 4), fill=(120, 170, 100, 255))
    save(add_outline(img, px=5), os.path.join("Things", "Building", "ChemicalDispersal.png"))

if __name__ == "__main__":
    experimentation_bench()
    compound_vial()
    cmd_new_experiment()
    chemical_dispersal()
    print("done")
