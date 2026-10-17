"""Headless-самопроверка Blender-тулинга FrostSpace.

Что проверяется:

1. `lowpoly.MeshBuilder` собирает замкнутый меш без non-manifold и boundary.
2. Материалы из sRGB-палитры создаются без ошибок.
3. FBX-экспорт и обратный импорт сохраняют габарит (допуск 5 мм).
4. `bake_space_transform=True` не переворачивает объект «на ребро»: сравнение
   покомпонентное, а не по длине вектора.

Запуск:
    blender --background --factory-startup --python Tools/Blender/selftest.py

Код возврата: 0 — PASS, 1 — FAIL. Это единственный источник истины о том,
что портированное ядро работает на текущей сборке Blender.
"""

from __future__ import annotations

import os
import sys
import tempfile
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import lowpoly  # noqa: E402

TOLERANCE = 0.005


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)


def world_bbox(objs):
    low = high = None
    for obj in objs:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low = point.copy() if low is None else Vector((
                min(low.x, point.x), min(low.y, point.y), min(low.z, point.z)))
            high = point.copy() if high is None else Vector((
                max(high.x, point.x), max(high.y, point.y), max(high.z, point.z)))
    return low, high


def main() -> int:
    configure_stdout()
    print("Blender %s" % bpy.app.version_string)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    # После factory settings список аддонов сброшен; экспортёр FBX в сборке
    # есть, но включаем явно, чтобы тест не зависел от сохранённых настроек.
    try:
        bpy.ops.preferences.addon_enable(module="io_scene_fbx")
    except Exception as exc:  # noqa: BLE001 — ниже экспорт всё равно проверится
        print("предупреждение: io_scene_fbx не включился: %r" % (exc,))

    clear_scene()
    mats = [
        lowpoly.make_material("Selftest_A", "#88AACC"),
        lowpoly.make_material("Selftest_B", "#CC8844"),
    ]
    builder = lowpoly.MeshBuilder()
    builder.tube([(0.0, 0.0, 0.0), (0.0, 0.0, 0.8)],
                 [(0.18, 0.18), (0.18, 0.18)], 0, n=8)
    builder.box((0.0, 0.0, 0.8), (0.36, 0.36, 0.36), 1, bevel=0.03)
    obj = builder.build("FrostSpaceSelftest", mats)

    stats = lowpoly.mesh_stats(obj)
    print("STATS verts=%d faces=%d tris=%d boundary=%d nonmanifold=%d shells=%d"
          % (stats["verts"], stats["faces"], stats["tris"],
             stats["boundary_edges"], stats["non_manifold_edges"],
             stats["shells"]))
    if stats["tris"] <= 0:
        raise AssertionError("меш пустой")
    if stats["boundary_edges"] != 0:
        raise AssertionError("есть открытые границы: %d" % stats["boundary_edges"])
    if stats["non_manifold_edges"] != 0:
        raise AssertionError("есть non-manifold рёбра: %d"
                             % stats["non_manifold_edges"])

    low, high = world_bbox([obj])
    if low is None or high is None:
        raise AssertionError("не удалось измерить габарит до экспорта")
    built = high - low
    print("BUILT %.4f × %.4f × %.4f" % (built.x, built.y, built.z))

    fbx = os.path.join(tempfile.gettempdir(), "frostspace_blender_selftest.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=fbx,
        use_selection=True,
        object_types={"MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
    )
    if not os.path.exists(fbx):
        raise AssertionError("FBX не создан: %s" % fbx)

    clear_scene()
    bpy.ops.import_scene.fbx(filepath=fbx)
    low, high = world_bbox(list(bpy.context.scene.objects))
    if low is None or high is None:
        raise AssertionError("после импорта нет мешей")
    back = high - low
    print("ROUNDTRIP %.4f × %.4f × %.4f" % (back.x, back.y, back.z))

    drift = max(abs(built.x - back.x), abs(built.y - back.y),
                abs(built.z - back.z))
    if drift > TOLERANCE:
        raise AssertionError(
            "габарит изменился на %.4f м (допуск %.4f)" % (drift, TOLERANCE))

    print("SELFTEST PASS  file=%s  drift=%.4f м" % (fbx, drift))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # noqa: BLE001 — тест обязан вернуть код 1
        print("SELFTEST FAIL: %s" % exc)
        traceback.print_exc()
        sys.exit(1)
