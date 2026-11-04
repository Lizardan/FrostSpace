"""Аудит декора клетки: кто висит над землёй и кто в неё утоплен.

Прогон:
    blender --background --factory-startup --python Tools/Blender/probes/probe_decor_ground.py -- [-2 -2]

Причина «висячих» деревьев не в том, что отметка взята плохая: рельеф рендерится
плоскими 15-метровыми квадами, а предмет ставится по аналитической высоте своей
оси. В вогнутом месте хорда ниже формулы, и нога повисает. Поэтому меряется
расстояние от низа предмета до ФАКТИЧЕСКОЙ поверхности земли — лучом вниз по
мешу террейна, а не по формуле.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
ROOT = os.path.dirname(TOOLS)
CELLS = os.path.join(os.path.dirname(ROOT), "Assets", "_Project", "Content",
                     "Models", "Level", "Cells")
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import world_layout as W  # noqa: E402

#: Предметы тоньше этого не считаются: шляпка гвоздя и кромка плиты не «висят».
TOUCH_TOL = 0.06
#: Насколько допустимо утонуть: юбка сосны и валун входят в грунт на десятки
#: сантиметров по замыслу, поэтому снизу прощаем до этого предела.
SINK_OK = -1.2
#: Сколько худших строк печатать на объект.
TOP = 6


def cell_parts(i: int, j: int) -> list[str]:
    d = CELLS
    out = []
    for f in sorted(os.listdir(d)):
        if f.endswith(".fbx") and f.endswith("_%d_%d.fbx" % (i, j)):
            out.append(os.path.join(d, f))
    return out


def import_all(paths: list[str]) -> dict:
    objs = {}
    for p in paths:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=p)
        for o in bpy.data.objects:
            if o in before or o.type != "MESH":
                continue
            objs[o.name] = o
    return objs


def ground(objs: dict):
    for name, o in objs.items():
        if name.startswith("Ground"):
            return o
    return None


def surface_at(gnd, x: float, y: float) -> float | None:
    """Отметка фактической земли под точкой: луч сверху вниз по мешу террейна.

    FBX импортируется с компенсацией 0,01 и поворотом 90° (ADR 0007), поэтому
    `ray_cast` отвечает в локальных осях объекта: без переноса в мировые
    сравнивали бы высоту предмета с его же координатой по Y.
    """
    origin = gnd.matrix_world.inverted() @ Vector((x, y, 500.0))
    direction = gnd.matrix_world.inverted().to_3x3() @ Vector((0, 0, -1))
    hit, loc, _n, _idx = gnd.ray_cast(origin, direction.normalized())
    return (gnd.matrix_world @ loc).z if hit else None


#: Декор обязан касаться земли: и висеть, и утонуть по самое основание — обе
#: беды видны в кадре. Постройкам и пути это не претензия: у них юбка уходит в
#: грунт на метры по замыслу (`BURY`), и закапывать их — норма.
DECOR = ("Grove", "Yard", "Posts", "Signs", "Rocks", "Drift", "Prop")
DECOR_UP = 0.12
DECOR_DOWN = 1.00


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    (i, j) = (int(argv[0]), int(argv[1])) if len(argv) >= 2 else W.DEPOT_CELL

    bpy.ops.wm.read_factory_settings(use_empty=True)
    paths = cell_parts(i, j)
    if not paths:
        print("НЕТ ФАЙЛОВ клетки %d_%d" % (i, j))
        return 1
    objs = import_all(paths)
    gnd = ground(objs)
    if gnd is None:
        print("НЕТ Ground в клетке")
        return 1

    print("КЛЕТКА %d_%d, объектов %d, земля %s" % (i, j, len(objs), gnd.name))
    bad = 0
    for name, obj in sorted(objs.items()):
        if obj is gnd or name.endswith("_Collider"):
            continue
        decor = any(k in name for k in DECOR)
        zs = [(obj.matrix_world @ v.co).z for v in obj.data.vertices]
        base = min(zs)
        rows = []
        for v, z in zip(obj.data.vertices, zs):
            if z - base > 0.35:
                continue        # меряем только подошву предмета
            wp = obj.matrix_world @ v.co
            s = surface_at(gnd, wp.x, wp.y)
            if s is None:
                rows.append((999.0, wp, None))
                continue
            rows.append((z - s, wp, s))
        if not rows:
            continue
        hang = [r for r in rows if r[0] > TOUCH_TOL]
        deep = [r for r in rows if r[0] < SINK_OK]
        worst = max(rows, key=lambda r: abs(r[0]))
        if decor:
            defect = any(r[0] > DECOR_UP or r[0] < -DECOR_DOWN for r in rows)
        else:
            defect = bool(hang)
        if defect:
            bad += 1
        print("%-26s %-8s %8s  подошвы точек %4d  %s" %
              (name, "декор" if decor else "постройка",
               ("%+.3f" % worst[0]), len(rows), "ДЕФЕКТ" if defect else ""))
        rows.sort(key=lambda r: -r[0])
        for r in rows[:TOP]:
            (gap, wp, s) = r
            if not decor and -TOUCH_TOL < gap < 1e5:
                continue
            # Аналитика против меша: если `world_height` под этой же точкой
            # не совпадает с поверхностью, виноват не предмет, а земля.
            (lx, lz) = (-wp.x, -wp.y)
            an = W.world_height(*W.to_world(lx, lz))
            print("    (%8.2f, %8.2f) низ=%8.3f земля=%s зазор=%+.3f "
                  "локально=(%7.1f, %7.1f) world_height=%8.3f" %
                  (wp.x, wp.y, wp.z, "нет луча" if s is None else "%.3f" % s,
                   gap, lx, lz, an))
    print("ИТОГ: объектов с дефектом %d из %d" % (bad, len(objs) - 1))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
