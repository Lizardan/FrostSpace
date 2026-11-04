"""Наполнение клетки: лес, наносы, валуны и шипы по всей карте.

Зачем отдельной частью. Клетка с одной землёй — не пустой кадр, а плохой: с
перрона станции и с обочины дороги видно, что мир кончается за пятнадцать метров
от полотна. В депо обстановка уже была (`depot_parts._grove`), но она авторская:
двадцать координат, написанных руками под конкретный двор. Здесь то же правило
работает на всех 121 клетке, и расстановка считается, а не пишется.

Плотность взята с эталона `MenuRideSegment_C`: у дороги два ряда сосен, между
стволами наносы, по кромкам валуны, изредка ледяной шип. Эталон даёт примерно
одно дерево на 110 м² полосы; на клетку 600×600 м это 3200 стволов и полтора
миллиона трис — весь бюджет кадра на одну клетку (ADR 0029 п. 4). Поэтому
плотность **придорожная**: густо в двух рядах по обе стороны каждой нити, реже в
третьем, и совсем редко — в глубине клетки. Лес читается коридором вдоль дороги,
а не ковром по всей площади, и это соответствует миру: дороги прокладывали по
самому твёрдому насту, а в стороне стоят пустоши.

Три числа держат цену:

* ярусы хвои по ряду (`TIERS`): ближний ряд 3, средний 2, дальний 1 — 316, 244 и
  172 трис на дерево. Дальше никто не считает ветки, а силуэт остаётся;
* силуэты (`VARIANTS`): пятнадцать базовых деревьев на клетку строятся один раз
  и вливаются повторами с поворотом. Иначе на одну клетку приходилось бы двести
  сорок сборок меша;
* `SCATTER_*` — редкая россыпь по пустой части клетки: она нужна, чтобы за
  придорожным коридором не начиналось «поле в никуда», но цену держит низкой.

Правила отказа (`_open`), и все они про кадр, а не про формулы:

* за краем клетки тело срезается пополам — отсюда `FILL_EDGE`;
* у оси линии дерево стоит на откосе, а на дороге — на полотне: отсюда плечи
  `ROAD_CLEAR`/`RAIL_CLEAR`, разные, потому что у пути выемка шире;
* внутри тёплого двора снег чистят, и там своя обстановка: `PAD_CLEAR` от
  прямоугольника узла;
* в корыте разлома ставить нечего — там лёд и пустота (`in_crack`).

Расстановка детерминирована: всё считается по `_hash` от мировых координат
точки, а не по счётчику внутри клетки. Поэтому соседние клетки стыкуются — дерево
на границе получает одна из них, но обе считают его в одном и том же месте, и
шва в лесу не видно.
"""

from __future__ import annotations

import math

import bpy

import cell_props as CP
import level_lib
import level_parts as LP
import world_layout as W

#: Отступ от ребра клетки: дерево с кроной 2,8 м у самого края срезается
#: вместе с чанком, и с соседней клетки видно половину ствола.
FILL_EDGE = 6.0

#: Плечо от оси дороги. Настил 4 м плюс кромка 0,9 плюс сугроб: ближе дерево
#: стоит на откосе выемки, а не на снегу.
ROAD_CLEAR = 10.5

#: Плечо от оси пути: выемка магистрали шире дорожной (`RAIL_CUT`).
RAIL_CLEAR = 13.0

#: Зазор от прямоугольника тёплого узла: двор чистят, и дерево в нём — ошибка.
PAD_CLEAR = 8.0

#: Шаг станций вдоль оси линии: 600 м на клетку дают 54 станции при 11 м.
ROW_STEP = 11.0

#: Ряды у линии: (плечо, разброс вдоль, разброс по плечу, ярусы, доля станций).
#: Числа повторяют эталон меню: ближний ряд густой и подробный, дальний редкий
#: и дешёвый — на глаз это один и тот же лес, а по трисам вдвое дешевле.
ROWS = ((13.5, 4.4, 3.4, 3, 0.90),
        (27.0, 5.2, 4.0, 2, 0.70),
        (40.0, 6.0, 4.6, 1, 0.45))

