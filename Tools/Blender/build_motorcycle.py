"""Собирает снежный мотоцикл FrostSpace и экспортирует FBX уровня.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_motorcycle.py

FBX пишется в Assets/_Project/Content/Models/Level/Motorcycle.fbx рядом с
остальной статикой уровня: `LevelAssetPostprocessor` подставит материалы
палитры Level_* вместо созданных при импорте.

В FBX три узла — MotorcycleBody, MotorcycleWheelF, MotorcycleWheelR, — чтобы в
Unity колесо вращалось вокруг своей оси.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import level_lib  # noqa: E402
import motorcycle_parts  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()

    parts = motorcycle_parts.build_motorcycle(mats)
    tris = level_lib.report(parts)
    level_lib.export_static(parts, os.path.join(OUT, "Motorcycle.fbx"))
    for part in parts:
        bpy.data.objects.remove(part, do_unlink=True)

    print("BUILT Motorcycle tris=%d" % tris)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
