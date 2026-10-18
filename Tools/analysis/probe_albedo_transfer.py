"""Измерить, во сколько раз кадр умножает альбедо: студия vs игра.

Для каждого ключевого цвета палитры роботов ищем в кадре пиксели того же оттенка
(±hue) и сравниваем их медианную светлоту с исходным hex. Отношение V_кадр / V_hex
и есть фактический множитель освещения (1.0 = «альбедо как есть»).
"""

from __future__ import annotations

import colorsys
import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STUDIO = os.path.join(ROOT, "AssetSource", "Previews", "Robots", "tpose_row.png")
GAME = os.path.join(ROOT, "AssetSource", "Previews", "Diagnostics", "fs_sat30_srgb.png")

# (робот, ключ, hex) — из robot_designs.py / docs/STYLE.md
PROBES = [
    ("R1", "base", "#2A3B52"),
    ("R1", "panel", "#435A75"),
    ("R1", "orange", "#F08A2E"),
    ("R2", "base", "#3A2B24"),
    ("R2", "panel", "#6A4734"),
    ("R2", "rust", "#8C4A32"),
    ("R3", "base", "#1D3944"),
    ("R3", "panel", "#2D5A68"),
    ("R4", "panel", "#6B6545"),
    ("R5", "base", "#26382F"),
    ("R5", "panel", "#3E5849"),
]


def hex_rgb(value: str) -> tuple[float, float, float]:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def hsv_of(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    delta = mx - mn
    sat = np.where(mx > 1e-6, delta / np.maximum(mx, 1e-6), 0.0)

    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.zeros_like(mx)
    safe = delta > 1e-6
    idx_r = safe & (mx == r)
    idx_g = safe & (mx == g) & ~idx_r
    idx_b = safe & (mx == b) & ~idx_r & ~idx_g
    hue[idx_r] = ((g - b) / np.maximum(delta, 1e-6))[idx_r] % 6
    hue[idx_g] = ((b - r) / np.maximum(delta, 1e-6) + 2)[idx_g]
    hue[idx_b] = ((r - g) / np.maximum(delta, 1e-6) + 4)[idx_b]
    return hue / 6.0, sat, mx


def probe(image: np.ndarray, hex_value: str, hue_tol: float = 0.035,
          sat_tol: float = 0.25) -> tuple[int, float, float]:
    """Вернуть (n, медиана V, медиана S) пикселей нужного оттенка."""
    target = np.array(hex_rgb(hex_value), dtype=np.float32)
    th, ts, tv = hsv_of(target.reshape(1, 1, 3))
    th, ts, tv = float(th[0, 0]), float(ts[0, 0]), float(tv[0, 0])

    hue, sat, val = hsv_of(image)
    dh = np.abs(hue - th)
    dh = np.minimum(dh, 1.0 - dh)

    mask = (dh <= hue_tol) & (np.abs(sat - ts) <= sat_tol) & (val > 0.02) & (sat > 0.12)
    n = int(mask.sum())
    if n < 20:
        return n, float("nan"), float("nan")
    return n, float(np.median(val[mask])), float(np.median(sat[mask]))


def load(path: str) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0


def main() -> int:
    studio = load(STUDIO)
    game = load(GAME)

    print("%-6s %-7s %-9s | %-22s | %-22s" % ("robot", "key", "hex", "studio n / V / ratio", "game n / V / ratio"))
    print("-" * 78)
    for robot, key, hex_value in PROBES:
        _, _, tv = hsv_of(np.array(hex_rgb(hex_value), dtype=np.float32).reshape(1, 1, 3))
        base_v = float(tv[0, 0])
        ns, vs, _ = probe(studio, hex_value)
        ng, vg, _ = probe(game, hex_value)
        print("%-6s %-7s %-9s | %6d %5.2f %5.2f      | %6d %5.2f %5.2f"
              % (robot, key, hex_value, ns, vs, vs / base_v if base_v else float("nan"),
                 ng, vg, vg / base_v if base_v else float("nan")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
