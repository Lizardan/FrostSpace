"""Проверка самого счётчика объёма: куб с нормалями наружу должен дать плюс.

Знак объёма — основа проверки перевёрнутых нормалей, поэтому счётчик надо
проверить на эталоне, где ответ известен заранее.

Запуск:
    blender --background --factory-startup --python check_volume_sign.py
"""

from __future__ import annotations

import os
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_mesh_integrity as CMI  # noqa: E402


def main() -> int:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0.0, 0.0, 0.0))
    obj = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    faces = CMI._shells(bm)[0]
    vol = CMI._shell_volume(faces)
    bm.free()
    print("куб 2×2×2: объём по счётчику = %.4f (ожидается +8.0 при нормалях наружу)"
          % vol)
    print("куб: вердикт счётчика =", CMI.mesh_report(obj))

    # Тот же куб, но с вывернутыми нормалями.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    obj = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    faces = CMI._shells(bm)[0]
    print("перевёрнутый куб: объём по счётчику = %.4f (ожидается -8.0)"
          % CMI._shell_volume(faces))
    bm.free()
    print("перевёрнутый куб: вердикт счётчика =", CMI.mesh_report(obj))
    return 0


if __name__ == "__main__":
    sys.exit(main())
