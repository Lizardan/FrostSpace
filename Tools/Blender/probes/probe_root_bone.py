"""Смотрит, как FBX с костью Root приезжает обратно: оси, масштаб, ход.

Нужен, чтобы понять, почему после добавления Root уезжает замер стопы и
габарит: экспорт печёт space transform, и новая корневая кость может менять
приведение осей при импорте.
"""

from __future__ import annotations

import os
import sys

import bpy

SPECIES = "Wolf"
if "--" in sys.argv:
    extra = sys.argv[sys.argv.index("--") + 1:]
    if extra:
        SPECIES = extra[0]

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..",
                    "Assets", "_Project", "Content", "Models", "RobotAnimals",
                    SPECIES, "RA_%s.fbx" % SPECIES)


def main() -> int:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(PATH))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    print("species", SPECIES, "path exists", os.path.exists(PATH))
    print("fps", bpy.context.scene.render.fps)
    print("arm matrix_world", [tuple(round(v, 4) for v in row)
                               for row in arm.matrix_world])
    print("arm loc", tuple(round(v, 4) for v in arm.location),
          "scale", tuple(round(v, 4) for v in arm.scale),
          "rot", tuple(round(v, 4) for v in arm.rotation_euler))
    print("bones", len(arm.data.bones))
    for name in ("Root", "Bone", "Bone.001", "Bone.010", "Bone.019"):
        bone = arm.data.bones.get(name)
        if bone is None:
            print(name, "НЕТ")
            continue
        print(name, "head_local", tuple(round(v, 3) for v in bone.head_local),
              "tail_local", tuple(round(v, 3) for v in bone.tail_local))

    action = next((a for a in bpy.data.actions if "move_loop" in a.name), None)
    print("action", action.name if action else None,
          "range", tuple(round(v, 3) for v in action.frame_range) if action else None)
    arm.animation_data_create()
    arm.animation_data.action = action
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    for frame in (1, 20, 40):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in ("Root", "Bone.019"):
            pose = arm.pose.bones.get(name)
            if pose is None:
                continue
            head = arm.matrix_world @ pose.head
            print("frame", frame, name, "world",
                  tuple(round(v, 3) for v in head))
    return 0


if __name__ == "__main__":
    sys.exit(main())
