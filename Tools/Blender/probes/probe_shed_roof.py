"""Разбор крыши коробки: сечение оборванного листа против верха стен.

Прогон:
    blender --background --factory-startup --python Tools/Blender/probes/probe_shed_roof.py

Кадры и числа отвечают на один вопрос: где лист, сорванный с кровли, ещё видно
над верхом стен, а где он уходит внутрь массы коробки и потому читается
обрывком, а не обрывом. Отметки даются над рельефом под каждой точкой, потому
что двор наклонён вдоль пути и абсолютная высота ничего не значит.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import depot_parts as DP  # noqa: E402
import level_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(TOOLS, "..", "..", "Temp", "shed"))
os.makedirs(OUT, exist_ok=True)

#: Локальные оси двора -> оси Blender для меша до экспорта: X поперёк,
#: Y — путь, Z — абсолютная отметка.


def eye(lx: float, h: float, lz: float) -> Vector:
    """Локальные оси двора -> оси Blender для НЕэкспортированного меша.

    `export_static` зеркалит геометрию по X, и потому камеры
    `render_cells_previews` идут через `to_blender`. Здесь меша из FBX нет:
    билдер кладет вершины как `(lx, -lz, отметка)`, и знак X в кадре обязан
    совпадать с билдером, иначе кадр смотрит в пустоту.
    """
    return Vector((lx, -lz, h))


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def shoot(name: str) -> None:
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("КАДР %s" % name)


def add_camera(name, from_pt, at_pt, lens=28.0, ortho=None):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    data.clip_start = 0.1
    data.clip_end = 4000.0
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(from_pt)
    direction = Vector(at_pt) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    if ortho is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho
    bpy.context.scene.camera = cam


def main() -> int:
    level_lib.configure_stdout()
    reset()
    materials = level_lib.make_level_materials()

    b = level_lib.LevelBuilder(materials)
    DP._shed(b)
    obj = b.build("DepotShed_probe", recalc=False)

    roof_ix = set(k for k, s in enumerate(obj.material_slots)
                  if s.material and s.material.name.startswith("Level_Roof"))
    print("СЛОТ КРОВЛИ %s ВЕРШИН %d ТРИС %d" %
          (sorted(roof_ix), len(obj.data.vertices), len(obj.data.polygons)))
    print("РАЗМЕР X %.2f Y %.2f Z %.2f..%.2f" % (
        max(v.co.x for v in obj.data.vertices) -
        min(v.co.x for v in obj.data.vertices),
        max(v.co.y for v in obj.data.vertices) -
        min(v.co.y for v in obj.data.vertices),
        min(v.co.z for v in obj.data.vertices),
        max(v.co.z for v in obj.data.vertices)))

    wall_top = max(py for (_px, py) in DP.SHED_WALL)
    print("ВЕРХ СТЕН (профиль) %.2f  КАРНИЗ %.2f..%.2f  КОНЁК %.2f" %
          (wall_top, DP.SHED_ROOF[1][1], DP.SHED_ROOF[2][1],
           DP.SHED_ROOF[3][1]))

    (sx0, sx1, sz0, sz1) = DP.local(DP.W.DEPOT_SHED)
    axis = (sx0 + sx1) * 0.5
    g = DP.yard(axis, DP.SHED_ROOF_TO)

    vix = set()
    for p in obj.data.polygons:
        if p.material_index in roof_ix:
            vix.update(p.vertices)
    rows = []
    for i in sorted(vix):
        v = obj.data.vertices[i]
        (lx, lz) = (v.co.x, -v.co.y)
        if not (DP.SHED_ROOF_TO - 3.0 <= lz <= DP.SHED_ROOF_TO + 12.0):
            continue
        above = v.co.z - DP.yard(lx, lz)
        half = abs(lx - axis)
        rows.append((round(lz, 2), round(lx, 2), round(above, 2)))
    for r in sorted(set(rows)):
        half = abs(r[1] - axis)
        buried = r[2] < wall_top and half < 14.0
        print("ЛИСТ z=%6.2f x=%7.2f над_полом=%5.2f полуборт=%5.2f %s" %
              (r[0], r[1], r[2], half, "ВНУТРИ МАССЫ" if buried else ""))

    bpy.context.scene.render.engine = "BLENDER_WORKBENCH"
    shading = bpy.context.scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    bpy.context.scene.display.render_aa = "8"
    if bpy.context.scene.world is None:
        bpy.context.scene.world = bpy.data.worlds.new("ProbeSky")
    bpy.context.scene.world.color = (0.52, 0.63, 0.72)

    bpy.context.scene.render.resolution_x = 1280
    bpy.context.scene.render.resolution_y = 720

    # Фасад с восточного борта (сторона двора): ортография в разрез, чтобы
    # видеть линию ската, обрыв и верх стен одной плоскостью сравнения.
    add_camera("elevation", eye(axis + 120.0, g + 9.0, 0.0),
               eye(axis, g + 7.0, 0.0), ortho=90.0)
    shoot("elevation")

    # Косой вид с северо-востока: дыра, фермы и то, как висит лист.
    add_camera("oblique", eye(axis + 58.0, g + 24.0, sz1 + 38.0),
               eye(axis - 2.0, g + 7.0, DP.SHED_ROOF_TO - 2.0), 30.0)
    shoot("oblique")

    # Глаз на перроне у проёма: правая часть кровли в кадре прибытия.
    add_camera("platform", eye(-6.0, DP.head(-6.0, -22.0) + 2.4, -22.0),
               eye(axis + 4.0, DP.yard(axis, -6.0) + 8.0, -2.0), 24.0)
    shoot("platform")

    # Вид сверху в пролом: обрешётка, висячие листы и то, что дыра действительно
    # дыра, а не второй ярус крыши.
    add_camera("hole", eye(axis + 26.0, g + 34.0, sz1 + 6.0),
               eye(axis, g + 1.0, 8.0), 30.0)
    shoot("hole")

    print("PROBE SHED OK")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
