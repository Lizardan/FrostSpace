"""Проверяет, рабочий ли клип Walk пака на его собственном меше.

Вопрос: кость Foot у пака — цель IK. Если в клипе она уезжает на метры, а
цепочка ног при этом не тянется за ней, значит FBX потерял IK и клип надо брать
из GLTF (там позы запечены). Скрипт смотрит именно это: расстояние от конца
голени до головы Foot и картинку кадра.

    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_ik.py -- FrogWalker
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import mech_lib  # noqa: E402

OUT = os.path.join(HERE, "..", "..", "AssetSource", "Previews", "Mechs", "_source")


def setup_camera(mid: float, height: float):
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 55.0
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector((height * 1.6, -height * 1.6, mid + height * 0.25))
    cam.rotation_euler = (Vector((0, 0, mid)) - cam.location).to_track_quat(
        "-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    scene = bpy.context.scene
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.7, 0.72, 0.75)
    scene.display.shading.show_shadows = True


def main() -> int:
    unit = (sys.argv[sys.argv.index("--") + 1:] or ["FrogWalker"])[0]
    cfg = mech_lib.MECHS[unit]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=mech_lib.extract(unit))
    bpy.context.scene.render.fps = mech_lib.FPS

    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    print("BONES", len(arm.data.bones))
    for bone in arm.data.bones:
        print("  %-14s parent=%-14s" % (
            bone.name, bone.parent.name if bone.parent else "-"))
    for pb in arm.pose.bones:
        if pb.constraints:
            print("  CONSTRAINT", pb.name,
                  [c.type for c in pb.constraints])

    action = mech_lib._action_by_role(cfg["walk"])
    print("ACTION", action.name, tuple(action.frame_range))

    mid = 1.0
    height = 2.7
    setup_camera(mid, height)
    labels = []
    arm.animation_data_create()
    arm.animation_data.action = action
    for frame in (1, 7, 14, 20):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        foot = arm.pose.bones["Foot.L"]
        shin = arm.pose.bones["LowerLeg.L"]
        gap = (foot.head - shin.tail).length
        print("FRAME %2d foot=(%.3f %.3f %.3f) shin_tail=(%.3f %.3f %.3f) gap=%.3f" % (
            frame, foot.head.x, foot.head.y, foot.head.z,
            shin.tail.x, shin.tail.y, shin.tail.z, gap))
        path = os.path.join(OUT, "Mech_FinnTheFrog_walk_probe_%02d.png" % frame)
        bpy.context.scene.render.filepath = os.path.abspath(path)
        bpy.ops.render.render(write_still=True)
        labels.append(path)
    print("RENDERED", labels)
    print("MESH", mesh.name, len(mesh.data.vertices))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
