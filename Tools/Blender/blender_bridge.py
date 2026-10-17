"""Прямой клиент к аддону «MCP for Blender» (порт 9876, без кадрирования).

Зачем отдельный клиент, когда есть MCP. MCP-инструменты привязываются к сессии
клиента при её старте: правка конфига вступает в силу только после перезапуска.
Пока перезапуска нет, работать надо — поэтому здесь тот же протокол, но по
сокету.

Протокол (из `blender_mcp.py`, `_handle_client` / `_drain_command_queue`):

    клиент -> аддон : сырые JSON-байты, БЕЗ префикса длины и БЕЗ перевода строки
                      {"type": "<команда>", "params": {...}}
    аддон -> клиент : сырые JSON-байты ответа, тоже без кадрирования

Разбор на стороне аддона инкрементальный: он копит буфер и пробует
`json.loads`, пока не получится. Значит ответ читается так же — копим и
пробуем разобрать.

Ловушка: команды исполняются на главном потоке Blender через `bpy.app.timers`.
Значит аддон отвечает только когда живой GUI-Blender не заблокирован. Если
ответ не пришёл за таймаут — вопрос не к мосту, а к тому, запущен ли Blender и
не занят ли его главный поток модальным оператором.

Адрес и порт берутся из аргументов или окружения:
    BLENDER_HOST     (по умолчанию 127.0.0.1)
    BLENDER_PORT     (по умолчанию 9876)
    BLENDER_TIMEOUT  (по умолчанию 300 секунд)

Использование:
    python blender_bridge.py ping
    python blender_bridge.py scene
    python blender_bridge.py call <type> '<params-json>'
    python blender_bridge.py code <файл.py>       # execute_code из файла
"""

from __future__ import annotations

import argparse
import json
import os
import socket
import sys
import time

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 9876
DEFAULT_TIMEOUT = 300.0
MAX_PRINT_CHARS = 8000


class BridgeError(RuntimeError):
    """Аддон недоступен или вернул `status: error`."""


def configure_stdout() -> None:
    """Русский текст в Windows-консоли не должен падать на cp1251."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def _env(name: str, default: str) -> str:
    value = os.environ.get(name)
    return default if value in (None, "") else value


def _parse_port(value) -> int:
    try:
        port = int(value)
    except (TypeError, ValueError) as exc:
        raise BridgeError("порт должен быть целым числом, получено %r" % (value,)) from exc
    if not 1 <= port <= 65535:
        raise BridgeError("порт вне диапазона 1..65535: %d" % port)
    return port


def _parse_timeout(value) -> float:
    try:
        timeout = float(value)
    except (TypeError, ValueError) as exc:
        raise BridgeError("таймаут должен быть числом, получено %r" % (value,)) from exc
    if timeout <= 0:
        raise BridgeError("таймаут должен быть больше нуля")
    return timeout


class Bridge:
    """Одно соединение — одна или несколько команд подряд."""

    def __init__(self, host: str | None = None, port: int | None = None,
                 timeout: float | None = None):
        self.host = host or _env("BLENDER_HOST", DEFAULT_HOST)
        self.port = _parse_port(port if port is not None
                                else _env("BLENDER_PORT", DEFAULT_PORT))
        self.timeout = _parse_timeout(timeout if timeout is not None
                                      else _env("BLENDER_TIMEOUT", DEFAULT_TIMEOUT))
        self.sock = socket.create_connection((self.host, self.port),
                                             timeout=self.timeout)
        self.sock.settimeout(self.timeout)
        self.buffer = b""

    def close(self) -> None:
        try:
            self.sock.close()
        except OSError:
            pass

    def __enter__(self) -> "Bridge":
        return self

    def __exit__(self, *_exc) -> None:
        self.close()

    def call(self, cmd_type: str, params: dict | None = None,
             expect_result: bool = True) -> dict:
        """Отправить команду и дождаться разобранного ответа."""
        payload = {"type": cmd_type, "params": params or {}}
        self.sock.sendall(json.dumps(payload).encode("utf-8"))

        deadline = time.monotonic() + self.timeout
        while True:
            response = self._try_parse()
            if response is not None:
                break
            if time.monotonic() > deadline:
                raise BridgeError(
                    "нет ответа на %r за %.0f с — Blender занят или не запущен"
                    % (cmd_type, self.timeout))
            try:
                chunk = self.sock.recv(65536)
            except socket.timeout as exc:
                raise BridgeError(
                    "таймаут %.0f с на команде %r — проверьте, что GUI-Blender "
                    "запущен и не заблокирован модальным окном"
                    % (self.timeout, cmd_type)) from exc
            except OSError as exc:
                raise BridgeError("ошибка сокета на команде %r: %s"
                                  % (cmd_type, exc)) from exc
            if not chunk:
                raise BridgeError("аддон закрыл соединение на команде %r" % cmd_type)
            self.buffer += chunk

        status = response.get("status")
        if status == "error":
            raise BridgeError("%s: %s" % (cmd_type, response.get("message")))
        if status != "success":
            raise BridgeError("неожиданный статус %r в ответе на %r"
                              % (status, cmd_type))
        if expect_result:
            return response.get("result", {})
        return response

    def _try_parse(self):
        """Разобрать накопленный буфер; None — данных ещё мало."""
        if not self.buffer:
            return None
        try:
            response = json.loads(self.buffer.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            return None
        self.buffer = b""
        return response

    def execute_code(self, code: str) -> dict:
        """Выполнить Python в живом Blender и вернуть захваченный stdout."""
        return self.call("execute_code", {"code": code})


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Клиент к аддону MCP for Blender (сырой JSON, порт 9876).")
    parser.add_argument("--host", default=None, help="хост аддона (BLENDER_HOST)")
    parser.add_argument("--port", type=int, default=None, help="порт аддона (BLENDER_PORT)")
    parser.add_argument("--timeout", type=float, default=None,
                        help="таймаут в секундах (BLENDER_TIMEOUT)")
    sub = parser.add_subparsers(dest="mode", required=True)

    sub.add_parser("ping", help="проверить, что аддон отвечает")
    sub.add_parser("scene", help="получить get_scene_info")

    call = sub.add_parser("call", help="вызвать произвольную команду аддона")
    call.add_argument("type", help="тип команды, например get_scene_info")
    call.add_argument("params", nargs="?", default="{}",
                      help="JSON-параметры команды (по умолчанию {})")

    code = sub.add_parser("code", help="выполнить Python-файл в живом Blender")
    code.add_argument("file", help="путь к .py с кодом")
    return parser


def main(argv: list[str] | None = None) -> int:
    configure_stdout()
    args = build_parser().parse_args(argv)

    try:
        with Bridge(args.host, args.port, args.timeout) as bridge:
            started = time.monotonic()
            if args.mode == "ping":
                result = bridge.call("ping")
            elif args.mode == "scene":
                result = bridge.call("get_scene_info")
            elif args.mode == "call":
                result = bridge.call(args.type, json.loads(args.params))
            else:  # code
                with open(args.file, "r", encoding="utf-8") as handle:
                    result = bridge.execute_code(handle.read())
            elapsed = time.monotonic() - started
    except (BridgeError, OSError, json.JSONDecodeError) as exc:
        print("МОСТ НЕ ОТВЕТИЛ: %s" % exc)
        return 1

    print("ответ за %.2f с" % elapsed)
    print(json.dumps(result, ensure_ascii=False, indent=2)[:MAX_PRINT_CHARS])
    return 0


if __name__ == "__main__":
    sys.exit(main())
