"""Проба ориентации: рендер модели строго спереди и строго сбоку.

    blender --background --factory-startup --python Tools/Blender/probes/probe_orientation.py -- \
        R8_Bulwark --out AssetSource/Previews/Robots/Orientation
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ROBOTS = os.path.join(ROOT, "Assets", "_Project", "Content", "Models", "Robots")


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи имя модели")
        return 2
    name = argv[0]
    out = os.path.join(ROOT, "AssetSource", "Previews", "Robots", "Orientation")
    if "--out" in argv:
        out = os.path.join(ROOT, argv[argv.index("--out") + 1])
    os.makedirs(out, exist_ok=True)

    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROBOTS, name + ".fbx"))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = 600
    scene.render.resolution_y = 700
    scene.render.image_settings.file_format = "PNG"

    for label, position in (("front", (0.0, -6.0, 1.05)),
                            ("side", (6.0, 0.0, 1.05)),
                            ("top", (0.0, -0.01, 6.0))):
        cam_data = bpy.data.cameras.new("Cam")
        cam_data.lens = 85
        cam = bpy.data.objects.new("Cam", cam_data)
        scene.collection.objects.link(cam)
        cam.location = Vector(position)
        cam.rotation_euler = (Vector((0, 0, 1.05)) - Vector(position)).to_track_quat(
            "-Z", "Y").to_euler()
        scene.camera = cam
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)
        print("RENDER", label)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
