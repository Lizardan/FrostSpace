"""Проверяет, что кисти робота одинаковы: слева и справа одна и та же деталь.

Зачем: в дизайнах R4/R9/R10/R13/R14/R16/R18/R20 в одной руке лежал предмет
(крюк, отвёртки, ключ, бур, шприц, дубинка, молот, гаечный ключ), а кисть
иногда отсутствовала вовсе. На симметрию это не проверялось ничем, поэтому
регрессия возвращалась молча. Скрипт смотрит на **экспортированный** FBX:
берёт вершины с весом в `hand_l` и `hand_r`, зеркалит левые относительно
плоскости между кистями и ищет ближайшую правую вершину.

Проверка ловит три случая:
- кисти разного размера (разные `hand=(...)` у левой и правой руки);
- предмет в кисти (выступ вбок/вперёд, которого нет на другой стороне);
- кисть отсутствует, вместо неё предмет (счётчик вершин не совпадает).

Запуск:
    blender --background --factory-startup --python Tools/Blender/check_hand_symmetry.py -- <fbx>
"""

from __future__ import annotations

import sys
import traceback

import bpy
from mathutils import Vector

#: Допуск совпадения зеркальной пары, метры. FBX хранит float32, координаты
#: порядка 1.5 м, поэтому 0.1 мм — с большим запасом.
TOLERANCE = 1e-4


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def bone_vertices(obj, bone):
    """Координаты вершин, у которых вес в группу `bone` не меньше половины."""
    group = obj.vertex_groups.get(bone)
    if group is None:
        return None
    index = group.index
    points = []
    for vert in obj.data.vertices:
        for item in vert.groups:
            if item.group == index and item.weight >= 0.5:
                points.append(obj.matrix_world @ vert.co)
                break
    return points


def extents(points):
    xs = [p.x for p in points]
    ys = [p.y for p in points]
    zs = [p.z for p in points]
    return (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))


def centroid(points):
    total = Vector((0.0, 0.0, 0.0))
    for point in points:
        total += point
    return total / len(points)


def mirror_mismatch(left, right):
    """Максимальное расстояние от зеркальной левой вершины до ближайшей правой."""
    axis = centroid(right) - centroid(left)
    if axis.length < 1e-6:
        raise RuntimeError("кисти в одной точке — нечего зеркалить")
    normal = axis.normalized()
    plane_point = (centroid(left) + centroid(right)) * 0.5
    worst = 0.0
    for point in left:
        mirrored = point - normal * (2.0 * (point - plane_point).dot(normal))
        nearest = min((mirrored - other).length for other in right)
        worst = max(worst, nearest)
    return worst


def check(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    obj = next((o for o in bpy.context.scene.objects if o.type == "MESH"), None)
    if obj is None:
        raise RuntimeError("FBX без меша: %s" % path)

    left = bone_vertices(obj, "hand_l")
    right = bone_vertices(obj, "hand_r")
    if not left or not right:
        raise RuntimeError("нет вершин в hand_l/hand_r: %s" % path)

    mismatch = mirror_mismatch(left, right)
    size_l, size_r = extents(left), extents(right)
    ok = len(left) == len(right) and mismatch <= TOLERANCE
    print("%-20s %s  verts=%d/%d  mirror=%.6f m  size_l=(%.3f, %.3f, %.3f)  "
          "size_r=(%.3f, %.3f, %.3f)"
          % (obj.name if obj.name else path, "OK  " if ok else "FAIL",
             len(left), len(right), mismatch,
             size_l[0], size_l[1], size_l[2], size_r[0], size_r[1], size_r[2]))
    return ok


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    failed = []
    for path in argv:
        try:
            if not check(path):
                failed.append(path)
        except Exception:  # noqa: BLE001
            traceback.print_exc()
            failed.append(path)
    if failed:
        print("АСИММЕТРИЯ КИСТЕЙ: %s" % ", ".join(failed))
        return 1
    print("кисти симметричны: %d файл(ов)" % len(argv))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
