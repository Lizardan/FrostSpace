"""Печатает rest-позиции костей ActionForge.

Нужно для проектирования мешей роботов: где плечо, локоть, кисть, шея, голова,
колено, стопа. Без этих чисел детали «на глаз» не совпадут с анимацией.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_actionforge_skeleton.py -- \
        AssetSource/ActionForge/walk_loop.glb
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy


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
        return 1
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=args[0])
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    print("ARMATURE", arm.name, "bones", len(arm.data.bones))
    for bone in arm.data.bones:
        head = arm.matrix_world @ bone.head_local
        tail = arm.matrix_world @ bone.tail_local
        print("%-20s parent=%-20s head=(%.4f %.4f %.4f) tail=(%.4f %.4f %.4f) len=%.4f"
              % (bone.name, bone.parent.name if bone.parent else "-",
                 *head, *tail, bone.length))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
