"""Рендерит превью робо-животного: rest, idle и движение.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_animal_previews.py -- \
        Assets/_Project/Content/Models/RobotAnimals/Wolf/RA_Wolf.fbx

Превью складываются в `AssetSource/Previews/Animals` (вне Unity и git).
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "AssetSource",
                                   "Previews", "Animals"))


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def add_ground(z: float = 0.0) -> None:
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-20, -20, z), (20, -20, z), (20, 20, z), (-20, 20, z)],
                     [], [(0, 1, 2, 3)])
    mesh.update()
    obj = bpy.data.objects.new("PreviewGround", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("PreviewSnow")
    if mat.node_tree is None:  # В Blender 4+ ноды уже есть, сеттер устарел.
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.72, 0.80, 0.88, 1)
        bsdf.inputs["Roughness"].default_value = 0.9
    mat.diffuse_color = (0.72, 0.80, 0.88, 1)
    mesh.materials.append(mat)


def add_camera(center: Vector, size: float) -> None:
    cam_data = bpy.data.cameras.new("PreviewCamera")
    cam_data.lens = 58
    cam = bpy.data.objects.new("PreviewCamera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    distance = max(size * 2.0, 1.4)
    cam.location = center + Vector((distance * 0.80, -distance, distance * 0.55))
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam


def set_action(arm, token: str, frame: int) -> bool:
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    action = next((a for a in bpy.data.actions if token in a.name), None)
    if action is None:
        print("НЕТ ACTION", token, [a.name for a in bpy.data.actions])
        return False
    arm.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    return True


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
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


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("укажи FBX после --")
        return 2
    fbx = os.path.abspath(args[0])
    out = args[args.index("--out") + 1] if "--out" in args else OUT
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if arm is None or not meshes:
        raise RuntimeError("FBX без арматуры или меша")

    # FBX из нашего экспортёра приходит в Blender уже Z-up: меш-потомок
    # компенсирует Y-up файл. Поворот арматуры ломал кадр (модель уходила под
    # пол), поэтому здесь только замер габарита.
    corners = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    center = (lo + hi) * 0.5
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z, 0.4)
    add_ground(lo.z)
    add_camera(center, size)
    setup_render()
    base = os.path.splitext(os.path.basename(fbx))[0]

    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    bpy.context.scene.render.filepath = os.path.join(out, base + "_rest.png")
    bpy.ops.render.render(write_still=True)

    for token, frames in (("idle_loop", (0, 20)), ("move_loop", (0, 10, 20, 30))):
        for frame in frames:
            if not set_action(arm, token, frame):
                break
            bpy.context.scene.render.filepath = os.path.join(
                out, "%s_%s_%02d.png" % (base, token, frame))
            bpy.ops.render.render(write_still=True)
    print("PREVIEWS", out)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
