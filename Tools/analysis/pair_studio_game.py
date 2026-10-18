"""Собрать пары «студия / игра» по одному роботу для глазной проверки."""

from __future__ import annotations

import os

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STUDIO = os.path.join(ROOT, "AssetSource", "Previews", "Robots", "tpose_row.png")
GAME = os.path.join(ROOT, "AssetSource", "Previews", "Diagnostics", "fs_sat30_srgb.png")
OUT = os.path.join(ROOT, "AssetSource", "Previews", "Diagnostics", "pair_studio_game.png")

# кропы игрового кадра (доли), по одному роботу
GAME_BOXES = [
    (0.09, 0.36, 0.24, 0.70),
    (0.25, 0.35, 0.39, 0.70),
    (0.44, 0.36, 0.56, 0.70),
    (0.59, 0.35, 0.72, 0.70),
    (0.75, 0.36, 0.89, 0.70),
]
TARGET_H = 300


def main() -> int:
    studio = Image.open(STUDIO).convert("RGB")
    game = Image.open(GAME).convert("RGB")
    sw, sh = studio.size
    gw, gh = game.size

    tiles = []
    for idx in range(5):
        s_panel = studio.crop((idx * sw // 5, 0, (idx + 1) * sw // 5, sh))
        # в панели робот по центру — берём средние 60 % ширины
        pw = s_panel.width
        s_panel = s_panel.crop((int(pw * 0.20), 0, int(pw * 0.80), sh))

        x0, y0, x1, y1 = GAME_BOXES[idx]
        g_crop = game.crop((int(x0 * gw), int(y0 * gh), int(x1 * gw), int(y1 * gh)))

        for im, name in ((s_panel, "studio"), (g_crop, "game")):
            k = TARGET_H / im.height
            im = im.resize((max(1, int(im.width * k)), TARGET_H), Image.LANCZOS)
            if name == "game":
                im = im.resize((im.width * 2, im.height * 2), Image.LANCZOS)
            tiles.append((name, im))

    pad = 8
    col_w = [max(t[1].width for t in tiles[i * 2:i * 2 + 2]) for i in range(5)]
    total_w = sum(col_w) + pad * 6
    out = Image.new("RGB", (total_w, TARGET_H * 2 + pad * 3), (24, 24, 24))
    x = pad
    for idx in range(5):
        s_img = tiles[idx * 2][1]
        g_img = tiles[idx * 2 + 1][1]
        out.paste(s_img, (x, pad))
        out.paste(g_img, (x, pad * 2 + TARGET_H))
        x += col_w[idx] + pad

    out.save(OUT)
    print("saved %s %s" % (OUT, out.size))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
