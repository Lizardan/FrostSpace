"""Поселение роботов в клетке (0,-2): изба с печью, забор, сосны.

Зачем это в чанке, а не в сцене. Депо уже показывает, как надо: узел мира —
это часть клетки, а не набор префабов, раскиданных руками (`depot_parts`).
Поселение собрано так же: меши живут в локальных осях клетки, отметки берутся
от пола двора узла, и весь узел выходит одним `WorldCell_0_-2.fbx` вместе с
землёй, дорогой и коллайдерами.

Дом не переизобретён: тот же `level_parts.build_cabin`, который собирал
`SnowCabin` для прежнего макета уровня `Game`, — просто переведён в мир.
Антураж (забор, сосны) берётся отт же, из `level_parts`.

Запуск (штатно — из пайплайна, отдельного шага нет):

    python Tools/Blender/build_all.py --asset cells --check

Координаты — локальные оси клетки: X на восток, Y в Blender = −z мира, вверх Z.
"""

from __future__ import annotations

import math

import level_lib
import level_parts as LP
import world_layout as W

#: Клетка поселения и её центр в мире.
CELL = W.cell_of(*W.SETTLEMENT)

#: Дом: северо-восточнее перекрёстка ног дороги. Порог смотрит на юг (Blender
#: +Y), то есть на восточную ногу дороги, и стоит за её откосом: до оси ноги
#: 40 м при полуширине выемки 26 м, до оси северной ноги 34 м.
HOUSE = (34.0, 40.0)

#: Забор двора: прямоугольник в локальных осях клетки и проём с южной стороны.
YARD = (16.0, 62.0, 20.0, 66.0)
GAP = (30.0, 40.0)          # проём по южной линии забора, по X

#: Сосны: вне обеих ног дороги и вне забора, но в поле двора.
PINES = ((-44.0, 46.0, 0.4), (-38.0, -44.0, 2.1), (58.0, 12.0, 3.8))

#: Габарит забора: столб, две лажи, высота.
POST = (0.12, 0.12, 1.15)
RAIL_H = (0.06, 0.06)
RAIL_Z = (0.55, 0.98)

#: На сколько усадить основание в снег, чтобы под плитой не было щели.
SINK = 0.08


def _place(lx: float, lz: float):
    """Локальная точка клетки -> оси Blender для этой же клетки."""
    return (lx, -lz)


def _ground(lx: float, lz: float) -> float:
    """Отметка пола двора поселения под локальной точкой."""
    return W.world_height(W.SETTLEMENT[0] + lx, W.SETTLEMENT[1] + lz)


def _move(obj, dx: float, dy: float, dz: float) -> None:
    """Перенести меш объекта на месте: объекты дома собраны вокруг нуля."""
    for v in obj.data.vertices:
        v.co.x += dx
        v.co.y += dy
        v.co.z += dz


def build_house(materials):
    """Изба: готовые секции `build_cabin`, переведённые в клетку поселения."""
    objs = LP.build_cabin(materials)
    (px, py) = _place(HOUSE[0], HOUSE[1])
    z = _ground(HOUSE[0], HOUSE[1]) - SINK
    for obj in objs:
        _move(obj, px, py, z)
        obj.name = "Hut_" + obj.name.split("_")[-1]
    return objs


def _fence(materials):
    """Забор двора с проёмом на дорогу: столбы и две лажи по каждому пролёту."""
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = YARD

    def run(a, c):
        """Пролёт от точки `a` до точки `c`: столбы на концах, лажи между."""
        (ax, az) = a
        (cx, cz) = c
        length = math.hypot(cx - ax, cz - az)
        n = max(1, int(round(length / 3.0)))
        rot = math.degrees(math.atan2(-(cx - ax), -(cz - az)))
        for k in range(n + 1):
            t = k / n
            (lx, lz) = (ax + (cx - ax) * t, az + (cz - az) * t)
            top = _ground(lx, lz) + POST[2]
            _post(b, lx, lz, top, rot)
        for z_off in RAIL_Z:
            for k in range(n):
                t0 = k / n
                t1 = (k + 1) / n
                mx = ax + (cx - ax) * (t0 + t1) * 0.5
                mz = az + (cz - az) * (t0 + t1) * 0.5
                seg = length / n
                (px, py) = _place(mx, mz)
                base = _ground(mx, mz)
                b.box_rot((px, py, base + z_off),
                          (RAIL_H[0], seg, RAIL_H[1]), (0.0, 0.0, rot),
                          "Level_WoodDark", bevel=0.01)

    def _post(bb, lx, lz, top, rot):
        (px, py) = _place(lx, lz)
        base = _ground(lx, lz) - 0.25
        bb.box_rot((px, py, (base + top) * 0.5),
                   (POST[0], POST[1], top - base), (0.0, 0.0, rot),
                   "Level_WoodDark", bevel=0.02)

    # Южная линия — с проёмом: два отрезка до и после GAP.
    run((x0, z0), (GAP[0], z0))
    run((GAP[1], z0), (x1, z0))
    run((x0, z1), (x1, z1))
    run((x0, z0), (x0, z1))
    run((x1, z0), (x1, z1))
    return b.build("Hut_Fence", recalc=False)


def build_cell_settlement(materials, i: int, j: int):
    """Объекты поселения клетки: пустой список, если клетка не поселения."""
    if (i, j) != CELL:
        return []
    objs = build_house(materials) + [_fence(materials)]
    for (n, (lx, lz, seed)) in enumerate(PINES):
        tree = LP.build_pine(materials, seed, name="Hut_Pine_%d" % n)
        (px, py) = _place(lx, lz)
        _move(tree, px, py, _ground(lx, lz) - 0.15)
        objs.append(tree)
    return objs
