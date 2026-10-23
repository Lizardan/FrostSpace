"""Собирает декорации вида меню «депо с поездом».

Запуск (штатно — через единый вход пайплайна):

    python Tools/Blender/build_all.py --asset menu-train --check

Прямой запуск для точечной отладки:

    blender --background --factory-startup --python Tools/Blender/build_menu_train.py

Выход в `Assets/_Project/Content/Models/Level/`:

    MenuTrainShed.fbx   — открытый навес 26 × 9 × 8 м: два ряда стоек, двускатная
                          крыша со снегом, задняя стена из панелей, платформа
                          0.45 м перед поездом, два фонаря;
    MenuTrainTrack.fbx  — путь на 120 м вдоль X: рельсы, шпалы, снежный настил
                          и апрон, чтобы состав уезжал по видимой дороге;
    MenuFreightTrain.fbx — локомотив и два вагона тремя объектами
                          (`TrainLoco`, `TrainCarA`, `TrainCarB`): поезд ездит как
                          одно целое, но объекты раздельные, чтобы можно было
                          переставить вагон, не пересобирая FBX.

Оси: рельсы идут вдоль Blender X; площадка перрона — со стороны −Y, после
`export_static` (зеркало X, ADR 0007) это Unity +Z, то есть между поездом и
камерой вида. Фасад навеса тоже смотрит в Blender −Y (Unity +Z, на камеру).

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

SHED_X = 13.0          # половина длины навеса по X
SHED_Y = 4.2           # половина глубины навеса по Y
RAIL_Z = 0.30          # верх шпал: по нему считается низ вагонов


# ------------------------------------------------------------------ навес

def build_train_shed(materials):
    b = level_lib.LevelBuilder(materials)

    # Пол депо: сплошная плита под всем навесом, иначе пропы висят над рельефом.
    b.box((0.0, 0.0, -0.15), (SHED_X * 2.0 + 1.0, SHED_Y * 2.0 + 1.0, 0.3),
          "Level_Stone", bevel=0.02)

    # Два ряда стоек: передний открыт, задний несёт стену.
    for i in range(5):
        x = -SHED_X + 1.0 + i * (SHED_X * 2.0 - 2.0) / 4.0
        for y in (-SHED_Y + 0.6, SHED_Y - 0.6):
            b.box((x, y, 3.6), (0.46, 0.46, 7.2), "Level_Metal", bevel=0.02)
            b.box((x, y, 0.35), (0.8, 0.8, 0.5), "Level_Stone", bevel=0.02)
            b.box((x, y, 7.05), (0.72, 0.72, 0.4), "Level_Rust", bevel=0.02)

    # Задняя стена: панели с зазором, между ними видно темноту.
    for i in range(5):
        x = -SHED_X + 1.0 + i * (SHED_X * 2.0 - 2.0) / 4.0
        b.box((x, SHED_Y + 0.1, 3.4), (5.0, 0.35, 6.4), "Level_WoodDark",
              bevel=0.03)
        b.box((x, SHED_Y + 0.05, 5.4), (4.4, 0.16, 1.1), "Level_Rust",
              bevel=0.02)
        b.box((x, SHED_Y + 0.05, 1.6), (4.4, 0.16, 2.2), "Level_Rust",
              bevel=0.02)

    # Крыша: две скатные плиты со снегом и коньковый брус.
    for sy in (-1, 1):
        b.box_rot((0.0, sy * SHED_Y * 0.5, 7.9), (SHED_X * 2.0 + 1.4,
                                                 SHED_Y + 0.9, 0.34),
                  (sy * -7.0, 0.0, 0.0), "Level_Roof", bevel=0.03)
        b.box_rot((0.0, sy * SHED_Y * 0.5, 8.16), (SHED_X * 2.0 + 1.0,
                                                  SHED_Y + 0.7, 0.16),
                  (sy * -7.0, 0.0, 0.0), "Level_Snow", bevel=0.02)
    b.box((0.0, 0.0, 8.42), (SHED_X * 2.0 + 1.4, 0.5, 0.3), "Level_Metal",
          bevel=0.02)

    # Задняя стенка над платформой: там стоят роботы, им нужен фон.
    b.box((0.0, SHED_Y - 0.2, 6.6), (SHED_X * 1.6, 0.24, 2.4), "Level_Wood",
          bevel=0.02)

    # Перрон перед поездом, со стороны камеры. Плита заходит под передние
    # стойки, но не перекрывает путь: поезд остаётся за ней.
    b.box((0.0, -2.4, 0.22), (SHED_X * 2.0, 2.2, 0.44), "Level_Stone",
          bevel=0.02)
    b.box((0.0, -3.55, 0.26), (SHED_X * 2.0, 0.3, 0.52), "Level_Snow",
          bevel=0.02)
    b.box((0.0, -3.72, 0.46), (SHED_X * 2.0, 0.24, 0.06),
          "Level_Orange", bevel=0.01)
    b.box((0.0, -1.35, 0.46), (SHED_X * 2.0, 0.14, 0.04),
          "Level_Orange", bevel=0.0)
    # Фонари перрона: два столба со светящейся полосой.
    for sx in (-1, 1):
        b.box((sx * 7.6, -3.0, 1.55), (0.18, 0.18, 3.1), "Level_Metal",
              bevel=0.01)
        b.box((sx * 7.6, -3.0, 3.18), (0.7, 0.3, 0.3), "Level_Rust",
              bevel=0.01)
        b.box((sx * 7.6, -3.15, 3.18), (0.5, 0.06, 0.2), "Level_Window",
              bevel=0.0)

    # Фонари на передних стойках: кожух и эмиссивная полоса.
    for step in (1, 3):
        x = -SHED_X + 1.0 + step * (SHED_X * 2.0 - 2.0) / 4.0
        y = -SHED_Y + 0.6
        b.box((x, y + 0.42, 5.7), (0.9, 0.7, 0.5), "Level_Metal", bevel=0.02)
        b.box((x, y + 0.05, 5.7), (0.74, 0.14, 0.36), "Level_Window",
              bevel=0.02)
        b.tube([(x, y, 6.2), (x, y + 0.35, 5.95)], [(0.07, 0.07)] * 2,
               "Level_Rust", n=3, caps=(False, False))

    # Ящики на перроне и занос по краям.
    for i, (x, scale) in enumerate(((-6.4, 1.0), (-4.9, 0.8), (5.6, 1.1))):
        b.box_rot((x, -2.6, 0.44 + 0.5 * scale),
                  (1.1 * scale, 1.1 * scale, 1.0 * scale),
                  (0.0, 0.0, i * 17.0), "Level_Wood", bevel=0.02)
    for sy in (-1, 1):
        b.ico((sy * SHED_X * 0.85, SHED_Y + 1.6, 0.1), 2.4, "Level_Snow",
              subdivisions=2, scale=(1.2, 1.0, 0.30), jitter=0.14,
              seeds=2.4 + sy)
        b.ico((sy * SHED_X * 0.9, -SHED_Y - 0.6, 0.05), 1.8, "Level_Snow",
              subdivisions=2, scale=(1.1, 1.0, 0.26), jitter=0.16,
              seeds=5.1 + sy)

    return b.build("MenuTrainShed")


# ------------------------------------------------------------------ поезд

def _wheels(b, x_offsets, y=2.35):
    for x in x_offsets:
        for sy in (-1, 1):
            b.box((x, sy * 0.78, 0.62), (0.72, 0.2, 0.72), "Level_Metal",
                  bevel=0.02)
            b.box((x, sy * 0.9, 0.62), (0.24, 0.1, 0.24), "Level_Rust",
                  bevel=0.01)


def build_train_track(materials):
    """Путь на 120 м: рельсы, шпалы и снежный настил вокруг."""
    b = level_lib.LevelBuilder(materials)
    length = 120.0
    half = length * 0.5

    # Апрон: верх на 6 см ниже пола навеса и перрона, копланарности нет.
    b.box((0.0, 0.0, -0.16), (length + 8.0, 40.0, 0.20), "Level_SnowDeep",
          bevel=0.02)
    b.box((0.0, 0.0, -0.055), (length + 8.0, 40.0, 0.02), "Level_Snow",
          bevel=0.0)

    # Насыпь под путём: чуть выше апрона, чтобы шпалы читались.
    b.box((0.0, 0.0, -0.01), (length, 5.2, 0.16), "Level_SnowDeep",
          bevel=0.02)
    b.box((0.0, 0.0, 0.07), (length, 4.8, 0.04), "Level_Snow", bevel=0.0)
    for sy in (-1, 1):
        b.box((0.0, sy * 2.75, 0.10), (length, 0.9, 0.28), "Level_Snow",
              bevel=0.03)

    # Рельсы и шпалы. Верх рельса 0.30 — по нему выставлен низ вагонов.
    for sy in (-1, 1):
        b.tube([(-half, sy * 0.72, RAIL_Z), (half, sy * 0.72, RAIL_Z)],
               [(0.11, 0.11)] * 2, "Level_Metal", n=4)
    for i in range(int(length / 2.0) + 1):
        x = -half + i * 2.0
        b.box((x, 0.0, 0.16), (0.34, 3.5, 0.16), "Level_WoodDark",
              bevel=0.02)

    # Снежные шапки между шпалами и путевые знаки по краям.
    for i in range(int(length / 8.0)):
        x = -half + 4.0 + i * 8.0
        b.box((x, 0.0, 0.22), (3.0, 1.1, 0.08), "Level_Snow", bevel=0.02)
        for sy in (-1, 1):
            b.box((x, sy * 3.9, 0.55), (0.16, 0.16, 1.1), "Level_Metal",
                  bevel=0.01)
            b.box((x, sy * 3.9, 1.15), (0.42, 0.10, 0.28),
                  "Level_Orange", bevel=0.0)

    return b.build("MenuTrainTrack")


def build_train_loco(materials):
    b = level_lib.LevelBuilder(materials)

    # Рама и обвязка по низу.
    b.box((0.0, 0.0, 1.05), (6.8, 2.5, 0.42), "Level_Metal", bevel=0.03)
    b.box((0.0, 0.0, 0.62), (5.6, 2.1, 0.34), "Level_WoodDark", bevel=0.02)
    _wheels(b, (-2.1, -0.9, 1.5, 2.6))

    # Капот с рёбрами и кабина со смотровым окном.
    b.box((0.9, 0.0, 2.15), (4.4, 2.3, 1.7), "Level_Rust", bevel=0.04)
    for i in range(4):
        b.box((0.9 - 1.5 + i * 1.0, 0.0, 2.15), (0.22, 2.36, 1.6),
              "Level_Metal", bevel=0.02)
    b.box((-2.0, 0.0, 2.55), (2.7, 2.4, 2.5), "Level_WoodDark", bevel=0.04)
    b.box((-2.0, 0.0, 3.92), (2.9, 2.6, 0.28), "Level_Snow", bevel=0.02)
    b.box((-3.32, 0.0, 2.95), (0.16, 1.8, 0.9), "Level_Window", bevel=0.02)
    for sy in (-1, 1):
        b.box((-1.6, sy * 1.22, 2.95), (1.4, 0.14, 0.8), "Level_Window",
              bevel=0.02)

    # Нос: скошенный отвал, фара, буферы и решётка.
    b.box_rot((-3.35, 0.0, 1.35), (0.9, 2.5, 1.2), (0.0, 34.0, 0.0),
              "Level_Orange", bevel=0.03)
    b.box((-3.55, 0.0, 1.85), (0.24, 1.1, 0.44), "Level_Window", bevel=0.02)
    for sy in (-1, 1):
        b.tube([(-3.6, sy * 0.8, 0.95), (-4.0, sy * 0.8, 0.95)],
               [(0.16, 0.16)] * 2, "Level_Metal", n=6)
    b.box((-2.9, 0.0, 2.0), (0.2, 1.9, 1.4), "Level_Metal", bevel=0.02)

    # Крыша капота, вентиляция и труба.
    b.box((0.9, 0.0, 3.06), (4.6, 2.4, 0.2), "Level_Metal", bevel=0.02)
    b.box((0.9, 0.0, 3.2), (4.2, 2.2, 0.1), "Level_Snow", bevel=0.02)
    for i in range(3):
        b.box((0.1 + i * 0.9, 0.0, 3.32), (0.56, 1.2, 0.26), "Level_Rust",
              bevel=0.02)
    b.tube([(-1.2, 0.62, 3.1), (-1.2, 0.62, 4.05)], [(0.19, 0.19)] * 2,
           "Level_Metal", n=6)

    # Сцепка и поручни по бортам.
    b.tube([(3.5, 0.0, 0.95), (4.1, 0.0, 0.95)], [(0.13, 0.13)] * 2,
           "Level_Metal", n=4)
    for sy in (-1, 1):
        b.tube([(-1.0, sy * 1.3, 1.5), (-1.0, sy * 1.3, 3.1)],
               [(0.05, 0.05)] * 2, "Level_Metal", n=4, caps=(False, False))

    return b.build("TrainLoco")


def build_train_car(materials, name, hopper):
    b = level_lib.LevelBuilder(materials)
    offset = 8.6 if name == "TrainCarA" else 15.6

    # Тележка и рама, по три колеса на борт.
    b.box((offset, 0.0, 1.05), (7.0, 2.5, 0.36), "Level_Metal", bevel=0.03)
    _wheels(b, (offset - 2.4, offset, offset + 2.4))

    if hopper:
        # Полувагон: борта с рёбрами, поверх — намёк на груду руды.
        b.box((offset, 0.0, 1.75), (6.6, 2.3, 0.3), "Level_WoodDark",
              bevel=0.02)
        for sy in (-1, 1):
            b.box((offset, sy * 1.15, 2.15), (6.6, 0.22, 1.1), "Level_Rust",
                  bevel=0.02)
        for i in range(5):
            x = offset - 3.0 + i * 1.5
            for sy in (-1, 1):
                b.box((x, sy * 1.24, 2.15), (0.24, 0.12, 1.06), "Level_Metal",
                      bevel=0.01)
        for i in range(4):
            x = offset - 2.2 + i * 1.5
            b.ico((x, 0.0, 1.95), 0.62, "Level_Stone", subdivisions=1,
                  scale=(1.1, 1.0, 0.7), jitter=0.2, seeds=6.0 + i)
    else:
        # Платформа с грузом: ящики и бочка, стянутые ремнём.
        b.box((offset, 0.0, 1.28), (6.8, 2.4, 0.22), "Level_Wood", bevel=0.02)
        for i, (dx, sx) in enumerate(((-2.2, 1.0), (-0.9, 0.8), (1.4, 1.1))):
            b.box_rot((offset + dx, -0.35, 1.39 + 0.55 * sx),
                      (1.2 * sx, 1.2 * sx, 1.1 * sx), (0.0, 0.0, i * 11.0),
                      "Level_Wood", bevel=0.02)
        b.band((offset + 2.4, 0.6), 0.45, 1.34, 2.1, "Level_Rust", n=8)
        for sy in (-1, 1):
            b.tube([(offset - 3.1, sy * 1.15, 1.44),
                    (offset + 3.1, sy * 1.15, 1.44)],
                   [(0.04, 0.04)] * 2, "Level_Metal", n=4, caps=(False, False))

    # Сцепки и тормозные площадки на концах.
    for dx in (-3.6, 3.6):
        b.tube([(offset + dx, 0.0, 0.95), (offset + dx * 1.18, 0.0, 0.95)],
               [(0.12, 0.12)] * 2, "Level_Metal", n=4)

    return b.build(name)


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    total = 0

    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    shed = build_train_shed(materials)
    total += level_lib.report([shed])
    level_lib.export_static([shed], os.path.join(OUT, "MenuTrainShed.fbx"))
    bpy.data.objects.remove(shed, do_unlink=True)

    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    track = build_train_track(materials)
    total += level_lib.report([track])
    level_lib.export_static([track], os.path.join(OUT, "MenuTrainTrack.fbx"))
    bpy.data.objects.remove(track, do_unlink=True)

    level_lib.clear_scene()
    materials = level_lib.make_level_materials()
    loco = build_train_loco(materials)
    car_a = build_train_car(materials, "TrainCarA", hopper=False)
    car_b = build_train_car(materials, "TrainCarB", hopper=True)
    total += level_lib.report([loco, car_a, car_b])
    level_lib.export_static([loco, car_a, car_b],
                            os.path.join(OUT, "MenuFreightTrain.fbx"))
    for obj in (loco, car_a, car_b):
        bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT menu train tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
