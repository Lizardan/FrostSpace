"""Рендерит превью транспорта FrostSpace: вид сбоку, три четверти, спереди и сверху.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_ride_previews.py -- \
        "Assets/_Project/Content/Models/Level/Hoverbike.fbx" \
        --out AssetSource/Previews/Ride

Workbench выбран намеренно: он быстрый, не требует GPU-контекста в headless и
показывает faceted-силуэт и материал-цвета. Камера кадрируется по габариту меша,
поэтому скрипт годится и для ховербайка, и для мотоцикла с колёсами.
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


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights):
        for item in list(block):
            block.remove(item)


def add_ground() -> None:
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)],
                     [], [(0, 1, 2, 3)])
    mesh.update()
    obj = bpy.data.objects.new("PreviewGround", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("PreviewSnow")
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.72, 0.80, 0.88, 1)
        bsdf.inputs["Roughness"].default_value = 0.9
    mat.diffuse_color = (0.72, 0.80, 0.88, 1)
    mesh.materials.append(mat)


def model_bounds() -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or obj.name == "PreviewGround":
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo.x, world.x), min(lo.y, world.y), min(lo.z, world.z)))
            hi = Vector((max(hi.x, world.x), max(hi.y, world.y), max(hi.z, world.z)))
    return lo, hi


def add_camera(name: str, location: Vector, target: Vector, lens: float = 60.0):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = location
    cam.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    return cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.60, 0.70, 0.80)

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


def render_to(path: str) -> None:
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "..", "AssetSource",
        "Previews", "Ride")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    lo, hi = model_bounds()
    center = (lo + hi) * 0.5
    size = hi - lo
    base = os.path.splitext(os.path.basename(fbx))[0]
    print("BOUNDS", tuple(round(v, 3) for v in size))

    add_ground()
    setup_render()

    # Нос в Blender −Y; камера сбоку смотрит на правый борт.
    distance = max(size.x, size.y, size.z) * 1.55 + 1.0
    views = {
        "side": (Vector((distance, 0.0, center.z + size.z * 0.35)), center),
        "three_quarter": (Vector((distance * 0.72, -distance * 0.78,
                                  center.z + size.z * 0.55)), center),
        "front": (Vector((0.0, -distance * 1.15, center.z + size.z * 0.30)),
                  center),
        "top": (Vector((center.x, center.y, center.z + distance * 1.25)),
                center),
    }

    for name, (location, target) in views.items():
        cam = add_camera("Cam_" + name, location, target,
                         lens=52.0 if name != "top" else 60.0)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "%s_%s.png" % (base, name)))

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
