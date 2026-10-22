"""Живой GUI-Blender по требованию: сам поднимается, сам закрывается.

Зачем: сборка, проверки и превью идут полностью в `--background` и GUI-Blender не
нужен вообще. Живой Blender нужен только для MCP-моста (аддон принципиально не
обслуживает фоновый режим — см. `blender_boot.py`), то есть когда сцену надо
посмотреть и потрогать извне. Держать окно открытым ради этого не хочется,
поэтому сессия здесь временная:

    start   — запустить GUI-Blender с `blender_boot.py`, дождаться моста;
    run     — выполнить .py в живой сессии (при необходимости сначала start);
    stop    — попросить Blender выйти, при отказе — завершить процесс;
    status  — отвечает ли мост и жив ли процесс.

Запуск (системный Python, как и `build_all.py`):

    python Tools/Blender/gui_session.py start
    python Tools/Blender/gui_session.py run Tools/Blender/probes/gui_ping.py --auto
    python Tools/Blender/gui_session.py stop

`run --auto` — это режим «нужен на минуту»: поднялся, выполнил, закрылся.

Окружение: BLENDER_HOST / BLENDER_PORT — как у `blender_bridge.py`.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import blender_bridge  # noqa: E402
import build_all  # noqa: E402

STATE = os.path.join(build_all.LOGS, "gui_session.json")
BOOT_SCRIPT = os.path.join(HERE, "blender_boot.py")

#: Сколько ждать мост после старта Blender.
START_TIMEOUT = 120.0

#: Сколько ждать добровольного выхода Blender перед принудительным закрытием.
STOP_TIMEOUT = 15.0

SYNCHRONIZE = 0x00100000
WAIT_TIMEOUT = 0x00000102


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


# ------------------------------------------------------------------ состояние


def read_state() -> dict:
    try:
        with open(STATE, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return {}


def write_state(data: dict) -> None:
    os.makedirs(os.path.dirname(STATE), exist_ok=True)
    with open(STATE, "w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2)


def clear_state() -> None:
    try:
        os.remove(STATE)
    except OSError:
        pass


def alive(pid: int | None) -> bool:
    """Жив ли процесс — по факту, а не по pid-файлу."""
    if not pid:
        return False
    handle = ctypes.windll.kernel32.OpenProcess(SYNCHRONIZE, False, int(pid))
    if not handle:
        return False
    try:
        return ctypes.windll.kernel32.WaitForSingleObject(handle, 0) == WAIT_TIMEOUT
    finally:
        ctypes.windll.kernel32.CloseHandle(handle)


# --------------------------------------------------------------------- мост


def bridge_ready(port: int) -> bool:
    """Порт слушает — аддон поднялся (полноценная проверка идёт через ping)."""
    try:
        with socket.create_connection((blender_bridge.DEFAULT_HOST, port), timeout=1.0):
            return True
    except OSError:
        return False


def ping(timeout: float = 5.0) -> bool:
    try:
        with blender_bridge.Bridge(timeout=timeout) as bridge:
            bridge.call("ping")
        return True
    except (blender_bridge.BridgeError, OSError):
        return False


def ready() -> bool:
    """Сессия поднята и мост живой — без печати (для `run --auto`)."""
    return alive(read_state().get("pid")) and ping()


# ------------------------------------------------------------------ действия


def start(blender: str, timeout: float) -> int:
    state = read_state()
    if state.get("pid") and alive(state["pid"]) and ping():
        print("уже запущен: pid=%s порт=%s" % (state["pid"], state.get("port")))
        return 0
    if state:
        clear_state()

    print("запускаю GUI-Blender: %s" % blender)
    started = time.monotonic()
    proc = subprocess.Popen([blender, "--factory-startup", "--python", BOOT_SCRIPT],
                            cwd=build_all.ROOT)
    port = blender_bridge.DEFAULT_PORT
    deadline = started + timeout
    while time.monotonic() < deadline:
        if bridge_ready(port) and ping():
            write_state({"pid": proc.pid, "port": port,
                         "started": time.strftime("%Y-%m-%d %H:%M:%S")})
            print("Blender готов: pid=%d порт=%d за %.1f с"
                  % (proc.pid, port, time.monotonic() - started))
            return 0
        if proc.poll() is not None:
            print("Blender закрылся при старте, код=%s" % proc.returncode)
            print("лог загрузки: %s" % os.path.join(HERE, "blender_boot.log"))
            return 1
        time.sleep(0.5)
    print("мост не поднялся за %.0f с — смотри %s"
          % (timeout, os.path.join(HERE, "blender_boot.log")))
    stop(quiet=True)
    return 1


def run(path: str) -> int:
    full = os.path.abspath(path)
    if not os.path.exists(full):
        print("НЕТ ФАЙЛА: %s" % full)
        return 2
    with open(full, "r", encoding="utf-8") as handle:
        code = handle.read()
    print("выполняю %s в живом Blender" % os.path.basename(full))
    try:
        with blender_bridge.Bridge() as bridge:
            result = bridge.execute_code(code)
    except (blender_bridge.BridgeError, OSError) as exc:
        print("МОСТ НЕ ОТВЕТИЛ: %s" % exc)
        return 1
    output = result.get("result") or ""
    for line in str(output).splitlines():
        print("  | %s" % line)
    print("готово")
    return 0


def stop(quiet: bool = False) -> int:
    state = read_state()
    pid = state.get("pid")
    if not pid:
        if not quiet:
            print("сессии нет")
        return 0
    if not alive(pid):
        clear_state()
        if not quiet:
            print("процесс уже не живёт, состояние сброшено")
        return 0

    # Сначала по-хорошему: пусть Blender сохранит настройки сам.
    try:
        with blender_bridge.Bridge(timeout=5.0) as bridge:
            bridge.call("execute_code", {"code": "import bpy\nbpy.ops.wm.quit_blender()\n"},
                        expect_result=False)
    except (blender_bridge.BridgeError, OSError):
        pass  # выход по себе обрывает соединение — это нормально

    deadline = time.monotonic() + STOP_TIMEOUT
    while time.monotonic() < deadline:
        if not alive(pid):
            clear_state()
            if not quiet:
                print("Blender закрылся сам")
            return 0
        time.sleep(0.3)

    subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                   capture_output=True, text=True)
    clear_state()
    if not quiet:
        print("Blender не вышел за %.0f с — процесс завершён принудительно" % STOP_TIMEOUT)
    return 0


def status() -> int:
    state = read_state()
    pid = state.get("pid")
    running = alive(pid)
    print("процесс: %s" % ("pid=%s" % pid if running else "нет"))
    answered = running and ping()
    print("мост:    %s" % ("отвечает" if answered else "не отвечает"))
    if state.get("started"):
        print("запущен: %s" % state["started"])
    return 0 if answered else 1


def main() -> int:
    configure_stdout()
    parser = argparse.ArgumentParser(
        description="Живой GUI-Blender по требованию (для MCP-моста).")
    sub = parser.add_subparsers(dest="mode", required=True)
    sub.add_parser("start", help="поднять GUI-Blender и дождаться моста")
    sub.add_parser("stop", help="закрыть Blender (при отказе — принудительно)")
    sub.add_parser("status", help="проверить процесс и мост")
    run_parser = sub.add_parser("run", help="выполнить .py в живой сессии")
    run_parser.add_argument("file", help="путь к .py")
    run_parser.add_argument("--auto", action="store_true",
                            help="поднять сессию перед запуском и закрыть после")
    parser.add_argument("--blender", default=None, help="путь к blender.exe")
    parser.add_argument("--timeout", type=float, default=START_TIMEOUT,
                        help="сколько ждать мост (секунды)")
    args = parser.parse_args()

    if args.mode == "status":
        return status()
    if args.mode == "stop":
        return stop()
    if args.mode == "start":
        return start(build_all.find_blender(args.blender), args.timeout)

    owned = False
    if args.auto and not ready():
        code = start(build_all.find_blender(args.blender), args.timeout)
        if code != 0:
            return code
        owned = True
    try:
        return run(args.file)
    finally:
        if args.auto and owned:
            stop()


if __name__ == "__main__":
    sys.exit(main())
