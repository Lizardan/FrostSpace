"""Собирает крупные детали обломков для открытого вида меню.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_wreck_parts.py

Выход в Content/Models/Level/:
    MenuWreckWing.fbx   — оторванное крыло с рваной кромкой и снегом;
    MenuWreckEngine.fbx — разрушенная гондола двигателя;
    MenuWreckFin.fbx    — вертикальный киль.

Это не замена диорамному ShipWreck, а его «разлёт»: крупные массы, которые
издалека читаются как части корабля, а не как ящики. Палитра — Level_*.
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


def _plate(builder, points, thickness, mat):
    """Экструдированный плоский полигон в плоскости XY."""
    half = thickness * 0.5
    n = len(points)
    verts = [Vector((x, y, -half)) for x, y in points]
    verts += [Vector((x, y, half)) for x, y in points]
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    builder.add(verts, faces, mat)


def build_wing(materials):
    b = level_lib.LevelBuilder(materials)
    planform = [
        (-4.2, -1.3), (2.4, -1.5), (3.9, -0.5), (3.1, 0.1),
        (4.3, 0.8), (2.7, 1.5), (-3.3, 1.3), (-4.1, 0.2),
    ]
    _plate(b, planform, 0.28, "Level_Metal")
    # Рваная кромка у корня: тёмная накладка и ржавый лонжерон.
    b.box((-3.3, 0.0, 0.02), (0.45, 2.5, 0.34), "Level_Rust", bevel=0.02)
    b.tube([(-3.6, -1.2, 0.02), (2.6, -1.38, 0.02)],
           [(0.12, 0.12)] * 2, "Level_Rust", n=6)
    # Снег на верхней плоскости: несколько крупных шапок.
    for x, y, sx, sy in ((-2.3, 0.1, 1.5, 0.9), (0.2, -0.4, 1.2, 0.7),
                         (2.1, 0.5, 0.9, 0.5)):
        b.box((x, y, 0.18), (sx, sy, 0.08), "Level_Snow", bevel=0.02)
    return b.build("MenuWreckWing")


def build_engine(materials):
    b = level_lib.LevelBuilder(materials)
    b.tube([(0.0, -1.7, 0.0), (0.0, 0.0, 0.0), (0.0, 1.6, 0.0)],
           [(0.55, 0.55), (0.92, 0.92), (0.78, 0.78)],
           "Level_Rust", n=10, caps=(True, False))
    b.tube([(0.0, 1.6, 0.0), (0.0, 1.75, 0.0)],
           [(0.78, 0.78), (0.68, 0.68)], "Level_Stone", n=10, caps=(False, True))
    # Обручи и верхний кожух.
    for y in (-1.0, 0.2, 1.1):
        b.tube([(0.0, y, 0.0), (0.0, y + 0.18, 0.0)],
               [(0.98, 0.98)] * 2, "Level_Metal", n=10, caps=(False, False))
    b.box((0.0, -0.2, 0.95), (0.9, 2.0, 0.18), "Level_Metal", bevel=0.03)
    b.box((0.0, -0.2, 1.08), (0.7, 1.6, 0.08), "Level_Snow", bevel=0.02)
    return b.build("MenuWreckEngine")


def build_fin(materials):
    b = level_lib.LevelBuilder(materials)
    # Плоскость киля — в YZ, толщина по X.
    points = [(-1.6, 0.0), (1.7, 0.0), (1.3, 2.5), (0.3, 3.1),
              (-0.7, 2.7), (-1.6, 1.3)]
    half = 0.14
    verts = [Vector((-half, y, z)) for y, z in points]
    verts += [Vector((half, y, z)) for y, z in points]
    n = len(points)
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    b.add(verts, faces, "Level_Rust")
    b.box((0.0, 0.0, 0.25), (0.35, 3.2, 0.5), "Level_Metal", bevel=0.03)
    b.box((0.0, -0.2, 1.8), (0.22, 0.7, 0.08), "Level_Snow", bevel=0.02)
    return b.build("MenuWreckFin")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    total = 0
    for builder, name in ((build_wing, "MenuWreckWing"),
                          (build_engine, "MenuWreckEngine"),
                          (build_fin, "MenuWreckFin")):
        level_lib.clear_scene()
        materials = level_lib.make_level_materials()
        obj = builder(materials)
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(OUT, name + ".fbx"))
        bpy.data.objects.remove(obj, do_unlink=True)
    print("BUILT wreck parts tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