#: Россыпь по пустой части клетки: шаг сетки и доли приёмки.
SCATTER_STEP = 46.0
SCATTER_PINE = 0.10
SCATTER_DRIFT = 0.22
SCATTER_ROCK = 0.06
SCATTER_SHARD = 0.03

#: Сколько разных силуэтов дерева строить на клетку и на ярус. Восемь: при пяти
#: в переднем плане читались двойники — одна и та же сосна стояла через десять
#: метров дважды. Сборка силуэта дешёвая, повтор в кадре — нет.
VARIANTS = 8

#: Доля стволов, под которыми лежит нанос: голый ствол на плоском снегу
#: выглядит поставленным, а не выросшим.
UNDER_DRIFT = 0.34

#: Насколько предмет уходит в снег: у сосны корни, у валуна — нижняя треть.
SINK = -0.30


def _hash(a: float, b: float) -> float:
    """Детерминированное число 0…1: расстановка воспроизводима без RNG."""
    v = math.sin(a * 12.9898 + b * 78.233) * 43758.5453
    return v - math.floor(v)


def _world(i: int, j: int, lx: float, lz: float):
    """Локальные оси клетки -> мировые. Blender-y = −(z − центр), как у земли."""
    (cx, cz) = W.cell_center(i, j)
    return (cx + lx, cz + lz)


def _ground(i: int, j: int, lx: float, lz: float) -> float:
    """Отметка рельефа под предметом: то же поле, из которого собрана земля."""
    (x, z) = _world(i, j, lx, lz)
    return W.world_height(x, z)


def _open(i: int, j: int, lx: float, lz: float) -> bool:
    """Можно ли поставить предмет в этой точке клетки (см. правила отказа выше)."""
    half = W.BLK * 0.5
    if abs(lx) > half - FILL_EDGE or abs(lz) > half - FILL_EDGE:
        return False
    (x, z) = _world(i, j, lx, lz)
    if W.in_crack(x, z):
        return False
    for ln in W.TRANSPORT:
        clear = RAIL_CLEAR if ln["kind"] == "rail" else ROAD_CLEAR
        if W.project_to_line(x, z, ln["pts"])[0] < clear:
            return False
    for n in W.NODES:
        if n["rect"] and W._rect_dist(x, z, n["rect"]) < PAD_CLEAR:
            return False
    return True


def _local(i: int, j: int, x: float, z: float):
    """Мировые координаты -> локальные оси клетки (обратное к `_world`)."""
    (cx, cz) = W.cell_center(i, j)
    return (x - cx, z - cz)


