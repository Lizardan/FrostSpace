"""Рендерит превью меню-ассетов: ангар, навес депо, путь и полосы RIDE.

Запуск (штатно — через `build_all.py --asset ... --preview`):

    blender --background --factory-startup --python Tools/Blender/render_menu_previews.py -- \
        Assets/_Project/Content/Models/Level/MenuStorageHall.fbx --out AssetSource/Previews/Menu

Workbench выбран намеренно: быстрый headless-рендер faceted-силуэта и цветов
палитры без GPU-контекста.
"""

from __future__ import annotations

import math
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


def reset() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def world_bounds() -> tuple[Vector, Vector]:
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or not obj.data.vertices:
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low.x, low.y, low.z = min(low.x, point.x), min(low.y, point.y), min(low.z, point.z)
            high.x, high.y, high.z = max(high.x, point.x), max(high.y, point.y), max(high.z, point.z)
    if low.x > high.x:
        return Vector((0.0, 0.0, 0.0)), Vector((1.0, 1.0, 1.0))
    return low, high


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 700
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.045, 0.075, 0.10)

    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    shading.curvature_ridge_factor = 1.4
    shading.curvature_valley_factor = 1.1
    shading.show_specular_highlight = True
    scene.display.render_aa = "16"


def add_camera(center: Vector, radius: float):
    cam_data = bpy.data.cameras.new("PreviewCamera")
    cam_data.lens = 52.0
    cam = bpy.data.objects.new("PreviewCamera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    distance = max(6.0, radius * 2.7)
    cam.location = center + Vector((distance * 0.62, -distance * 0.78,
                                    distance * 0.42))
    direction = center - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def render_file(path: str, out_dir: str) -> int:
    reset()
    if not os.path.exists(path):
        print("НЕТ ФАЙЛА", path)
        return 2

    bpy.ops.import_scene.fbx(filepath=path)
    low, high = world_bounds()
    center = (low + high) * 0.5
    radius = max((high - low).length * 0.5, 0.5)
    add_camera(center, radius)
    setup_render()

    name = os.path.splitext(os.path.basename(path))[0]
    os.makedirs(out_dir, exist_ok=True)
    target = os.path.join(out_dir, name + ".png")
    bpy.context.scene.render.filepath = target
    bpy.ops.render.render(write_still=True)
    print("RENDER", target)
    return 0


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                       "AssetSource", "Previews", "Menu")
    if "--out" in argv:
        index = argv.index("--out")
        if index + 1 < len(argv):
            out = argv[index + 1]
        argv = argv[:index] + argv[index + 2:]
    if not argv:
        print("укажи FBX после --")
        return 2
    return render_file(os.path.abspath(argv[0]), os.path.abspath(out))


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
