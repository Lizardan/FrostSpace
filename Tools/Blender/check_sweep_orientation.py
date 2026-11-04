"""Эталон: от чего зависит ориентация протянутого профиля.

Строит одну и ту же трубу вдоль пути «вперёд» и «назад» и смотрит, куда
смотрят верхние грани. Если ориентация зависит от направления пути, это
значит, что перевёрнуты одни только те линии, чей путь идёт «не тем
концом».

Запуск:
    blender --background --factory-startup --python check_sweep_orientation.py
"""

from __future__ import annotations

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import frontier_parts as FP  # noqa: E402
import level_lib  # noqa: E402
import lowpoly  # noqa: E402
import transport_parts as TP  # noqa: E402


def build(profile, path, name, recalc=True, as_built=False):
    # `_sweep` в transport_parts/depot_parts всегда прогоняет профиль через
    # `level_lib.outward_profile`. Без этого эталон врал: правая полка
    # (`rim_profile R`) показывалась вывернутой, хотя в собранной клетке
    # `outward_profile` разворачивает её и верх смотрит вверх.
    if as_built:
        profile = level_lib.outward_profile(profile)
    mats = {"Level_SnowDeep": lowpoly.make_material("Level_SnowDeep", "#FFFFFF"),
            "Level_Metal": lowpoly.make_material("Level_Metal", "#888888"),
            "Level_Stone": lowpoly.make_material("Level_Stone", "#999999")}
    b = level_lib.LevelBuilder(mats)
    b.tube(path, [(1.0, 1.0)] * len(path), "Level_SnowDeep", n=4, profile=profile,
           ref=(0.0, 0.0, 1.0), fan_caps=True)
    obj = b.build(name, recalc=recalc)
    mw = obj.matrix_world
    top = None
    for poly in obj.data.polygons:
        cen = mw @ poly.center
        if top is None or cen.z > top:
            top = cen.z
    up = down = 0
    for poly in obj.data.polygons:
        cen = mw @ poly.center
        if cen.z < top - 0.05:
            continue
        nrm = (mw.to_3x3() @ poly.normal).normalized()
        if nrm.z > 0.5:
            up += 1
        elif nrm.z < -0.5:
            down += 1
    print("%-28s путь %s: верх смотрит вверх=%d, вниз=%d  -> %s"
          % (name, "вперёд" if path[-1][0] > path[0][0] else "назад",
             up, down, "ВЕРХ ВВЕРХ" if up > down else "ВЕРХ ВНИЗ"))


def check_caps() -> int:
    """Регрессия: оба торца трубы смотрят наружу, ни один не просвечивает.

    Первый торец долго строился тем же обходом, что и последний, и смотрел
    внутрь: на конце дороги была видна «сквозная» грань. Знак объёма меша
    такой дефект не ловит, поэтому проверяется направление нормали каждого
    торца отдельно.
    """
    mats = {"Level_SnowDeep": lowpoly.make_material("Level_SnowDeep", "#FFFFFF")}
    b = level_lib.LevelBuilder(mats)
    pts = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (20.0, 0.0, 0.0)]
    b.tube(pts, [(1.0, 1.0)] * len(pts), "Level_SnowDeep", n=4,
           profile=[(-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0)],
           ref=(0.0, 0.0, 1.0), fan_caps=True)
    obj = b.build("caps_probe", recalc=False)
    mw = obj.matrix_world
    start = end = 0
    for poly in obj.data.polygons:
        cen = mw @ poly.center
        nrm = (mw.to_3x3() @ poly.normal).normalized()
        if abs(cen.x) < 0.01 and nrm.x < -0.5:
            start += 1
        elif abs(cen.x - 20.0) < 0.01 and nrm.x > 0.5:
            end += 1
    ok = start > 0 and end > 0
    print("торцы: начальный наружу=%d, конечный наружу=%d -> %s"
          % (start, end, "OK" if ok else "FAIL"))
    return 0 if ok else 1


def main() -> int:
    fwd = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (20.0, 0.0, 0.0)]
    back = [(0.0, 0.0, 0.0), (-10.0, 0.0, 0.0), (-20.0, 0.0, 0.0)]
    # Полотное и борт дороги больше не константы модуля: их отметка зависит от
    # самой дороги (`road_crown`), поэтому проверка берёт профиль с базовой
    # кроной.
    # Кромки дороги — два профиля (левая и правая), а не одна лента через всю
    # дорогу: именно из-за ленты снег ложился поверх льда.
    road = ((TP.pave_profile(TP.W.PAVE_CROWN), "pave_profile"),
            (TP.rim_profile(TP.W.PAVE_CROWN, 1.0), "rim_profile R"),
            (TP.rim_profile(TP.W.PAVE_CROWN, -1.0), "rim_profile L"),
            (TP.track_profile(TP.W.PAVE_CROWN), "track_profile"))
    all_profiles = ((FP.RAIL_PROFILE, "RAIL_PROFILE"),
                    (FP.BED_PROFILE, "BED_PROFILE")) + road
    print("--- как в клетках: outward_profile + recalc=False ---")
    for prof, nm in all_profiles:
        build(prof, fwd, nm + " built вперёд", recalc=False, as_built=True)
        build(prof, back, nm + " built назад", recalc=False, as_built=True)
    print("--- обход профиля без нормализации (диагностика) ---")
    for prof, nm in all_profiles:
        build(prof, fwd, nm + " raw вперёд", recalc=False)
        build(prof, back, nm + " raw назад", recalc=False)
    print("--- с пересчётом (recalc=True) ---")
    for prof, nm in ((FP.RAIL_PROFILE, "RAIL_PROFILE"),
                     (FP.BED_PROFILE, "BED_PROFILE")) + road:
        build(prof, fwd, nm + " recalc вперёд", recalc=True)
    return check_caps()


if __name__ == "__main__":
    sys.exit(main())
