"""Рендерит превью робота: T-поза и кадры клипов.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_robot_previews.py -- \
        "Assets/_Project/Content/Models/Robots/R1_PolarMechanic.fbx" \
        --out AssetSource/Previews/Robots

Workbench выбран намеренно: он быстрый, не требует GPU-контекста в headless и
показывает faceted-силуэт и материал-цвета. Для финальных «геройских» кадров
можно переключить --engine cycles.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))


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


def add_ground():
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)],
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
    return obj


def add_camera():
    cam_data = bpy.data.cameras.new("PreviewCamera")
    cam_data.lens = 62
    cam = bpy.data.objects.new("PreviewCamera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector((3.3, -4.6, 2.35))
    target = Vector((0.0, 0.0, 0.98))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def set_action(arm, clip: str, frame: int):
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    action = None
    for candidate in bpy.data.actions:
        if clip in candidate.name:
            action = candidate
            break
    if action is None:
        print("НЕТ ACTION для", clip, "есть:", [a.name for a in bpy.data.actions])
        return
    arm.animation_data.action = action
    slot = getattr(arm.animation_data, "action_slot", None)
    if slot is None and getattr(action, "slots", None):
        try:
            arm.animation_data.action_slot = action.slots[0]
        except Exception:
            pass
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def setup_render(out_dir: str, engine: str):
    scene = bpy.context.scene
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.60, 0.70, 0.80)

    if engine == "workbench":
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
    else:
        scene.render.engine = "CYCLES"
        scene.cycles.samples = 48
        scene.cycles.use_denoising = True


def render_to(path: str):
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
        HERE, "..", "..", "AssetSource", "Previews", "Robots")
    engine = argv[argv.index("--engine") + 1] if "--engine" in argv else "workbench"
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    mesh = next((o for o in bpy.context.scene.objects if o.type == "MESH"), None)
    if arm is None or mesh is None:
        raise RuntimeError("FBX без арматуры или меша")

    add_ground()
    add_camera()
    setup_render(out, engine)
    base = os.path.splitext(os.path.basename(fbx))[0]

    # T-поза: снимаем действие и ставим rest
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    render_to(os.path.join(out, "%s_tpose.png" % base))

    for clip, frames in (("walk_loop", (0, 10, 20, 30)),
                         ("idle_loop", (0, 30)),
                         ("sprint_loop", (0, 5, 10, 15))):
        arm.data.pose_position = "POSE"
        for frame in frames:
            set_action(arm, clip, frame)
            render_to(os.path.join(out, "%s_%s_%02d.png" % (base, clip, frame)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
