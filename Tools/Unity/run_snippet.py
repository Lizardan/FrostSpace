"""Выполнить C#-сниппет в живом редакторе Unity через мост MCP.

Зачем: `unity_bridge.py call execute_code '<json>'` ломается на кавычках и `$`
внутри C#-кода. Этот скрипт читает код из файла и отправляет его как JSON.

Использование:
    python Tools/Unity/run_snippet.py Temp/snippet.cs
    python Tools/Unity/run_snippet.py Temp/snippet.cs --raw

Правила сниппета (docs/UNITY_EDITOR.md §2): без `using`, полные имена типов,
`return <строка>` в конце.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from unity_bridge import Bridge, configure_stdout  # noqa: E402


def main(argv: list[str] | None = None) -> int:
    configure_stdout()
    parser = argparse.ArgumentParser(description="Выполнить C#-сниппет в Unity через MCP.")
    parser.add_argument("path", help="файл с C#-кодом сниппета")
    parser.add_argument("--raw", action="store_true", help="печатать полный ответ")
    parser.add_argument("--no-safety", action="store_true",
                        help="передать safety_checks=false (разрешить удаление ассетов)")
    parser.add_argument("--retries", type=int, default=4,
                        help="сколько раз переподключиться, пока редактор занят (4)")
    parser.add_argument("--wait", type=float, default=5.0,
                        help="пауза между попытками, секунды (5)")
    parser.add_argument("--timeout", type=float, default=None,
                        help="таймаут сокета, секунды (по умолчанию UNITY_MCP_TIMEOUT)")
    args = parser.parse_args(argv)

    with open(args.path, "r", encoding="utf-8") as handle:
        code = handle.read()

    params = {"action": "execute", "code": code}
    if args.no_safety:
        params["safety_checks"] = False

    # Мост принимает ОДНО соединение, а пока редактор досводит импорт или
    # перекомпиляцию, он не переслушивает сокет и второе подключение встречает
    # ECONNREFUSED. Это не «мост сломан»: пауза и повтор лечат (проверено
    # 2026-11-02, когда отказывало сразу после refresh_unity).
    attempts = max(1, int(args.retries))
    result = None
    for i in range(attempts):
        try:
            with Bridge(timeout=args.timeout) as bridge:
                result = bridge.call("execute_code", params)
            break
        except (OSError, RuntimeError) as exc:
            if i + 1 >= attempts:
                print("МОСТ НЕ ОТВЕТИЛ после %d попыток: %s" % (attempts, exc))
                return 1
            print("повтор %d/%d: %s" % (i + 1, attempts, exc))
            time.sleep(args.wait)

    if args.raw:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        payload = result.get("result", result)
        if isinstance(payload, dict):
            text = payload.get("result", payload.get("message", payload))
        else:
            text = payload
        print(text)
    return 0 if result.get("status") == "success" else 2


if __name__ == "__main__":
    sys.exit(main())
