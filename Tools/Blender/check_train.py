"""Проверяет инварианты ассетов вагона и груза после сборки.

Запуск (штатно — из `build_all.py`, per = file):

    blender --background --factory-startup --python Tools/Blender/check_train.py -- \
        "Assets/_Project/Content/Models/Level/TrainInterior.fbx"

Проверяется то, что нельзя увидеть на превью одним взглядом: габарит, низ на
полу (пивот пропов), наличие створки и её размеры. Ошибка — ненулевой код
возврата, как у остальных `check_*` пайплайна.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def bounds(obj) -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for corner in obj.bound_box:
        p = obj.matrix_world @ Vector(corner)
        lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
        hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def check_range(name: str, value: float, lo: float, hi: float,
                problems: list[str]) -> None:
    if not (lo <= value <= hi):
        problems.append("%s=%.3f вне [%.3f, %.3f]" % (name, value, lo, hi))


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    path = argv[0]
    base = os.path.splitext(os.path.basename(path))[0]
    if not os.path.exists(path):
        print("CHECK %s FAIL нет файла" % base)
        return 1

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = {obj.name: obj for obj in bpy.context.scene.objects
              if obj.type == "MESH"}
    problems: list[str] = []

    if base == "TrainInterior":
        body = meshes.get("TrainBody")
        door = meshes.get("TrainDoor")
        if body is None:
            problems.append("нет объекта TrainBody")
        if door is None:
            problems.append("нет объекта TrainDoor")
        if body is not None:
            lo, hi = bounds(body)
            size = hi - lo
            check_range("body.width", size.x, 7.9, 8.3, problems)
            check_range("body.length", size.y, 23.8, 24.8, problems)
            check_range("body.top", hi.z, 3.2, 3.6, problems)
            check_range("body.bottom", lo.z, -0.45, -0.15, problems)
        if door is not None:
            lo, hi = bounds(door)
            size = hi - lo
            check_range("door.width", size.x, 3.0, 3.25, problems)
            check_range("door.top", hi.z, 2.4, 2.75, problems)
            check_range("door.bottom", lo.z, 0.0, 0.15, problems)
            if abs((lo.y + hi.y) * 0.5 + 11.9) > 0.3:
                problems.append("створка не у торца: y=%.2f" % ((lo.y + hi.y) * 0.5))

    elif base == "Prop_Battery":
        if len(meshes) != 1:
            problems.append("ожидался один меш, найдено %d" % len(meshes))
        obj = next(iter(meshes.values()), None)
        if obj is not None:
            lo, hi = bounds(obj)
            size = hi - lo
            check_range("battery.width", size.x, 0.9, 1.1, problems)
            check_range("battery.depth", size.y, 0.6, 0.8, problems)
            check_range("battery.height", size.z, 0.85, 1.05, problems)
            check_range("battery.bottom", lo.z, -0.02, 0.02, problems)

    elif base == "Prop_GravCart":
        if len(meshes) != 1:
            problems.append("ожидался один меш, найдено %d" % len(meshes))
        obj = next(iter(meshes.values()), None)
        if obj is not None:
            lo, hi = bounds(obj)
            size = hi - lo
            check_range("cart.width", size.x, 2.3, 2.5, problems)
            check_range("cart.depth", size.y, 1.8, 2.0, problems)
            check_range("cart.height", size.z, 1.05, 1.25, problems)
            check_range("cart.bottom", lo.z, -0.02, 0.02, problems)
    else:
        print("CHECK %s SKIP неизвестный ассет" % base)
        return 0

    if problems:
        print("CHECK %s FAIL: %s" % (base, "; ".join(problems)))
        return 1
    print("CHECK %s PASS" % base)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
