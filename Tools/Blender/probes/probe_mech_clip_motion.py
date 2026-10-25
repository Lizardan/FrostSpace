"""Движение костей и меша в клипе: кость уезжает или меш отстаёт от кости.

    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_clip_motion.py -- ME_FrogWalker.fbx walk_loop

Печатает по кадрам: голову кости, хвост её родителя и центр габарита группы
вершин этой кости. Если кость и группа рядом — скин цел, а «летит» сам клип.
Если группа далеко от кости — виноват bind.
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

import robot_lib  # noqa: E402


def group_center(obj, group_name):
    group = obj.vertex_groups.get(group_name)
    if group is None:
        return None
    depsgraph = bpy.context.evaluated_depsgraph_get()
    deformed = obj.evaluated_get(depsgraph).to_mesh()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    count = 0
    try:
        for vertex in deformed.vertices:
            for element in vertex.groups:
                if element.group == group.index and element.weight > 0.5:
                    co = vertex.co
                    lo = Vector((min(lo.x, co.x), min(lo.y, co.y), min(lo.z, co.z)))
                    hi = Vector((max(hi.x, co.x), max(hi.y, co.y), max(hi.z, co.z)))
                    count += 1
                    break
    finally:
        obj.evaluated_get(depsgraph).to_mesh_clear()
    if not count:
        return None
    return obj.matrix_world @ ((lo + hi) * 0.5)


def main() -> int:
    robot_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]
    clip = argv[1] if len(argv) > 1 else "walk_loop"

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")

    action = next((a for a in bpy.data.actions if clip in a.name), None)
    if action is None:
        print("нет клипа", clip, [a.name for a in bpy.data.actions])
        return 1

    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    arm.animation_data.action = action
    slots = getattr(action, "slots", None)
    if slots:
        try:
            arm.animation_data.action_slot = slots[0]
        except Exception:  # noqa: BLE001
            pass

    print("ACTION %s range=%s" % (action.name, tuple(action.frame_range)))
    start, end = (int(round(v)) for v in action.frame_range)
    print("%5s %-26s %-26s %-26s %s" % ("frame", "Foot.L bone", "LowerLeg.L tail",
                                        "Foot.L mesh", "delta"))
    for frame in range(start, end + 1, max(1, (end - start) // 8)):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        foot = arm.matrix_world @ arm.pose.bones["Foot.L"].head
        shin = arm.matrix_world @ arm.pose.bones["LowerLeg.L"].tail
        mesh = group_center(obj, "Foot.L")
        delta = (mesh - foot).length if mesh is not None else float("nan")
        print("%5d %-26s %-26s %-26s %.3f" % (
            frame,
            "(%.2f %.2f %.2f)" % (foot.x, foot.y, foot.z),
            "(%.2f %.2f %.2f)" % (shin.x, shin.y, shin.z),
            "(%.2f %.2f %.2f)" % (mesh.x, mesh.y, mesh.z) if mesh else "-",
            delta))

    # размах кости и меша за клип
    foot_positions, mesh_positions = [], []
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        foot_positions.append(arm.matrix_world @ arm.pose.bones["Foot.L"].head)
        center = group_center(obj, "Foot.L")
        if center is not None:
            mesh_positions.append(center)

    def spread(points):
        lo = Vector((min(p.x for p in points), min(p.y for p in points),
                     min(p.z for p in points)))
        hi = Vector((max(p.x for p in points), max(p.y for p in points),
                     max(p.z for p in points)))
        return hi - lo

    print("\nSPREAD bone  = %s" % tuple(round(v, 3) for v in spread(foot_positions)))
    print("SPREAD mesh  = %s" % tuple(round(v, 3) for v in spread(mesh_positions)))
    arm.animation_data.action = None
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
