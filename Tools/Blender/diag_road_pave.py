"""Диагностика дорожного полотна: где оно лежит относительно стола линии.

Берёт собранный FBX клетки, находит меш дороги и сравнивает высоту её
полотна (материал `Level_Stone`) с отметкой стола дорожной линии в раскладке.
Полотно должно лежать на столе (+0,02), а не ниже него.

Запуск:
    blender --background --factory-startup --python diag_road_pave.py -- <file.fbx> <road_key>
"""

from __future__ import annotations

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import world_layout as W  # noqa: E402


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 2:
        print("укажи FBX после -- и ключ дороги (например road 0)")
        return 2
    path = argv[0]
    key = argv[1]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
    road_objs = [n for n in meshes if n.startswith("Road_")]
    if not road_objs:
        print("в файле нет дорожных мешей:", sorted(meshes))
        return 1

    ln = None
    for l in W.TRANSPORT:
        if l["k"] == key:
            ln = l
    if ln is None:
        print("нет линии", key, "в раскладке")
        return 1

    base = os.path.splitext(os.path.basename(path))[0]
    # WorldCell_<i>_<j> — индексы клетки из имени файла.
    parts = base.split("_")
    (ci, cj) = (int(parts[1]), int(parts[2]))
    center = W.cell_center(ci, cj)

    for name in sorted(road_objs):
        obj = meshes[name]
        mw = obj.matrix_world
        mats = [s.material.name if s.material else "?" for s in obj.material_slots]
        print("=== %s ===" % name)
        print("   материалы: %s" % ", ".join(mats))
        shown = 0
        up = 0
        down = 0
        top_z = None
        for poly in obj.data.polygons:
            mi = poly.material_index
            mat = mats[mi] if mi < len(mats) else "?"
            if mat != "Level_Stone":
                continue
            corners = [mw @ obj.data.vertices[i].co for i in poly.vertices]
            z = [c.z for c in corners]
            if top_z is None or max(z) > top_z:
                top_z = max(z)
            # Отметка стола линии в этой точке: проецируем на ось линии.
            wx = center[0] + corners[0].x
            wz = center[1] - corners[0].y
            d, s, _dir = W.project_to_line(wx, wz, ln["pts"])
            table = W.level_at(ln["table"], s)
            nrm = (mw.to_3x3() @ poly.normal).normalized()
            if nrm.z > 0.7:
                up += 1
            elif nrm.z < -0.7:
                down += 1
            if shown < 6:
                print("   полотно: z=%.3f..%.3f | стол линии %.3f | разница %.3f | до оси %.1f м | нормаль z=%.3f"
                      % (min(z), max(z), table, min(z) - table, d, nrm.z))
            shown += 1
        if shown:
            print("   всего граней полотна: %d, из них вверх %d, вниз %d, верх полотна z=%.3f"
                  % (shown, up, down, top_z))
    return 0


if __name__ == "__main__":
    sys.exit(main())
