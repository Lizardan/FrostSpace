"""Ориентация импортированного FBX: оси, повороты, bbox, кость головы.

Нужна, чтобы камера крупного плана ставилась в реальных осях модели,
а не по догадке.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_orientation.py -- <file.fbx>
"""

from __future__ import annotations

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


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        return 2
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(args[0]))
    for obj in bpy.context.scene.objects:
        if obj.type not in {"ARMATURE", "MESH"}:
            continue
        print("OBJ", obj.name, obj.type,
              "loc", tuple(round(v, 3) for v in obj.location),
              "rot", tuple(round(v, 3) for v in obj.rotation_euler),
              "scale", tuple(round(v, 3) for v in obj.scale))
        if obj.type == "MESH":
            corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
            lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                         min(c.z for c in corners)))
            hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                         max(c.z for c in corners)))
            print("  bbox lo", tuple(round(v, 3) for v in lo),
                  "hi", tuple(round(v, 3) for v in hi),
                  "size", tuple(round(v, 3) for v in (hi - lo)))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    for bone in arm.data.bones:
        if bone.name in ("Bone.003", "Head", "Bone.002", "Neck", "Bone",
                         "Bone.001", "Body", "Hips", "head", "body"):
            head = arm.matrix_world @ bone.head_local
            tail = arm.matrix_world @ bone.tail_local
            print("BONE %-10s head=%s tail=%s" % (
                bone.name, tuple(round(v, 3) for v in head),
                tuple(round(v, 3) for v in tail)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