def _along(i: int, j: int):
    """Стволы вдоль осей линий: (lx, lz, seed, scale, ярусы).

    Идём по каждой нити с шагом `ROW_STEP` и на каждой станции ставим кандидата
    в каждый ряд с обеих сторон. Смещения вдоль и по плечу — из `_hash` от
    **мирового** пути `s`: точка принадлежит линии, а не клетке, поэтому соседние
    клетки считают её одинаково и лес на стыке не рвётся.
    """
    out = []
    for ln in W.TRANSPORT:
        pts = ln["pts"]
        s0 = 0.0
        for (a, b) in zip(pts, pts[1:]):
            seg = math.hypot(b[0] - a[0], b[1] - a[1])
            if seg <= 1e-6:
                continue
            (dx, dz) = ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
            (nx, nz) = (-dz, dx)
            s = s0 + (ROW_STEP - (s0 % ROW_STEP)) % ROW_STEP
            while s < s0 + seg:
                t = s - s0
                for (row, (off, jit, spread, tiers, chance)) in enumerate(ROWS):
                    # Пропуск считается на станцию и ряд, но не на сторону: у
                    # обоих плеч одна причина отсутствия — редкость ряда.
                    if _hash(s * 0.27, row * 3.7 + 1.3) > chance:
                        continue
                    for side in (-1.0, 1.0):
                        if _hash(s * 0.61, side * 4.9 + row * 2.1) > chance:
                            continue
                        # Смещение вдоль оси: ряды не должны стоять в линейку.
                        al = (_hash(s * 0.71, side * 5.1 + row * 2.3) - 0.5) * 2.0 * jit
                        # Смещение по плечу: кромка леса волнистая, а не по линейке.
                        sh = (_hash(s * 0.13, side * 7.3 + row * 1.7) - 0.5) * 2.0 * spread
                        px = a[0] + dx * (t + al) + nx * side * (off + sh)
                        pz = a[1] + dz * (t + al) + nz * side * (off + sh)
                        (lx, lz) = _local(i, j, px, pz)
                        if not _open(i, j, lx, lz):
                            continue
                        out.append((lx, lz, s * 0.37 + side * 1.9 + row,
                                    0.85 + 0.50 * _hash(s * 0.19, side + row),
                                    tiers))
                s += ROW_STEP
    return out


def _scatter(i: int, j: int):
    """Редкая россыпь по пустой части клетки: (lx, lz, seed) узлов сетки.

    Сетка мировая (начало в нуле карты), поэтому узел на границе клеток
    считается одинаково у обеих; принадлежность решается по индексу клетки.
    """
    out = []
    (cx, cz) = W.cell_center(i, j)
    half = W.BLK * 0.5
    gx0 = math.floor((cx - half) / SCATTER_STEP) - 1
    gz0 = math.floor((cz - half) / SCATTER_STEP) - 1
    for gx in range(gx0, gx0 + int(W.BLK / SCATTER_STEP) + 3):
        for gz in range(gz0, gz0 + int(W.BLK / SCATTER_STEP) + 3):
            x = (gx + _hash(gx * 0.31, gz * 0.77)) * SCATTER_STEP
            z = (gz + _hash(gx * 0.53 + 9.1, gz * 0.13)) * SCATTER_STEP
            (lx, lz) = _local(i, j, x, z)
            if not _open(i, j, lx, lz):
                continue
            out.append((lx, lz, gx * 0.37 + gz * 1.13))
    return out


def _variants(materials, i: int, j: int):
    """Базовые силуэты клетки: {ярусы: [объект, …]} и список валунов.

    Силуэт строится один раз и вливается копиями: пятнадцать сборок вместо
    двухсот сорока — и правка `level_parts.pine` остаётся одной на весь мир.
    """
    out = {}
    for tiers in sorted({row[3] for row in ROWS}):
        made = []
        for k in range(VARIANTS):
            b = level_lib.LevelBuilder(materials)
            LP.pine(b, 3.1 + k * 7.3 + i * 1.7 + j * 2.9, (0.0, 0.0),
                    1.0, 0.0, tiers)
            made.append(b.build("FillPine_%d_%d_%d_%d" % (i, j, tiers, k)))
        out[tiers] = made
    out["rock"] = [LP.build_rock(materials, 0.4 + k * 1.7) for k in range(3)]
    return out


def _place(b, src, lx, lz, ground, rot, scale, sink=SINK):
    """Влить копию силуэта с поворотом и масштабом, не забирая сам силуэт.

    `cell_props.merge` строит предмет под одну клетку и убирает источник. Лесу
    на клетку нужно двести сорок стволов, и строить двести сорок мешей — это
    секунды сборки на каждую клетку. Поэтому копируем: копия уходит в меш, а
    исходник живёт до конца клетки.
    """
    dup = src.copy()
    dup.data = src.data.copy()
    # Масштаб вливается через вершины: `merge` умеет только поворот, потому что
    # в депо деревья были одного размера по построению.
    if abs(scale - 1.0) > 1e-6:
        for v in dup.data.vertices:
            v.co.x *= scale
            v.co.y *= scale
            v.co.z *= scale
    CP.merge(b, dup, lx, lz, ground, sink=sink, rot=rot)


