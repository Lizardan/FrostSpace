"""Собирает роботов FrostSpace и экспортирует FBX с клипами.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- R1
    blender --background --factory-startup --python Tools/Blender/build_robots.py
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- --jump -- R1

Без аргументов собираются все дизайны из `robot_designs.BUILDERS`.
Флаг `--jump` добавляет к клипам jump_start/jump_loop/jump_land из ActionForge;
без списка роботов он собирает только R1 — прыжок нужен игроку, а не всем NPC.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import robot_designs  # noqa: E402
import robot_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Robots"))


def main() -> int:
    robot_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    include_jump = any(a.lower() == "--jump" for a in args)
    selected = {a.lower() for a in args if not a.startswith("--")}
    if include_jump and not selected:
        selected = {"r1"}

    arm, _actions = robot_lib.load_rig(include_jump=include_jump)
    built = []
    for builder in robot_designs.BUILDERS:
        name, obj, _mats = builder(arm)
        if selected and not any(name.lower() == s or
                                name.lower().startswith(s + "_") for s in selected):
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        robot_lib.bind(obj, arm)
        robot_lib.stats(obj, arm)
        path = os.path.join(OUT, name + ".fbx")
        robot_lib.export_fbx(arm, obj, path)
        built.append(path)

    if not built:
        raise RuntimeError("ни один робот не собран")
    print("BUILT", len(built))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
