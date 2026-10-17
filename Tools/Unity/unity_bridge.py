"""Прямой клиент к мосту Unity MCP (порт 6400, FRAMING=1).

Протокол (из StdioBridgeHost.cs, пакет com.coplaydev.unity-mcp):
    сервер -> клиент : ASCII-строка "WELCOME UNITY-MCP 1 FRAMING=1\\n"
    в обе стороны    : 8 байт BIG-ENDIAN длины + UTF-8 JSON
    команда          : {"type": "<tool>", "params": {...}}
    ответ            : {"status": "success"|"error", ...}

Зачем отдельный клиент: MCP-инструменты привязываются к сессии клиента при её
старте. Если в текущем клиенте их нет, а Unity уже запущен, этот мост даёт
доступ к тому же серверу по сокету.

Адрес и порт берутся из аргументов или окружения:
    UNITY_MCP_HOST     (по умолчанию 127.0.0.1)
    UNITY_MCP_PORT     (по умолчанию 6400)
    UNITY_MCP_TIMEOUT  (по умолчанию 300 секунд)

Использование:
    python unity_bridge.py ping
    python unity_bridge.py call <type> '<params-json>'
"""

from __future__ import annotations

import argparse
import json
import os
import socket
import struct
import sys
import time

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 6400
DEFAULT_TIMEOUT = 300.0
MAX_FRAME = 64 * 1024 * 1024
MAX_PRINT_CHARS = 8000


def configure_stdout() -> None:
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
        raise RuntimeError("порт должен быть целым числом, получено %r"
                           % (value,)) from exc
    if not 1 <= port <= 65535:
        raise RuntimeError("порт вне диапазона 1..65535: %d" % port)
    return port


def _parse_timeout(value) -> float:
    try:
        timeout = float(value)
    except (TypeError, ValueError) as exc:
        raise RuntimeError("таймаут должен быть числом, получено %r"
                           % (value,)) from exc
    if timeout <= 0:
        raise RuntimeError("таймаут должен быть больше нуля")
    return timeout


class Bridge:
    def __init__(self, host: str | None = None, port: int | None = None,
                 timeout: float | None = None):
        host = host or _env("UNITY_MCP_HOST", DEFAULT_HOST)
        port = _parse_port(port if port is not None
                           else _env("UNITY_MCP_PORT", DEFAULT_PORT))
        timeout = _parse_timeout(timeout if timeout is not None
                                 else _env("UNITY_MCP_TIMEOUT", DEFAULT_TIMEOUT))
        self.timeout = timeout
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.settimeout(timeout)
        self.buf = b""
        line = self._read_line()
        if not line.startswith(b"WELCOME"):
            raise RuntimeError("неожиданное приветствие: %r" % line[:80])

    def _read_line(self) -> bytes:
        while b"\n" not in self.buf:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise RuntimeError("соединение закрыто до приветствия")
            self.buf += chunk
        line, self.buf = self.buf.split(b"\n", 1)
        return line

    def _read_exact(self, n: int) -> bytes:
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise RuntimeError("соединение закрыто посреди кадра")
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out

    def call(self, ctype: str, params: dict | None = None) -> dict:
        payload = json.dumps({"type": ctype, "params": params or {}}).encode("utf-8")
        self.sock.sendall(struct.pack(">Q", len(payload)) + payload)
        head = self._read_exact(8)
        (n,) = struct.unpack(">Q", head)
        if n > MAX_FRAME:
            raise RuntimeError("слишком большой кадр: %d байт" % n)
        body = self._read_exact(n)
        return json.loads(body.decode("utf-8"))

    def close(self) -> None:
        try:
            self.sock.close()
        except OSError:
            pass

    def __enter__(self) -> "Bridge":
        return self

    def __exit__(self, *_exc) -> None:
        self.close()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Клиент к мосту Unity MCP (FRAMING=1, порт 6400).")
    parser.add_argument("--host", default=None, help="хост моста (UNITY_MCP_HOST)")
    parser.add_argument("--port", type=int, default=None, help="порт моста (UNITY_MCP_PORT)")
    parser.add_argument("--timeout", type=float, default=None,
                        help="таймаут в секундах (UNITY_MCP_TIMEOUT)")
    sub = parser.add_subparsers(dest="mode", required=True)
    sub.add_parser("ping", help="проверить, что мост отвечает")
    call = sub.add_parser("call", help="вызвать произвольный инструмент")
    call.add_argument("type", help="имя инструмента, например read_console")
    call.add_argument("params", nargs="?", default="{}",
                      help="JSON-параметры инструмента (по умолчанию {})")
    return parser


def main(argv: list[str] | None = None) -> int:
    configure_stdout()
    args = build_parser().parse_args(argv)
    try:
        with Bridge(args.host, args.port, args.timeout) as bridge:
            started = time.monotonic()
            if args.mode == "ping":
                result = bridge.call("ping")
            else:
                result = bridge.call(args.type, json.loads(args.params))
            elapsed = time.monotonic() - started
    except (OSError, RuntimeError, json.JSONDecodeError) as exc:
        print("МОСТ НЕ ОТВЕТИЛ: %s" % exc)
        return 1

    print("ответ за %.1f с" % elapsed)
    print(json.dumps(result, ensure_ascii=False, indent=2)[:MAX_PRINT_CHARS])
    return 0


if __name__ == "__main__":
    sys.exit(main())
