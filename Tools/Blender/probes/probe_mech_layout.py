"""Разбор подготовленного рига ходуна: роли, опорные точки, клипы, скорости.

Запуск:
    blender --background --factory-startup --python probe_mech_layout.py -- FrogWalker

Печатает то, из чего дизайн берёт числа: точки костей после масштаба и сужения
стойки, длины клипов, ход корня и скорость стопы. Нужен, когда меняются цель
роста или стойка в `mech_lib.MECHS`.
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402

import mech_lib  # noqa: E402


def main() -> int:
    mech_lib.robot_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = args or list(mech_lib.MECHS)

    for name in names:
        arm, meta = mech_lib.load(name)
        print("\n%s  height=%.3f scale=%.4f stance=x%.3f" % (
            name, meta["height"], meta["scale"], meta["stance_factor"]))
        print("  layout:")
        for key, value in meta["layout"].items():
            print("    %-12s %s" % (key, value))
        mech_lib.prepare_clips(arm, meta)
        mech_lib.flatten_root(arm, meta)
        mech_lib.measure_speed(arm, meta)
        print("  drift:", meta["root_drift"])
        print("  speed:", meta["clip_speed"])
        print("  bones:", len(arm.data.bones))
    return 0


if __name__ == "__main__":
    sys.exit(main())
