"""Собирает роботов FrostSpace и экспортирует FBX с тремя клипами.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- R1
    blender --background --factory-startup --python Tools/Blender/build_robots.py

Без аргументов собираются все дизайны из `robot_designs.BUILDERS`.
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
    selected = {a.lower() for a in args}

    arm, _actions = robot_lib.load_rig()
    built = []
    for builder in robot_designs.BUILDERS:
        name, obj, _mats = builder(arm)
        if selected and not any(name.lower().startswith(s) or
                                s in name.lower() for s in selected):
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
