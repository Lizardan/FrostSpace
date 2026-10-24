"""Собирает ангар вида меню «аварийный склад».

Запуск (штатно — через единый вход пайплайна):

    python Tools/Blender/build_all.py --asset menu-storage --check

Прямой запуск для точечной отладки:

    blender --background --factory-startup --python Tools/Blender/build_menu_storage.py

Выход в `Assets/_Project/Content/Models/Level/`:

    MenuStorageHall.fbx — ангар 20 × 14 м: плита пола, три стены, открытый фасад,
                          двускатная крыша с фермами и снегом, высокие окна,
                          лампы под перекрытием и снежный апрон вокруг.

Оси: фасад смотрит в Blender +Y, после `export_static` (зеркало X, ADR 0007) —
в Unity −Z, то есть на камеру вида STORAGE. Задняя стена — Blender −Y (Unity +Z).

Палитра — `Level_*`, материалы подставит `LevelAssetPostprocessor`.
"""

from __future__ import annotations

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

HALF_X = 10.0       # половина ширины ангара
HALF_Y = 7.0        # половина глубины
WALL_H = 4.8        # высота стены по карнизу
RIDGE_H = 6.2       # конёк крыши
WALL_T = 0.30       # толщина стены


def _walls(b):
    # Пол: верх ровно на z = 0, по нему ставится вся сцена.
    b.box((0.0, 0.0, -0.15), (HALF_X * 2.0, HALF_Y * 2.0, 0.30),
          "Level_Stone", bevel=0.02)

    # Боковые стены и задняя. Передняя сторона открыта: там ворота.
    for sx in (-1, 1):
        b.box((sx * (HALF_X - WALL_T * 0.5), 0.0, WALL_H * 0.5),
              (WALL_T, HALF_Y * 2.0, WALL_H), "Level_WoodDark", bevel=0.02)
        # Нижний металлический пояс и деревянная обшивка выше.
        b.box((sx * (HALF_X - WALL_T - 0.04), 0.0, 0.55),
              (0.08, HALF_Y * 2.0 - 0.3, 1.1), "Level_Metal", bevel=0.01)
        for i in range(5):
            y = -HALF_Y + 1.2 + i * (HALF_Y * 2.0 - 2.4) / 4.0
            b.box((sx * (HALF_X - WALL_T - 0.04), y, 2.55),
                  (0.10, 2.6, 2.7), "Level_Wood", bevel=0.02)
        for i in range(4):
            y = -HALF_Y + 1.2 + i * (HALF_Y * 2.0 - 2.4) / 3.0
            b.box((sx * (HALF_X - WALL_T - 0.05), y, WALL_H * 0.5),
                  (0.16, 0.22, WALL_H - 0.3), "Level_Rust", bevel=0.01)

    b.box((0.0, -HALF_Y + WALL_T * 0.5, WALL_H * 0.5),
          (HALF_X * 2.0, WALL_T, WALL_H), "Level_WoodDark", bevel=0.02)
    b.box((0.0, -HALF_Y + WALL_T + 0.04, 0.55),
          (HALF_X * 2.0 - 0.3, 0.08, 1.1), "Level_Metal", bevel=0.01)

    # Рёбра задней стены и высокие окна.
    for i in range(5):
        x = -HALF_X + 2.0 + i * (HALF_X * 2.0 - 4.0) / 4.0
        b.box((x, -HALF_Y + WALL_T + 0.05, WALL_H * 0.5),
              (0.22, 0.16, WALL_H - 0.3), "Level_Rust", bevel=0.01)
    for sx in (-1, 1):
        b.box((sx * 4.4, -HALF_Y + WALL_T + 0.07, 3.65),
              (2.4, 0.06, 0.9), "Level_Window", bevel=0.01)
        b.box((sx * 4.4, -HALF_Y + WALL_T + 0.02, 3.65),
              (2.7, 0.10, 1.15), "Level_Metal", bevel=0.01)

    # Фасад: угловые стойки, перекладина и раскосы. Проём остаётся открытым.
    for sx in (-1, 1):
        b.box((sx * (HALF_X - 0.55), HALF_Y - 0.55, WALL_H * 0.5),
              (0.55, 0.55, WALL_H), "Level_Metal", bevel=0.02)
        b.box((sx * (HALF_X - 0.55), HALF_Y - 0.55, 0.35),
              (0.9, 0.9, 0.5), "Level_Stone", bevel=0.02)
        b.tube([(sx * (HALF_X - 1.1), HALF_Y - 0.35, 0.4),
                (sx * (HALF_X - 4.2), HALF_Y - 0.35, 4.3)],
               [(0.11, 0.11), (0.11, 0.11)], "Level_Rust", n=4,
               caps=(False, False))
    b.box((0.0, HALF_Y - 0.55, WALL_H - 0.30),
          (HALF_X * 2.0, 0.55, 0.6), "Level_Metal", bevel=0.02)
    b.box((0.0, HALF_Y - 0.55, WALL_H + 0.02),
          (HALF_X * 2.0, 0.7, 0.22), "Level_Snow", bevel=0.02)

    # Предупредительная маркировка над воротами и на пороге.
    for i in range(6):
        x = -7.5 + i * 3.0
        b.box((x, HALF_Y - 0.25, WALL_H - 0.58),
              (1.5, 0.08, 0.20), "Level_Orange", bevel=0.0)
    b.box((0.0, HALF_Y - 1.25, 0.02), (12.0, 0.18, 0.04), "Level_Orange",
          bevel=0.0)
    for sx in (-1, 1):
        b.box((sx * 3.2, HALF_Y - 2.9, 0.02), (4.0, 0.12, 0.04),
              "Level_Orange", bevel=0.0)


