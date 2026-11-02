"""Собирает силуэты дальнего слоя и экспортирует FBX.

Запуск (обычно через пайплайн):

    blender --background --factory-startup --python Tools/Blender/build_far.py
    blender --background --factory-startup --python Tools/Blender/build_far.py -- station

Без аргументов собирается всё, что `far_layer()` помечено как читаемое. Файлы
идут в `Assets/_Project/Content/Models/Level/` — только там работает
`LevelAssetPostprocessor`, подменяющий материалы на проектные (ADR 0002).

Размер меша сверяется с раскладкой жёстче, чем у руин: руина обязана быть
заявленной высоты, а карточка обязана быть заявленной высоты **и заявленной
ширины**, потому что и то и другое сошло с углового размера настоящего якоря.
Габабит не совпал — значит профиль съел чужой азимут, и сборка падает, а не
«примерно сошло».
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import far_parts  # noqa: E402
import level_lib  # noqa: E402
import world_layout as W  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))

#: Допуск сверки габарита карточки с раскладкой: профиль строится коробками
#: ровно по числам, так что сантиметр — это уже не формула, а ошибка сборки.
TOL = 0.01


def _cards(selected):
    """Читаемые карточки раскладки, отфильтрованные аргументом `--ids`."""
    cards = [c for c in W.far_layer() if c["draw"]]
    named = {c["k"] for c in cards}
    unknown = set(selected) - named
    if unknown:
        print("не собираются (не читаются из депо): %s" % ", ".join(sorted(unknown)))
        print("читаемые: %s" % ", ".join(sorted(named)))
        return None
    if selected:
        cards = [c for c in cards if c["k"] in selected]
    missing = named - set(far_parts.FILENAMES)
    if missing:
        print("читаемый якорь без имени файла: %s — добавь в far_parts.FILENAMES"
              % ", ".join(sorted(missing)))
        return None
    extra = set(far_parts.FILENAMES) - named
    if extra:
        print("файл назначен якорю, которого слой не рисует: %s"
              % ", ".join(sorted(extra)))
        return None
    return cards


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cards = _cards({a.lower() for a in args})
    if cards is None:
        return 1

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    os.makedirs(OUT, exist_ok=True)
    total = 0
    built = []

    for card in cards:
        obj = far_parts.build_card(mats, card)
        top = max((obj.matrix_world @ v.co).z for v in obj.data.vertices)
        span = max((obj.matrix_world @ v.co).x for v in obj.data.vertices) \
            - min((obj.matrix_world @ v.co).x for v in obj.data.vertices)
        if abs(top - card["h"]) > TOL or abs(span - card["w"]) > TOL:
            print("FAIL %s: габарит %.2f x %.2f м, раскладка обещает %.2f x %.2f "
                  "(допуск %.2f)" % (far_parts.FILENAMES[card["k"]], span, top,
                                     card["w"], card["h"], TOL))
            return 1
        print("%s: габарит %.2f x %.2f м (азимут %.1f°, макушка %.2f°, контраст "
              "%.2f), настоящий якорь на %.0f м" % (
                  far_parts.FILENAMES[card["k"]], span, top, card["bearing"],
                  card["deg"], card["dark"], card["real"]))
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(
            OUT, far_parts.FILENAMES[card["k"]]))
        built.append(card["k"])
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
