"""Загрузка GUI-Blender: включить аддоны, поднять MCP-сервер, отметить готовность.

Почему GUI, а не `--background`. В аддоне `_blendermcp_ensure_server_running`
начинается с `if bpy.app.background: return None` — в фоновом режиме сервер не
поднимается по замыслу автора. Причина не в капризе: команды исполняются на
главном потоке через `bpy.app.timers`, а в фоновом прогоне скрипт, удерживающий
процесс живым, этот поток как раз и блокирует. Фоновый путь остаётся, но только
для сборки (`blender --background --python ...`), не для MCP.

Скрипт пишет собственный лог: у GUI-приложения на Windows stdout может не
доехать до перенаправленного файла, а признак готовности нужен надёжный.

Запуск:
    blender.exe --python Tools/Blender/blender_boot.py
    blender.exe --python Tools/Blender/blender_boot.py -- --save-prefs

Окружение:
    BLENDER_ADDONS      список аддонов через запятую (по умолчанию blender_mcp)
    BLENDER_BOOT_LOG    путь к логу (по умолчанию рядом со скриптом)
    BLENDER_BOOT_CHECKS число проверок сервера (по умолчанию 12)
"""

from __future__ import annotations

import os
import sys
import time

import bpy  # noqa: E402  (импорт требует запущенного Blender)

HERE = os.path.dirname(os.path.abspath(__file__))
LOG_PATH = os.environ.get("BLENDER_BOOT_LOG") or os.path.join(HERE, "blender_boot.log")

#: Аддоны, которые нужно включить. По умолчанию только мост: остальные аддоны
#: пользователя не трогаем.
ADDONS = tuple(
    name.strip()
    for name in os.environ.get("BLENDER_ADDONS", "blender_mcp").split(",")
    if name.strip()
)

#: Сколько раз проверять сервер, прежде чем объявить отказ.
CHECKS = int(os.environ.get("BLENDER_BOOT_CHECKS", "12"))
CHECK_INTERVAL = 1.0


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def log(message: str) -> None:
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), message)
    print(line)
    try:
        with open(LOG_PATH, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")
    except OSError:
        # Лог — диагностика, ронять из-за него загрузку нельзя.
        pass


def enable(module: str) -> bool:
    """Включить аддон, не падая, если он уже включён."""
    try:
        if module in bpy.context.preferences.addons:
            log("уже включён: %s" % module)
            return True
        bpy.ops.preferences.addon_enable(module=module)
        log("включён: %s" % module)
        return True
    except Exception as exc:  # noqa: BLE001 — сообщаем и продолжаем
        log("ОШИБКА включения %s: %r" % (module, exc))
        return False


def server_state() -> tuple[bool, int | None]:
    server = getattr(bpy.types, "blendermcp_server", None)
    if server is None:
        return False, None
    return bool(getattr(server, "running", False)), getattr(server, "port", None)


def check(attempt: int = 0):
    """Таймер-проверка: сервер либо поднялся, либо пробуем ещё раз.

    Тело обёрнуто в try целиком, и это не перестраховка. Исключение внутри
    таймера печатается в консоль Blender, а не в наш лог: маркер готовности
    пропадал бы, и загрузка выглядела как «сервер не встал», хотя он работал.
    Строка про готовность пишется ПЕРВОЙ, до любой диагностики, которая может
    упасть.
    """
    try:
        running, port = server_state()
        if running:
            log("BLENDER_READY порт=%s" % port)
            try:
                log("включены аддоны: %s"
                    % ", ".join(sorted(bpy.context.preferences.addons.keys())))
            except Exception as exc:  # noqa: BLE001 — диагностика, не результат
                log("список аддонов недоступен: %r" % exc)
            return None

        if attempt >= CHECKS:
            log("BLENDER_FAIL: сервер не поднялся за %d попыток" % CHECKS)
            return None

        # Автостарт мог быть выключен в сохранённых настройках — пробуем вручную.
        if attempt == 3:
            try:
                scene = bpy.context.scene
                scene.blendermcp_auto_start_server = True
                log("автостарт включён принудительно, порт=%s" % scene.blendermcp_port)
                bpy.ops.blendermcp.start_server()
                log("вызван blendermcp.start_server()")
            except Exception as exc:  # noqa: BLE001
                log("ручной старт не удался: %r" % exc)
    except Exception as exc:  # noqa: BLE001
        log("BLENDER_FAIL: проверка упала: %r" % exc)
        return None

    return CHECK_INTERVAL


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    save_prefs = "--save-prefs" in argv

    log("boot: Blender %s, background=%s" % (bpy.app.version_string,
                                            bpy.app.background))
    try:
        log("каталог аддонов: %s" % bpy.utils.user_resource("SCRIPTS", path="addons"))
    except Exception as exc:  # noqa: BLE001 — путь нужен только для лога
        log("каталог аддонов не определён: %r" % exc)

    if bpy.app.background:
        log("ОТКАЗ: MCP-аддон не обслуживает команды в фоновом режиме; "
            "для сборки используйте --background без MCP")
        return 0

    enabled = [module for module in ADDONS if enable(module)]

    if save_prefs:
        try:
            bpy.ops.wm.save_userpref()
            log("настройки сохранены (аддоны включены навсегда)")
        except Exception as exc:  # noqa: BLE001
            log("сохранить настройки не удалось: %r" % exc)

    if "blender_mcp" not in enabled:
        log("BLENDER_FAIL: аддон blender_mcp не включился")
        return 1

    if not bpy.app.timers.is_registered(check):
        bpy.app.timers.register(check, first_interval=1.5, persistent=True)
    return 0


if __name__ == "__main__":
    # Это GUI-скрипт: sys.exit() здесь закрыл бы Blender целиком.
    main()
