"""Сравнить цвет роботов: студийное превью Blender vs игровой кадр Unity.

Метрика по «цветным» пикселям (S > порога) в кропе каждого робота:
средняя насыщенность, средняя светлота, средняя яркость в sRGB.

Запуск (нужен venv с PIL/numpy):
    python Tools/analysis/compare_robot_color.py
"""

from __future__ import annotations

import colorsys
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STUDIO = os.path.join(ROOT, "AssetSource", "Previews", "Robots", "tpose_row.png")
GAME = os.path.join(ROOT, "AssetSource", "Previews", "Diagnostics", "fs_sat30_srgb.png")


def hsv_stats(rgb: np.ndarray, label: str) -> None:
    """rgb: (N,3) float 0..1 в sRGB."""
    if len(rgb) == 0:
        print("%-22s пусто" % label)
        return
    mx = rgb.max(axis=1)
    mn = rgb.min(axis=1)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    color = sat > 0.18
    sub = rgb[color]
    if len(sub) == 0:
        print("%-22s нет цветных пикселей" % label)
        return
    mx2 = sub.max(axis=1)
    mn2 = sub.min(axis=1)
    s2 = (mx2 - mn2) / np.maximum(mx2, 1e-6)
    print("%-22s n=%6d  S=%4.2f  V=%4.2f  lum=%4.2f  mean=(%3d,%3d,%3d)"
          % (label, len(sub), s2.mean(), mx2.mean(),
             (sub * np.array([0.2126, 0.7152, 0.0722])).sum(axis=1).mean(),
             int(sub[:, 0].mean() * 255), int(sub[:, 1].mean() * 255),
             int(sub[:, 2].mean() * 255)))


def crop_panels(img: Image.Image, count: int) -> list[Image.Image]:
    """Разрезать широкую картинку на count панелей, отбросив тёмные разделители."""
    w, h = img.size
    step = w / count
    out = []
    for i in range(count):
        left = int(i * step)
        right = int((i + 1) * step)
        out.append(img.crop((left, 0, right, h)))
    return out


def main() -> int:
    for path in (STUDIO, GAME):
        if not os.path.exists(path):
            print("нет файла: %s" % path)
            return 1

    studio = Image.open(STUDIO).convert("RGB")
    game = Image.open(GAME).convert("RGB")
    print("studio %s %s" % (studio.size, STUDIO))
    print("game   %s %s" % (game.size, GAME))
    print()

    print("== студия (5 панелей) ==")
    for idx, panel in enumerate(crop_panels(studio, 5), start=1):
        arr = np.asarray(panel).astype(np.float32) / 255.0
        flat = arr.reshape(-1, 3)
        # фон студии — серый градиент (S<0.1), его отсекает порог насыщенности
        hsv_stats(flat, "R%d studio" % idx)
    print()

    # игровой кадр: роботы стоят в ряд, кропы заданы в долях ширины/высоты
    boxes = [
        (0.00, 0.30, 0.30, 0.75),
        (0.24, 0.28, 0.45, 0.75),
        (0.44, 0.28, 0.63, 0.75),
        (0.60, 0.28, 0.83, 0.75),
        (0.79, 0.30, 1.00, 0.75),
    ]
    w, h = game.size
    print("== игра (5 кропов) ==")
    for idx, (x0, y0, x1, y1) in enumerate(boxes, start=1):
        arr = np.asarray(game.crop((int(x0 * w), int(y0 * h), int(x1 * w), int(y1 * h))))
        arr = arr.astype(np.float32) / 255.0
        flat = arr.reshape(-1, 3)
        # снег почти белый и нейтральный — отсекается порогом S
        hsv_stats(flat, "R%d game" % idx)
    return 0


if __name__ == "__main__":
    sys.exit(main())
