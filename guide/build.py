"""Builds guide/KiraDoki-使用介绍.html: one file a friend can double-click, no internet needed.

template.html + the island's icon font (src/IsleBar.App/Assets/Fonts/IsleIcons.ttf) and the mascot (Assets/mascot.png,
shrunk to 336 px) inlined as base64. Run after changing either:  python guide/build.py   (needs Pillow)
"""
import base64, io, os
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
assets = os.path.join(here, "..", "src", "IsleBar.App", "Assets")

with open(os.path.join(assets, "Fonts", "IsleIcons.ttf"), "rb") as f:
    font = base64.b64encode(f.read()).decode()

im = Image.open(os.path.join(assets, "mascot.png")).convert("RGBA")
im = im.crop(im.getbbox())
side = max(im.size)
square = Image.new("RGBA", (side, side))
square.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
buf = io.BytesIO()
square.resize((336, 336), Image.LANCZOS).save(buf, "PNG", optimize=True)
mascot = base64.b64encode(buf.getvalue()).decode()

with open(os.path.join(here, "template.html"), encoding="utf-8") as f:
    page = f.read().replace("{{FONT}}", font).replace("{{MASCOT}}", mascot)

out = os.path.join(here, "KiraDoki-使用介绍.html")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write(page)
print(out, len(page) // 1024, "KB")
