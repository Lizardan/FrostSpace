"""Крупные планы головы животного с прицелом по кости головы.

В отличие от `probe_animal_geometry.py`, камера ставится от фактического
положения головы, а не от габарита всей модели: у длинных животных голова
уезжала из кадра.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_head.py -- <file.fbx> [--out DIR]

Рендерит 4 вида: front, left, right, top. Включает backface culling — дыры
и изнанка видны как пустоты, а не как «стекло».
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))


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


def add_ground(z: float = 0.0) -> None:
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-30, -30, z), (30, -30, z), (30, 30, z), (-30, 30, z)],
                     [], [(0, 1, 2, 3)])
    mesh.update()
    obj = bpy.data.objects.new("PreviewGround", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("PreviewSnow")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.72, 0.80, 0.88, 1)
    mat.diffuse_color = (0.72, 0.80, 0.88, 1)
    mesh.materials.append(mat)


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 800
    scene.render.resolution_y = 800
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
    shading.curvature_ridge_factor = 1.4
    shading.curvature_valley_factor = 1.1
    shading.show_specular_highlight = True
    shading.show_backface_culling = True
    scene.display.render_aa = "16"


def add_camera(location, target, lens=70):
    cam_data = bpy.data.cameras.new("ProbeCamera")
    cam_data.lens = lens
    cam = bpy.data.objects.new("ProbeCamera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat(
        "-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def head_frame(arm):
    """Точки головы из имён костей Quaternius."""
    points = {}
    for bone in arm.data.bones:
        points[bone.name] = (arm.matrix_world @ bone.head_local,
                             arm.matrix_world @ bone.tail_local)
    names = {b.name for b in arm.data.bones}
    if "Bone.003" in names:
        head = "Bone.003"
    elif "Head" in names:
        head = "Head"
    else:
        raise RuntimeError("не найдена кость головы: %s" % sorted(names))
    h0, h1 = points[head]
    return h0, h1, (h1 - h0).normalized(), (h1 - h0).length


def bounds(meshes):
    corners = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    return lo, hi


def render(path: str) -> None:
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("укажи FBX после --")
        return 2
    fbx = os.path.abspath(args[0])
    out = args[args.index("--out") + 1] if "--out" in args else os.path.join(
        ROOT, "AssetSource", "Previews", "Animals", "_head")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next((o for o in bpy.context.scene.objects
                if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if arm is None or not meshes:
        raise RuntimeError("FBX без арматуры или меша")
    # FBX из нашего экспортёра приходит Z-up (меш-потомок компенсирует Y-up
    # файл), поэтому ориентация не правится: раньше лишний поворот уводил
    # модель под пол и кадр выходил пустым.
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()

    h0, h1, hd, hl = head_frame(arm)
    center = (h0 + h1) * 0.5
    lo, _hi = bounds(meshes)
    print("HEAD h0=%s h1=%s dir=%s len=%.3f center=%s" % (
        tuple(round(v, 3) for v in h0), tuple(round(v, 3) for v in h1),
        tuple(round(v, 3) for v in hd), hl,
        tuple(round(v, 3) for v in center)))

    add_ground(lo.z)
    setup_render()
    base = os.path.splitext(os.path.basename(fbx))[0]
    distance = max(hl * 4.2, 1.4)
    right = hd.cross(Vector((0.0, 0.0, 1.0))).normalized()
    up = right.cross(hd).normalized()
    views = {
        "front": center + hd * distance + up * hl * 0.15,
        "back": center - hd * distance + up * hl * 0.15,
        "left": center + right * distance + up * hl * 0.10,
        "right": center - right * distance + up * hl * 0.10,
        "top": center + up * distance - hd * hl * 0.20,
    }
    for name, location in views.items():
        add_camera(location, center, lens=75)
        render(os.path.join(out, "%s_%s.png" % (base, name)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
