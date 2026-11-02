# -*- coding: utf-8 -*-
"""Превью транспортного слоя: линия на плите, стык двух клеток, угол трассы.

    blender --background --factory-startup --python Tools/Blender/render_transport_previews.py
        -- --out AssetSource/Previews/Transport

Куски собираются тем же билдером, что и замер (`transport_parts`), и ставятся на
свои места в осях депо: картинка показывает линию целиком, а не отдельный меш.
Под неё кладётся грубая плита (160×160, тот же `plate_height`) — без неё путь
висит в пустоте, и ровно это слой A и обязан был показать.

Четыре вопроса, на которые числами не ответить:

* `map` — план всей карты: видно трассу, её повороты и ветки дорог;
* `joint` — глаз на ребре решётки там, где линия переходит из чанка в чанк:
  числами стык совпадает до нуля, а вот как он выглядит — только здесь;
* `kink` — угол трассы у станции: там метла раньше вспухала над столом;
* `depot` — вид вдоль оси от южного края клетки депо: путь читается прорезанным
  в снегу или положенным на стол.

Workbench не знает ни тумана, ни неба, поэтому «красиво ли в игре» решает кадр
из редактора после расстановки.
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
import transport_parts as T  # noqa: E402
import world_layout as W  # noqa: E402

#: Разрешение подложки: 6600 м / 160 = 41 м на ячейку. Плита нужна только чтобы
#: видеть, где линия лежит на земле, а где висит; рельеф клетки — другой кадр.
PLATE_N = 160

#: Стык для кадра `joint`: переход линии между южными клетками, самый прямой и
#: самый длинный — там видно даже миллиметровую ступеньку.
JOINT_CELLS = ((-2, -5), (-2, -4))

#: Угол трассы для кадра `kink`: вершина ломаной у южных ворот станции.
KINK = (0.0, -80.0)


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def to_blender(ux: float, uy: float, uz: float) -> Vector:
    """Unity -> Blender для `export_static`: (x, y, z) = (-u.x, -u.z, u.y)."""
    return Vector((-ux, -uz, uy))


def add_plate(materials) -> None:
    """Грубая плита под линией: тот же `plate_height`, но редко."""
    n = PLATE_N
    (x0, x1, z0, z1) = W.BB
    verts = []
    for j in range(n):
        wz = z0 + (z1 - z0) * j / (n - 1)
        for i in range(n):
            wx = x0 + (x1 - x0) * i / (n - 1)
            p = to_blender(wx - W.DEPOT[0], W.plate_height(wx, wz),
                           wz - W.DEPOT[1])
            verts.append(p)
    faces = []
    for j in range(n - 1):
        for i in range(n - 1):
            a = j * n + i
            faces.append((a, a + n, a + n + 1, a + 1))
    mesh = bpy.data.meshes.new("Plate")
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.validate(verbose=False)
    mesh.update()
    obj = bpy.data.objects.new("Plate", mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(materials["Level_Snow"])


def add_transport(materials) -> int:
    """Все куски линии на своих местах в осях депо."""
    count = 0
    for ln in W.TRANSPORT:
        for (i, j) in W.line_cells(ln["pts"]):
            if ln["kind"] == "rail":
                pieces = [T.build_rail(materials, i, j),
                          T.build_ties(materials, i, j),
                          T.build_signs(materials, i, j)]
            else:
                pieces = [T.build_road(materials, i, j, ln["k"])]
            (cx, cz) = W.cell_center(i, j)
            pos = to_blender(cx - W.DEPOT[0], 0.0, cz - W.DEPOT[1])
            for obj in pieces:
                if obj is None:
                    continue
                obj.location = (pos.x, pos.y, 0.0)
                count += 1
    return count


def add_camera(name, eye, target, lens=24.0, ortho=None):
    data = bpy.data.cameras.new(name)
    data.lens = lens
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
        scene.world = bpy.data.worlds.new("TransportSky")
    scene.world.color = (0.52, 0.63, 0.72)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    scene.display.render_aa = "8"


def shoot(out: str, name: str) -> None:
    bpy.context.scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("КАДР %s" % name)


def s_of(key: str, at) -> float:
    """Путь вдоль трассы для точки, заданной координатами."""
    return W.project_to_line(at[0], at[1], T.line_of(key)["pts"])[1]


def pose(key: str, s: float, side: float = 0.0, height: float = 1.7):
    """Точка в осях Blender на трассе: путь `s`, вбок `side`, вверх `height`.

    Высота берётся от стола, а не от нуля: глаз идёт вдоль линии так же, как
    идёт по ней метла, иначе кадр покажет уклон, которого в геометрии нет.
    """
    ln = T.line_of(key)
    (x, z) = W.point_at_distance(ln["pts"], s)
    (nx, nz) = W.point_at_distance(ln["pts"], s + 1.0)
    (tx, tz) = (nx - x, nz - z)
    length = math.hypot(tx, tz) or 1.0
    px = x + tz / length * side
    pz = z - tx / length * side
    y = W.level_at(ln["table"], s) + height
    return to_blender(px - W.DEPOT[0], y, pz - W.DEPOT[1])


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.abspath(argv[argv.index("--out") + 1] if "--out" in argv
                          else os.path.join(HERE, "..", "..", "AssetSource",
                                            "Previews", "Transport"))

    reset()
    materials = level_lib.make_level_materials()
    os.makedirs(out, exist_ok=True)
    add_plate(materials)
    count = add_transport(materials)
    print("ОБЪЕКТОВ ЛИНИИ %d, клеток с линией %d"
          % (count, len(T.transport_cells())))

    # План всей карты: трасса, её повороты и ветки дорог. Плита при этом
    # выключена: на 6,6 км она выходит ровным серым полем, и линия на её фоне
    # читается хуже, чем линия на фоне неба. Здесь нужен чертёж сети, а не
    # пейзаж — пейзаж есть в трёх других кадрах.
    setup_render(square=True)
    plate = bpy.data.objects.get("Plate")
    if plate is not None:
        plate.hide_render = True
    # Фон на этом кадре — тёмный, а не небесный: линия на 6,6 км занимает
    # несколько пикселей, и на светлом поле она исчезает. Это чертёж сети,
    # ему нужен контраст, а не погода.
    sky = bpy.context.scene.world.color
    bpy.context.scene.world.color = (0.04, 0.05, 0.07)
    add_camera("map", to_blender(0.0, 6000.0, 0.0), (0.0, 0.0, 0.0),
               ortho=7000.0)
    shoot(out, "map")
    bpy.context.scene.world.color = sky
    if plate is not None:
        plate.hide_render = False

    # Стык двух чанков: глаз на ребре решётки, взгляд вдоль линии. Кадр стоит
    # чуть в стороне от оси, чтобы было видно и постель, и линию шпал.
    setup_render()
    (ca, cb) = JOINT_CELLS
    edge_z = (W.cell_center(*ca)[1] + W.cell_center(*cb)[1]) * 0.5
    s = s_of("rail", (W.cell_center(*ca)[0], edge_z))
    add_camera("joint", pose("rail", s - 70.0, side=6.0, height=2.4),
               pose("rail", s + 120.0, side=0.0, height=1.0), 35.0)
    shoot(out, "joint")

    # Угол трассы у станции: метла на повороте не должна вспухать над столом.
    # Снимается сверху ортографически: с земли угол уходит в две линии на
    # горизонте, и ни ширину постели, ни стык колец не разобрать — первый
    # вариант кадра именно так и провалился.
    s_kink = s_of("rail", KINK)
    top = pose("rail", s_kink, side=0.0, height=140.0)
    aim = pose("rail", s_kink, side=0.0, height=0.0)
    add_camera("kink", top, aim, ortho=260.0)
    shoot(out, "kink")

    # Вид вдоль оси от южного края клетки депо: путь прорезан или положен.
    # Глаз в стороне от оси и чуть выше головки рельса: с самой оси путь
    # уходит вниз кадра, и остаётся только линия знаков на горизонте.
    s_depot = s_of("rail", (W.DEPOT[0] - 8.0, W.DEPOT[1] - 260.0))
    add_camera("depot", pose("rail", s_depot, side=-9.0, height=3.0),
               pose("rail", s_depot + 240.0, side=-2.0, height=1.2), 40.0)
    shoot(out, "depot")

    print("PREVIEWS 4")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
