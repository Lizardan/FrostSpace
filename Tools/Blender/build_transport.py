# -*- coding: utf-8 -*-
"""Сборка и замер транспортного слоя: путь и дороги по клеткам.

    blender --background --factory-startup --python Tools/Blender/build_transport.py

Артефакт этого шага — не FBX. Куски линии eventually лягут в тот же префаб
клетки, что и земля (слой C), а пока существуют только как замер и как кадр
превью. Экспортировать их сейчас означало бы пересобирать 20 файлов дважды:
сначала путь на белом полу, потом тот же путь под прорезанным рельефом.

Что замеряется:

1. **Диапазон высот.** Каждая вершина внутри коридора обязана лежать на
   отметке `transport_floor` плюс принятый стек пути — не «примерно на
   рельефе». Это ловит и уехавший стол, и коробку, поставленную не там.
2. **Стык соседних клеток.** У куска каждой клетки есть вершина ровно на ребре
   решётки, и у соседа — та же точка с той же высотой. Меряется расстояние между
   множествами вершин, а не «на глаз по превью».
3. **Фаза шпал.** Через границу клетки шаг шпал обязан остаться 2,0 м: иначе на
   стыке шпала удвоится или пропадёт, и путь читался бы собранным из двух
   половинок.
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
import transport_parts as T  # noqa: E402
import world_layout as W  # noqa: E402

#: Насколько вершина внутри коридора имеет право отойти от стола. Стол — это
#: поверхность линии, поэтому **видимый** стек пути — от низа балласта (-0,78)
#: до гребня заноса (+0,28), дороги — от дна постели (-0,55) до шапки заноса
#: (+0,25). Ниже дна уходит погребённая юбка: и у постели, и у обочины наружные
#: точки опущены на 18-20 см, потому что там, где коридор идёт насыпью, плита
#: лежит выше дна, и короткая юбка легла бы с её гранью вровень — мерцание по
#: кромке, а не деталь. Глубже нечего: двухметровая юбка съедает рельеф, и
#: дорога выглядит вмазанной в снег. Верх остаётся 0,48 — выше гребня заноса
#: деталь в коридоре появляться не должна.
Z_LOW = -2.2
Z_HIGH = 0.48

#: Дальше края собственного коридора у куска детали быть не может: постель
#: уходит на 7,6 м при коридоре 22, обочина дороги — на 14 при коридоре 26.
#: Предел берётся от линии, а не одной константой: коридоры шире пути, и
#: «восьмиметровая» проверка ловила бы обочину как чужой объект.
OFF_AXIS_MARGIN = 0.5

TOL = 0.01


def _world(obj, i: int, j: int):
    """Вершины объекта в мировых координатах: локальные оси клетки плюс центр."""
    (cx, cz) = W.cell_center(i, j)
    for v in obj.data.vertices:
        yield (cx + v.co.x, cz - v.co.y, v.co.z)


def measure(objs) -> list:
    """Высоты и габарит каждого куска относительно стола.

    Допуск по высоте растёт с расстоянием от оси, и это не мягкость, а честный
    учёт способа измерения: `project_to_line` даёт ближайшую точку оси, а для
    вершины внешнего гребня на уклоне в 6 % эта ближайшая точка отстоит на
    несколько метров вдоль пути — стол там уже другой. Без такой поправки
    проверка требовала бы от боковой вершины высоты, которой в природе нет.
    """
    problems = []
    for (key, i, j, obj) in objs:
        ln = T.line_of(key)
        for (x, z, y) in _world(obj, i, j):
            (d, s, _dir) = W.project_to_line(x, z, ln["pts"])
            if d > ln["cut"][1] + OFF_AXIS_MARGIN:
                problems.append("%s %d:%d: деталь в %.1f м от оси — за краем "
                                "коридора" % (obj.name, i, j, d))
                continue
            grade = abs(W.level_at(ln["table"], s + 1.0)
                        - W.level_at(ln["table"], s - 1.0)) * 0.5
            slack = grade * d + 0.05
            dev = y - W.level_at(ln["table"], s)
            if not (Z_LOW - slack <= dev <= Z_HIGH + slack):
                problems.append("%s %d:%d: вершина на %+.2f м от стола "
                                "(допуск ±%.2f при уклоне %.1f%%)"
                                % (obj.name, i, j, dev, slack, grade * 100.0))
    return problems


def _section(obj, i: int, j: int, pts, s_joint: float, tol: float = 0.05):
    """Поперечное сечение метлы на данном пути: [(вбок от оси, высота)]."""
    out = []
    for (x, z, y) in _world(obj, i, j):
        (d, s, _dir) = W.project_to_line(x, z, pts)
        if abs(s - s_joint) <= tol:
            out.append((round(d, 3), round(y, 3)))
    return out


def measure_joints(objs) -> list:
    """Стык двух кусков: на границе клеток у соседей одинаковое сечение метлы.

    Мерить «вершину рядом с точкой стыка» нельзя: у пути все вершины стоят в
    стороне от оси (постель уходит на 7,6 м), и на оси их нет вовсе. Сравнивается
    набор (смещение от оси, высота) ровно на границе — это и есть то, что увидит
    глаз, когда два чанка встанут рядом. Берутся только метельные объекты
    (`Rail_*`, `Road_*`): шпала — коробка, и её совпадение с соседом проверяется
    фазой, а не сечением.
    """
    problems = []
    by_cell = {}
    for (key, i, j, obj) in objs:
        if not (obj.name.startswith("Rail_") or obj.name.startswith("Road_")):
            continue
        by_cell.setdefault(key, {})[(i, j)] = obj

    checked = 0
    worst = 0.0
    for ln in W.TRANSPORT:
        cells = by_cell.get(ln["k"], {})
        for (p, ca, cb) in W.line_crossings(ln["pts"]):
            oa = cells.get(ca)
            ob = cells.get(cb)
            if oa is None or ob is None:
                continue
            s_joint = W.project_to_line(p[0], p[1], ln["pts"])[1]
            sa = _section(oa, ca[0], ca[1], ln["pts"], s_joint)
            sb = _section(ob, cb[0], cb[1], ln["pts"], s_joint)
            if not sa or not sb:
                problems.append("%s: у границы %d:%d|%d:%d нет сечения"
                                % (ln["k"], ca[0], ca[1], cb[0], cb[1]))
                continue
            if sorted(set(sa)) != sorted(set(sb)):
                problems.append("%s: сечения у границы %d:%d|%d:%d разные по составу"
                                % (ln["k"], ca[0], ca[1], cb[0], cb[1]))
                continue
            gap = 0.0
            for (da, ya) in sa:
                near = min((abs(da - db), abs(ya - yb)) for (db, yb) in sb)
                gap = max(gap, near[0], near[1])
            worst = max(worst, gap)
            checked += 1
            if gap > TOL:
                problems.append("%s: стык %d:%d|%d:%d разошёлся на %.3f м"
                                % (ln["k"], ca[0], ca[1], cb[0], cb[1], gap))
    print("стыков проверено: %d, худший расход %.4f м" % (checked, worst))
    return problems


def measure_phase(key: str) -> list:
    """Шаг шпал по дуге через границу клетки: он обязан остаться `STATION_STEP`.

    Считается по пути вдоль трассы, а не по прямой между соседними шпалами: на
    углу хорда всегда короче дуги, и «1,996 м вместо 2,0» на повороте — это не
    дефект укладки, а геометрия. Последний узел списка — конец трассы, его шаг
    меньше по построению, и он не проверяется.
    """
    problems = []
    st = T.stations(key)[:-1]
    for k in range(len(st) - 1):
        (a, b) = (st[k], st[k + 1])
        ca = W.cell_of(a[0], a[1])
        cb = W.cell_of(b[0], b[1])
        if ca == cb:
            continue
        d = b[2] - a[2]
        if abs(d - T.STATION_STEP) > 1e-6:
            problems.append("%s: шаг шпал по дуге через границу %d:%d|%d:%d = %.3f м"
                            % (key, ca[0], ca[1], cb[0], cb[1], d))
    return problems


def build_all(verbose: bool = True):
    """Собрать куски всех линий: (измеряемые, пропы) — знаки меряются отдельно.

    Знаки путевые стоят на бровке и на повороте попадают внутрь коридора
    соседнего колена, поэтому проверять их тем же правилом «вершина на столе»
    нельзя: они проверятся как пропы слоя D.
    """
    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    objs = []
    props = []
    for ln in W.TRANSPORT:
        for (i, j) in W.line_cells(ln["pts"]):
            obj = (T.build_rail(mats, i, j) if ln["kind"] == "rail"
                   else T.build_road(mats, i, j, ln["k"]))
            if obj is not None:
                objs.append((ln["k"], i, j, obj))
            if ln["kind"] == "rail":
                ties = T.build_ties(mats, i, j)
                if ties is not None:
                    objs.append((ln["k"], i, j, ties))
                signs = T.build_signs(mats, i, j)
                if signs is not None:
                    props.append((ln["k"], i, j, signs))
    if verbose:
        rail = [o for (_k, _i, _j, o) in objs if o.name.startswith("Rail_")]
        road = [o for (_k, _i, _j, o) in objs if o.name.startswith("Road_")]
        verts = sum(len(o.data.vertices) for (_k, _i, _j, o) in objs)
        faces = sum(len(o.data.polygons) for (_k, _i, _j, o) in objs)
        pverts = sum(len(o.data.vertices) for (_k, _i, _j, o) in props)
        print("кусков: %d (путь %d, шпалы %d, дорога %d), вершин %d, граней %d; "
              "знаков: %d объектов, %d вершин"
              % (len(objs), len(rail), len(objs) - len(rail) - len(road),
                 len(road), verts, faces, len(props), pverts))
    return objs, props


def run() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    objs, props = build_all()
    if not objs:
        print("FAIL: ни одного куска линии")
        return 1
    problems = measure(objs)
    problems += measure_joints(objs)
    problems += measure_phase("rail")
    level_lib.report([o for (_k, _i, _j, o) in objs + props])
    if problems:
        for p in problems[:20]:
            print("ПРОБЛЕМА:", p)
        print("всего проблем: %d" % len(problems))
        return 1
    print("OK: транспорт лежит на столе, стыки совпадают, фаза шпал не рвётся")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(run())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
