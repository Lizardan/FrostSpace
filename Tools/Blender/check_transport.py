# -*- coding: utf-8 -*-
"""Проверка транспортного слоя: тот же замер, что и на сборке.

    blender --background --factory-startup --python Tools/Blender/check_transport.py

Отдельный файл нужен потому, что пайплайн зовёт сборку и проверку разными
шагами: проверка обязана работать и сама по себе, когда сборку не запускали.
Сам замер живёт в `build_transport` — два разных представления о том, что
такое «стык сошёлся», были бы хуже, чем один медленный.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import build_transport  # noqa: E402

if __name__ == "__main__":
    try:
        sys.exit(build_transport.run())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
