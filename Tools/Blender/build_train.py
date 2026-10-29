"""Собирает интерьер вагона и груз для среза «Поезд».

Запуск (штатно — через единый вход пайплайна):

    python Tools/Blender/build_all.py --asset train --check

Прямой запуск для точечной отладки:

    blender --background --factory-startup --python Tools/Blender/build_train.py

Выход в `Assets/_Project/Content/Models/Train/`:

    TrainInterior.fbx  — один большой вагон: корпус `TrainBody` и створка
                         `TrainDoor` отдельным объектом (её двигает сцена);
                         интерьер 8 × 24 м, высота 3.2 м, дверь в Blender −Y
                         (после `export_static` это Unity +Z, нос вагона);
    Prop_Battery.fbx   — тяжёлый аккумулятор: корпус, ручки, полосы, индикатор;
    Prop_GravCart.fbx  — грави-тележка на четыре слота с ручкой и пульта.

Оси и единицы — как у остальной статики (ADR 0002, 0007): метры, Z вверх,
дверь в −Y, `export_static` зеркалит X и компенсирует `bake_space_transform`.
Пивот пропов — низ по центру (z = 0): в сцене они ставятся на пол как есть.

Палитра — `Level_*`; материалы подставит `LevelAssetPostprocessor` (папки
`Models/Level` и `Models/Train` используют один контракт палитры).
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
                                   "Content", "Models", "Train"))

HALF_W = 4.0          # половина ширины вагона, м
HALF_L = 12.0         # половина длины, м
WALL = 0.2            # толщина стены
HEIGHT = 3.2          # высота интерьера
DOOR_W = 3.2          # ширина дверного проёма
DOOR_H = 2.6          # высота проёма


# ------------------------------------------------------------------ вагон

def _wall(b, sx, materials):
    """Борт: нижняя панель, окна между стойками, верхняя полоса и рёбра."""
    x = sx * (HALF_W - WALL * 0.5)

    # Нижняя панель — дерево: металл в интерьере отражает небо и седеет.
    b.box((x, 0.0, 0.6), (WALL, HALF_L * 2.0, 1.2), "Level_Wood", bevel=0.02)
    b.box((x, 0.0, 2.85), (WALL, HALF_L * 2.0, 0.7), "Level_Stone", bevel=0.02)

    # Подоконник и перемычка: тёмная ржавчина отделяет остекление.
    b.box((sx * (HALF_W - 0.16), 0.0, 1.26), (0.24, HALF_L * 2.0, 0.12),
          "Level_Rust", bevel=0.01)
    b.box((sx * (HALF_W - 0.16), 0.0, 2.44), (0.24, HALF_L * 2.0, 0.12),
          "Level_Rust", bevel=0.01)

    # Стойки между окнами: восемь пролётов по 3 м.
    for i in range(9):
        y = -HALF_L + 1.5 + i * 2.625
        b.box((x, y, 1.85), (0.24, 0.3, 1.3), "Level_Metal", bevel=0.015)
        # Оранжевая метка на стойке — ориентир в длинном вагоне.
        b.box((sx * (HALF_W - 0.3), y, 1.9), (0.06, 0.12, 0.5),
              "Level_Orange", bevel=0.0)

    # Рёбра жёсткости по нижней панели.
    for i in range(17):
        y = -HALF_L + 1.0 + i * 1.375
        b.box((sx * (HALF_W - 0.16), y, 0.55), (0.12, 0.18, 1.0),
              "Level_Metal", bevel=0.01)

    # Иней у пола ближе к двери: вагон холодный, дверь открывают редко.
    for i in range(3):
        y = -HALF_L + 1.2 + i * 1.1
        b.box((sx * (HALF_W - 0.45), y, 0.07), (0.55, 0.8, 0.1),
              "Level_Snow", bevel=0.02)


def _ceiling(b):
    """Потолок с рёбрами, два плафона и трубы по бортам."""
    b.box((0.0, 0.0, HEIGHT + 0.1), (HALF_W * 2.0, HALF_L * 2.0, 0.2),
          "Level_Stone", bevel=0.02)

    for i in range(13):
        y = -HALF_L + i * 2.0
        b.box((0.0, y, HEIGHT - 0.02), (HALF_W * 2.0, 0.16, 0.14),
              "Level_Metal", bevel=0.01)

    # Плафоны: широкий кожух и утопленная тёплая полоса. Свет в вагоне — единственный.
    for y in (-6.0, 6.0):
        b.box((0.0, y, HEIGHT - 0.1), (1.3, 3.2, 0.16), "Level_Metal",
              bevel=0.02)
        b.box((0.0, y, HEIGHT - 0.19), (0.55, 2.5, 0.06), "Level_Window",
              bevel=0.0)

    # Трубы и хомуты.
    for sx in (-1, 1):
        b.tube([(sx * 3.5, -HALF_L + 0.4, HEIGHT - 0.18),
                (sx * 3.5, HALF_L - 0.4, HEIGHT - 0.18)],
               [(0.08, 0.08)] * 2, "Level_Rust", n=6, caps=(False, False))
        for i in range(6):
            y = -HALF_L + 2.0 + i * 4.0
            b.box((sx * 3.5, y, HEIGHT - 0.1), (0.16, 0.12, 0.2),
                  "Level_Metal", bevel=0.01)
    b.tube([(0.0, -HALF_L + 0.4, HEIGHT - 0.06),
            (0.0, HALF_L - 0.4, HEIGHT - 0.06)],
           [(0.05, 0.05)] * 2, "Level_Rust", n=6, caps=(False, False))


def _far_end(b):
    """Глухая стена: панели, люк, щиток с индикаторами, вертикальные трубы."""
    y = HALF_L - WALL * 0.5
    b.box((0.0, y, HEIGHT * 0.5), (HALF_W * 2.0, WALL, HEIGHT),
          "Level_Metal", bevel=0.02)

    for x in (-2.6, 0.0, 2.6):
        b.box((x, y - 0.08, 1.6), (0.3, 0.16, 2.9), "Level_Metal",
              bevel=0.015)

    # Люк в стене: тёмная панель в оранжевой рамке.
    b.box((0.0, y - 0.16, 1.1), (1.6, 0.12, 1.8), "Level_WoodDark",
          bevel=0.02)
    b.box((0.0, y - 0.22, 2.02), (1.8, 0.08, 0.12), "Level_Orange", bevel=0.0)
    for sx in (-1, 1):
        b.box((sx * 0.84, y - 0.22, 1.1), (0.08, 0.08, 1.9),
              "Level_Orange", bevel=0.0)

    # Щиток: три индикатора, читаются как «система жива».
    b.box((1.9, y - 0.14, 2.15), (1.2, 0.1, 0.7), "Level_Metal", bevel=0.02)
    for i in range(3):
        b.box((1.55 + i * 0.3, y - 0.2, 2.15), (0.12, 0.04, 0.12),
              "Level_Window", bevel=0.0)

    for sx in (-3.1, -3.45):
        b.tube([(sx, y - 0.12, 0.1), (sx, y - 0.12, HEIGHT - 0.1)],
               [(0.07, 0.07)] * 2, "Level_Rust", n=6)


def _door_end(b):
    """Торец с проёмом: сегменты стены, перемычка, рамка и рельс створки."""
    y = -HALF_L + WALL * 0.5
    side = (HALF_W * 2.0 - DOOR_W) * 0.5

    for sx in (-1, 1):
        b.box((sx * (DOOR_W * 0.5 + side * 0.5), y, HEIGHT * 0.5),
              (side, WALL, HEIGHT), "Level_Metal", bevel=0.02)
        # Вертикальные рёбра на глухих частях.
        b.box((sx * (DOOR_W * 0.5 + side * 0.5), y - 0.08, 1.6),
              (side * 0.5, 0.16, 2.9), "Level_Metal", bevel=0.015)

    # Перемычка над проёмом.
    b.box((0.0, y, DOOR_H + (HEIGHT - DOOR_H) * 0.5),
          (DOOR_W, WALL, HEIGHT - DOOR_H), "Level_Metal", bevel=0.02)

    # Оранжевая рамка проёма: сквозь стену, видна и изнутри, и снаружи.
    for sx in (-1, 1):
        b.box((sx * (DOOR_W * 0.5 + 0.06), y, DOOR_H * 0.5),
              (0.12, WALL + 0.08, DOOR_H), "Level_Orange", bevel=0.0)
    b.box((0.0, y, DOOR_H + 0.06), (DOOR_W + 0.24, WALL + 0.08, 0.12),
          "Level_Orange", bevel=0.0)

    # Рельс, по которому уезжает створка, и порог — внутри вагона.
    b.box((0.0, y + 0.22, DOOR_H + 0.22), (DOOR_W + 0.6, 0.28, 0.14),
          "Level_Rust", bevel=0.02)
    b.box((0.0, y + 0.2, 0.05), (DOOR_W, 0.5, 0.1), "Level_Rust", bevel=0.01)

    # Занос у двери: внутрь наметает.
    b.box((0.0, y + 1.1, 0.06), (DOOR_W - 0.4, 1.1, 0.12), "Level_Snow",
          bevel=0.03)


def _furniture(b):
    """Лавки, верстак, ящики и полка — чтобы вагон читался как рабочий."""
    for sx in (-1, 1):
        # Лавка: сиденье, спинка, ножки.
        b.box((sx * 3.25, 6.4, 0.5), (0.6, 2.4, 0.1), "Level_Wood",
              bevel=0.02)
        b.box((sx * 3.5, 6.4, 0.85), (0.12, 2.4, 0.6), "Level_WoodDark",
              bevel=0.02)
        for dy in (-1.0, 1.0):
            b.box((sx * 3.25, 6.4 + dy, 0.25), (0.5, 0.12, 0.5),
                  "Level_Metal", bevel=0.01)

    # Верстак в дальнем конце: столешница, ножки, тиски, лампа.
    b.box((0.0, 10.5, 0.95), (2.4, 0.9, 0.12), "Level_Wood", bevel=0.02)
    for sx in (-1, 1):
        for dy in (-0.35, 0.35):
            b.box((sx * 1.0, 10.5 + dy, 0.45), (0.14, 0.14, 0.9),
                  "Level_Metal", bevel=0.01)
    b.box((0.7, 10.5, 1.12), (0.5, 0.4, 0.22), "Level_Rust", bevel=0.02)
    b.box((0.7, 10.5, 1.26), (0.24, 0.2, 0.12), "Level_Metal", bevel=0.01)
    b.box((-0.9, 10.5, 1.15), (0.12, 0.12, 0.3), "Level_Metal", bevel=0.01)
    b.box((-0.9, 10.5, 1.32), (0.5, 0.3, 0.1), "Level_Metal", bevel=0.02)
    b.box((-0.9, 10.5, 1.26), (0.4, 0.2, 0.04), "Level_Window", bevel=0.0)

    # Ящики по углам.
    for x, y, size in ((3.1, -9.4, 0.9), (-3.0, 9.7, 0.8)):
        b.box((x, y, size * 0.5), (size, size, size), "Level_Wood",
              bevel=0.03)
        b.box((x, y, size * 0.55), (size + 0.04, 0.1, 0.12), "Level_Rust",
              bevel=0.0)
        b.box((x, y, size + 0.02), (size * 0.9, size * 0.9, 0.06),
              "Level_Snow", bevel=0.0)

    # Полка на дальнем борту.
    b.box((3.6, 9.6, 1.9), (0.35, 3.0, 0.1), "Level_WoodDark", bevel=0.01)
    for dy in (-1.2, 1.2):
        b.box((3.45, 9.6 + dy, 1.8), (0.2, 0.12, 0.3), "Level_Metal",
              bevel=0.01)


def _floor(b):
    """Пол: сталь, деревянный настил по центру, оранжевые кромки."""
    b.box((0.0, 0.0, -0.15), (HALF_W * 2.0, HALF_L * 2.0, 0.3),
          "Level_Stone", bevel=0.02)
    b.box((0.0, 0.0, 0.025), (2.6, HALF_L * 2.0 - 0.8, 0.05),
          "Level_WoodDark", bevel=0.01)
    for sx in (-1, 1):
        b.box((sx * 1.36, 0.0, 0.04), (0.08, HALF_L * 2.0 - 0.8, 0.03),
              "Level_Orange", bevel=0.0)

    # Поперечные швы настила: крупные, чтобы не читались «сыпью».
    for i in range(11):
        y = -HALF_L + 1.4 + i * 2.0
        b.box((0.0, y, 0.055), (2.6, 0.08, 0.02), "Level_Rust", bevel=0.0)


def build_interior(materials):
    b = level_lib.LevelBuilder(materials)
    _floor(b)
    for sx in (-1, 1):
        _wall(b, sx, materials)
    _ceiling(b)
    _far_end(b)
    _door_end(b)
    _furniture(b)
    body = b.build("TrainBody")

    # Створка отдельным объектом: сцена двигает её в финале. Меш — вокруг
    # нуля, объект ставится в проём: иначе пивот уезжает на 12 м и коллайдер
    # не совпадает с полотном (та же грабля, что у колеса мотоцикла).
    d = level_lib.LevelBuilder(materials)
    y = 0.0
    d.box((0.0, y, DOOR_H * 0.5), (DOOR_W - 0.1, 0.12, DOOR_H - 0.05),
          "Level_Metal", bevel=0.02)
    # Рёбра с обеих сторон: створка читается и изнутри, и снаружи.
    for x in (-1.0, 0.0, 1.0):
        for sy in (-1, 1):
            d.box((x, y + sy * 0.08, DOOR_H * 0.5),
                  (0.2, 0.06, DOOR_H - 0.3), "Level_Metal", bevel=0.015)
    # Окно — сквозная вставка, видна с двух сторон, в ржавой рамке.
    d.box((0.0, y, 2.0), (1.4, 0.16, 0.5), "Level_SnowDeep", bevel=0.01)
    for dz in (-1, 1):
        d.box((0.0, y, 2.0 + dz * 0.29), (1.5, 0.16, 0.08), "Level_Rust",
              bevel=0.0)
    for dx in (-1, 1):
        d.box((dx * 0.74, y, 2.0), (0.08, 0.16, 0.58), "Level_Rust",
              bevel=0.0)
    # Аварийная полоса и ручки с обеих сторон.
    d.box((0.0, y, 0.35), (DOOR_W - 0.3, 0.16, 0.18), "Level_Orange",
          bevel=0.0)
    for sy in (-1, 1):
        d.tube([(1.2, y + sy * 0.1, 1.0), (1.2, y + sy * 0.1, 1.6)],
               [(0.05, 0.05)] * 2, "Level_Metal", n=6)
    door = d.build("TrainDoor")
    door.location = (0.0, -HALF_L + 0.06, 0.0)

    return [body, door]


# ------------------------------------------------------------------ груз

def build_battery(materials):
    b = level_lib.LevelBuilder(materials)

    # Корпус и опорная рама: низ на z = 0, пивот по центру.
    b.box((0.0, 0.0, 0.05), (0.96, 0.66, 0.1), "Level_WoodDark", bevel=0.02)
    b.box((0.0, 0.0, 0.37), (0.9, 0.6, 0.64), "Level_Metal", bevel=0.03)
    b.box((0.0, 0.0, 0.71), (0.94, 0.64, 0.1), "Level_Rust", bevel=0.02)

    # Оранжевые полосы по четырём сторонам.
    for sy in (-1, 1):
        b.box((0.0, sy * 0.31, 0.37), (0.9, 0.06, 0.12), "Level_Orange",
              bevel=0.0)
    for sx in (-1, 1):
        b.box((sx * 0.46, 0.0, 0.37), (0.06, 0.6, 0.12), "Level_Orange",
              bevel=0.0)

    # Ручки: две скобы на крышке.
    for sx in (-1, 1):
        x = sx * 0.28
        b.tube([(x, -0.2, 0.76), (x, -0.2, 0.9), (x, 0.2, 0.9),
                (x, 0.2, 0.76)], [(0.05, 0.05)] * 4, "Level_Metal", n=6)

    # Индикатор заряда: панель и три деления.
    b.box((0.22, -0.31, 0.45), (0.34, 0.06, 0.2), "Level_WoodDark",
          bevel=0.01)
    for i in range(3):
        b.box((0.1 + i * 0.11, -0.345, 0.45), (0.07, 0.03, 0.1),
              "Level_Window", bevel=0.0)

    # Предупреждающая табличка и уголки.
    b.box((-0.22, -0.32, 0.55), (0.26, 0.06, 0.16), "Level_Orange",
          bevel=0.0)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.4, sy * 0.27, 0.16), (0.14, 0.14, 0.22),
                  "Level_Rust", bevel=0.01)

    return b.build("Prop_Battery")


def build_cart(materials):
    b = level_lib.LevelBuilder(materials)

    # Платформа на подушках: низ 0, подушки чуть приподняты.
    b.box((0.0, 0.0, 0.35), (2.4, 1.6, 0.3), "Level_Metal", bevel=0.03)
    b.box((0.0, 0.0, 0.52), (2.2, 1.4, 0.06), "Level_Rust", bevel=0.01)

    # Борта.
    for sy in (-1, 1):
        b.box((0.0, sy * 0.75, 0.62), (2.4, 0.1, 0.24), "Level_Metal",
              bevel=0.01)
    for sx in (-1, 1):
        b.box((sx * 1.15, 0.0, 0.62), (0.1, 1.6, 0.24), "Level_Metal",
              bevel=0.01)

    # Четыре гнезда 2×2: приподнятые бортики, оранжевая кромка, тёплая полоса.
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * 0.55, sy * 0.4
            # Дно гнезда темнее платформы.
            b.box((cx, cy, 0.53), (1.02, 0.64, 0.04), "Level_WoodDark",
                  bevel=0.0)
            # Бортики: между гнёздами две стенки образуют разделитель.
            for dy in (-1, 1):
                b.box((cx, cy + dy * 0.36, 0.63), (1.16, 0.08, 0.16),
                      "Level_Metal", bevel=0.01)
                b.box((cx, cy + dy * 0.36, 0.72), (1.16, 0.06, 0.06),
                      "Level_Orange", bevel=0.0)
            for dx in (-1, 1):
                b.box((cx + dx * 0.54, cy, 0.63), (0.08, 0.64, 0.16),
                      "Level_Metal", bevel=0.01)
                b.box((cx + dx * 0.54, cy, 0.72), (0.06, 0.64, 0.06),
                      "Level_Orange", bevel=0.0)
            # Тёплая полоса по внутренней кромке: гнездо «под напряжением».
            for dy in (-1, 1):
                b.box((cx, cy + dy * 0.31, 0.66), (0.92, 0.05, 0.06),
                      "Level_Window", bevel=0.0)

    # Подушки: четыре опоры, без колёс. Оранжевая плита шире подушки —
    # читается как тёплый низ, а не как ножка.
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.85, sy * 0.55, 0.15), (0.5, 0.4, 0.14),
                  "Level_Metal", bevel=0.02)
            b.box((sx * 0.85, sy * 0.55, 0.05), (0.58, 0.48, 0.1),
                  "Level_Orange", bevel=0.01)

    # Бампер со стороны двери: тележку толкают, а не тянут.
    b.box((0.0, -0.84, 0.4), (2.2, 0.12, 0.18), "Level_Rust", bevel=0.01)
    b.box((0.0, -0.9, 0.4), (2.0, 0.06, 0.08), "Level_Orange", bevel=0.0)

    # Ручка и пульт со стороны +Y.
    for sx in (-1, 1):
        b.tube([(sx * 0.7, 0.78, 0.5), (sx * 0.7, 0.9, 0.95),
                (sx * 0.7, 0.9, 1.1)], [(0.06, 0.06)] * 3, "Level_Metal", n=6)
    b.tube([(-0.7, 0.9, 1.1), (0.7, 0.9, 1.1)], [(0.06, 0.06)] * 2,
           "Level_Metal", n=6)

    b.box((0.0, 0.8, 0.6), (0.3, 0.14, 0.2), "Level_Metal", bevel=0.01)
    b.box_rot((0.0, 0.8, 0.78), (0.5, 0.16, 0.26), (-20.0, 0.0, 0.0),
              "Level_Rust", bevel=0.02)
    b.box_rot((0.0, 0.73, 0.83), (0.32, 0.05, 0.14), (-20.0, 0.0, 0.0),
              "Level_Window", bevel=0.0)

    return b.build("Prop_GravCart")


# ------------------------------------------------------------------ сборка

def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    jobs = (
        ("TrainInterior", build_interior),
        ("Prop_Battery", build_battery),
        ("Prop_GravCart", build_cart),
    )
    total = 0
    for name, builder in jobs:
        level_lib.clear_scene()
        materials = level_lib.make_level_materials()
        objs = builder(materials)
        if not isinstance(objs, list):
            objs = [objs]
        total += level_lib.report(objs)
        level_lib.export_static(objs, os.path.join(OUT, name + ".fbx"))
        for obj in objs:
            bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT train assets tris=%d" % total)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
