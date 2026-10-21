"""Собирает снежный занос для открытого вида главного меню.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_menu_open.py

Выход: Assets/_Project/Content/Models/Level/MenuDrift.fbx — один крупный
фасетный занос, который в сцене масштабируется и поворачивается как проп.
Палитра — Level_Snow / Level_SnowDeep, поэтому импорт подхватывает те же
материалы, что и остальной уровень.
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

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()

    b = level_lib.LevelBuilder(mats)
    # Основная масса: широкая, низкая и пологая. Высокий и гранёный занос
    # читался бы как камень; снег — это плоскость с мягкими перекатами.
    b.ico((0.0, 0.0, -0.55), 4.2, "Level_Snow", subdivisions=2,
          scale=(1.35, 0.95, 0.30), jitter=0.12, seeds=1.7)
    # Второй язык заноса, чтобы силуэт не был правильной полусферой.
    b.ico((-3.1, 0.6, -0.65), 2.3, "Level_Snow", subdivisions=2,
          scale=(1.25, 0.85, 0.32), jitter=0.10, seeds=4.2)

    obj = b.build("MenuDrift")
    tris = level_lib.report([obj])
    level_lib.export_static([obj], os.path.join(OUT, "MenuDrift.fbx"))
    bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT MenuDrift tris=%d" % tris)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
