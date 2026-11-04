"""Превью раскладки мира: план и поле высот в SVG, без зависимостей.

Зачем это нужно, если числа уже проверены: `check_world.py` доказывает, что
раскладка не противоречит себе, но не показывает, читается ли она как место.
По SVG видно глазами то, чего провалом инварианта не поймаешь: узлы на границах
клеток, тёплый каркас полосой вместо площадок, то, что магистраль идёт своей
нитью и тепла не даёт, и насколько пусто между узлами.

Blender не нужен: `runner` = python, выход — два файла в `AssetSource/Previews/
World/` (вне git, как и остальные превью — docs/BLENDER.md).

Запуск: `python Tools/Blender/render_world_previews.py --out AssetSource/Previews/World`
"""

from __future__ import annotations

import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import world_layout as W  # noqa: E402

#: Размер холста в пикселях и поля вокруг: подписи узлов лезут за край карты.
SIZE = 1320
MARGIN = 46

BG = "#0a1118"
GRID = "#1d2c39"
CELL_FILL = "#12202b"
WARM = "#33566b"
COLD = "#233646"
WARM_RECT = "#4a7f9b"
RAIL_C = "#8fb6cc"
ROAD_C = "#c9d8e6"
PATH_C = "#40566b"
ANCHOR_C = "#6fe3ff"
TEXT = "#dbe8f3"
DIM = "#7f98a6"
HOT = "#ffc978"

#: Поле высот: от самого низкого к самому высокому, пять ступеней. Легенды тут
#: нет намеренно — рисунок обязан читаться сам, иначе он ничего не показывает.
HEIGHT_RAMP = ("#123046", "#1c4a63", "#2c6d84", "#5f9db0", "#cfe2ee")


def _xy(x: float, z: float, k: float) -> tuple[float, float]:
    """Мировая точка -> пиксели: север наверху, как в игре сверху вниз."""
    return (MARGIN + (x - W.BB[0]) * k, MARGIN + (W.BB[3] - z) * k)


def _line(pts, k: float) -> str:
    return " ".join("%.1f,%.1f" % _xy(x, z, k) for (x, z) in pts)


