"""Контактный лист роботов: сетка кадров с подписями.

Собирается средствами bpy — плоскости с эмиссионным материалом и текстовые
объекты, — поэтому не требует PIL и работает headless. Цвета кадров не
меняются: включён view transform `Standard`.

Запуск:
    blender --background --factory-startup --python Tools/Blender/make_robot_sheet.py -- \
        --out AssetSource/Previews/Robots/Lineup/robots_batch2.png --cols 5 \
        R6_Smelter R7_Drifter R8_Bulwark

Без имён берёт все PNG `*_front.png` из папки кадров (`--src`).
"""

from __future__ import annotations

import glob
import math
import os
import sys
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_SRC = os.path.join(ROOT, "AssetSource", "Previews", "Robots", "Lineup")
DEFAULT_OUT = os.path.join(DEFAULT_SRC, "robots_sheet.png")

PITCH_X = 1.12
PITCH_Y = 1.26
LABEL_DROP = 0.56
PLANE = 1.0


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def parse_args(argv):
    out = DEFAULT_OUT
    src = DEFAULT_SRC
    cols = 5
    width = 2400
    title = ""
    names = []
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg in ("--out", "--src", "--cols", "--title", "--width") and i + 1 < len(argv):
            value = argv[i + 1]
            if arg == "--out":
                out = value
            elif arg == "--src":
                src = value
            elif arg == "--cols":
                cols = int(value)
            elif arg == "--width":
                width = int(value)
            else:
                title = value
            i += 2
            continue
        names.append(arg)
        i += 1
    return os.path.abspath(out), os.path.abspath(src), cols, width, title, names


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.curves, bpy.data.images):
        for item in list(block):
            block.remove(item)


def emission_material(name: str, color=None, image=None):
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:  # В Blender 4+ ноды уже есть, сеттер устарел.
        mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = 1.0
    if image is not None:
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.interpolation = "Linear"
        links.new(texture.outputs["Color"], emission.inputs["Color"])
    else:
        emission.inputs["Color"].default_value = color
    links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return mat


def add_image_plane(path: str, center):
    image = bpy.data.images.load(path)
    bpy.ops.mesh.primitive_plane_add(size=PLANE, location=center)
    obj = bpy.context.active_object
    obj.name = os.path.basename(path)
    obj.data.materials.append(emission_material("M_" + obj.name, image=image))
    return obj


def add_label(text: str, center, size: float = 0.085):
    curve = bpy.data.curves.new(name="Label_" + text, type="FONT")
    curve.body = text
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.size = size
    obj = bpy.data.objects.new("Label_" + text, curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = center
    obj.data.materials.append(
        emission_material("M_Label_" + text, color=(0.78, 0.82, 0.86, 1.0)))
    return obj


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out, src, cols, width, title, names = parse_args(argv)

    if not names:
        names = sorted(os.path.basename(p)[:-len("_front.png")]
                       for p in glob.glob(os.path.join(src, "*_front.png")))
    if not names:
        print("нечего собирать: нет кадров в", src)
        return 2

    paths = []
    for name in names:
        path = os.path.join(src, name + "_front.png")
        if not os.path.exists(path):
            print("НЕТ КАДРА", path)
            continue
        paths.append((name, path))
    if not paths:
        return 2

    reset()
    rows = int(math.ceil(len(paths) / float(cols)))
    grid_w = (cols - 1) * PITCH_X + PLANE
    half = (rows - 1) * PITCH_Y * 0.5 + PLANE * 0.5
    y_min = -half - 0.24
    y_max = half + (0.62 if title else 0.0)
    grid_h = y_max - y_min
    cam_y = (y_max + y_min) * 0.5

    for index, (name, path) in enumerate(paths):
        col = index % cols
        row = index // cols
        x = col * PITCH_X - (cols - 1) * PITCH_X * 0.5
        y = -row * PITCH_Y + (rows - 1) * PITCH_Y * 0.5
        add_image_plane(path, (x, y, 0.0))
        add_label(name, (x, y - LABEL_DROP, 0.02))

    if title:
        add_label(title, (0.0, y_max - 0.30, 0.02), 0.12)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.cycles.device = "CPU"
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    if scene.world is None:
        scene.world = bpy.data.worlds.new("SheetWorld")
    if scene.world.node_tree is None:
        scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.10, 0.11, 0.13, 1.0)
        background.inputs["Strength"].default_value = 1.0

    res_x = width
    scene.render.resolution_x = res_x
    scene.render.resolution_y = int(round(res_x * grid_h / grid_w))
    scene.render.resolution_percentage = 100

    cam_data = bpy.data.cameras.new("SheetCamera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = grid_w
    cam_data.sensor_fit = "HORIZONTAL"
    cam = bpy.data.objects.new("SheetCamera", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.0, cam_y, 10.0)
    cam.rotation_euler = (0.0, 0.0, 0.0)
    scene.camera = cam

    os.makedirs(os.path.dirname(out), exist_ok=True)
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("SHEET", out, scene.render.resolution_x, scene.render.resolution_y,
          "items", len(paths))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