def _roof(b):
    # Две скатные плиты: конёк по X, скаты уходят к боковым стенам.
    slope = 11.5
    for sy in (-1, 1):
        b.box_rot((0.0, sy * 3.75, 5.52), (HALF_X * 2.0 + 0.7, 7.9, 0.28),
                  (sy * slope, 0.0, 0.0), "Level_Roof", bevel=0.02)
        b.box_rot((0.0, sy * 3.75, 5.68), (HALF_X * 2.0 + 0.3, 7.5, 0.15),
                  (sy * slope, 0.0, 0.0), "Level_Snow", bevel=0.02)
    b.box((0.0, 0.0, RIDGE_H + 0.08), (HALF_X * 2.0 + 0.8, 0.5, 0.32),
          "Level_Metal", bevel=0.02)
    b.box((0.0, 0.0, RIDGE_H + 0.28), (HALF_X * 2.0 + 0.8, 0.38, 0.10),
          "Level_Snow", bevel=0.02)

    # Фермы: две наклонные ветви, затяжка и подвеска.
    for i in range(4):
        x = -7.5 + i * 5.0
        b.tube([(x, -6.6, 4.75), (x, 0.0, 5.95), (x, 6.6, 4.75)],
               [(0.09, 0.09)] * 3, "Level_Rust", n=4, caps=(False, False))
        b.tube([(x, -6.6, 4.82), (x, 6.6, 4.82)],
               [(0.08, 0.08)] * 2, "Level_Metal", n=4, caps=(False, False))
        b.tube([(x, 0.0, 4.82), (x, 0.0, 5.92)],
               [(0.07, 0.07)] * 2, "Level_Rust", n=4, caps=(False, False))
        # Прогон по скатам под кровлей.
        for sy in (-1, 1):
            b.tube([(x, sy * 0.2, 5.9), (x, sy * 6.6, 4.82)],
                   [(0.06, 0.06)] * 2, "Level_Metal", n=3, caps=(False, False))

    # Лампы под перекрытием: корпус и светящаяся полоса снизу.
    for sx in (-1, 1):
        b.box((sx * 4.0, 0.0, 4.45), (1.5, 0.55, 0.28), "Level_Metal",
              bevel=0.02)
        b.box((sx * 4.0, 0.0, 4.30), (1.25, 0.34, 0.06), "Level_Window",
              bevel=0.0)
        b.tube([(sx * 4.0, 0.0, 4.58), (sx * 4.0, 0.0, 5.55)],
               [(0.05, 0.05)] * 2, "Level_Rust", n=3, caps=(False, False))


def _apron(b):
    # Снежная плита вокруг ангара: верх наста на 4 см ниже пола.
    # Подложка кончается ровно под настом (-0.06): раньше обе плиты имели
    # общий верх на -0.04, и две плоскости 40 x 36 мерцали друг сквозь друга.
    b.box((0.0, 0.0, -0.18), (40.0, 36.0, 0.24), "Level_SnowDeep", bevel=0.02)
    b.box((0.0, 0.0, -0.05), (40.0, 36.0, 0.02), "Level_Snow", bevel=0.0)
    # Низкие заносы у входа, чтобы апрон не читался плоским листом.
    for sx, sy, r in ((-11.5, 7.5, 1.7), (11.0, 8.5, 1.3),
                      (-13.5, -6.0, 1.2), (12.5, -7.5, 1.5)):
        b.ico((sx, sy, 0.10), r, "Level_Snow", subdivisions=2,
              scale=(1.5, 1.0, 0.24), jitter=0.14, seeds=7.0 + sx)


def build_hall(materials):
    b = level_lib.LevelBuilder(materials)
    _apron(b)
    _walls(b)
    _roof(b)
    return b.build("MenuStorageHall")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    total = 0
    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    hall = build_hall(materials)
    total += level_lib.report([hall])
    level_lib.export_static([hall], os.path.join(OUT, "MenuStorageHall.fbx"))
    bpy.data.objects.remove(hall, do_unlink=True)

    print("BUILT menu storage tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
