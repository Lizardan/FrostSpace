"""Превью наследия: читается ли масштаб и human-реперы.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_heritage_previews.py
    ... -- --out AssetSource/Previews/Heritage --views foot deck

Сцена собирается теми же билдерами, что и FBX (`heritage_parts`), поэтому
превью показывает именно тот меш, что уйдёт в Unity, а не его рисунок.
Workbench выбран намеренно: нужен фасетный силуэт и цвета материалов, без GPU.

Что здесь можно решить глазами и нельзя решить числами: не «сколько треугольников»,
а «верит ли глаз, что по этой плите ходят роботы» — поэтому камеры стоят там, где
встанет игрок: у двери, на площадке и на середине подъёма. Настоящий туман и небо
проверяются скриншотом в Play Mode (`UNITY_MCP.md`), здесь их нет.
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

import heritage_parts  # noqa: E402
import level_lib  # noqa: E402

#: Виды по дизайнам. Камеры стоят там, где встанет игрок: у подошвы, на середине
#: подъёма и на уровне кронштейна — потому что вопрос не «сколько треугольников»,
#: а «верит ли глаз, что это построено и по нему ходят».
VIEWS = {
    "slide": {
        "overall": ((-168.0, -52.0, 44.0), (0.0, 4.0, 26.0), 42.0, None),
        "side": ((14.0, -150.0, 22.0), (0.0, -20.0, 14.0), 45.0, None),
        "foot": ((6.6, -49.0, 1.8), (6.0, -40.0, 4.2), 32.0, None),
        "climb": ((-2.0, -6.0, 16.8), (0.0, 26.0, 43.0), 35.0, None),
        "deck": ((-4.0, 16.4, 45.0), (2.0, 22.0, 43.4), 40.0, None),
        "top": ((0.0, 0.0, 150.0), (0.0, 0.0, 0.0), 50.0, 130.0),
    },
    "pylon": {
        "overall": ((-52.0, 40.0, 20.0), (0.0, 0.0, 16.0), 45.0, None),
        "foot": ((0.0, -15.0, 1.7), (0.0, -3.0, 10.0), 38.0, None),
        "ladder": ((0.0, -10.5, 7.0), (0.0, -3.8, 8.6), 35.0, None),
        "arm": ((16.0, -10.0, 27.0), (2.5, 0.0, 30.5), 40.0, None),
    },
    # Ниже — шесть руин второго прогона. Камеры заданы по осям самих билдеров
    # (Blender: X вдоль тела, Z вверх), и каждый набор отвечает на свой вопрос:
    # `overall` — верит ли глаз масштабу, `foot`/`wagon`/`door` — виден ли
    # human-репер, остальные — читается ли история поломки.
    "crane": {
        "overall": ((-58.0, -46.0, 14.0), (2.0, 0.0, 20.0), 40.0, None),
        "foot": ((10.0, -22.0, 1.8), (0.0, -4.0, 8.0), 35.0, None),
        "cabin": ((14.0, -18.0, 34.0), (2.0, 0.0, 34.5), 40.0, None),
        "boom": ((34.0, 26.0, 30.0), (16.0, 0.0, 36.0), 45.0, None),
    },
    "span": {
        "overall": ((-56.0, -50.0, 16.0), (0.0, 0.0, 16.0), 40.0, None),
        "pier": ((36.0, -18.0, 8.0), (24.0, 0.0, 12.0), 38.0, None),
        "break": ((-38.0, -10.0, 10.0), (-25.0, 0.0, 19.0), 35.0, None),
        "catwalk": ((10.0, -22.0, 10.0), (10.0, -6.4, 8.6), 32.0, None),
    },
    "hangar": {
        "overall": ((-40.0, -70.0, 20.0), (2.0, 0.0, 12.0), 42.0, None),
        "ridge": ((6.0, -30.0, 20.0), (0.0, 0.0, 21.0), 35.0, None),
        "door": ((20.0, -8.0, 1.8), (16.7, -2.0, 3.0), 32.0, None),
        "inside": ((10.0, 0.0, 3.0), (0.0, 6.0, 12.0), 55.0, None),
    },
    "tanks": {
        "overall": ((-30.0, -56.0, 16.0), (0.0, 0.0, 9.0), 42.0, None),
        "tall": ((6.0, -22.0, 6.0), (0.0, 0.0, 12.0), 38.0, None),
        "side": ((20.0, 22.0, 5.0), (15.0, 6.0, 4.0), 35.0, None),
        "valve": ((-2.0, -12.0, 3.2), (0.0, -3.0, 3.0), 30.0, None),
    },
    "bridge": {
        "overall": ((-40.0, -80.0, 24.0), (14.0, 0.0, 24.0), 40.0, None),
        "deck": ((30.0, -26.0, 40.0), (13.0, 0.0, 41.0), 38.0, None),
        "pier": ((14.0, -22.0, 6.0), (0.0, 0.0, 20.0), 42.0, None),
        "gap": ((44.0, -30.0, 12.0), (36.0, 2.0, 6.0), 36.0, None),
    },
    "yard": {
        "overall": ((-46.0, -70.0, 26.0), (0.0, 0.0, 28.0), 40.0, None),
        "wagon": ((-20.0, -14.0, 2.2), (-4.8, 0.0, 3.0), 35.0, None),
        "tower": ((11.0, -26.0, 8.0), (11.0, 0.0, 30.0), 45.0, None),
        "beam": ((2.0, -20.0, 44.0), (4.0, 0.0, 48.0), 38.0, None),
    },
}

BUILDERS = {
    "slide": heritage_parts.build_slide,
    "pylon": heritage_parts.build_pylon,
    "crane": heritage_parts.build_crane,
    "span": heritage_parts.build_span,
    "hangar": heritage_parts.build_hangar,
    "tanks": heritage_parts.build_tanks,
    "bridge": heritage_parts.build_bridge,
    "yard": heritage_parts.build_yard,
}


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def add_camera(name, loc, target, lens, ortho):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(loc)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    if ortho is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho
    bpy.context.scene.camera = cam


def add_stage(materials) -> None:
    """Снежная равнина под руиной и свет, близкий к дневному холодному."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, -0.6), (520.0, 520.0, 1.0), "Level_SnowDeep", bevel=0.0)
    b.build("PreviewGround")

    sun_data = bpy.data.lights.new("sun", "SUN")
    sun_data.energy = 3.2
    sun_data.angle = math.radians(2.0)
    sun = bpy.data.objects.new("sun", sun_data)
    bpy.context.scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(52.0), math.radians(-14.0),
                          math.radians(28.0))


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("HeritageSky")
    # Небо на превью — холодная дымка, а не игровой туман: нужно прочитать
    # силуэт тела, а не цвет неба (это проверяется в Play Mode).
    scene.world.color = (0.44, 0.53, 0.62)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    scene.display.render_aa = "16"


def _clear_meshes() -> None:
    for obj in list(bpy.data.objects):
        if obj.type in {"MESH", "LIGHT"}:
            bpy.data.objects.remove(obj, do_unlink=True)


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Heritage")
    # `abspath` обязателен: `build_all.py` передаёт путь относительно корня
    # проекта, а Blender считает относительные пути от своего рабочего каталога
    # и на Windows уходит в корень диска — так превью молча писались в C:\.
    out = os.path.abspath(out)
    wanted = [a for a in argv if a in BUILDERS] or list(BUILDERS)

    reset()
    materials = level_lib.make_level_materials()
    setup_render()
    os.makedirs(out, exist_ok=True)

    count = 0
    for design in wanted:
        _clear_meshes()
        add_stage(materials)
        BUILDERS[design](materials)
        for (name, (loc, target, lens, ortho)) in VIEWS[design].items():
            add_camera(name, loc, target, lens, ortho)
            path = os.path.join(out, "%s_%s.png" % (design, name))
            bpy.context.scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            print("RENDER", os.path.basename(path))
            count += 1
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    print("PREVIEWS %d" % count)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
