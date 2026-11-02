"""Собирает край открытого мира у депо и экспортирует FBX.

Запуск (обычно через `build_all.py --asset frontier`):

    blender --background --factory-startup --python Tools/Blender/build_frontier.py
    blender --background --factory-startup --python Tools/Blender/build_frontier.py -- ground track

Без аргументов собирается всё. Секции: ground, track, grove, prop.
Файлы идут в `Assets/_Project/Content/Models/Level/` — только там работает
`LevelAssetPostprocessor`, который подменяет материалы на проектные.

Имена файлов построены как `Frontier_<секция>[_<вариант>]`, и это не косметика:
`build_all.py` передаёт `--ids` и в этот скрипт (как имя секции), и в
`filter_by_ids` (как токен имени файла). Совпали токены — совпали и секция, и
проверка, и превью.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import frontier_parts  # noqa: E402
import level_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))
SECTIONS = ("ground", "track", "grove", "prop")


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    selected = {a.lower() for a in args} or {"all"}
    unknown = selected - set(SECTIONS) - {"all"}
    if unknown:
        print("неизвестные секции:", ", ".join(sorted(unknown)))
        return 2

    def wanted(kind: str) -> bool:
        return "all" in selected or kind in selected

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    total = 0
    built = []

    def emit(objs, filename: str) -> None:
        nonlocal total
        total += level_lib.report(objs)
        level_lib.export_static(objs, os.path.join(OUT, filename))
        built.append(filename)
        for obj in objs:
            bpy.data.objects.remove(obj, do_unlink=True)

    if wanted("ground"):
        emit([frontier_parts.build_ground(mats),
              frontier_parts.build_ground_collider(mats)],
             "Frontier_Ground.fbx")

    if wanted("track"):
        emit(frontier_parts.build_track(mats), "Frontier_Track.fbx")

    if wanted("grove"):
        # По одному файлу на кластер: инстансов в сцене их больше, чем
        # дизайнов, и плодить файлы «на каждую постановку» незачем.
        for band in ("near", "mid", "far"):
            for variant in (0, 1):
                emit([frontier_parts.build_grove(mats, band, variant)],
                     "Frontier_Grove_%s_%s.fbx" % (band.capitalize(),
                                                   "AB"[variant]))
        for variant in (0, 1):
            emit([frontier_parts.build_rock_band(mats, variant)],
                 "Frontier_Rocks_%s.fbx" % "AB"[variant])

    if wanted("prop"):
        emit([frontier_parts.build_telegraph_span(mats)],
             "Frontier_Prop_Telegraph.fbx")
        emit([frontier_parts.build_signal_mast(mats)],
             "Frontier_Prop_SignalMast.fbx")
        emit([frontier_parts.build_road_sign(mats)],
             "Frontier_Prop_RoadSign.fbx")
        emit([frontier_parts.build_wreck_hauler(mats)],
             "Frontier_Prop_Wreck.fbx")

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
