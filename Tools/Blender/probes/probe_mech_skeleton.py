"""Печатает скелет ходуна: иерархию, головы/хвосты, родителей, длины.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_skeleton.py -- \
        "Assets/_Project/Content/Models/Mechs/ME_FrogWalker.fbx"
"""

from __future__ import annotations

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX")
        return 2

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=argv[0])
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    print("ARMATURE %s scale=%s" % (arm.name, tuple(round(v, 4) for v in arm.scale)))
    print("%-14s %-14s %-26s %-26s %7s %6s %6s"
          % ("bone", "parent", "head", "tail", "len", "conn", "deform"))
    for bone in arm.data.bones:
        head = arm.matrix_world @ bone.head_local
        tail = arm.matrix_world @ bone.tail_local
        print("%-14s %-14s (%7.3f %7.3f %7.3f) (%7.3f %7.3f %7.3f) %7.3f %6s %6s"
              % (bone.name, bone.parent.name if bone.parent else "-",
                 head.x, head.y, head.z, tail.x, tail.y, tail.z,
                 (tail - head).length, bone.use_connect, bone.use_deform))
    return 0


if __name__ == "__main__":
    sys.exit(main())
