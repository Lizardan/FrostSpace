"""Превью клеток: стык двух плиток и край дырки под депо.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_cells_previews.py
    ... -- --out AssetSource/Previews/Cells

Клетки собираются тем же билдером, что и FBX (`world_cell_parts`), и ставятся на
свои места в осях депо — то есть картинка показывает именно стык, а не отдельный
меш. Три вопроса, на которые числами не ответить:

* `seam` — вдоль общей стороны двух соседних клеток: нет ли просвета и ступеньки;
* `edge` — с края авторской земли депо наружу: как выглядит граница выреза;
* `plan` — сверху все клетки разом: где дырки и сходятся ли квадраты в сетку.

Workbench не знает ни тумана, ни неба `SkyNightStars`, поэтому «красиво ли в
игре» здесь не решается — это решает кадр из редактора после расстановки.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import level_lib  # noqa: E402
import world_cell_parts as C  # noqa: E402
import world_layout as W  # noqa: E402

#: Пара соседних клеток, у которых общая сторона не тронута квадратом депо:
#: стык меряется именно там. (0:0) и (0:1) — станция и «Реле» по обе стороны
#: от мировой линии z = 0, обе генерируются.
SEAM_PAIR = ((0, 0), (0, 1))


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def to_blender(ux: float, uy: float, uz: float) -> Vector:
    """Unity -> Blender для `export_static`: (x, y, z) = (-u.x, -u.z, u.y).

    Клетка авторится в локальных осях, поэтому её вершины уже лежат в осях
    Unity относительно центра; сюда приходит позиция центра в осях депо.
    """
    return Vector((-ux, -uz, uy))


def add_camera(name, eye, target, lens=24.0, ortho=None):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(eye)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    if ortho is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho
    bpy.context.scene.camera = cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 800
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("CellsSky")
    scene.world.color = (0.52, 0.63, 0.72)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    # Свет обязателен: без него план и стык выходят однотонным серым полем, в
    # котором нельзя ни рельефа, ни дырки. Ровно поэтому у превью наследия солнце
    # есть, а у первой версии этого скрипта его не было.
    shading.show_shadows = True
    scene.display.render_aa = "8"


def add_sun() -> None:
    data = bpy.data.lights.new("sun", "SUN")
    data.energy = 3.0
    data.angle = math.radians(2.0)
    sun = bpy.data.objects.new("sun", data)
    bpy.context.scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50.0), math.radians(-12.0),
                          math.radians(30.0))


def build_all_cells(materials):
    """Все генерируемые клетки на своих местах в осях депо."""
    placed = {}
    for c in W.cells():
        if not c["content"] or W.depot_cover(c["i"], c["j"]) == "full":
            continue
        obj = C.build_cell(materials, c["i"], c["j"])
        if obj is None:
            continue
        (cx, cz) = W.cell_center(c["i"], c["j"])
        pos = to_blender(cx - W.DEPOT[0], 0.0, cz - W.DEPOT[1])
        obj.location = (pos.x, pos.y, 0.0)
        placed["%d:%d" % (c["i"], c["j"])] = obj
    return placed


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.abspath(argv[argv.index("--out") + 1] if "--out" in argv
                          else os.path.join(HERE, "..", "..", "AssetSource",
                                            "Previews", "Cells"))

    reset()
    materials = level_lib.make_level_materials()
    setup_render()
    add_sun()
    os.makedirs(out, exist_ok=True)
    placed = build_all_cells(materials)
    print("КЛЕТОК %d, из них с вырезом %d" % (
        len(placed), sum(1 for c in W.cells()
                        if W.depot_cover(c["i"], c["j"]) == "part" and c["content"])))

    # Стык двух соседних клеток: глаз на их общей стороне, взгляд вдоль неё.
    (a, b) = SEAM_PAIR
    assert ("%d:%d" % a) in placed and ("%d:%d" % b) in placed, \
        "пара для стыка не генерируется — поправь SEAM_PAIR"
    edge_x = (W.cell_center(*a)[0] + W.cell_center(*b)[0]) * 0.5
    edge_z = (W.cell_center(*a)[1] + W.cell_center(*b)[1]) * 0.5
    h = W.world_height(edge_x, edge_z)
    eye = to_blender(edge_x - W.DEPOT[0], h + 1.7, edge_z - W.DEPOT[1] - 120.0)
    aim = to_blender(edge_x - W.DEPOT[0], h + 1.2, edge_z - W.DEPOT[1] + 240.0)
    add_camera("seam", eye, aim, 35.0)
    bpy.context.scene.render.filepath = os.path.join(out, "seam.png")
    bpy.ops.render.render(write_still=True)

    # Край дырки: с авторской земли депо наружу, вдоль мировой x = -600.
    gx1 = W.DEPOT_GROUND[1]
    z = W.DEPOT[1]
    eye = to_blender(gx1 - W.DEPOT[0] - 40.0, W.world_height(gx1 - 40.0, z) + 1.7,
                     z - W.DEPOT[1])
    aim = to_blender(gx1 - W.DEPOT[0] + 260.0, W.world_height(gx1 + 260.0, z) + 1.0,
                     z - W.DEPOT[1])
    add_camera("edge", eye, aim, 30.0)
    bpy.context.scene.render.filepath = os.path.join(out, "edge.png")
    bpy.ops.render.render(write_still=True)

    # План: всё разом, сверху.
    add_camera("plan", (0.0, 0.0, 5200.0), (0.0, 0.0, 0.0), ortho=6800.0)
    bpy.context.scene.render.filepath = os.path.join(out, "plan.png")
    bpy.ops.render.render(write_still=True)

    print("PREVIEWS 3")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
