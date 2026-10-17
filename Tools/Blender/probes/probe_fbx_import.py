"""Почему обратный импорт FBX даёт не тот габарит — разбор по объектам.

Запуск:
    blender --background --factory-startup --python probe_fbx_import.py -- <файл.fbx> [...]

Печатает по каждому импортированному объекту: тип, родителя, трансформ,
локальный габарит (bound_box), габарит после матрицы мира и габарит
ВЫЧИСЛЕННОГО меша. Этого достаточно, чтобы отличить «файл неверный» от
«замер неверный» — это разные выводы и разные правки.
"""

from __future__ import annotations

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)


def size_of(corners, matrix: Matrix):
    low = high = None
    for corner in corners:
        point = matrix @ Vector(corner)
        low = point.copy() if low is None else Vector((
            min(low.x, point.x), min(low.y, point.y), min(low.z, point.z)))
        high = point.copy() if high is None else Vector((
            max(high.x, point.x), max(high.y, point.y), max(high.z, point.z)))
    return (high - low) if low is not None else None


def fmt(vec) -> str:
    return "(%s)" % ", ".join("%.4f" % v for v in vec) if vec else "—"


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи путь к FBX после --")
        return 2

    for path in argv:
        if not os.path.exists(path):
            print("НЕТ ФАЙЛА", path)
            continue
        reset()
        bpy.ops.import_scene.fbx(filepath=path)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        depsgraph.update()
        print("=" * 92)
        print("ФАЙЛ %s" % path)
        for obj in bpy.context.scene.objects:
            evaluated = obj.evaluated_get(depsgraph)
            print("  %-20s тип=%-9s родитель=%-16s лок=%s поворот=%s масштаб=%s"
                  % (obj.name, obj.type,
                     obj.parent.name if obj.parent else "—",
                     fmt(obj.location),
                     fmt(tuple(math.degrees(v) for v in obj.rotation_euler)),
                     fmt(obj.scale)))
            print("      локальный=%s мировой=%s вычисленный=%s"
                  % (fmt(size_of(obj.bound_box, Matrix.Identity(4))),
                     fmt(size_of(obj.bound_box, obj.matrix_world)),
                     fmt(size_of(evaluated.bound_box, evaluated.matrix_world))))
            if obj.animation_data and obj.animation_data.action:
                print("      ДЕЙСТВИЕ: %s" % obj.animation_data.action.name)
    return 0


if __name__ == "__main__":
    if bpy.app.background:
        sys.exit(main())
    else:
        main()
