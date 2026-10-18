"""Превью партии роботов: по одному кадру с двух сторон на модель.

В отличие от `render_robot_previews.py` (T-поза + кадры всех клипов) этот скрипт
делает обзорные кадры для выбора: робот в idle, камера ближе, спереди-сбоку и
сзади-сбоку. Один прогон — все переданные FBX.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_robot_lineup.py -- \
        R6_Smelter R7_Drifter --out AssetSource/Previews/Robots/Lineup

Без списка имён берёт все FBX из `Content/Models/Robots`.
"""

from __future__ import annotations

import glob
import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROBOTS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                      "Content", "Models", "Robots"))

#: (имя, позиция камеры) — все смотрят в центр корпуса. Бок важен для деталей,
#: которые выходят вперёд (крюк, щит на внешней стороне руки).
VIEWS = (
    ("front", Vector((2.90, -4.30, 1.75))),
    ("side", Vector((4.60, 0.70, 1.75))),
    ("back", Vector((-2.90, 4.30, 1.75))),
)
TARGET = Vector((0.0, 0.0, 1.02))


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
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.72, 0.80, 0.88, 1)
        bsdf.inputs["Roughness"].default_value = 0.9
    mat.diffuse_color = (0.72, 0.80, 0.88, 1)
    mesh.materials.append(mat)


def add_camera(position: Vector):
    cam_data = bpy.data.cameras.new("PreviewCamera")
    cam_data.lens = 68
    cam = bpy.data.objects.new("PreviewCamera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = position
    cam.rotation_euler = (TARGET - position).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
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


def set_idle(arm, frame: int = 0) -> None:
    """Ставит idle-клип на кадр: видно, как робот стоит в игре."""
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    action = None
    for candidate in bpy.data.actions:
        if "idle_loop" in candidate.name:
            action = candidate
            break
    if action is None:
        print("НЕТ idle_loop, остаётся rest")
        arm.data.pose_position = "REST"
        return
    arm.animation_data.action = action
    if getattr(arm.animation_data, "action_slot", None) is None and getattr(
            action, "slots", None):
        try:
            arm.animation_data.action_slot = action.slots[0]
        except Exception:  # noqa: BLE001
            pass
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def parse_args(argv):
    """Разбирает `--out DIR`, `--frame N` и имена моделей."""
    out = os.path.join(HERE, "..", "..", "AssetSource", "Previews", "Robots",
                       "Lineup")
    frame = 0
    names = []
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg == "--out" and i + 1 < len(argv):
            out = argv[i + 1]
            i += 2
            continue
        if arg == "--frame" and i + 1 < len(argv):
            frame = int(argv[i + 1])
            i += 2
            continue
        names.append(arg)
        i += 1
    return os.path.abspath(out), frame, names


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out, frame, names = parse_args(argv)
    os.makedirs(out, exist_ok=True)

    names = names or sorted(os.path.splitext(os.path.basename(p))[0]
                           for p in glob.glob(os.path.join(ROBOTS, "*.fbx")))
    if not names:
        print("нет FBX в", ROBOTS)
        return 2

    for name in names:
        fbx = os.path.join(ROBOTS, name + ".fbx")
        if not os.path.exists(fbx):
            print("НЕТ ФАЙЛА", fbx)
            continue
        reset()
        bpy.ops.import_scene.fbx(filepath=fbx)
        arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"),
                   None)
        mesh = next((o for o in bpy.context.scene.objects if o.type == "MESH"), None)
        if arm is None or mesh is None:
            print("ПРОПУСК (нет арматуры/меша):", name)
            continue
        add_ground()
        setup_render()
        set_idle(arm, frame)
        for view, position in VIEWS:
            add_camera(position)
            bpy.context.scene.render.filepath = os.path.join(
                out, "%s_%s.png" % (name, view))
            bpy.ops.render.render(write_still=True)
            print("RENDER", name, view)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
