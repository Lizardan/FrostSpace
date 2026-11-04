"""Превью клеток: стык двух плиток, выемка под коридором и общий вид карты.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_cells_previews.py
    ... -- --out AssetSource/Previews/Cells

Клетки собираются теми же билдерами, что и FBX (`world_cell_parts` плюс
`transport_parts`), и ставятся на свои места в осях депо — то есть картинка
показывает сборку, а не отдельный меш. Четыре вопроса, на которые числами не
ответить:

* `seam` — вдоль общей стороны двух соседних клеток: нет ли просвета и ступеньки;
* `corridor` — глаз на бровке там, где линия идёт по склону: путь читается
  прорезанным в снегу или положенным на стол;
* `depot` — косой вид на клетку депо: сквозная линия через неё проходит, а не
  кончается в ней;
* `yard` — глаз в уровне порога вагона вдоль двора: отметки платформы, рампы и
  коробки сходятся с путём или нет;
* `yard_plan` — ортоплан двора: геометрия съезда, платформы и ворот без
  искажения перспективы;
* `map` — план всей карты с рельефом: сеть читается единой, а не лоскутной.

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

import depot_parts as DP  # noqa: E402
import level_lib  # noqa: E402
import transport_parts as TP  # noqa: E402
import world_cell_parts as C  # noqa: E402
import world_layout as W  # noqa: E402

#: Пара соседних клеток, у которых общая сторона длинная и идёт через разные
#: формы рельефа: стык меряется именно там.
SEAM_PAIR = ((0, 0), (0, 1))

#: Клетка депо: её вид обязан показывать сквозную линию, а не тупик.
DEPOT_CELL = (-2, -2)


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def to_blender(ux: float, uy: float, uz: float) -> Vector:
    """Unity -> Blender для `export_static`: (x, y, z) = (-u.x, -u.z, u.y).

    Клетка авторится в локальных осях, поэтому её вершины уже лежат в осях
    Unity относительно центра; сюда приходит позиция центра в осях депо.

    Знак по X — не опечатка и не зеркало кадра: `export_static` перед выгрузкой
    зеркалит меш по X, и превью без этого знака показывало бы карту не так, как
    её увидит Unity. Как именно совпадают лево и право, проверяется кадром из
    редактора (`Temp/shot_yard.cs`), а не рассуждением о знаках.
    """
    return Vector((-ux, -uz, uy))


def add_camera(name, eye, target, lens=24.0, ortho=None):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    # Дефолтный `clip_end` камеры Blender — 100 м, а клетка в шесть раз больше.
    # Без этой строки верхний план выходит однотонным: карта просто отрезана по
    # дистанции, и выглядит это как «рельефа нет», хотя рельеф на месте.
    data.clip_start = 0.5
    data.clip_end = 40000.0
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(eye)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    if ortho is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho
    bpy.context.scene.camera = cam


def setup_render(square: bool = False) -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1400 if square else 1200
    scene.render.resolution_y = 1400 if square else 800
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("CellsSky")
    scene.world.color = (0.52, 0.63, 0.72)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    # Тот же приём, что у превью наследия: HDRI `paint.sl` даёт направленный
    # свет, а полсть (`cavity`) поднимает рёбра фасеток. Против однотонного
    # снега помогает только контраст по краям граней, поэтому полость обязательна.
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    scene.display.render_aa = "8"


def build_all_cells(materials):
    """Все клетки на своих местах в осях депо: земля, транспорт и двор клетки."""
    placed = {}
    for c in W.cells():
        (i, j) = (c["i"], c["j"])
        pieces = [C.build_cell(materials, i, j)]
        pieces += TP.build_cell_transport(materials, i, j)
        pieces += DP.build_cell_depot(materials, i, j)
        (cx, cz) = W.cell_center(i, j)
        pos = to_blender(cx - W.DEPOT[0], 0.0, cz - W.DEPOT[1])
        for obj in pieces:
            obj.location = (pos.x, pos.y, 0.0)
        placed["%d:%d" % (i, j)] = pieces
    return placed


def shoot(out: str, name: str) -> None:
    bpy.context.scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("КАДР %s" % name)


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.abspath(argv[argv.index("--out") + 1] if "--out" in argv
                          else os.path.join(HERE, "..", "..", "AssetSource",
                                            "Previews", "Cells"))

    reset()
    materials = level_lib.make_level_materials()
    setup_render()
    os.makedirs(out, exist_ok=True)
    placed = build_all_cells(materials)
    tris = sum(len(o.data.polygons) for pieces in placed.values() for o in pieces)
    print("КЛЕТОК %d, трис на всей карте %d" % (len(placed), tris))

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
    shoot(out, "seam")

    # Выемка: глаз на бровке там, где линия идёт поперёк склона. Если путь
    # положен на стол, а не прорезан, это видно именно здесь.
    ln = TP.line_of("rail")
    s = W.project_to_line(0.0, 400.0, ln["pts"])[1]
    (ex, ez) = W.point_at_distance(ln["pts"], s)
    level = W.level_at(ln["table"], s)
    eye = to_blender(ex - 13.0 - W.DEPOT[0], level + 3.2,
                     ez - 70.0 - W.DEPOT[1])
    aim = to_blender(ex + 2.0 - W.DEPOT[0], level + 1.0,
                     ez + 120.0 - W.DEPOT[1])
    add_camera("corridor", eye, aim, 35.0)
    shoot(out, "corridor")

    # Косой вид на клетку депо: линия через неё идёт насквозь, а не упирается.
    (di, dj) = DEPOT_CELL
    (dcx, dcz) = W.cell_center(di, dj)
    eye = to_blender(dcx - W.DEPOT[0] - 520.0, W.world_height(dcx, dcz) + 260.0,
                     dcz - W.DEPOT[1] - 520.0)
    add_camera("depot", eye, to_blender(dcx - W.DEPOT[0], 0.0,
                                        dcz - W.DEPOT[1] + 200.0), 24.0)
    shoot(out, "depot")

    # Глаз в уровне порога вагона вдоль двора: по этому кадру видно, что пол
    # депо лежит в уровне пути, а не в яме с насыпью, и что платформа, рампа и
    # коробка сходятся по отметкам. Числа это доказать не могут — они меряют
    # одну точку, а кадр смотрит вдоль 80 м.
    eye = to_blender(0.5, DP.head(0.5, -38.0) + 2.20, -38.0)
    aim = to_blender(6.0, DP.apron(6.0, 26.0) + 1.00, 30.0)
    add_camera("yard", eye, aim, 24.0)
    shoot(out, "yard")

    # Ворота и выезд: глаз во дворе на осевой дороги, взгляд сквозь арку наружу.
    # Кадр обязан показать три вещи: полотно дороги входит во двор вровень, а не
    # обрывом в ров; проём читается створом в капитальной стене, а не дырой; и
    # свет из-под перемычки ведёт человека от вагона к дороге.
    (gx, gz) = DP._gate_center()
    eye = to_blender(gx - 30.0, DP.yard(gx - 30.0, gz - 30.0) + 2.30, gz - 30.0)
    (rx, rz) = W.to_world(gx + 12.0, gz + 4.0)
    road = W.transport_floor(rx, rz)
    aim = to_blender(gx + 12.0,
                     (road["level"] if road else W.world_height(rx, rz)) + 2.20,
                     gz + 4.0)
    add_camera("gate", eye, aim, 30.0)
    shoot(out, "gate")

    # Тот же створ с дороги: кадр снаружи показывает, читается ли арка как
    # ворота в капитальной стене для того, кто едет со стороны посёлка.
    eye = to_blender(gx + 34.0, DP.yard(gx + 34.0, gz) + 2.60, gz + 10.0)
    aim = to_blender(gx - 20.0, DP.yard(gx - 20.0, gz) + 3.20, gz)
    add_camera("gate_out", eye, aim, 32.0)
    shoot(out, "gate_out")

    # Стена в косом виде из двора: зубцы, фонари и тумбы на краях проёма. Числа
    # доказывают отметки, а то, читается ли периметр ограждением или лентой,
    # видно только глазом.
    eye = to_blender(-36.0, DP.yard(-36.0, -110.0) + 1.75, -110.0)
    aim = to_blender(gx, DP.yard(gx, gz) + 2.60, gz)
    add_camera("wall", eye, aim, 32.0)
    shoot(out, "wall")

    # Южный путевой разрыв: глаз на оси главного пути южнее стены, взгляд вдоль
    # всего двора к северной паре столбов. Это кадр прибытия по геометрии: если
    # столбы стоят не на оси или ушли в габарит кузова, видно именно здесь.
    rail_x = W.DEPOT_MAIN_X - W.DEPOT[0]
    (x0, x1, z0, z1) = DP.local(W.DEPOT_YARD)
    eye = to_blender(rail_x + 6.0, DP.head(rail_x, z0 + 26.0) + 2.60, z0 + 26.0)
    aim = to_blender(rail_x, DP.head(rail_x, z1) + 1.60, z1 + 26.0)
    add_camera("masts", eye, aim, 30.0)
    shoot(out, "masts")

    # План двора: ортография сверху, чтобы видеть геометрию съезда, платформы
    # и ворот без искажения перспективы.
    setup_render(square=True)
    add_camera("yard_plan", to_blender(-16.0, 400.0, -30.0),
               to_blender(-16.0, 0.0, -30.0), ortho=420.0)
    shoot(out, "yard_plan")

    # План всей карты с рельефом: сеть читается единой, а не лоскутной.
    add_camera("map", to_blender(0.0, 6000.0, 0.0), (0.0, 0.0, 0.0),
               ortho=7000.0)
    shoot(out, "map")

    print("PREVIEWS 7")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
