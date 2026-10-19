"""Крупные планы и отчёт по геометрии робо-животного.

Нужен, чтобы ловить то, что не видно на общем превью: дыры и стыки на шее,
просветы под бронёй, «мусорные» грани без материала.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_geometry.py -- \
        Assets/_Project/Content/Models/RobotAnimals/Wolf/RA_Wolf.fbx \
        --out AssetSource/Previews/Animals/_probe
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bmesh
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


def report(meshes) -> None:
    for obj in meshes:
        mesh = obj.data
        bm = bmesh.new()
        bm.from_mesh(mesh)
        boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        loose = sum(1 for e in bm.edges if not e.link_faces)
        bm.free()
        slots = [m.name if m else "<None>" for m in mesh.materials]
        print("MESH", obj.name, "verts=%d" % len(mesh.vertices),
              "polys=%d" % len(mesh.polygons),
              "boundary_edges=%d" % boundary, "loose_edges=%d" % loose)
        used = {}
        for poly in mesh.polygons:
            used[poly.material_index] = used.get(poly.material_index, 0) + 1
        for index in range(len(slots)):
            print("  slot %2d %-28s faces=%d" % (index, slots[index],
                                                 used.get(index, 0)))
        out_of_range = sum(n for i, n in used.items() if i >= len(slots))
        if out_of_range:
            print("  OUT_OF_RANGE_FACES", out_of_range)
        # Грани без материала рисуются серым — визуально «стекло».
        zero_area = 0
        for poly in mesh.polygons:
            if poly.area < 1e-9:
                zero_area += 1
        print("  zero_area_faces=%d" % zero_area)


def add_ground(z: float = 0.0) -> None:
    mesh = bpy.data.meshes.new("PreviewGround")
    mesh.from_pydata([(-20, -20, z), (20, -20, z), (20, 20, z), (-20, 20, z)],
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
    scene.display.render_aa = "16"


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
    report_only = "--report-only" in args
    out = args[args.index("--out") + 1] if "--out" in args else os.path.join(
        ROOT, "AssetSource", "Previews", "Animals", "_probe")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if arm is None or not meshes:
        raise RuntimeError("FBX без арматуры или меша")
    # FBX из нашего экспортёра приходит Z-up; поворот не нужен.
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    report(meshes)

    lo, hi = bounds(meshes)
    print("BBOX lo=%s hi=%s size=%s" % (tuple(round(v, 3) for v in lo),
                                        tuple(round(v, 3) for v in hi),
                                        tuple(round(v, 3) for v in (hi - lo))))
    if report_only:
        return 0

    center = (lo + hi) * 0.5
    head_z = hi.z - (hi.z - lo.z) * 0.10
    head_y = lo.y + (hi.y - lo.y) * 0.88

    add_ground(lo.z)
    setup_render()

    base = os.path.splitext(os.path.basename(fbx))[0]
    views = {
        "head_front": ((center.x + 0.75, head_y - 1.05, head_z + 0.42),
                       (center.x, head_y, head_z), 85),
        "head_side": ((center.x + 1.35, head_y - 0.25, head_z + 0.30),
                      (center.x, head_y, head_z), 85),
        "back_top": ((center.x + 0.85, center.y - 1.25, hi.z + 0.90),
                     (center.x, center.y, hi.z - 0.15), 70),
    }
    for name, (loc, target, lens) in views.items():
        add_camera(loc, target, lens)
        render(os.path.join(out, "%s_%s.png" % (base, name)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
