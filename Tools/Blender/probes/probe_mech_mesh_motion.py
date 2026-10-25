"""Что на самом деле делает меш пака в клипе: габарит и низ по кадрам.

Запуск:
    blender --background --factory-startup --python probe_mech_mesh_motion.py -- Mech_FinnTheFrog walk

Считает габарит ОЦЕНЁННОГО меша (с арматурой и констрейнтами) по кадрам. Это
единственный способ отличить «кость-цель уехала» от «модель реально шагает на
метр»: у пака стопа — IK-цель, и её собственные ключи ничего не говорят о том,
где окажется геометрия.
"""

from __future__ import annotations

import os
import sys
import zipfile

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PACK = os.path.join(
    ROOT, "AssetSource",
    "Ultimate Space Kit - March 2023-20261024T221803Z-1-001.zip")
MEMBER = "Ultimate Space Kit - March 2023/Characters/FBX/%s.fbx"
STAGING = os.path.join(ROOT, "AssetSource", "_staging", "space_kit")


def extract(name: str) -> str:
    os.makedirs(STAGING, exist_ok=True)
    out = os.path.join(STAGING, name + ".fbx")
    if not os.path.exists(out):
        with zipfile.ZipFile(PACK) as archive:
            with open(out, "wb") as handle:
                handle.write(archive.read(MEMBER % name))
    return out


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass

    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    name = args[0] if args else "Mech_FinnTheFrog"
    clip = (args[1] if len(args) > 1 else "walk").lower()

    scene = bpy.context.scene
    scene.render.fps = 30
    # Стартовая сцена Blender содержит куб: без очистки `next(MESH)` берёт его.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=extract(name))
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    print("mesh=%s arm=%s verts=%d" % (mesh.name, arm.name, len(mesh.data.vertices)))
    arm.animation_data_create()

    action = next(a for a in bpy.data.actions
                  if a.name.split("|")[-1].lower() == clip)
    arm.animation_data.action = action
    start, end = (int(round(v)) for v in action.frame_range)

    print("%s %s frames %d..%d" % (name, clip, start, end))
    print("  f |    y lo..hi  (depth) |   z lo..hi  (height)")
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        depsgraph.update()
        evaluated = mesh.evaluated_get(depsgraph)
        data = evaluated.to_mesh()
        ys = [(evaluated.matrix_world @ v.co).y for v in data.vertices]
        zs = [(evaluated.matrix_world @ v.co).z for v in data.vertices]
        evaluated.to_mesh_clear()
        print("  %2d | %6.2f..%6.2f (%5.2f) | %6.2f..%6.2f (%5.2f)" % (
            frame, min(ys), max(ys), max(ys) - min(ys),
            min(zs), max(zs), max(zs) - min(zs)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
