"""Мелкие формы в клетке: общий код для депо и посёлка.

Здесь только приёмка: поставить готовый предмет на отметку земли, насыпать
нанос, проложить тропу. Сами предметы собирает `level_parts` — их не
переизобретают.

Оси клетки: X — на восток, Y в Blender = −z мира, вверх Z. Точка `(lx, lz)` —
локальные оси клетки, `ground(lx, lz)` — пол под ней. Предметы из
`level_parts` собраны с началом в основании, поэтому ставятся прямо на отметку.
"""

from __future__ import annotations

import math

from mathutils import Vector


def merge(b, obj, lx: float, lz: float, ground: float, sink: float = 0.0,
          rot: float = 0.0) -> None:
    """Влить готовый предмет в сборщик клетки: перенести, повернуть, посадить.

    Вливание, а не отдельный объект: двор депо — одна формация по замыслу
    (`depot_parts.build_depot`), и мелкие предметы не должны разваливать её
    на десяток объектов, которые потом надо помнить вместе.
    """
    mats = [s.material.name if s.material else "?" for s in obj.material_slots]
    a = math.radians(rot)
    ca, sa = math.cos(a), math.sin(a)
    base = len(b.mb.verts)
    for v in obj.data.vertices:
        x = v.co.x * ca - v.co.y * sa
        y = v.co.x * sa + v.co.y * ca
        b.mb.verts.append(Vector((x + lx, -y + lz, v.co.z + ground + sink)))
    for poly in obj.data.polygons:
        name = mats[poly.material_index] if poly.material_index < len(mats) else "?"
        # Y переносится с отрицанием (Blender-Y = −z мира), а это зеркало:
        # обход граней обязан развернуться, иначе весь влитый предмет
        # оказывается вывернутым — снаружи просвечивает, изнутри виден.
        b.mb.add_face([base + i for i in reversed(poly.vertices)],
                      b._index(name), "surface")


def drift(b, lx: float, lz: float, ground: float, rx: float, ry: float,
          height: float, seed: float = 0.0) -> None:
    """Снежный нанос: полусфера, наполовину в снегу.

    Форма та же, что у валуна (`level_parts.build_rock`), только из снега: в
    мире нет отдельной «наледь» — есть снег, который скопился.
    """
    b.ico((lx, -lz, ground + height * 0.35), 1.0, "Level_Snow",
          subdivisions=1, scale=(rx, ry, height), jitter=0.22, seeds=seed)


def trodden(b, pts, ground, width: float = 1.8, mat: str = "Level_SnowDeep",
            lift: float = 0.05, thick: float = 0.12) -> None:
    """Протоптанная тропа: узкая лента утоптанного снега по ломаной.

    Лента идёт на `lift` выше земли, чтобы не тонуть в ней: утоптанный снег
    темнее и лежит поверх. Точки ломаной — локальные оси клетки.

    Лента **замкнута**: у неё есть толщина, иначе это открытый лист, а
    проверка стиля ловит оболочки тоньше 6 см (у листа толщина нулевая).
    Профиль замкнут и обойдён так, чтобы верх смотрел вверх: порядок точек
    задаёт направление нормалей, как и в трубе (`lowpoly.MeshBuilder.tube`).
    """
    if len(pts) < 2:
        return
    path = [(lx, -lz, ground(lx, lz) + lift) for (lx, lz) in pts]
    half = width * 0.5
    profile = [(-half, -thick), (half, -thick), (half, 0.0), (-half, 0.0)]
    b.tube(path, [(1.0, 1.0)] * len(path), mat, n=4, profile=profile,
           ref=(0.0, 0.0, 1.0), fan_caps=True)


def charging_post(b, lx: float, lz: float, ground: float, height: float = 2.1) -> None:
    """Зарядный узел: стойка с катушкой и клеммами.

    По канону зарядные узлы есть в депо и на станции, и между ними — ничего
    (`WORLD.md`). Тёплого света здесь нет: в депо только три тёплых акцента —
    лампа на мачте, фара и свет из проёма (`STYLE.md`), поэтому узел читается
    формой и оранжевой маркировкой.
    """
    b.box((lx, -lz, ground + height * 0.5), (0.34, 0.34, height),
          "Level_Metal", bevel=0.03)
    b.box((lx, -lz, ground + 0.12), (0.62, 0.62, 0.24), "Level_Stone",
          bevel=0.03)
    b.box((lx, -lz, ground + height - 0.12), (0.46, 0.46, 0.24), "Level_Metal",
          bevel=0.03)
    b.box((lx, -lz, ground + height + 0.02), (0.22, 0.22, 0.12), "Level_Orange",
          bevel=0.02)
    # клеммы и кабель к земле
    b.box((lx + 0.26, -lz, ground + height * 0.62), (0.18, 0.18, 0.30),
          "Level_Orange", bevel=0.02)
    b.tube([(lx, -lz, ground + 0.35), (lx + 0.42, -lz - 0.18, ground + 0.18),
            (lx + 0.30, -lz - 0.55, ground + 0.10)], [(0.05, 0.05)] * 3,
           "Level_Rust", n=6)
