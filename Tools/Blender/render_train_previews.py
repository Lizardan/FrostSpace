"""Рендерит превью вагона и груза: изнутри, от двери, нос и крышу.

Запуск (штатно — из `build_all.py`, per = file):

    blender --background --factory-startup --python Tools/Blender/render_train_previews.py -- \
        "Assets/_Project/Content/Models/Train/TrainCar.fbx" --out AssetSource/Previews/Train

Вагон один на обе сцены, поэтому снимается и изнутри (окна, потолок, мебель,
проём), и снаружи (силуэт, нос, крыша). Для пропов — четыре общих вида, как у
транспорта. Workbench выбран по той же причине: быстрый headless-рендер
faceted-силуэта и материал-цветов без GPU-контекста.
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights):
        for item in list(block):
            block.remove(item)


def add_ground(z: float = 0.0) -> None:
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-20, -20, z), (20, -20, z), (20, 20, z), (-20, 20, z)],
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


def model_bounds() -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or obj.name == "PreviewGround":
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo.x, world.x), min(lo.y, world.y),
                         min(lo.z, world.z)))
            hi = Vector((max(hi.x, world.x), max(hi.y, world.y),
                         max(hi.z, world.z)))
    return lo, hi


def add_camera(name: str, location: Vector, target: Vector, lens: float = 60.0):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = location
    cam.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    return cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 700
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


def render_to(path: str) -> None:
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def interior_views(out: str, base: str) -> None:
    views = {
        # Взгляд от двери в глубину вагона.
        "inside_forward": (Vector((0.0, -10.2, 1.65)),
                           Vector((0.0, 8.0, 1.45)), 26.0),
        # Взгляд от верстака на дверь.
        "inside_door": (Vector((0.0, 8.6, 1.7)),
                        Vector((0.0, -12.0, 1.25)), 26.0),
        # Снаружи: читаются проём, створка, рамка и торец целиком.
        "door_outside": (Vector((0.0, -20.5, 2.9)),
                         Vector((0.0, -12.0, 1.5)), 34.0),
        # Боковой грузовой проём изнутри: створки снаружи, проём открыт.
        "inside_side_door": (Vector((1.2, -4.5, 1.7)),
                             Vector((-4.0, -1.0, 1.3)), 26.0),
        # От торцевой двери в кабину: проверяет, что нос полый и стыкуется.
        "inside_to_nose": (Vector((0.0, 7.5, 1.7)),
                           Vector((0.0, -15.9, 1.55)), 30.0),
        # Изнутри кабины: от угла на стекло и место водителя. Широкий угол:
        # кабина 4,2 м, иначе пульт занимает весь кадр.
        "inside_cabin": (Vector((1.45, -12.15, 1.95)),
                         Vector((-0.55, -15.35, 1.25)), 18.0),
        # Кабина изнутри сбоку: читаются боковые окна и их угол.
        "cabin_side": (Vector((2.30, -12.35, 1.90)),
                       Vector((-0.8, -14.9, 1.45)), 20.0),
        # Пульт от двери: экраны, кнопки, штурвал и рычаги.
        "cabin_dash": (Vector((0.15, -13.25, 1.70)),
                       Vector((0.0, -15.55, 1.20)), 24.0),
        # Пульт сверху: компоновка и свободное кресло.
        "cabin_console": (Vector((0.9, -13.60, 2.30)),
                          Vector((0.0, -15.10, 1.05)), 28.0),
    }
    for name, (location, target, lens) in views.items():
        cam = add_camera("Cam_" + name, location, target, lens=lens)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "%s_%s.png" % (base, name)))


def exterior_views(out: str, base: str) -> None:
    # Низ вагона — на уровне -1.0 (колёса на рельсе): иначе земля режет тележки.
    add_ground(-1.02)
    views = {
        # Три четверти спереди: нос ближе к камере, силуэт читается.
        "three_quarter": (Vector((16.0, -25.5, 6.5)),
                          Vector((0.0, -3.0, 1.2)), 50.0),
        # Борт со створками: видно, что дверь накладная и раздвижная.
        "side_door": (Vector((-16.0, -1.0, 3.0)),
                      Vector((-4.0, -1.0, 1.1)), 58.0),
        # Нос: заострённая кабина, стекло, фары и отвал.
        "front": (Vector((6.0, -21.0, 2.6)),
                  Vector((0.0, -16.1, 1.4)), 55.0),
        # Корма и тележки.
        "rear": (Vector((-8.0, 20.0, 3.4)),
                 Vector((0.0, 9.0, 0.4)), 52.0),
        # Нос крупно: силуэт кабины и стык со стеной вагона.
        "nose_detail": (Vector((6.5, -20.5, 3.8)),
                        Vector((0.0, -15.8, 1.9)), 55.0),
        # Крыша: снег, вентиляция, выхлоп, антенна, поручни.
        "roof_detail": (Vector((11.0, -9.0, 9.5)),
                        Vector((0.0, -2.0, 3.4)), 55.0),
    }
    for name, (location, target, lens) in views.items():
        cam = add_camera("Cam_" + name, location, target, lens=lens)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "%s_%s.png" % (base, name)))


def prop_views(out: str, base: str) -> None:
    add_ground()
    lo, hi = model_bounds()
    center = (lo + hi) * 0.5
    size = hi - lo
    print("BOUNDS", tuple(round(v, 3) for v in size))

    distance = max(size.x, size.y, size.z) * 1.55 + 0.6
    views = {
        "side": (Vector((distance, 0.0, center.z + size.z * 0.35)), center, 52.0),
        "three_quarter": (Vector((distance * 0.72, -distance * 0.78,
                                  center.z + size.z * 0.55)), center, 52.0),
        "front": (Vector((0.0, -distance * 1.15, center.z + size.z * 0.30)),
                  center, 52.0),
        "top": (Vector((center.x, center.y, center.z + distance * 1.25)),
                center, 60.0),
    }
    for name, (location, target, lens) in views.items():
        cam = add_camera("Cam_" + name, location, target, lens=lens)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "%s_%s.png" % (base, name)))


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "..", "AssetSource",
        "Previews", "Train")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    setup_render()
    base = os.path.splitext(os.path.basename(fbx))[0]

    if base.startswith("TrainCar"):
        interior_views(out, base)
        exterior_views(out, base)
    else:
        prop_views(out, base)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
