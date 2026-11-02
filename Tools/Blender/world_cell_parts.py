"""Меш одной клетки мира: 600×600 м, решётка 30 м, без исключений.

Запуск (штатно — из пайплайна):

    blender --background --factory-startup --python Tools/Blender/build_world_cells.py

Клетка авторится **в своих локальных осях**: центр клетки в нуле, стороны
±300 м, высота — абсолютная отметка мирового поля. Так префаб клетки открывается
в Prefab Mode как самостоятельный кусок земли (ради этого всё и затеяно), а
расстановка в сцене — это просто позиция центра клетки, без ручных высот.

Почему сетка обязана быть ровно по границам клетки. Тогда общая сторона двух
соседей — это один и тот же набор точек мирового поля (`world_height` зависит от
мировых координат, а не от клетки), и стыка в геометрии нет по построению.

Земля клетки несёт и коридор: `world_height` прорезает её под транспортный стол,
поэтому путь, легший в тот же префаб (`transport_parts`), стоит на своей выемке,
а не на чужом рельефе. Исключений в сетке больше нет: с тех пор как депо стало
обычным чанком, клетка (−2,−2) генерируется наравне с остальными.

Нормали не считаются аналитически из градиента поля, как можно было бы ожидать:
весь мир проекта собирается плоским (экспорт `mesh_smooth_type = FACE`), и у
соседних граней и так разные нормали — фасет это стиль, а не дефект. Аналитические
нормали имели бы смысл только для гладкого шейдинга.
"""

from __future__ import annotations

import math

from mathutils import Vector

import frontier_layout as FL
import level_lib
import world_layout as W

#: Граница материала: вклад заносов и корыто разлома дают более тёмный снег,
#: тот же признак, что и у земли депо (`frontier_parts.build_ground`).
DEEP_TONE = 0.05


def _deep(x: float, z: float) -> bool:
    """Тёмный тон снега: занос якоря или корыто разлома в этой мировой точке."""
    return (abs(FL.drift_sum(x, z, W.ANCHOR_DRIFTS)) > DEEP_TONE
            or W.in_crack(x, z))


def _quads(i: int, j: int):
    """Список квадов клетки: (x0, z0, x1, z1) в мировых координатах.

    Все 400 квадов, без исключений: с тех пор как депо стало обычным чанком, в
    сетке не осталось клетки, у которой земля чужая. Раньше здесь вырезался
    квадрат авторской земли депо, и `TERRAIN_STEP` обязан был делить его границы
    нацело — иначе дырка резалась бы дробными квадами. Теперь это свойство
    нечем проверить, потому что дырок больше нет.
    """
    (x0, x1, z0, z1) = W.cell_bounds(i, j)
    step = W.TERRAIN_STEP
    n = int(round(W.BLK / step))
    out = []
    for a in range(n):
        for b in range(n):
            (qx0, qx1) = (x0 + a * step, x0 + (a + 1) * step)
            (qz0, qz1) = (z0 + b * step, z0 + (b + 1) * step)
            out.append((qx0, qz0, qx1, qz1))
    return out


def build_cell(materials, i: int, j: int):
    """Земля клетки `i:j` в локальных осях."""
    quads = _quads(i, j)

    (cx, cz) = W.cell_center(i, j)
    step = W.TERRAIN_STEP
    n = int(round(W.BLK / step))
    b = level_lib.LevelBuilder(materials)
    snow = b._index("Level_Snow")
    deep = b._index("Level_SnowDeep")

    # Узлы сетки собираются по возрастанию Blender-y (Unity-z = -Blender-y), как
    # в `build_ground`: порядок обхода граней даёт нормали вверх без пересчёта.
    index = {}
    for (qx0, qz0, qx1, qz1) in quads:
        for (px, pz) in ((qx0, qz0), (qx1, qz0), (qx1, qz1), (qx0, qz1)):
            key = (int(round((px - cx) / step)), int(round((pz - cz) / step)))
            if key in index:
                continue
            h = W.world_height(px, pz)
            index[key] = len(b.mb.verts)
            b.mb.verts.append(Vector((px - cx, -(pz - cz), h)))

    for (qx0, qz0, qx1, qz1) in quads:
        a = index[(int(round((qx0 - cx) / step)), int(round((qz0 - cz) / step)))]
        bb = index[(int(round((qx1 - cx) / step)), int(round((qz0 - cz) / step)))]
        c = index[(int(round((qx1 - cx) / step)), int(round((qz1 - cz) / step)))]
        d = index[(int(round((qx0 - cx) / step)), int(round((qz1 - cz) / step)))]
        # Тон грани — по центру: четыре угла у одной грани могут попасть в
        # разные заносы, а решение на грань — это и есть грань.
        (mx, mz) = ((qx0 + qx1) * 0.5, (qz0 + qz1) * 0.5)
        b.mb.add_face((a, bb, c, d), deep if _deep(mx, mz) else snow, "surface")

    return b.build("WorldCell_%d_%d" % (i, j), recalc=False)
