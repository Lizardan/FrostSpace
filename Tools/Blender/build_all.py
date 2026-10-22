"""Единая точка входа пайплайна ассетов FrostSpace.

Зачем: в `Tools/Blender/` лежит больше десятка скриптов (`build_*`, `render_*`,
`selftest`, `measure_locomotion`, `fbx_units`), и каждый запускался своей командой.
Из-за этого путь к ассету зависел от памяти запускающего. Здесь один вход:

    сборка -> проверка инвариантов -> превью -> отчёт

Скрипт запускается **системным Python** (не внутри Blender): он сам находит `blender.exe`
и запускает нужный скрипт в `--background --factory-startup`. Поэтому блендер-окно не
открывается, а каждый шаг остаётся изолированным процессом с чистым кодом возврата.

Запуск:

    python Tools/Blender/build_all.py --list
    python Tools/Blender/build_all.py --asset hoverbike
    python Tools/Blender/build_all.py --asset robots --ids R1 R2
    python Tools/Blender/build_all.py --asset robots --ids R1 --jump
    python Tools/Blender/build_all.py --asset level --ids terrain house --check
    python Tools/Blender/build_all.py --all --check
    python Tools/Blender/build_all.py --asset robots --preview
    python Tools/Blender/build_all.py --all --dry-run

Правило проекта (`AGENTS.md`, `docs/BLENDER.md`, ADR 0017): ассет считается сделанным,
только если он появился из прогона этого скрипта, прошёл `--check` и оставил превью.
Разовая правка в GUI-Blender — черновик, пока не закреплена скриптом.

Путь к Blender не хардкодится: `--blender`, затем env `BLENDER_EXE`, затем `PATH`,
затем типовые каталоги установки (Steam, Blender Foundation).
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
LOGS = os.path.join(HERE, "logs")

#: Каталоги вывода ассетов: всё, что скрипт обязан создать.
MODELS = "Assets/_Project/Content/Models"
PREVIEWS = "AssetSource/Previews"

#: Таймауты по умолчанию (секунды). Переопределяются `--timeout`.
TIMEOUT_BUILD = 1800
TIMEOUT_PREVIEW = 900
TIMEOUT_CHECK = 600


# --------------------------------------------------------------------- реестр


def _step(script: str, per: str = "none", args: list[str] | None = None,
          runner: str = "blender") -> dict:
    """Один шаг пайплайна.

    per = "file" — шаг запускается на каждый файл из `outputs` (путь идёт первым
    аргументом после `--`); per = "none" — один раз без файла.
    """
    return {"script": script, "per": per, "args": list(args or []), "runner": runner}


#: Пайплайн ассетов. Ключ — идентификатор для `--asset`.
#: outputs — glob-шаблоны относительно корня проекта; пустой список = нет артефактов.
PIPELINE: dict[str, dict] = {
    "robots": {
        "script": "build_robots.py",
        "outputs": [MODELS + "/Robots/*.fbx"],
        "previews": [_step("render_robot_previews.py", "file",
                           ["--out", PREVIEWS + "/Robots"])],
        # ball_l есть только у роботов: для животных замер локомотива не применим.
        "checks": [_step("measure_locomotion.py", "file")],
    },
    "animals": {
        "script": "build_animals.py",
        "outputs": [MODELS + "/RobotAnimals/*/RA_*.fbx"],
        "previews": [_step("render_animal_previews.py", "file",
                           ["--out", PREVIEWS + "/Animals"])],
        # Кости ball_l у животных нет, поэтому проверка своя: клипы, веса и
        # скорость стопы против animal_manifest.json.
        "checks": [_step("check_animal_clips.py", "file")],
    },
    "level": {
        "script": "build_level.py",
        "outputs": [
            MODELS + "/Level/SnowTerrain.fbx",
            MODELS + "/Level/SnowCabin.fbx",
            MODELS + "/Level/Tree_Pine_*.fbx",
            MODELS + "/Level/Prop_*.fbx",
        ],
        "previews": [_step("render_level_previews.py", "none",
                           ["--out", PREVIEWS + "/Level"])],
        "checks": [],
    },
    "hoverbike": {
        "script": "build_hoverbike.py",
        "outputs": [MODELS + "/Level/Hoverbike.fbx"],
        "previews": [],
        "checks": [],
    },
    "motorcycle": {
        "script": "build_motorcycle.py",
        "outputs": [MODELS + "/Level/Motorcycle.fbx"],
        "previews": [],
        "checks": [],
    },
    "menu-drift": {
        "script": "build_menu_open.py",
        "outputs": [MODELS + "/Level/MenuDrift.fbx"],
        "previews": [],
        "checks": [],
    },
    "menu-views": {
        "script": "build_menu_views.py",
        "outputs": [
            MODELS + "/Level/MenuStationMast.fbx",
            MODELS + "/Level/MenuIceWall.fbx",
            MODELS + "/Level/MenuCanyonBridge.fbx",
            MODELS + "/Level/MenuDepotGate.fbx",
        ],
        "previews": [],
        "checks": [],
    },
    "menu-wreck": {
        "script": "build_wreck_parts.py",
        "outputs": [
            MODELS + "/Level/MenuWreckWing.fbx",
            MODELS + "/Level/MenuWreckEngine.fbx",
            MODELS + "/Level/MenuWreckFin.fbx",
        ],
        "previews": [],
        "checks": [],
    },
}

#: Глобальные проверки: выполняются при `--check` независимо от выбранных ассетов.
GLOBAL_CHECKS = [
    {"script": "selftest.py", "per": "none", "args": [], "runner": "blender"},
    # Единица внутри FBX: быстрый разбор бинарного файла, bpy не нужен.
    {"script": "fbx_units.py", "per": "output", "args": [], "runner": "python"},
]


# ------------------------------------------------------------------ окружение


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def _blender_candidates() -> list[str]:
    """Типовые места установки. Путь — параметр окружения, а не константа в коде."""
    found: list[str] = []
    patterns = [
        os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"),
                     "Blender Foundation", "Blender*", "blender.exe"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs",
                     "Blender Foundation", "Blender*", "blender.exe"),
    ]
    for pattern in patterns:
        found.extend(sorted(glob.glob(pattern)))
    # Steam: библиотека может лежать на любом диске.
    for drive in "CDEFGHIJKLMNOPQRSTUVWXYZ":
        for library in ("Steam", "SteamLibrary"):
            path = os.path.join("%s:\\" % drive, library, "steamapps", "common",
                                "Blender", "blender.exe")
            if os.path.exists(path):
                found.append(path)
    return found


def find_blender(explicit: str | None) -> str:
    if explicit:
        path = os.path.abspath(explicit)
        if not os.path.exists(path):
            raise SystemExit("НЕТ ФАЙЛА: %s (--blender)" % path)
        return path
    env = os.environ.get("BLENDER_EXE")
    if env:
        path = os.path.abspath(env)
        if not os.path.exists(path):
            raise SystemExit("НЕТ ФАЙЛА: %s (BLENDER_EXE)" % path)
        return path
    from_path = shutil.which("blender")
    if from_path:
        return from_path
    candidates = _blender_candidates()
    if candidates:
        return candidates[-1]  # свежая версия — последняя в сортировке
    raise SystemExit(
        "blender.exe не найден. Укажи --blender <путь> или задай BLENDER_EXE.")


# ------------------------------------------------------------------- запуск


def _command(blender: str, step: dict, extra: list[str], target: str | None,
             python: str) -> list[str]:
    args = []
    if target:
        args.append(target)
    args.extend(extra)
    script = os.path.join(HERE, step["script"])
    if step["runner"] == "python":
        return [python, script] + args
    cmd = [blender, "--background", "--factory-startup", "--python", script]
    if args:
        cmd.append("--")
        cmd.extend(args)
    return cmd


def _quote(cmd: list[str]) -> str:
    return " ".join('"%s"' % part if " " in part else part for part in cmd)


def run_step(label: str, cmd: list[str], timeout: int, log) -> tuple[bool, str]:
    started = time.time()
    print("  -> %s" % label)
    print("     %s" % _quote(cmd))
    log.write("\n$ %s\n" % _quote(cmd))
    try:
        proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired:
        log.write("[TIMEOUT после %d с]\n" % timeout)
        print("     ТАЙМАУТ %d с" % timeout)
        return False, "таймаут %d с" % timeout
    output = (proc.stdout or "") + (proc.stderr or "")
    log.write(output)
    elapsed = time.time() - started
    tail = [line for line in output.strip().splitlines() if line.strip()][-6:]
    for line in tail:
        print("     | %s" % line)
    if proc.returncode != 0:
        print("     FAIL код=%d за %.1f с" % (proc.returncode, elapsed))
        return False, "код %d" % proc.returncode
    print("     OK за %.1f с" % elapsed)
    return True, "%.1f с" % elapsed


def expand(patterns: list[str]) -> list[str]:
    files: list[str] = []
    for pattern in patterns:
        full = os.path.join(ROOT, pattern.replace("/", os.sep))
        files.extend(sorted(glob.glob(full)))
    seen, unique = set(), []
    for path in files:
        if path not in seen:
            seen.add(path)
            unique.append(path)
    return unique


def relative(path: str) -> str:
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def filter_by_ids(files: list[str], ids: list[str]) -> list[str]:
    """Ограничить превью и проверки выбранными именами (`--ids`).

    Сравнение идёт по имени файла без расширения и по границам `_`: `R1` не должен
    затянуть `R10_Pipewright`.
    """
    if not ids:
        return files
    patterns = [re.compile(r"(?:^|_)%s(?:_|$)" % re.escape(item), re.IGNORECASE)
                for item in ids]
    picked = []
    for path in files:
        base = os.path.splitext(os.path.basename(path))[0]
        if any(pattern.search(base) for pattern in patterns):
            picked.append(path)
    return picked


# ---------------------------------------------------------------- пайплайн


def print_registry() -> None:
    print("%-12s %-24s %-6s %-8s %s" % ("АССЕТ", "СКРИПТ СБОРКИ", "ФАЙЛЫ",
                                        "ПРЕВЬЮ", "ПРОВЕРКИ"))
    for name, asset in PIPELINE.items():
        files = len(expand(asset["outputs"])) if asset["outputs"] else 0
        previews = ", ".join(s["script"] for s in asset["previews"]) or "-"
        checks = ", ".join(s["script"] for s in asset["checks"]) or "-"
        print("%-12s %-24s %-6d %-8s %s" % (name, asset["script"], files,
                                            "да" if asset["previews"] else "нет",
                                            checks))
        print("%12s превью: %s" % ("", previews))
    print()
    print("Глобальные проверки (--check): %s"
          % ", ".join(s["script"] for s in GLOBAL_CHECKS))


def main() -> int:
    configure_stdout()

    parser = argparse.ArgumentParser(
        description="Единая сборка, проверка и превью Blender-ассетов FrostSpace.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="Примеры:\n"
               "  python Tools/Blender/build_all.py --asset hoverbike\n"
               "  python Tools/Blender/build_all.py --asset robots --ids R1 --jump --preview\n"
               "  python Tools/Blender/build_all.py --all --check\n")
    parser.add_argument("--asset", action="append", default=[], metavar="ID",
                        help="идентификатор ассета (можно повторять)")
    parser.add_argument("--all", action="store_true", help="все ассеты реестра")
    parser.add_argument("--list", action="store_true", help="показать реестр и выйти")
    parser.add_argument("--ids", nargs="+", default=[], metavar="NAME",
                        help="имена/секции для скрипта сборки (после --)")
    parser.add_argument("--jump", action="store_true",
                        help="прокинуть --jump в build_robots.py (клипы прыжка)")
    parser.add_argument("--preview", action="store_true", help="рендерить превью")
    parser.add_argument("--check", action="store_true",
                        help="проверки: selftest.py, fbx_units.py, профильные шаги")
    parser.add_argument("--skip-build", action="store_true",
                        help="не пересобирать: только превью/проверки по готовым файлам")
    parser.add_argument("--dry-run", action="store_true",
                        help="напечатать команды, ничего не запускать")
    parser.add_argument("--blender", default=None, help="путь к blender.exe")
    parser.add_argument("--timeout", type=int, default=0,
                        help="таймаут шага в секундах (0 — значения по умолчанию)")
    args = parser.parse_args()

    if args.list:
        print_registry()
        return 0

    selected = list(PIPELINE) if args.all else list(dict.fromkeys(args.asset))
    if not selected:
        parser.error("укажи --asset <id>, --all или --list")
    unknown = [name for name in selected if name not in PIPELINE]
    if unknown:
        parser.error("неизвестный ассет: %s (см. --list)" % ", ".join(unknown))
    if args.jump and any(name != "robots" for name in selected):
        print("предупреждение: --jump относится только к robots, для остальных игнорируется")

    python = sys.executable
    blender = "-" if args.dry_run else find_blender(args.blender)
    print("Blender: %s" % blender)
    print("Python:  %s" % python)
    print("Ассеты:  %s" % ", ".join(selected))
    print()

    build_timeout = args.timeout or TIMEOUT_BUILD
    preview_timeout = args.timeout or TIMEOUT_PREVIEW
    check_timeout = args.timeout or TIMEOUT_CHECK

    os.makedirs(LOGS, exist_ok=True)
    log_path = os.path.join(LOGS, "build_all_%s.log" % time.strftime("%Y%m%d_%H%M%S"))
    results: list[tuple[str, bool, str]] = []
    produced: list[str] = []

    with open(log_path, "w", encoding="utf-8") as log:
        log.write("build_all %s\n" % time.strftime("%Y-%m-%d %H:%M:%S"))
        for name in selected:
            asset = PIPELINE[name]
            extra = list(asset.get("args", []))
            if args.ids:
                extra.extend(args.ids)
            if args.jump and name == "robots":
                extra.insert(0, "--jump")

            print("[%s] сборка" % name)
            cmd = _command(blender, {"script": asset["script"], "runner": "blender"},
                           extra, None, python)
            if args.skip_build:
                print("  -> пропущено (--skip-build)")
            elif args.dry_run:
                print("  -> %s" % _quote(cmd))
                results.append((name + ":сборка", True, "dry-run"))
            else:
                ok, note = run_step("сборка %s" % asset["script"], cmd,
                                    build_timeout, log)
                results.append((name + ":сборка", ok, note))

            files = expand(asset["outputs"]) if asset["outputs"] else []
            if asset["outputs"]:
                if files:
                    print("  файлов на выходе: %d" % len(files))
                elif not args.dry_run:
                    results.append((name + ":артефакты", False, "файлов нет"))
                    print("  ОШИБКА: ни один файл из outputs не создан")
            produced.extend(files)

            for step in asset["previews"] if args.preview else []:
                targets = (filter_by_ids(files, args.ids) if step["per"] == "file"
                           else [None])
                if step["per"] == "file" and not targets:
                    results.append((name + ":превью", False, "нет файлов под --ids"))
                    continue
                print("[%s] превью: %s" % (name, step["script"]))
                for target in targets:
                    cmd = _command(blender, step, step["args"], target, python)
                    if args.dry_run:
                        print("  -> %s" % _quote(cmd))
                        results.append((name + ":превью", True, "dry-run"))
                    else:
                        ok, note = run_step("превью %s" % os.path.basename(target or "-"),
                                            cmd, preview_timeout, log)
                        results.append((name + ":превью", ok, note))

            for step in asset["checks"] if args.check else []:
                targets = (filter_by_ids(files, args.ids) if step["per"] == "file"
                           else [None])
                print("[%s] проверка: %s" % (name, step["script"]))
                for target in targets:
                    cmd = _command(blender, step, step["args"], target, python)
                    if args.dry_run:
                        print("  -> %s" % _quote(cmd))
                        results.append((name + ":проверка", True, "dry-run"))
                    else:
                        ok, note = run_step("проверка %s" % os.path.basename(target or "-"),
                                            cmd, check_timeout, log)
                        results.append((name + ":проверка", ok, note))
            print()

        if args.check:
            print("[global] проверки пайплайна")
            for step in GLOBAL_CHECKS:
                targets = produced if step["per"] == "output" else [None]
                if step["per"] == "output" and not targets:
                    continue
                cmd = _command(blender, step, step["args"], None, python)
                if step["runner"] == "python":
                    cmd = [python, os.path.join(HERE, step["script"])]
                if targets and step["per"] == "output":
                    cmd = cmd + targets
                if args.dry_run:
                    print("  -> %s" % _quote(cmd))
                    results.append((step["script"], True, "dry-run"))
                else:
                    ok, note = run_step(step["script"], cmd, check_timeout, log)
                    results.append((step["script"], ok, note))
            print()

        log.write("\nИТОГ\n")
        for label, ok, note in results:
            log.write("%-28s %-8s %s\n" % (label, "OK" if ok else "FAIL", note))

    failed = [item for item in results if not item[1]]
    print("ИТОГ")
    for label, ok, note in results:
        print("  %-28s %-6s %s" % (label, "OK" if ok else "FAIL", note))
    print()
    print("Лог: %s" % relative(log_path))
    if failed:
        print("ПРОВАЛ: %d шаг(ов)" % len(failed))
        return 1
    print("ПАЙПЛАЙН PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
