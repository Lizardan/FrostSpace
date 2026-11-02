"""Мировые данные: `world_layout.py` -> JSON, который читает Unity.

Один источник чисел: раскладку правят в модуле, файл — производная и вручную не
редактируется. Это та же логика, что у ADR 0035 для края депо: числа живут в
модуле, а меши, проверки, превью и C# читают результат. ADR 0005 не затронут:
прогон не касается ни сцен, ни префабов, ни `ProjectSettings` — он пишет один
TextAsset с данными.

Запуск: `python Tools/Blender/build_world.py` или через пайплайн
(`python Tools/Blender/build_all.py --asset world`).

Выход — `Assets/_Project/Content/Data/World/world_layout.json`. Это первый JSON
внутри `Assets/`: игровые данные пайплайна, а не манифест инструмента. Причина
занесена в ADR 0036 — `Tools/Blender/*_manifest.json` в сборку WebGL не попадают,
и миникарте, которой нужны узлы и линии в рантайме, такой путь недоступен.
"""

from __future__ import annotations

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import world_layout as W  # noqa: E402

#: Куда пишется файл — от корня проекта, как `outputs` в реестре `build_all.py`.
OUT = "Assets/_Project/Content/Data/World/world_layout.json"


def root() -> str:
    return os.path.abspath(os.path.join(HERE, "..", ".."))


def dump(path: str | None = None) -> str:
    """Собирает манифест и пишет его; возвращает абсолютный путь файла."""
    target = path or os.path.join(root(), OUT)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    text = json.dumps(W.manifest(), ensure_ascii=False, indent=1, sort_keys=True)
    with open(target, "w", encoding="utf-8") as stream:
        stream.write(text + "\n")
    return target


def main() -> int:
    W.configure_stdout()
    failed = [name for (name, ok, _detail) in W.invariants() if not ok]
    if failed:
        print("ОШИБКА: раскладка не проходит инварианты, JSON не пишется:")
        for name in failed:
            print("  - %s" % name)
        return 1
    target = dump()
    size = os.path.getsize(target)
    data = json.loads(open(target, encoding="utf-8").read())
    print("написан %s (%d КБ): узлов %d, якорей %d, клеток с контентом %d" % (
        os.path.relpath(target, root()).replace("\\", "/"), size // 1024,
        len(data["nodes"]), len(data["anchors"]), len(data["cellsContent"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
