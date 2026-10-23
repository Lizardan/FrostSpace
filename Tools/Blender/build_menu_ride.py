"""Собирает полосы ледяной дороги для вида меню «проезд».

Запуск (штатно — через единый вход пайплайна):

    python Tools/Blender/build_all.py --asset menu-ride --check

Прямой запуск для точечной отладки:

    blender --background --factory-startup --python Tools/Blender/build_menu_ride.py

Выход в `Assets/_Project/Content/Models/Level/`:

    MenuRideSegment_A/B/C.fbx — полосы 56 × 32 м: снежный пол, ледяная дорога,
                          обочины, плотные сосны, сугробы, камни и ледяные шипы.
                          Торцы по Y у всех вариантов ровные, поэтому полосы
                          сшиваются в петлю без швов;
    MenuRidePlain.fbx     — статичное снежное поле 240 × 300 м с дальними гребнями:
                          закрывает фон за пределами петли, пока полосы едут.

Оси: движение — вдоль Blender Y (в Unity это Z, вперёд мотика), ширина полосы —
Blender X. Дорога идёт по X = 0, поэтому зеркало X при экспорте (ADR 0007) её
не сдвигает. Дальние гребни стоят по обе стороны, так что камера сзади-слева
видит плотные стены леса, а не пустой снег.

Палитра — `Level_*`; лёд — новый ключ `Level_Ice`.
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

import level_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))

SEG_X = 28.0   # половина ширины полосы (56 м)
SEG_Y = 16.0   # половина длины полосы (шаг петли = 32 м)
ROAD_HALF = 3.7


# ------------------------------------------------------------------ рельеф

def _pine(b, x, y, scale, seed):
    """Сосна: ствол и три яруса кроны; силуэт важнее деталей."""
    b.tube([(x, y, 0.0), (x, y, 1.15 * scale)],
           [(0.20 * scale, 0.20 * scale), (0.13 * scale, 0.13 * scale)],
           "Level_WoodDark", n=5, caps=(False, True))
    for i, (dz, r) in enumerate(((1.00, 1.15), (1.75, 0.88), (2.40, 0.56))):
        b.ico((x, y, dz * scale), r * scale, "Level_Pine", subdivisions=1,
              scale=(1.0, 1.0, 0.92), jitter=0.12, seeds=seed + i)
        b.ico((x, y, (dz + 0.18) * scale), r * 0.76 * scale,
              "Level_PineSnow", subdivisions=1, scale=(1.0, 1.0, 0.52),
              jitter=0.09, seeds=seed + 2.0 + i)
    # Сугроб у корней.
    b.ico((x, y, 0.10), 0.95 * scale, "Level_Snow", subdivisions=1,
          scale=(1.35, 1.0, 0.24), jitter=0.12, seeds=seed + 8.0)


def _drift(b, x, y, sx, sy, seed):
    b.ico((x, y, 0.10), 1.0, "Level_Snow", subdivisions=2,
          scale=(sx, sy, 0.28), jitter=0.16, seeds=seed)


def _rock(b, x, y, r, seed):
    b.ico((x, y, r * 0.28), r, "Level_Stone", subdivisions=1,
          scale=(1.2, 1.0, 0.8), jitter=0.20, seeds=seed)
    b.ico((x, y, r * 0.52), r * 0.74, "Level_Snow", subdivisions=1,
          scale=(1.2, 1.0, 0.32), jitter=0.12, seeds=seed + 4.0)


def _ice_shard(b, x, y, h, seed):
    b.tube([(x, y, 0.0), (x + 0.10, y + 0.12, h)],
           [(0.34, 0.34), (0.05, 0.05)], "Level_Ice", n=5)


def _road(b):
    """Ледяная дорога и обочины: непрерывны по Y, торцы ровные."""
    b.box((0.0, 0.0, -0.30), (SEG_X * 2.0, SEG_Y * 2.0, 0.60),
          "Level_Snow", bevel=0.02)
    b.box((0.0, 0.0, 0.020), (ROAD_HALF * 2.0, SEG_Y * 2.0, 0.04),
          "Level_Ice", bevel=0.015)
    # Накатанные полосы: чуть темнее льда, не копланарны ему.
    for sx in (-1, 1):
        b.box((sx * 1.25, 0.0, 0.045), (0.55, SEG_Y * 2.0, 0.03),
              "Level_SnowDeep", bevel=0.01)
    # Обочины: снежная кромка по краю дороги.
    for sx in (-1, 1):
        b.box((sx * (ROAD_HALF + 0.42), 0.0, 0.10),
              (0.9, SEG_Y * 2.0, 0.24), "Level_Snow", bevel=0.03)
        b.box((sx * (ROAD_HALF + 0.18), 0.0, 0.02),
              (0.30, SEG_Y * 2.0, 0.04), "Level_SnowDeep", bevel=0.01)


def _edge_posts(b):
    """Путевые столбики: одинаковый ритм во всех вариантах, стык читается."""
    for i, y in enumerate((-12.0, -4.0, 4.0, 12.0)):
        for sx in (-1, 1):
            x = sx * (ROAD_HALF + 1.15)
            b.box((x, y, 0.55), (0.16, 0.16, 1.1), "Level_Metal", bevel=0.01)
            b.box((x, y, 1.15), (0.44, 0.12, 0.24), "Level_Orange",
                  bevel=0.0)
            b.ico((x, y, 1.28), 0.22, "Level_Snow", subdivisions=1,
                  scale=(1.0, 1.0, 0.45), jitter=0.08, seeds=11.0 + i + sx)


def _ridgeline(b):
    """Дальние гребни по сторонам: постоянны вдоль Y, поэтому шов не виден."""
    for sx in (-1, 1):
        b.box((sx * 24.0, 0.0, 1.10), (7.0, SEG_Y * 2.0, 2.2),
              "Level_SnowDeep", bevel=0.04)
        b.box((sx * 23.6, 0.0, 2.25), (5.6, SEG_Y * 2.0, 0.42),
              "Level_Snow", bevel=0.03)
        for i, (dx, dy, r) in enumerate(((-0.6, -9.4, 1.7),
                                         (0.5, -2.0, 2.0),
                                         (-0.4, 6.2, 1.4),
                                         (0.7, 12.0, 1.2))):
            b.ico((sx * (23.0 + dx), dy, r * 0.30), r, "Level_Stone",
                  subdivisions=1, scale=(1.2, 1.0, 0.8), jitter=0.18,
                  seeds=21.0 + i + sx)
        b.ico((sx * 22.2, 1.4, 2.35), 1.8, "Level_Snow", subdivisions=2,
              scale=(1.5, 1.0, 0.30), jitter=0.12, seeds=27.0 + sx)


def _forest(b, variant):
    """Плотная расстановка по обе стороны: у камеры не остаётся пустот."""
    seed = {"A": 3.0, "B": 17.0, "C": 31.0}[variant]
    jitter = {"A": 0.0, "B": 1.7, "C": -1.2}[variant]

    # Ближний ряд: 4 дерева на сторону, шаг ~8 м.
    near = ((-11.8, 0.8, 1.15), (-8.2, 8.6, 0.95),
            (-10.5, 0.2, 1.05), (-7.6, -8.4, 0.85)) if variant == "A" else (
            ((-12.4, -2.0, 1.25), (-8.6, 6.0, 1.00),
             (-10.0, 12.4, 0.90), (-7.4, -10.0, 0.80)) if variant == "B" else (
             (-11.2, 4.2, 1.10), (-8.8, -5.4, 0.95),
             (-9.8, 11.0, 1.20), (-7.7, -12.2, 0.78)))
    for side in (-1, 1):
        for i, (x, y, s) in enumerate(near):
            _pine(b, side * -x, y + jitter * (i % 2), s, seed + i + side)

    # Средний ряд: крупнее и дальше, закрывает просветы между ближними.
    mid = ((-17.0, -10.0, 1.35), (-15.0, 3.2, 1.20),
           (-18.5, 12.4, 1.45), (-14.2, -2.4, 1.05))
    for side in (-1, 1):
        for i, (x, y, s) in enumerate(mid):
            if variant == "B" and i == 3:
                continue
            _pine(b, side * -x, y - jitter, s, seed + 10.0 + i + side)

    # Сугробы вдоль обочин: шаг 4 м, без разрывов.
    for i in range(8):
        y = -14.0 + i * 4.0
        for side in (-1, 1):
            _drift(b, side * 6.2, y, 1.6, 1.0, seed + 20.0 + i + side)
    for i in range(4):
        y = -12.0 + i * 8.0
        for side in (-1, 1):
            _drift(b, side * 13.0, y + jitter, 2.6, 1.2, seed + 30.0 + i + side)

    # Камни и ледяные шипы: дают движение по вертикали.
    rocks = ((-6.0, -9.0, 0.7), (-6.4, 2.2, 0.55),
             (-12.0, -3.6, 0.9), (-13.6, 9.4, 0.7),
             (6.1, -6.4, 0.5), (6.6, 7.0, 0.8),
             (12.4, 1.0, 0.65), (14.0, -11.0, 0.9))
    for i, (x, y, r) in enumerate(rocks):
        if variant == "C" and i % 4 == 3:
            continue
        _rock(b, x, y + jitter * 0.6, r, seed + 40.0 + i)
    shards = ((-5.0, -4.0, 1.0), (5.2, 2.6, 1.3), (-5.2, 10.0, 0.8),
              (5.0, -12.0, 1.1))
    for i, (x, y, h) in enumerate(shards):
        if variant == "A" and i == 3:
            continue
        _ice_shard(b, x, y, h, seed + 50.0 + i)


def build_segment(materials, variant):
    b = level_lib.LevelBuilder(materials)
    _road(b)
    _edge_posts(b)
    _ridgeline(b)
    _forest(b, variant)
    return b.build("MenuRideSegment_" + variant)


def build_plain(materials):
    """Статичное поле: закрывает фон за петлёй и горизонт по сторонам."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, -0.30), (240.0, 300.0, 0.60), "Level_Snow",
          bevel=0.04)
    for sx in (-1, 1):
        b.box((sx * 48.0, 0.0, 1.6), (22.0, 300.0, 3.2),
              "Level_SnowDeep", bevel=0.06)
        b.box((sx * 46.0, 0.0, 3.1), (18.0, 300.0, 0.5), "Level_Snow",
              bevel=0.05)
    for sz in (-1, 1):
        b.box((0.0, sz * 135.0, 1.2), (240.0, 18.0, 2.4),
              "Level_SnowDeep", bevel=0.06)
        b.box((0.0, sz * 132.0, 2.3), (240.0, 14.0, 0.4), "Level_Snow",
              bevel=0.05)
    return b.build("MenuRidePlain")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    total = 0
    for variant in ("A", "B", "C"):
        level_lib.clear_scene()
        materials = level_lib.make_level_materials()
        obj = build_segment(materials, variant)
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(OUT, obj.name + ".fbx"))
        bpy.data.objects.remove(obj, do_unlink=True)

    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    plain = build_plain(materials)
    total += level_lib.report([plain])
    level_lib.export_static([plain], os.path.join(OUT, "MenuRidePlain.fbx"))
    bpy.data.objects.remove(plain, do_unlink=True)

    print("BUILT menu ride tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
