"""Аудит посадки предметов клетки: кто висит, кто утонул.

Прогон:
    blender --background --factory-startup --python Tools/Blender/probes/probe_decor_ground.py -- [-2 -2]

Меряется не аналитическая высота, а то, на что предмет реально опирается: луч
от подошвы вниз по всем остальным объектам клетки и самое верхнее попадание.
Причина в том, что пола у клетки несколько — рельеф, терраса двора, настил
перрона, — и предмет, который «висит 0,9 м над землёй», стоит на террасе и
правильный. Аналитика же молчала ровно там, где была права: сосны в чанке
депо стояли на отметке своей точки, а тело получалось в зеркальной половине
мира (зеркальщина `CP.merge`), и только такой луч это показывает.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import world_layout as W  # noqa: E402

CELLS = os.path.join(os.path.dirname(os.path.dirname(TOOLS)), "Assets",
                     "_Project", "Content", "Models", "Level", "Cells")

#: Зазор, который глаз ещё читает касанием.
TOUCH_TOL = 0.06
#: Декор обязан касаться опоры: висеть выше 12 см или тонуть глубже метра —
#: обе беды видны в кадре.
DECOR_UP = 0.12
DECOR_DOWN = 1.00
#: Постройкам и пути претензий нет: у них юбка уходит в грунт на метры по
#: замыслу (`BURY`), и это норма, а не дефект.
DECOR = ("Grove", "Yard", "Posts", "Signs", "Rocks", "Drift", "Prop")
#: Глубину закапывания проверяем у предметов, у которых основание видно: у
#: сосны, валуна и заноса провал ниже метра — это уже пропавший предмет. Столб
#: и знак тонут глубже: у них под снегом стойка, и в земле её не видно по
#: замыслу, но в пять метров она быть не обязана.
SUNK_CHECK = ("Grove", "Rocks", "Drift", "Prop")
SUNK_POSTS = ("Posts", "Signs", "Masts")
SUNK_POSTS_DOWN = 2.00
#: Сколько метров над подошвой стартовать луч. Брать надо с запасом: юбка
#: стены уходит в грунт на 1,6 м, и луч, пущенный с полуметра, стартует под
#: землёй, ничего не находит, и закопанное приходит в отчёт как висящее.
PROBE_DOWN = 3.00
#: Сколько худших строк печатать на объект.
TOP = 6


def cell_parts(i: int, j: int) -> list[str]:
    out = []
    for f in sorted(os.listdir(CELLS)):
        if f.endswith(".fbx") and f.endswith("_%d_%d.fbx" % (i, j)):
            out.append(os.path.join(CELLS, f))
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


#: Что считается полом. Брать самое верхнее попадание по всем объектам
#: нельзя: у постройки подошва.foundation лежит под её же стеной, стена под
#: кровлей, и «опорой» стала бы крыша на пять метров выше. Полом клетки бывают
#: рельеф, полотна дорог и пути, боковой путь и плиты двора.
SUPPORT = ("Ground", "DepotApron", "DepotPlatform", "Road_", "Rail_",
           "Siding_")


def support(objs, skip, x: float, y: float, z: float):
    """Самая верхняя грань пола над точкой — то, на чём предмет стоит.

    Луч идёт сверху клетки, а не от самой подошвы: короткий луч на три метра
    ведёт себя нестабильно (над ровной террасой он не находил грань, которая
    стоит прямо под ним), а длинный находит всегда. Если самое верхнее
    попадание выше подошвы — предмет закопан, если ниже — висит.
    """
    best = None
    for (name, other) in objs.items():
        if other is skip or not any(k in name for k in SUPPORT):
            continue
        inv = other.matrix_world.inverted()
        origin = inv @ Vector((x, y, z + 60.0))
        direction = (inv.to_3x3() @ Vector((0, 0, -1))).normalized()
        hit, loc, _n, _i = other.ray_cast(origin, direction)
        if not hit:
            continue
        wz = (other.matrix_world @ loc).z
        if best is None or wz > best:
            best = wz
    return best


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    (i, j) = (int(argv[0]), int(argv[1])) if len(argv) >= 2 else W.DEPOT_CELL

    bpy.ops.wm.read_factory_settings(use_empty=True)
    paths = cell_parts(i, j)
    if not paths:
        print("НЕТ ФАЙЛОВ клетки %d_%d" % (i, j))
        return 1
    objs = import_all(paths)
    if not any(n.startswith("Ground") for n in objs):
        print("НЕТ Ground в клетке")
        return 1

    print("КЛЕТКА %d_%d, объектов %d" % (i, j, len(objs)))
    bad = 0
    measured = 0
    for name, obj in sorted(objs.items()):
        if name.endswith("_Collider") or name.startswith("Ground"):
            continue        # земля и есть опора, мерить её нечем
        decor = any(k in name for k in DECOR)
        if not decor:
            continue        # постройки меряем другим правилом: у них юбка в
                            # грунте по замыслу, а висячим может быть только декор
        measured += 1
        world = [obj.matrix_world @ v.co for v in obj.data.vertices]
        base = min(p.z for p in world)
        rows = []
        for wp in world:
            if wp.z - base > 0.35:
                continue        # меряем только подошву предмета
            if abs(wp.x) > 300.0 or abs(wp.y) > 300.0:
                continue        # за край клетки: там опора у соседнего чанка
            s = support(objs, obj, wp.x, wp.y, wp.z)
            rows.append((wp.z - s if s is not None else 999.0, wp, s))
        if not rows:
            continue
        hang = [r for r in rows if r[0] > TOUCH_TOL]
        worst = max(rows, key=lambda r: abs(r[0]))
        sunk = any(k in name for k in SUNK_CHECK)
        posts = any(k in name for k in SUNK_POSTS)
        if decor:
            defect = any(r[0] > DECOR_UP for r in rows) or (
                sunk and any(r[0] < -DECOR_DOWN for r in rows)) or (
                posts and any(r[0] < -SUNK_POSTS_DOWN for r in rows))
        else:
            defect = bool(hang)
        if defect:
            bad += 1
        print("%-26s %-9s худший %+7.3f м  подошвы %4d  %s" %
              (name, "декор" if decor else "постройка", worst[0], len(rows),
               "ДЕФЕКТ" if defect else ""))
        rows.sort(key=lambda r: -abs(r[0]))
        for r in rows[:TOP]:
            (gap, wp, s) = r
            if not decor and -TOUCH_TOL < gap < 1e5:
                continue
            print("    (%8.2f, %8.2f) низ=%8.3f опора=%s зазор=%+.3f" %
                  (wp.x, wp.y, wp.z, "ничего" if s is None else "%.3f" % s, gap))
    print("ИТОГ: предметов декора с дефектом %d из %d" % (bad, measured))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
