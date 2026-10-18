"""Собирает статические ассеты уровня FrostSpace и экспортирует FBX.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_level.py
    blender --background --factory-startup --python Tools/Blender/build_level.py -- terrain house

Без аргументов собирается всё. Секции: terrain, house, trees, props, interior.
FBX пишутся в Assets/_Project/Content/Models/Level/.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import level_layout  # noqa: E402
import level_lib  # noqa: E402
import level_parts  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))
SECTIONS = ("terrain", "house", "trees", "props", "interior")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    selected = {a.lower() for a in args} or {"all"}
    unknown = selected - set(SECTIONS) - {"all"}
    if unknown:
        print("неизвестные секции:", ", ".join(sorted(unknown)))
        return 2

    def wanted(kind: str) -> bool:
        return "all" in selected or kind in selected

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    total = 0
    built = []

    def emit(objs, filename: str) -> None:
        nonlocal total
        total += level_lib.report(objs)
        level_lib.export_static(objs, os.path.join(OUT, filename))
        built.append(filename)
        for obj in objs:
            bpy.data.objects.remove(obj, do_unlink=True)

    if wanted("terrain"):
        drifts = [(x, -z, rx, rz, dz)
                  for (x, z, rx, rz, dz) in level_layout.DRIFTS]
        emit([level_parts.build_terrain(mats, drifts)], "SnowTerrain.fbx")

    if wanted("house"):
        emit(level_parts.build_cabin(mats), "SnowCabin.fbx")

    if wanted("trees"):
        for name, seed in (("A", 0.4), ("B", 2.1), ("C", 3.8)):
            obj = level_parts.build_pine(mats, seed, name="Tree_Pine_%s" % name)
            emit([obj], "Tree_Pine_%s.fbx" % name)

    if wanted("props"):
        emit([level_parts.build_barrel(mats)], "Prop_Barrel.fbx")
        emit([level_parts.build_crate(mats)], "Prop_Crate.fbx")
        emit([level_parts.build_rock(mats, seed=0.4, name="Prop_Rock_A")],
             "Prop_Rock_A.fbx")
        emit([level_parts.build_rock(mats, seed=2.7, name="Prop_Rock_B")],
             "Prop_Rock_B.fbx")
        emit([level_parts.build_fence_post(mats)], "Prop_FencePost.fbx")
        emit([level_parts.build_fence_rail(mats)], "Prop_FenceRail.fbx")
        emit([level_parts.build_log_pile(mats)], "Prop_LogPile.fbx")

    if wanted("interior"):
        emit([level_parts.build_stove(mats)], "Prop_Stove.fbx")
        emit([level_parts.build_bed(mats)], "Prop_Bed.fbx")
        emit([level_parts.build_workbench(mats)], "Prop_Workbench.fbx")
        emit([level_parts.build_stool(mats)], "Prop_Stool.fbx")
        emit([level_parts.build_shelf(mats)], "Prop_Shelf.fbx")
        emit([level_parts.build_lamp(mats)], "Prop_Lamp.fbx")
        emit([level_parts.build_log_bundle(mats)], "Prop_LogBundle.fbx")

    if not built:
        raise RuntimeError("ничего не собрано")
    print("BUILT %d files, TOTAL tris %d" % (len(built), total))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
