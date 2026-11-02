"""Собирает меши наследия и экспортирует FBX.

Запуск (обычно через пайплайн):

    blender --background --factory-startup --python Tools/Blender/build_heritage.py
    blender --background --factory-startup --python Tools/Blender/build_heritage.py -- slide

Без аргументов собирается всё. Файлы идут в `Assets/_Project/Content/Models/
Level/` — только там работает `LevelAssetPostprocessor`, подменяющий материалы на
проектные (ADR 0002).

Высота каждого меша сверяется с `world_layout.py`: руина обязана быть ровно той
высоты, которую обещает карта и что нарисовано на плане. Расхождение — ошибка
сборки, а не «примерно сошло».
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import heritage_parts  # noqa: E402
import level_lib  # noqa: E402
import world_layout as W  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))

#: Допуск сверки вершины меша с высотой из раскладки: фаска, снежная шапка и
#  округление в JSON складываются в десятки сантиметров.
TOP_TOL = 0.8


def _top(obj) -> float:
    return max((obj.matrix_world @ v.co).z for v in obj.data.vertices)


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    selected = {a.lower() for a in args} or set(heritage_parts.SECTIONS)
    unknown = selected - set(heritage_parts.SECTIONS)
    if unknown:
        print("неизвестные секции:", ", ".join(sorted(unknown)))
        return 2

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    os.makedirs(OUT, exist_ok=True)
    total = 0
    built = []

    for name in heritage_parts.SECTIONS:
        if name not in selected:
            continue
        build, filename, ruin_key = heritage_parts.SECTIONS[name]
        obj = build(mats)
        top = _top(obj)
        declared = W.ruin(ruin_key)["h"]
        if abs(top - declared) > TOP_TOL:
            print("FAIL %s: вершина %.2f м, раскладка обещает %.2f м (допуск %.2f)"
                  % (filename, top, declared, TOP_TOL))
            return 1
        print("%s: вершина %.2f м (раскладка %.1f), габарит %.1f x %.1f x %.1f" % (
            filename, top, declared, obj.dimensions.x, obj.dimensions.y,
            obj.dimensions.z))
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(OUT, filename))
        built.append(filename)
        bpy.data.objects.remove(obj, do_unlink=True)

    if not built:
        raise RuntimeError("ничего не собрано")
    print("BUILT %d files, TOTAL tris %d" % (len(built), total))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
