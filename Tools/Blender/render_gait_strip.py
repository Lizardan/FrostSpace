"""Полоса кадров походки: N кадров клипа в один ряд, вид сбоку и спереди.

Зачем: один кадр шага ничего не говорит о шаге. Читается только **последовательность**:
где стопа встала, где оторвалась, качается ли корпус в такт. `render_mech_previews.py`
снимает четыре фазы, но они лежат отдельными файлами, и сравнить их глазами дорого.
Здесь клип рендерится в сетку и склеивается в одну картинку (numpy + `bpy.data.images`),
поэтому дефект цикла видно сразу.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_gait_strip.py -- \
        "Assets/_Project/Content/Models/Mechs/ME_FrogWalker.fbx" \
        --clip Walk --frames 10 --out AssetSource/Previews/Gait

Камера неподвижна между кадрами: иначе движение не читается как движение.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

#: Размер одного кадра в полосе и отступ между ними, px.
CELL_W = 470
CELL_H = 640
GAP = 4


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
    mesh = bpy.data.meshes.new("StripGround")
    mesh.from_pydata([(-30, -30, 0), (30, -30, 0), (30, 30, 0), (-30, 30, 0)],
                     [], [(0, 1, 2, 3)])
    mesh.update()
    obj = bpy.data.objects.new("StripGround", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("StripSnow")
    mat.diffuse_color = (0.70, 0.78, 0.86, 1)
    mesh.materials.append(mat)


def make_camera(name: str, location, target, lens: float = 60.0):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat(
        "-Z", "Y").to_euler()
    return cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = CELL_W
    scene.render.resolution_y = CELL_H
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
    shading.show_specular_highlight = True
    scene.display.render_aa = "8"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("StripWorld")
    scene.world.color = (0.55, 0.65, 0.78)


def load_pixels(path: str) -> np.ndarray:
    image = bpy.data.images.load(path)
    width, height = image.size
    data = np.array(image.pixels[:], dtype=np.float32)
    bpy.data.images.remove(image)
    return data.reshape(height, width, 4)


def save_strip(cells: list, path: str) -> None:
    """Склеивает кадры в ряд. Строки numpy идут снизу вверх — как у Blender."""
    height = max(cell.shape[0] for cell in cells)
    width = sum(cell.shape[1] for cell in cells) + GAP * (len(cells) - 1)
    canvas = np.zeros((height, width, 4), dtype=np.float32)
    canvas[..., 3] = 1.0
    x = 0
    for cell in cells:
        canvas[:cell.shape[0], x:x + cell.shape[1]] = cell
        x += cell.shape[1] + GAP
    image = bpy.data.images.new("Strip", width=width, height=height, alpha=True)
    image.pixels = canvas.ravel().tolist()
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("STRIP", path, "%dx%d" % (width, height))


def set_action(arm, clip: str):
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    action = next((a for a in bpy.data.actions if clip.lower() in a.name.lower()),
                  None)
    if action is None:
        raise RuntimeError("нет клипа %s" % clip)
    arm.animation_data.action = action
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    if getattr(arm.animation_data, "action_slot", None) is None:
        slots = list(getattr(action, "slots", []) or [])
        if slots:
            try:
                arm.animation_data.action_slot = slots[0]
            except (AttributeError, TypeError):
                pass
    return action


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]
    clip = argv[argv.index("--clip") + 1] if "--clip" in argv else "Walk"
    count = int(argv[argv.index("--frames") + 1]) if "--frames" in argv else 10
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Gait")
    views = (argv[argv.index("--views") + 1] if "--views" in argv
             else "side,front").split(",")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    mesh = next((o for o in bpy.context.scene.objects if o.type == "MESH"), None)
    if arm is None or mesh is None:
        raise RuntimeError("FBX без арматуры или меша")

    add_ground()
    setup_render()

    base = os.path.splitext(os.path.basename(fbx))[0]
    action = set_action(arm, clip)
    start, end = (int(round(v)) for v in action.frame_range)
    span = max(1, end - start)

    # Габарит по всему циклу: лапы уходят вперёд/назад на метры, и камера,
    # поставленная по одному кадру, обрезает их на замахе.
    low, high, span_x, span_y = 1e9, -1e9, 0.0, 0.0
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = mesh.evaluated_get(depsgraph)
        data = evaluated.to_mesh()
        matrix = evaluated.matrix_world
        for vertex in data.vertices:
            point = matrix @ vertex.co
            low = min(low, point.z)
            high = max(high, point.z)
            span_x = max(span_x, abs(point.x))
            span_y = max(span_y, abs(point.y))
        evaluated.to_mesh_clear()

    mid = (low + high) * 0.5
    height = max(high - low, 0.5)

    # Кадрирование считается, а не подбирается: `sensor_fit = AUTO` вешает
    # 36 мм на большую сторону кадра, поэтому вертикальный и горизонтальный
    # углы разные, и дистанция — максимум из двух требований.
    lens, sensor = 55.0, 36.0
    if CELL_H >= CELL_W:
        tan_v = sensor * 0.5 / lens
        tan_h = tan_v * CELL_W / CELL_H
    else:
        tan_h = sensor * 0.5 / lens
        tan_v = tan_h * CELL_H / CELL_W
    # Запас 18 % по вертикали (корпус дышит) и 8 % по горизонтали.
    need_v = height * 0.59
    dist_side = max(need_v / tan_v, span_y * 1.08 / tan_h)
    dist_front = max(need_v / tan_v, span_x * 1.08 / tan_h)
    cams = {
        "side": make_camera("Cam_side", (dist_side, 0.0, mid), (0.0, 0.0, mid),
                            lens=lens),
        "front": make_camera("Cam_front", (0.0, -dist_front, mid),
                             (0.0, 0.0, mid), lens=lens),
        "quarter": make_camera(
            "Cam_quarter",
            (dist_side * 0.70, -dist_front * 0.72, mid + height * 0.20),
            (0.0, 0.0, mid), lens=lens),
    }
    print("FRAME height=%.2f span_y=%.2f span_x=%.2f dist side/front=%.2f/%.2f"
          % (height, span_y, span_x, dist_side, dist_front))

    scene = bpy.context.scene
    for view in views:
        camera = cams[view]
        scene.camera = camera
        cells, paths = [], []
        for index in range(count):
            # Сэмплы по циклу, последний кадр не дублирует первый.
            frame = start + int(round(span * index / float(count)))
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            path = os.path.join(out, "_cell_%s_%s_%02d.png" % (base, view, index))
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            cells.append(load_pixels(path))
            paths.append(path)
        strip = os.path.join(out, "%s_%s_%s.png" % (base, clip.lower(), view))
        save_strip(cells, strip)
        for path in paths:
            try:
                os.remove(path)
            except OSError:
                pass
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
