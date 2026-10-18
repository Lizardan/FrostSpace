"""Монтаж вариантов палитры на игровом кадре (кроп игрока, увеличение x4)."""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCES = [
    ("baseline", os.path.join(ROOT, "Temp", "gameview.png")),
    ("v0 V*1.45 S*1.30", os.path.join(ROOT, "Temp", "variant_0.png")),
    ("v1 V*1.75 S*1.55", os.path.join(ROOT, "Temp", "variant_1.png")),
    ("v2 +dark*2.4", os.path.join(ROOT, "Temp", "variant_2.png")),
]
OUT = os.path.join(ROOT, "AssetSource", "Previews", "Diagnostics", "palette_variants_gameplay.png")
BOX = (268, 118, 362, 216)
SCALE = 4


def main() -> int:
    tiles = []
    for label, path in SOURCES:
        if not os.path.exists(path):
            print("нет файла: %s" % path)
            return 1
        im = Image.open(path).convert("RGB").crop(BOX)
        im = im.resize((im.width * SCALE, im.height * SCALE), Image.NEAREST)
        tiles.append((label, im))

    pad = 10
    tw = tiles[0][1].width
    th = tiles[0][1].height
    out = Image.new("RGB", (tw * len(tiles) + pad * (len(tiles) + 1), th + 34 + pad * 2), (22, 22, 22))
    draw = ImageDraw.Draw(out)
    for i, (label, im) in enumerate(tiles):
        x = pad + i * (tw + pad)
        out.paste(im, (x, pad + 24))
        draw.text((x + 4, pad + 6), label, fill=(235, 235, 235))
    out.save(OUT)
    print("saved %s %s" % (OUT, out.size))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