def plan_svg() -> str:
    k = (SIZE - 2 * MARGIN) / (W.BB[1] - W.BB[0])
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" '
           'viewBox="0 0 %d %d" font-family="Segoe UI, sans-serif">' % (
               SIZE, SIZE, SIZE, SIZE)]
    out.append('<rect width="%d" height="%d" fill="%s"/>' % (SIZE, SIZE, BG))

    # Клетки чанков: заполнены только те, где есть контент.
    for c in W.cells():
        (x0, x1, z0, z1) = W.cell_bounds(c["i"], c["j"])
        (px0, py0) = _xy(x0, z1, k)
        (px1, py1) = _xy(x1, z0, k)
        if c["content"]:
            out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s"/>'
                       % (px0, py0, px1 - px0, py1 - py0, CELL_FILL))
    for i in range(W.CELL_MIN - 1, W.CELL_MAX + 2):
        (px, py) = _xy(i * W.BLK, W.BB[3], k)
        out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" '
                   'stroke-width="0.6"/>' % (px, py, px, SIZE - MARGIN, GRID))
        (px, py) = _xy(W.BB[0], i * W.BLK, k)
        out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" '
                   'stroke-width="0.6"/>' % (px, py, SIZE - MARGIN, py, GRID))

    # Тёплый каркас: коридоры линиями, площадки — прямоугольниками.
    def corridor(pts, half, color):
        out.append('<polyline points="%s" fill="none" stroke="%s" stroke-opacity="0.55" '
                   'stroke-width="%.1f" stroke-linejoin="round"/>'
                   % (_line(pts, k), color, 2 * half * k))

    # Магистраль — холодная: она режет плиту коридором, но тепла не даёт, и
    # поэтому её полоса рисуется отдельно от тёплых дорог.
    corridor(W.RAIL, W.RAIL_CUT[1], COLD)
    for pts in W.ROADS:
        corridor(pts, W.WARM_ROAD_HALF, WARM)
    for n in W.NODES:
        if not n["rect"]:
            continue
        (x0, x1, z0, z1) = n["rect"]
        (px0, py0) = _xy(x0, z1, k)
        (px1, py1) = _xy(x1, z0, k)
        out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s" '
                   'fill-opacity="0.5" stroke="%s" stroke-width="1"/>'
                   % (px0, py0, px1 - px0, py1 - py0, WARM_RECT, WARM_RECT))

    # Тропы — холодные, пунктиром: они ведут к закрытому.
    for pts in W.PATHS:
        out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="1.6" '
                   'stroke-dasharray="5 7"/>' % (_line(pts, k), PATH_C))

    # Сами линии поверх тепла, чтобы читался маршрут.
    out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="2.2" '
               'stroke-dasharray="11 5"/>' % (_line(W.RAIL, k), RAIL_C))
    for pts in W.ROADS:
        out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="1.6"/>'
                   % (_line(pts, k), ROAD_C))

    # Дальность: 99 % тумана и far clip вокруг стартовой точки.
    (dx, dz) = W.DEPOT
    (px, py) = _xy(dx, dz, k)
    for (r, col, dash) in ((W.fog_distance(0.01), "#5f9db0", "2 5"),
                           (W.FAR_CLIP, "#8fb6cc", "6 4")):
        out.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="none" stroke="%s" '
                   'stroke-width="1" stroke-dasharray="%s"/>' % (px, py, r * k, col, dash))

    # Якоря: квадрат по габариту, подпись с высотой.
    for a in W.ANCHORS:
        (px, py) = _xy(a["x"], a["z"], k)
        half = a["w"] * 0.5 * k
        out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="none" '
                   'stroke="%s" stroke-width="%.1f"/>'
                   % (px - half, py - half, 2 * half, 2 * half, ANCHOR_C,
                      2.4 if a["kind"] in ("main", "lean") else 1.2))
        out.append('<text x="%.1f" y="%.1f" fill="%s" font-size="12">%s · %.0f м</text>'
                   % (px + half + 4, py + 4, ANCHOR_C, a["n"], a["h"]))

    # Пролёты коммуникаций между якорями.
    for ((ax, ay, az), (bx, by, bz)) in W.span_points():
        (p1, q1) = _xy(ax, az, k)
        (p2, q2) = _xy(bx, bz, k)
        out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" '
                   'stroke-width="1" stroke-opacity="0.7" stroke-dasharray="9 6"/>'
                   % (p1, q1, p2, q2, ANCHOR_C))

    # Руины — штриховой ромб: это не якорь и не узел, а обломок на 15-60 м.
    for r in W.RUINS:
        (px, py) = _xy(r["x"], r["z"], k)
        s = 8.0 if r["kind"] == "reach" else 5.5
        out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="none" '
                   'stroke="%s" stroke-width="1.2" stroke-dasharray="3 2" '
                   'transform="rotate(45 %.1f %.1f)"/>'
                   % (px - s / 2, py - s / 2, s, s, DIM, px, py))
        out.append('<text x="%.1f" y="%.1f" fill="%s" font-size="10">%s · %.0f м</text>'
                   % (px + 7, py + 12, DIM, r["n"], r["h"]))

    for n in W.NODES:
        (px, py) = _xy(n["x"], n["z"], k)
        col = HOT if n["warm"] else DIM
        out.append('<circle cx="%.1f" cy="%.1f" r="3.4" fill="%s"/>' % (px, py, col))
        out.append('<text x="%.1f" y="%.1f" fill="%s" font-size="11">%s %s</text>'
                   % (px + 6, py - 5, col, n["n"],
                      "(%+d,%+d)" % n["cell"]))

    # Линейка и заголовок.
    (sx, sy) = _xy(W.BB[0] + 200, W.BB[2] + 220, k)
    out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" '
               'stroke-width="2"/>' % (sx, sy, sx + W.BLK * k, sy, TEXT))
    out.append('<text x="%.1f" y="%.1f" fill="%s" font-size="12">%.0f м = клетка '
               'чанка</text>' % (sx, sy - 8, TEXT, W.BLK))
    out.append('<text x="%d" y="%d" fill="%s" font-size="15">Мир: %d x %d м · '
               'клеток %d · с контентом %d · якорей %d · узлов %d</text>'
               % (MARGIN, 26, TEXT, W.BB[1] - W.BB[0], W.BB[3] - W.BB[2],
                  (W.CELL_MAX - W.CELL_MIN + 1) ** 2, len(W.content_cells()),
                  len(W.ANCHORS), len(W.NODES)))
    out.append('<text x="%d" y="%d" fill="%s" font-size="12">жёлтый — тёплый узел, '
               'серый — холодный; ромбом — руины 15-60 м; пунктир у депо — вал '
               '140/260 м, круги — 99%% тумана и far clip; голубое — якоря</text>'
               % (MARGIN, SIZE - 18, DIM))
    out.append('</svg>')
    return "\n".join(out)


