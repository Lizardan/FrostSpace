"""Смотрит, что делают стопы и корень в клипах Walk/Run пака.

Нужно, чтобы понять, от чего мерить скорость земли: у ходуна кость Foot — цель
IK с горизонтальной ориентацией, и её высота в клипе может не меняться, из-за
чего «стойка» определяется неверно.

    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_stride.py -- FrogWalker
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402

import mech_lib  # noqa: E402


def main() -> int:
    unit = (sys.argv[sys.argv.index("--") + 1:] or ["FrogWalker"])[0]
    arm, meta = mech_lib.load(unit)
    mech_lib.prepare_clips(arm, meta)

    root = meta["roles"]["root"]
    for key in ("walk", "run"):
        info = meta["clips"][key]
        arm.animation_data.action = info["action"]
        for track in arm.animation_data.nla_tracks:
            track.mute = True
        print("\n--- %s %s frames %d..%d" % (unit, key, info["start"], info["end"]))
        print("%4s %8s %8s %8s %8s %8s %8s" % (
            "f", "hipY", "L.z", "L.y", "R.z", "R.y", "toeL.y"))
        for frame in range(info["start"], info["end"] + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            hip = arm.matrix_world @ arm.pose.bones["UpperLeg.L"].head
            fl = arm.pose.bones["Foot.L"]
            fr = arm.pose.bones["Foot.R"]
            print("%4d %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f" % (
                frame, hip.y,
                (arm.matrix_world @ fl.head).z, (arm.matrix_world @ fl.head).y,
                (arm.matrix_world @ fr.head).z, (arm.matrix_world @ fr.head).y,
                (arm.matrix_world @ fl.tail).y))
        arm.animation_data.action = None
        for track in arm.animation_data.nla_tracks:
            track.mute = False
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