def build_cell_fill(materials, i: int, j: int):
    """Собрать часть `Fill_<i>_<j>`: лес, наносы, валуны и шипы клетки.

    Пустая клетка пропускается: файл на 121 клетку без содержимого — это 121
    лишний FBX в префабе и 121 лишний объект в иерархии.
    """
    points = [(lx, lz, seed, scale, tiers)
              for (lx, lz, seed, scale, tiers) in _along(i, j)]
    spots = _scatter(i, j)
    # Россыпь разыгрывается один раз по узлу, вид предмета — по его номеру.
    pines = [p for p in points]
    for (n, (lx, lz, seed)) in enumerate(spots):
        if _hash(seed, 1.9) < SCATTER_PINE:
            pines.append((lx, lz, seed + 4.4, 0.80 + 0.55 * _hash(seed, 2.7), 2))
    if not pines and not spots:
        return []

    b = level_lib.LevelBuilder(materials)
    kinds = _variants(materials, i, j)
    for (n, (lx, lz, seed, scale, tiers)) in enumerate(pines):
        made = kinds[tiers]
        src = made[int(_hash(seed, 3.7) * len(made)) % len(made)]
        ground = _ground(i, j, lx, lz)
        _place(b, src, lx, lz, ground, 360.0 * _hash(seed, 9.1), scale)
        if _hash(seed, 5.3) > UNDER_DRIFT:
            continue
        # Нанос под стволом: смещение долей масштаба, чтобы у мелкой сосны не
        # было сугроба в рост человека.
        ox = lx + 1.1 * scale * (_hash(seed, 6.1) - 0.5) * 2.0
        oz = lz + 1.1 * scale * (_hash(seed, 7.7) - 0.5) * 2.0
        if _open(i, j, ox, oz):
            CP.drift(b, ox, oz, _ground(i, j, ox, oz), 1.5 * scale,
                     2.2 * scale, 0.55 * scale, seed=seed + 1.3)

    for (n, (lx, lz, seed)) in enumerate(spots):
        if _hash(seed, 4.3) < SCATTER_DRIFT:
            CP.drift(b, lx, lz, _ground(i, j, lx, lz),
                     2.2 + 2.4 * _hash(seed, 8.1), 3.0 + 3.2 * _hash(seed, 8.9),
                     0.9 + 1.1 * _hash(seed, 9.7), seed=seed)
        if _hash(seed, 2.3) < SCATTER_ROCK:
            rocks = kinds["rock"]
            src = rocks[int(_hash(seed, 6.9) * len(rocks)) % len(rocks)]
            _place(b, src, lx, lz, _ground(i, j, lx, lz),
                   37.0 * n, 0.85 + 0.5 * _hash(seed, 7.3), sink=-0.35)
        if _hash(seed, 3.3) < SCATTER_SHARD:
            CP.shard(b, lx, lz, _ground(i, j, lx, lz),
                     1.2 + 1.6 * _hash(seed, 1.1), seed=seed + 3.1)

    obj = b.build("Fill_%d_%d" % (i, j))
    obj.name = "Fill_%d_%d" % (i, j)
    # Силуэты отработали: в меш ушли копии, а сами объекты иначе остались бы в
    # сцене до конца сборки — по восемнадцать на клетку.
    for made in kinds.values():
        for src in made:
            mesh = src.data
            bpy.data.objects.remove(src, do_unlink=True)
            if mesh is not None and mesh.users == 0:
                bpy.data.meshes.remove(mesh)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print("  наполнение %d_%d: стволов %d, узлов россыпи %d, трис %d"
          % (i, j, len(pines), len(spots), tris))
    return [obj]