def heights_svg() -> str:
    """Поле высот сеткой 100 м: чаша, плита, разлом и юбки площадок."""
    step = 100.0
    n = int((W.BB[1] - W.BB[0]) / step) + 1
    k = (SIZE - 2 * MARGIN) / (W.BB[1] - W.BB[0])
    vals = [[W.world_height(W.BB[0] + i * step, W.BB[2] + j * step)
             for j in range(n)] for i in range(n)]
    lo = min(min(r) for r in vals)
    hi = max(max(r) for r in vals)
    span = (hi - lo) or 1.0

    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" '
           'viewBox="0 0 %d %d" font-family="Segoe UI, sans-serif">' % (
               SIZE, SIZE, SIZE, SIZE)]
    out.append('<rect width="%d" height="%d" fill="%s"/>' % (SIZE, SIZE, BG))
    cw = step * k * 1.05          # нахлёст: между клетками не должно быть швов
    for i in range(n):
        for j in range(n):
            t = (vals[i][j] - lo) / span
            col = HEIGHT_RAMP[min(len(HEIGHT_RAMP) - 1, int(t * len(HEIGHT_RAMP)))]
            (px, py) = _xy(W.BB[0] + i * step, W.BB[2] + (j + 1) * step, k)
            out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s"/>'
                       % (px, py, cw, cw, col))

    # Поверх — те же линии, чтобы читать, где узел стоит на склоне.
    for n_ in W.NODES:
        (px, py) = _xy(n_["x"], n_["z"], k)
        col = HOT if n_["warm"] else DIM
        out.append('<circle cx="%.1f" cy="%.1f" r="3" fill="%s"/>' % (px, py, col))
    out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="1.4" '
               'stroke-opacity="0.8"/>' % (_line(W.RAIL, k), TEXT))
    for pts in W.ROADS:
        out.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="1.1" '
                   'stroke-opacity="0.7"/>' % (_line(pts, k), TEXT))

    out.append('<text x="%d" y="%d" fill="%s" font-size="15">Поле высот: от %.0f до '
               '%+.0f м · шаг пробы %.0f м</text>'
               % (MARGIN, 26, TEXT, lo, hi, step))
    out.append('<text x="%d" y="%d" fill="%s" font-size="12">тёмное — низина '
               '(разлом на востоке), светлое — плита; у каждого тёплого узла видна '
               'площадка: срез плиты и её юбка</text>' % (MARGIN, SIZE - 18, DIM))
    out.append('</svg>')
    return "\n".join(out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", required=True, help="каталог для SVG")
    args = parser.parse_args()

    W.configure_stdout()
    os.makedirs(args.out, exist_ok=True)
    for (name, text) in (("world_plan.svg", plan_svg()),
                         ("world_heights.svg", heights_svg())):
        path = os.path.join(args.out, name)
        with open(path, "w", encoding="utf-8") as stream:
            stream.write(text)
        print("написано %s (%d КБ)" % (path, os.path.getsize(path) // 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())
