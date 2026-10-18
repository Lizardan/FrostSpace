"""Рендерит превью уровня FrostSpace: двор, дом, интерьер, сосны, пропы.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_level_previews.py
    ... -- --out AssetSource/Previews/Level --views yard approach cabin_south

Сцена собирается теми же билдерами, что и FBX (`level_parts`), раскладка —
из `level_layout` (координаты Unity, `to_blender` переводит в оси Blender).
Workbench выбран намеренно: быстрый headless-рендер faceted-силуэта и
материал-цветов без GPU-контекста.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import level_layout  # noqa: E402
import level_lib  # noqa: E402
import level_parts  # noqa: E402


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights):
        for item in list(block):
            block.remove(item)


# ------------------------------------------------------------------ сцена


def _clone(scene, src, name, x, z, rot_z=0.0, scale=1.0, lift=0.0):
    """Копия объекта раскладки: (x, z) — координаты Unity, lift — высота."""
    cp = src.copy()
    cp.data = src.data
    cp.name = name
    cp.location = Vector((x, -z, lift))
    cp.rotation_euler = (0.0, 0.0, math.radians(rot_z))
    cp.scale = (scale, scale, scale)
    scene.collection.objects.link(cp)
    return cp


def build_scene() -> dict:
    scene = bpy.context.scene
    mats = level_lib.make_level_materials()

    drifts = [(x, -z, rx, rz, dz)
              for (x, z, rx, rz, dz) in level_layout.DRIFTS]
    terrain = level_parts.build_terrain(mats, drifts)
    terrain.name = "PreviewTerrain"

    # дом
    hx, _, hz = level_layout.HOUSE
    cabin = level_parts.build_cabin(mats)
    for obj in cabin:
        obj.location = Vector((hx, -hz, 0.0))

    # сосны: три варианта, клоны по раскладке
    pines = [level_parts.build_pine(mats, seed, name="Pine_Src_%d" % i)
             for i, seed in enumerate((0.4, 2.1, 3.8))]
    for i, (x, z, s) in enumerate(level_layout.TREES):
        _clone(scene, pines[i % 3], "Tree_%02d" % i, x, z,
               rot_z=(i * 47.3) % 360.0, scale=s)
    for src in pines:
        bpy.data.objects.remove(src, do_unlink=True)

    # бочки, ящики, камни
    barrel = level_parts.build_barrel(mats)
    for i, (x, z, rot, lift) in enumerate(level_layout.BARRELS):
        _clone(scene, barrel, "Barrel_%02d" % i, x, z, rot_z=rot, lift=lift)
    bpy.data.objects.remove(barrel, do_unlink=True)

    crate = level_parts.build_crate(mats)
    for i, (x, z, rot, lift) in enumerate(level_layout.CRATES):
        _clone(scene, crate, "Crate_%02d" % i, x, z, rot_z=rot, lift=lift)
    bpy.data.objects.remove(crate, do_unlink=True)

    rocks = [level_parts.build_rock(mats, seed, name="Rock_Src_%d" % i)
             for i, seed in enumerate((0.4, 2.7))]
    for i, (x, z, rot) in enumerate(level_layout.ROCKS):
        _clone(scene, rocks[i % 2], "Rock_%02d" % i, x, z, rot_z=rot)
    for src in rocks:
        bpy.data.objects.remove(src, do_unlink=True)

    # забор: столбы и жерди с проёмом
    post = level_parts.build_fence_post(mats)
    for i, x in enumerate(level_layout.FENCE_POSTS):
        _clone(scene, post, "FencePost_%02d" % i, x, level_layout.FENCE_Z)
    bpy.data.objects.remove(post, do_unlink=True)

    rail = level_parts.build_fence_rail(mats)
    idx = 0
    for (a, b) in level_layout.FENCE_SPANS:
        count = max(1, int(round((b - a) / 2.30)))
        step = (b - a) / count
        for k in range(count):
            cx = a + step * (k + 0.5)
            for y in level_layout.FENCE_RAIL_Y:
                _clone(scene, rail, "FenceRail_%02d" % idx, cx,
                       level_layout.FENCE_Z, lift=y)
                idx += 1
    bpy.data.objects.remove(rail, do_unlink=True)

    # поленница
    pile = level_parts.build_log_pile(mats)
    px, pz, prot = level_layout.LOG_PILE
    _clone(scene, pile, "LogPile", px, pz, rot_z=prot)
    bpy.data.objects.remove(pile, do_unlink=True)

    # интерьер: ставится внутри дома на пол (0.25)
    floor = 0.25
    stove = level_parts.build_stove(mats)
    _clone(scene, stove, "Stove", 14.0, 9.0, lift=floor)
    bpy.data.objects.remove(stove, do_unlink=True)

    bundle = level_parts.build_log_bundle(mats)
    _clone(scene, bundle, "LogBundle", 13.15, 9.45, rot_z=90.0, lift=floor)
    bpy.data.objects.remove(bundle, do_unlink=True)

    bed = level_parts.build_bed(mats)
    _clone(scene, bed, "Bed", 9.75, 8.35, rot_z=90.0, lift=floor)
    bpy.data.objects.remove(bed, do_unlink=True)

    bench = level_parts.build_workbench(mats)
    _clone(scene, bench, "Workbench", 12.0, 10.2, lift=floor)
    bpy.data.objects.remove(bench, do_unlink=True)

    stool = level_parts.build_stool(mats)
    _clone(scene, stool, "Stool_0", 10.85, 9.55, rot_z=25.0, lift=floor)
    _clone(scene, stool, "Stool_1", 13.35, 8.55, rot_z=200.0, lift=floor)
    _clone(scene, stool, "Stool_2", 11.15, 6.05, rot_z=110.0, lift=floor)
    bpy.data.objects.remove(stool, do_unlink=True)

    shelf = level_parts.build_shelf(mats)
    _clone(scene, shelf, "Shelf", 12.35, 10.60, lift=floor - 0.0)
    bpy.data.objects.remove(shelf, do_unlink=True)

    lamp = level_parts.build_lamp(mats)
    _clone(scene, lamp, "Lamp", 12.10, 9.40, lift=3.50)
    bpy.data.objects.remove(lamp, do_unlink=True)

    return {"terrain": terrain, "cabin": cabin}


# ------------------------------------------------------------------ камера


def add_camera(scene, name, loc, target, lens=50.0, ortho=None):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector(loc)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    if ortho is not None:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho
    return cam


VIEWS = {
    # имя: (позиция, цель, фокус, орто-масштаб)
    "yard": ((-4.0, 14.0, 12.0), (10.0, -4.0, 0.4), 42.0, None),
    "top": ((10.0, -3.0, 60.0), (10.0, -1.0, 0.0), 50.0, 76.0),
    "top_house": ((12.0, -8.0, 60.0), (12.0, -8.0, 0.0), 50.0, 34.0),
    "approach": ((0.0, 8.0, 1.75), (12.0, -8.0, 1.5), 50.0, None),
    "yard_low": ((4.2, 6.0, 1.7), (12.0, -8.0, 1.6), 50.0, None),
    "cabin_south": ((10.0, 7.0, 3.4), (12.2, -8.0, 2.2), 45.0, None),
    "cabin_west": ((-2.0, -10.0, 4.2), (12.0, -8.0, 2.4), 50.0, None),
    "interior": ((10.3, 1.5, 7.2), (12.3, -8.6, 0.8), 40.0, None),
    "trees": ((2.0, 24.0, 2.4), (-5.0, 2.0, 3.2), 46.0, None),
    "props": ((-2.0, -4.0, 2.6), (7.0, -5.0, 0.8), 45.0, None),
}


def setup_render(engine: str) -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.62, 0.72, 0.82)

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


def render_to(path: str) -> None:
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Level")
    engine = argv[argv.index("--engine") + 1] if "--engine" in argv else "workbench"
    names = [a for a in argv if a in VIEWS] or list(VIEWS)
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    build_scene()
    setup_render(engine)

    roof_hidden = {"Cabin_Roof", "Cabin_RoofSnow"}
    for view in names:
        loc, target, lens, ortho = VIEWS[view]
        for obj in bpy.context.scene.objects:
            if obj.type == "MESH":
                obj.hide_render = obj.name in roof_hidden and view == "interior"
        cam = add_camera(bpy.context.scene, "Cam_%s" % view, loc, target,
                         lens=lens, ortho=ortho)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "level_%s.png" % view))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
