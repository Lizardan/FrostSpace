"""Печатает rest-точки костей скелета ActionForge после widen_rig.

Нужен для проектирования деталей: где на самом деле плечо, локоть, кисть,
бедро, колено, стопа. Запуск:

    blender --background --factory-startup --python Tools/Blender/probes/probe_rig_points.py
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import robot_lib  # noqa: E402

WANTED = (
    "root", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "Head",
    "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
    "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
    "thigh_l", "calf_l", "foot_l", "ball_l",
    "thigh_r", "calf_r", "foot_r", "ball_r",
)


def main() -> int:
    robot_lib.configure_stdout()
    arm, _actions = robot_lib.load_rig()
    points = robot_lib.rig_points(arm)
    for name in WANTED:
        if name not in points:
            print("MISSING", name)
            continue
        head, tail = points[name]
        print("%-12s head=(%7.3f,%7.3f,%7.3f) tail=(%7.3f,%7.3f,%7.3f)"
              % (name, head.x, head.y, head.z, tail.x, tail.y, tail.z))
    return 0


if __name__ == "__main__":
    sys.exit(main())
