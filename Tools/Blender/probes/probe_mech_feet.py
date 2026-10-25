"""Траектория стоп ходуна по кадрам: где контакт и как быстро едет стопа.

Запуск:
    blender --background --factory-startup --python probe_mech_feet.py -- FrogWalker walk

Печатает по кадрам Y/Z середины кости стопы (и её хвоста) для обеих ног. Нужно,
чтобы понять, где на самом деле стойка: у пака стопа — IK-цель, и «медиана
скорости» без разбора траектории даёт мусор.
"""

from __future__ import annotations

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402

import mech_lib  # noqa: E402


def main() -> int:
    mech_lib.robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    name = args[0] if args else "FrogWalker"
    clip = args[1] if len(args) > 1 else "walk"

    arm, meta = mech_lib.load(name)
    mech_lib.prepare_clips(arm, meta)
    mech_lib.flatten_root(arm, meta)

    info = meta["clips"][clip]
    arm.animation_data.action = info["action"]
    for track in arm.animation_data.nla_tracks:
        track.mute = True

    print("frames %d..%d" % (info["start"], info["end"]))
    print("  f | Foot.L head(y,z)  tail(y,z) | Foot.R head(y,z)  tail(y,z)")
    previous = {}
    for frame in range(info["start"], info["end"] + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        row = []
        for foot in ("Foot.L", "Foot.R"):
            pose_bone = arm.pose.bones[foot]
            head = arm.matrix_world @ pose_bone.head
            tail = arm.matrix_world @ pose_bone.tail
            step = 0.0
            if foot in previous:
                px, py = previous[foot]
                step = math.hypot(head.x - px, head.y - py) * mech_lib.FPS
            previous[foot] = (head.x, head.y)
            row.append("%6.3f,%6.3f  %6.3f,%6.3f  v=%5.2f" % (
                head.y, head.z, tail.y, tail.z, step))
        print("  %2d | %s | %s" % (frame, row[0], row[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
