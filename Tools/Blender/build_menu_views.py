"""Собирает крупные декорации трёх новых видов главного меню.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_menu_views.py

Выход в Content/Models/Level/:
    MenuStationMast.fbx  — мачта полярной станции 17 м: ферма, площадка,
                           лестница, растяжки; отдельные объекты `MastDish`
                           (тарелка радара, pivot в ступице) и `MastBeacon`
                           (маячок, pivot в лампе);
    MenuIceWall.fbx      — ледяная стена каньона 12 × 14 м с полками снега;
    MenuCanyonBridge.fbx — висячий мост 9.2 м: доски с провисом, стойки,
                           тросы и подвесы;
    MenuDepotGate.fbx    — фасад ворот депо 12 × 8.4 м: пилоны, рёбра
                           створок, козырёк, тёплая щель и рельсы.

Палитра — `Level_*`, поэтому импорт подхватывает те же материалы, что и
остальной уровень (`LevelAssetPostprocessor`). Статика выгружается только
через `export_static`: он зеркалит X и выворачивает грани (ADR 0007).

Ориентация: фасад ворот смотрит в Blender −Y, то есть в Unity +Z —
на камеру вида. У мачты ось симметрии совпадает с X = 0, поэтому зеркало не
сдвигает pivot тарелки.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import level_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))


def _slab_xz(builder, points, thickness, mat):
    """Экструдированный силуэт в плоскости XZ, толщина по Y."""
    half = thickness * 0.5
    n = len(points)
    verts = [Vector((x, -half, z)) for x, z in points]
    verts += [Vector((x, half, z)) for x, z in points]
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    builder.add(verts, faces, mat)


# ------------------------------------------------------------------ мачта


def _mast_half(width, z):
    """Полуширина фермы на высоте z: от 1.1 м у земли до 0.5 м наверху."""
    t = min(max((z - 0.4) / 15.2, 0.0), 1.0)
    return width * (1.0 - 0.55 * t)


def build_mast(materials):
    b = level_lib.LevelBuilder(materials)

    # Фундамент: плита и четыре башмака под ноги фермы.
    b.box((0.0, 0.0, 0.2), (5.4, 5.4, 0.4), "Level_Stone", bevel=0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 1.1, sy * 1.1, 0.55), (0.62, 0.62, 0.34),
                  "Level_Metal", bevel=0.02)

    # Ноги фермы: четыре трубы с наклоном внутрь.
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.tube([(sx * 1.1, sy * 1.1, 0.4), (sx * 0.5, sy * 0.5, 15.6)],
                   [(0.19, 0.19), (0.13, 0.13)], "Level_Metal", n=5)

    # Обвязка: квадратные пояса фермы на четырёх высотах.
    for z in (3.5, 7.0, 10.5, 13.5):
        half = _mast_half(1.1, z)
        corners = [(-half, -half), (half, -half), (half, half), (-half, half)]
        for i in range(4):
            x0, y0 = corners[i]
            x1, y1 = corners[(i + 1) % 4]
            b.tube([(x0, y0, z), (x1, y1, z)],
                   [(0.11, 0.11)] * 2, "Level_Metal", n=4,
                   caps=(False, False))
        # Раскос из пояса вверх к следующему — читается как решётка.
        if z < 13.0:
            z1 = z + 3.0
            half1 = _mast_half(1.1, z1)
            b.tube([(-half, -half, z), (half1, -half1, z1)],
                   [(0.09, 0.09)] * 2, "Level_Rust", n=4, caps=(False, False))

    # Площадка обслуживания с ограждением и снегом.
    b.box((0.0, 0.0, 10.72), (3.8, 3.8, 0.24), "Level_Metal", bevel=0.02)
    b.box((0.0, 0.0, 10.92), (3.3, 3.3, 0.14), "Level_Snow", bevel=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 1.75, sy * 1.75, 11.45), (0.16, 0.16, 1.2),
                  "Level_Metal", bevel=0.015)
    for axis in (0, 1):
        for sign in (-1, 1):
            a = (sign * 1.75, -1.75) if axis == 0 else (-1.75, sign * 1.75)
            c = (sign * 1.75, 1.75) if axis == 0 else (1.75, sign * 1.75)
            b.tube([(a[0], a[1], 12.05), (c[0], c[1], 12.05)],
                   [(0.08, 0.08)] * 2, "Level_Metal", n=4, caps=(False, False))

    # Прожектор на площадке: корпус и тёплая линза, повёрнутая к земле.
    b.box((1.55, 0.0, 11.35), (0.9, 1.5, 0.9), "Level_Metal", bevel=0.03)
    b.box((1.55, -0.78, 11.35), (0.78, 0.12, 0.78), "Level_Window", bevel=0.02)
    b.box((1.55, 0.0, 10.95), (0.4, 0.4, 0.36), "Level_Rust", bevel=0.02)

    # Лестница по передней грани: два поручня и ступени.
    for sx in (-1, 1):
        b.tube([(sx * 0.34, -1.28, 0.4), (sx * 0.34, -0.78, 10.6)],
               [(0.09, 0.09)] * 2, "Level_Rust", n=4, caps=(False, False))
    for i in range(12):
        z = 1.1 + i * 0.8
        y = -1.28 + (z - 0.4) * 0.05 * 5.0 * 0.1
        b.box((0.0, y - 0.1, z), (0.74, 0.1, 0.09), "Level_Metal", bevel=0.01)

    # Антенна и траверса над площадкой.
    b.tube([(0.0, 0.0, 15.6), (0.0, 0.0, 16.9)],
           [(0.11, 0.11), (0.07, 0.07)], "Level_Metal", n=5)
    b.tube([(-0.9, 0.0, 15.3), (0.9, 0.0, 15.3)],
           [(0.07, 0.07)] * 2, "Level_Rust", n=4, caps=(False, False))
    b.box((0.0, 0.0, 15.95), (0.5, 0.5, 0.3), "Level_Metal", bevel=0.02)

    # Барабан с кабелем у подножия и занос — обжитая деталь у мачты.
    b.tube([(2.9, -1.2, 0.42), (2.9, -1.2, 1.42)],
           [(0.62, 0.62)] * 2, "Level_Rust", n=10)
    b.box((2.9, -1.2, 1.48), (1.0, 1.0, 0.12), "Level_Metal", bevel=0.02)
    b.ico((-2.6, 1.4, 0.1), 1.7, "Level_Snow", subdivisions=2,
          scale=(1.25, 0.95, 0.28), jitter=0.14, seeds=2.3)

    return b.build("MenuStationMast")


def build_mast_dish(materials):
    """Тарелка радара: pivot в ступице, ось вращения — вертикаль (Blender Z)."""
    b = level_lib.LevelBuilder(materials)

    # Чаша: замкнутый фасетный конус с приподнятым бортом.
    b.tube([(0.0, 0.0, 0.0), (0.0, -0.35, 0.0), (0.0, -0.78, 0.0),
            (0.0, -0.62, 0.0)],
           [(0.30, 0.30), (1.05, 1.05), (1.90, 1.90), (1.90, 1.90)],
           "Level_Metal", n=12, caps=(True, True), fan_caps=True)
    # Обод и ступица.
    b.tube([(0.0, -0.52, 0.0), (0.0, -0.12, 0.0)],
           [(0.34, 0.34)] * 2, "Level_Rust", n=8, caps=(False, False))
    b.tube([(0.0, 0.05, 0.55), (0.0, 0.05, -0.05)],
           [(0.3, 0.3)] * 2, "Level_Metal", n=8)
    # Облучатель на трёх растяжках перед чашей.
    b.tube([(0.0, -0.25, 0.0), (0.0, -1.95, 0.0)],
           [(0.09, 0.09)] * 2, "Level_Metal", n=4)
    b.box((0.0, -2.05, 0.0), (0.34, 0.4, 0.34), "Level_Rust", bevel=0.02)
    for sx, sy in ((1, 1), (-1, 1), (0, -1)):
        b.tube([(sx * 1.7, -0.7, sy * 1.7 if sy > 0 else 0.0),
                (0.0, -1.9, 0.0)],
               [(0.06, 0.06)] * 2, "Level_Rust", n=3, caps=(False, False))

    obj = b.build("MastDish")
    obj.location = (0.0, 0.0, 14.9)
    return obj


def build_mast_beacon(materials):
    """Маячок на вершине: отдельный объект, чтобы менять материал на Menu_TechGlow."""
    b = level_lib.LevelBuilder(materials)
    b.tube([(0.0, 0.0, -0.28), (0.0, 0.0, -0.14)],
           [(0.3, 0.3)] * 2, "Level_Metal", n=8)
    b.box((0.0, 0.0, 0.0), (0.4, 0.4, 0.34), "Level_Window", bevel=0.03)
    b.box((0.0, 0.0, 0.22), (0.24, 0.24, 0.1), "Level_Metal", bevel=0.02)

    obj = b.build("MastBeacon")
    obj.location = (0.0, 0.0, 17.12)
    return obj


# ------------------------------------------------------------- ледяная стена


def build_ice_wall(materials):
    b = level_lib.LevelBuilder(materials)

    outline = [
        (-6.0, 0.0), (6.0, 0.0), (5.9, 3.4), (4.6, 5.2), (5.2, 8.6),
        (3.4, 12.8), (1.8, 10.2), (0.4, 13.4), (-1.4, 10.0), (-3.2, 12.2),
        (-4.4, 8.4), (-5.6, 5.6), (-5.9, 2.2),
    ]
    _slab_xz(b, outline, 2.6, "Level_SnowDeep")

    # Передняя плоскость: крупные ледяные пласты, чтобы стена не читалась
    # одной плитой и давала фасетные уступы.
    plates = [
        ((-2.4, 0.6, 2.6), (3.2, 1.1, 4.0)),
        ((2.2, 0.4, 5.0), (2.6, 1.0, 3.4)),
        ((-1.0, 0.5, 8.4), (3.0, 1.1, 3.2)),
        ((3.0, 0.6, 10.6), (1.8, 1.0, 2.4)),
    ]
    for center, size in plates:
        b.box(center, size, "Level_Snow", bevel=0.05)

    # Полки снега на уступах — горизонталь всегда под шапкой.
    ledges = [
        ((-3.6, 0.4, 3.45), (3.0, 2.0, 0.2)),
        ((2.4, 0.3, 6.9), (2.8, 1.9, 0.18)),
        ((-1.2, 0.3, 10.0), (2.4, 1.9, 0.18)),
        ((0.6, 0.2, 13.3), (2.0, 1.8, 0.18)),
    ]
    for center, size in ledges:
        b.box(center, size, "Level_Snow", bevel=0.04)

    # Трещина и тёмный камень в основании — глубина и масштаб.
    b.box((-0.4, -0.6, 4.2), (0.42, 1.6, 7.0), "Level_Stone", bevel=0.03)
    for x, scale, seed in ((-4.6, 1.5, 1.1), (4.4, 1.8, 3.7), (0.8, 1.2, 5.4)):
        b.ico((x, -1.4, 0.25), scale, "Level_Stone", subdivisions=2,
              scale=(1.2, 0.9, 0.55), jitter=0.18, seeds=seed)

    return b.build("MenuIceWall")


# ------------------------------------------------------------------- мост


def build_canyon_bridge(materials):
    b = level_lib.LevelBuilder(materials)
    span = 4.6
    width = 1.2

    def sag(x, depth=0.62):
        return -depth * (1.0 - (x / span) ** 2)

    # Балки по краям и доски поперёк, с провисом по параболе.
    for sy in (-1, 1):
        b.box((0.0, sy * width, sag(0.0)), (span * 1.95, 0.28, 0.34),
              "Level_WoodDark", bevel=0.02)
    steps = 13
    for i in range(steps):
        x = -span + (2.0 * span) * i / (steps - 1)
        b.box((x, 0.0, sag(x) + 0.2), (0.62, width * 2.0, 0.16),
              "Level_Wood", bevel=0.02)
    for x in (-3.2, -0.6, 2.4):
        b.box((x, 0.0, sag(x) + 0.31), (0.56, width * 1.7, 0.1),
              "Level_Snow", bevel=0.02)

    # Стойки по краям с шапками снега.
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * span, sy * width, 1.0), (0.34, 0.34, 2.0),
                  "Level_WoodDark", bevel=0.02)
            b.box((sx * span, sy * width, 2.08), (0.42, 0.42, 0.12),
                  "Level_Snow", bevel=0.02)

    # Тросы с тем же провисом и вертикальные подвесы к настилу.
    for sy in (-1, 1):
        pts = []
        for i in range(5):
            x = -span + 2.0 * span * i / 4.0
            pts.append((x, sy * width, 1.95 + sag(x) * 0.85))
        b.tube(pts, [(0.08, 0.08)] * len(pts), "Level_Rust", n=4,
               caps=(False, False))
        for i in range(1, 4):
            x = -span + 2.0 * span * i / 4.0
            b.tube([(x, sy * width, 1.95 + sag(x) * 0.85),
                    (x, sy * width, sag(x) + 0.28)],
                   [(0.06, 0.06)] * 2, "Level_Rust", n=3, caps=(False, False))

    # Подкосы под настилом.
    for sy in (-1, 1):
        b.tube([(-span, sy * width, 0.1), (0.0, sy * width, sag(0.0) - 0.1),
                (span, sy * width, 0.1)],
               [(0.1, 0.1)] * 3, "Level_WoodDark", n=4, caps=(False, False))

    return b.build("MenuCanyonBridge")


# --------------------------------------------------------------- ворота депо


def build_depot_gate(materials):
    b = level_lib.LevelBuilder(materials)
    front = -0.9

    # Пилоны и перекладина.
    for sx in (-1, 1):
        b.box((sx * 5.4, 0.0, 4.0), (1.1, 1.7, 8.0), "Level_Stone",
              bevel=0.04)
        b.box((sx * 5.4, 0.0, 8.1), (1.3, 1.9, 0.3), "Level_Metal",
              bevel=0.03)
        # Маркировка на пилоне.
        b.box((sx * 5.4, front - 0.02, 3.2), (1.14, 0.1, 0.42),
              "Level_Orange", bevel=0.02)
    b.box((0.0, 0.0, 8.55), (11.9, 1.9, 1.1), "Level_Metal", bevel=0.04)
    b.box((0.0, 0.0, 9.2), (11.2, 1.7, 0.18), "Level_Snow", bevel=0.03)

    # Створки: тёмная плита и рёбра поверх неё.
    b.box((0.0, 0.1, 3.2), (10.2, 0.5, 6.2), "Level_WoodDark", bevel=0.03)
    for i in range(6):
        x = -3.9 + i * 1.56
        b.box((x, front + 0.1, 3.2), (0.62, 0.55, 5.9), "Level_Rust",
              bevel=0.03)
    b.box((0.0, front + 0.1, 6.35), (10.0, 0.5, 0.3), "Level_Metal",
          bevel=0.02)

    # Тёплая щель над створками и два окна-бойницы: свет изнутри.
    b.box((0.0, front + 0.02, 6.95), (10.2, 0.16, 0.4), "Level_Window",
          bevel=0.02)
    for sx in (-1, 1):
        b.box((sx * 3.5, front + 0.02, 7.65), (1.5, 0.14, 0.8),
              "Level_Window", bevel=0.02)

    # Козырёк над воротами с подкосами.
    b.box_rot((0.0, front - 1.1, 7.6), (11.4, 2.8, 0.24), (18.0, 0.0, 0.0),
              "Level_Roof", bevel=0.03)
    b.box_rot((0.0, front - 1.15, 7.78), (10.8, 2.6, 0.14), (18.0, 0.0, 0.0),
              "Level_Snow", bevel=0.02)
    for sx in (-1, 1):
        b.tube([(sx * 4.6, front - 2.35, 7.32), (sx * 4.6, front - 0.1, 6.3)],
               [(0.11, 0.11)] * 2, "Level_Rust", n=4, caps=(False, False))

    # Фонари в кожухах на пилонах.
    for sx in (-1, 1):
        b.box((sx * 4.55, front - 0.15, 5.55), (0.8, 0.7, 0.62),
              "Level_Metal", bevel=0.03)
        b.box((sx * 4.55, front - 0.52, 5.55), (0.66, 0.12, 0.5),
              "Level_Window", bevel=0.02)

    # Рельсы и шпалы перед воротами — читается как депо, а не как сарай.
    for sx in (-1, 1):
        b.tube([(sx * 1.45, front - 0.2, 0.14), (sx * 1.45, front - 7.0, 0.14)],
               [(0.11, 0.11)] * 2, "Level_Metal", n=4)
    for i in range(5):
        y = front - 0.9 - i * 1.45
        b.box((0.0, y, 0.05), (3.6, 0.36, 0.14), "Level_WoodDark",
              bevel=0.02)

    # Вентиляционные короба на перекладине и занос у пилонов.
    for sx in (-1, 1):
        b.box((sx * 2.4, 0.0, 8.9), (1.4, 1.2, 0.5), "Level_Rust",
              bevel=0.03)
    for sx in (-1, 1):
        b.ico((sx * 4.6, -1.9, 0.15), 1.4, "Level_Snow", subdivisions=2,
              scale=(1.15, 1.0, 0.30), jitter=0.14, seeds=3.1 + sx)

    return b.build("MenuDepotGate")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    total = 0

    # Файлы с одним объектом.
    for builder, name in ((build_ice_wall, "MenuIceWall"),
                          (build_canyon_bridge, "MenuCanyonBridge"),
                          (build_depot_gate, "MenuDepotGate")):
        level_lib.clear_scene()
        materials = level_lib.make_level_materials()
        obj = builder(materials)
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(OUT, name + ".fbx"))
        bpy.data.objects.remove(obj, do_unlink=True)

    # Мачта: три объекта в одном FBX, у тарелки и маячка свой pivot.
    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    mast = build_mast(materials)
    dish = build_mast_dish(materials)
    beacon = build_mast_beacon(materials)
    total += level_lib.report([mast, dish, beacon])
    level_lib.export_static([mast, dish, beacon],
                            os.path.join(OUT, "MenuStationMast.fbx"))
    for obj in (mast, dish, beacon):
        bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT menu views tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
