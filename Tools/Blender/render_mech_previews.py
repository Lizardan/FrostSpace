"""Превью ходуна: rest-поза с трёх сторон и кадры клипов Idle/Walk/Run.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_mech_previews.py -- \
        "Assets/_Project/Content/Models/Mechs/ME_FrogWalker.fbx" \
        --out AssetSource/Previews/Mechs

Отличие от `render_robot_previews.py`: камера ниже и дальше (ходун выше робота),
клипы ищутся по подстроке имени (`Idle`/`Walk`/`Run`), кадры берутся из реального
диапазона действия, а не из констант, и есть кадр «кабина» — вид спереди по груди,
чтобы видеть открытый проём и посадку.

Workbench выбран по той же причине, что у роботов: быстро, headless, показывает
плоские нормали и цвет материала.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# Фазы походки берутся из того же `GAIT_STYLES`, чем печётся клип: кадры
# превью обязаны попадать в стойку и в середину замаха, а не в равные шаги.
# Стиль у каждого юнита свой, поэтому он ищется по имени FBX в `MECHS`.
import mech_lib  # noqa: E402

CLIPS = ("Idle", "Walk", "Run")


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


def make_camera(name: str, location, target, lens: float = 55.0):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat(
        "-Z", "Y").to_euler()
    return cam


def setup_render(engine: str) -> None:
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


def render_to(path: str, camera) -> None:
    bpy.context.scene.camera = camera
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def set_pose(arm, clip: str, frame: int) -> bool:
    """Ставит действие, чьё имя содержит `clip`, на кадр `frame`."""
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    action = next((a for a in bpy.data.actions if clip.lower() in a.name.lower()),
                  None)
    if action is None:
        return False
    arm.animation_data.action = action
    slot = getattr(arm.animation_data, "action_slot", None)
    if slot is None and getattr(action, "slots", None):
        try:
            arm.animation_data.action_slot = action.slots[0]
        except Exception:
            pass
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    return True


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Mechs")
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
    setup_render(engine)
    base = os.path.splitext(os.path.basename(fbx))[0]

    low = min((mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box)
    high = max((mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box)
    height = max(high - low, 0.5)
    # Кадр целиком: лапы ходуна уходят далеко вперёд/назад, и камера по центру
    # габарита обрезала их на шаге (превью перестало быть проверкой походки).
    mid = (low + high) * 0.5
    distance = height * 2.3

    views = {
        "front": ((0.0, -distance, mid + height * 0.05), (0.0, 0.0, mid)),
        "side": ((distance, 0.0, mid + height * 0.05), (0.0, 0.0, mid)),
        "three_quarter": ((distance * 0.72, -distance * 0.72, mid + height * 0.30),
                          (0.0, 0.0, mid)),
    }
    cameras = {name: make_camera("Cam_" + name, loc, target)
               for name, (loc, target) in views.items()}

    # Открытый проём кабины: смотрим в грудь спереди, ближе и ниже.
    chest = mid + height * 0.18
    cockpit = make_camera("Cam_cockpit", (0.0, -height * 1.15, chest + 0.35),
                          (0.0, 0.0, chest), lens=45.0)

    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    for name, camera in cameras.items():
        render_to(os.path.join(out, "%s_%s.png" % (base, name)), camera)
    render_to(os.path.join(out, "%s_cockpit.png" % base), cockpit)

    # Кадры клипов: шаг и бег снимаются **сбоку** — спереди походку не проверить
    # (шаг читается только по профилю). Точки берутся из фаз стиля юнита:
    # первая и последняя кадры стойки, середина замаха и середина стойки.
    unit = base[3:] if base.startswith("ME_") else base
    style = mech_lib.GAIT_STYLES.get(
        mech_lib.MECHS.get(unit, {}).get("gait", mech_lib.DEFAULT_GAIT), {})
    for clip in CLIPS:
        action = next((a for a in bpy.data.actions
                       if clip.lower() in a.name.lower()), None)
        if action is None:
            print("НЕТ КЛИПА", clip)
            continue
        start, end = (int(round(v)) for v in action.frame_range)
        span = max(1, end - start)
        key = clip.lower()
        cfg = style.get(key, {})
        # Доля стойки в цикле: по ней берутся фазы «касание», «середина стойки»,
        # «отрыв» и «середина замаха» — иначе кадр попадает между фазами.
        duty = float(cfg.get("duty", 1.0))
        if duty >= 1.0:
            # Стойка без шага: смотрим позу от начала до конца цикла.
            phases = (0.0, 1.0 / 3.0, 2.0 / 3.0, 1.0)
        else:
            phases = (0.0, duty * 0.5, duty,
                      duty + (1.0 - duty) * 0.5)
        camera = cameras["front"] if duty >= 1.0 else cameras["side"]
        for offset, phase in enumerate(phases):
            frame = min(int(round(start + phase * span)), end)
            if not set_pose(arm, clip, frame):
                break
            render_to(os.path.join(
                out, "%s_%s_%02d.png" % (base, key, offset)), camera)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
