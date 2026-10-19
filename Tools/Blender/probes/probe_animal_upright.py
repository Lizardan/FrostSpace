"""Проверка гипотезы «upright»: что делает поворот арматуры на -90° по X.

Импортирует FBX, печатает bbox до и после поворота арматуры, чтобы понять,
нужен ли этот поворот для превью.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_upright.py -- <file.fbx>
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bpy
from mathutils import Vector


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def bbox(meshes):
    corners = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    return lo, hi


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        return 2
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(args[0]))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    mesh = max(meshes, key=lambda o: len(o.data.vertices))
    lo, hi = bbox(meshes)
    print("BEFORE lo=%s hi=%s size=%s" % (
        tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi),
        tuple(round(v, 3) for v in (hi - lo))))
    print("MESH rot", tuple(round(v, 3) for v in mesh.rotation_euler),
          "parent", mesh.parent.name if mesh.parent else None)
    print("ARM rot", tuple(round(v, 3) for v in arm.rotation_euler))
    arm.rotation_mode = "XYZ"
    arm.rotation_euler.x -= math.pi * 0.5
    bpy.context.view_layer.update()
    lo, hi = bbox(meshes)
    print("AFTER  lo=%s hi=%s size=%s" % (
        tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi),
        tuple(round(v, 3) for v in (hi - lo))))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
