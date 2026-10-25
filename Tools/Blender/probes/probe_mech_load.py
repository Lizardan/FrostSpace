"""Пошаговая трассировка `mech_lib.load`: на каком шаге едет rest-поза.

    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_load.py -- FrogWalker

Печатает головы/хвосты ног после импорта, после масштаба и после сужения
стойки. Шаг, после которого бедро перестаёт быть вертикальным или стопа
съезжает с голени, — виновник.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, ".."))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import mech_lib  # noqa: E402
import robot_lib  # noqa: E402

LEG = ("UpperLeg.L", "LowerLeg.L", "Foot.L")


def show(arm, label):
    print("\n--- %s" % label)
    for name in LEG:
        bone = arm.data.bones.get(name)
        if bone is None:
            print("  %-12s НЕТ" % name)
            continue
        head = arm.matrix_world @ bone.head_local
        tail = arm.matrix_world @ bone.tail_local
        print("  %-12s head=(%7.3f %7.3f %7.3f) tail=(%7.3f %7.3f %7.3f)" % (
            name, head.x, head.y, head.z, tail.x, tail.y, tail.z))
    # поза, если она есть
    for name in LEG:
        pose_bone = arm.pose.bones.get(name)
        if pose_bone is None:
            continue
        basis = pose_bone.matrix_basis
        print("    pose %-12s t=(%6.3f %6.3f %6.3f) rot=%s" % (
            name, basis.translation.x, basis.translation.y, basis.translation.z,
            tuple(round(v, 2) for v in pose_bone.rotation_quaternion)))


def main() -> int:
    robot_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    unit = argv[0] if argv else "FrogWalker"
    cfg = mech_lib.MECHS[unit]

    robot_lib.clear_scene()
    bpy.ops.import_scene.fbx(filepath=mech_lib.extract(unit))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    show(arm, "после импорта")

    lo, hi = mech_lib._mesh_bbox(meshes)
    scale = cfg["target"] / max((hi - lo).z, 1e-6)
    print("scale=%.4f" % scale)
    for obj in meshes:
        bpy.data.objects.remove(obj, do_unlink=True)

    foot_points = [arm.matrix_world @ arm.data.bones[n].head_local
                   for n in ("Foot.L", "Foot.R") if n in arm.data.bones]
    anchor = sum(foot_points, Vector((0, 0, 0))) / len(foot_points)
    arm.scale = arm.scale * scale
    arm.location.x -= anchor.x * scale
    arm.location.y -= anchor.y * scale
    arm.location.z -= lo.z * scale
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()
    show(arm, "после transform_apply")

    mech_lib.narrow_stance(arm, cfg["stance"])
    show(arm, "после narrow_stance")

    # как выглядит покой, если обнулить позу
    from mathutils import Matrix
    for bone in arm.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    show(arm, "после сброса позы в ноль")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
