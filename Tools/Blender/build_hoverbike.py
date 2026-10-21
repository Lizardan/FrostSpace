"""Собирает ховербайк FrostSpace и экспортирует FBX уровня.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_hoverbike.py

FBX пишется в Assets/_Project/Content/Models/Level/Hoverbike.fbx — рядом с
остальными статическими ассетами, чтобы его подхватил `LevelAssetPostprocessor`
и подставил материалы палитры `Level_*` вместо созданных при импорте.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import hoverbike_parts  # noqa: E402
import level_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()

    obj = hoverbike_parts.build_hoverbike(mats)
    tris = level_lib.report([obj])
    level_lib.export_static([obj], os.path.join(OUT, "Hoverbike.fbx"))
    bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT Hoverbike tris=%d" % tris)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
