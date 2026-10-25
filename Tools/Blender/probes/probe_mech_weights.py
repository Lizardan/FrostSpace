"""Диагностика скина ходуна: какая группа вершин куда уезжает в клипе.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_weights.py -- <файл.fbx>

Печатает по каждой группе вершин: число вершин, rest-габарит и максимальное
смещение центра габарита при проигрывании клипов. Группа, чей центр уезжает
дальше всех от rest, — виновник «разлетающихся частей».
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


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def group_bounds(mesh, group_index):
    """Габарит вершин группы по координатам меша (rest)."""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    count = 0
    for vertex in mesh.vertices:
        for element in vertex.groups:
            if element.group == group_index and element.weight > 0.5:
                co = vertex.co
                lo = Vector((min(lo.x, co.x), min(lo.y, co.y), min(lo.z, co.z)))
                hi = Vector((max(hi.x, co.x), max(hi.y, co.y), max(hi.z, co.z)))
                count += 1
                break
    if count == 0:
        return None, 0
    return (lo + hi) * 0.5, count


def evaluated_bounds(obj, mesh, group_index):
    """Габарит группы по деформированному мешу (после модификатора)."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    deformed = evaluated.to_mesh()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    count = 0
    try:
        for vertex in deformed.vertices:
            for element in vertex.groups:
                if element.group == group_index and element.weight > 0.5:
                    co = vertex.co
                    lo = Vector((min(lo.x, co.x), min(lo.y, co.y), min(lo.z, co.z)))
                    hi = Vector((max(hi.x, co.x), max(hi.y, co.y), max(hi.z, co.z)))
                    count += 1
                    break
    finally:
        evaluated.to_mesh_clear()
    if count == 0:
        return None
    return (lo + hi) * 0.5


def main() -> int:
    robot_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    fbx = argv[0]

    reset()
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    mesh = obj.data

    print("MESH %s verts=%d groups=%d" % (obj.name, len(mesh.vertices),
                                          len(obj.vertex_groups)))
    print("ARM %s bones=%d" % (arm.name, len(arm.data.bones)))
    print("scale arm=%s obj=%s" % (tuple(round(v, 4) for v in arm.scale),
                                   tuple(round(v, 4) for v in obj.scale)))

    # rest-точки костей для сверки
    print("\nBONES")
    for bone in arm.data.bones:
        head = arm.matrix_world @ bone.head_local
        print("  %-16s head=(%6.3f %6.3f %6.3f)" % (bone.name, head.x, head.y, head.z))

    print("\nGROUPS (rest)")
    rest = {}
    for group in obj.vertex_groups:
        center, count = group_bounds(mesh, group.index)
        rest[group.name] = center
        if center is None:
            print("  %-16s verts=0  <-- ПУСТАЯ" % group.name)
            continue
        print("  %-16s verts=%-5d center=(%6.3f %6.3f %6.3f)" % (
            group.name, count, center.x, center.y, center.z))

    actions = [a for a in bpy.data.actions]
    print("\nACTIONS %s" % [a.name for a in actions])
    if not actions:
        return 0

    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    for action in actions:
        if "run" not in action.name.lower() and "walk" not in action.name.lower():
            continue
        arm.animation_data.action = action
        slots = getattr(action, "slots", None)
        if slots:
            try:
                arm.animation_data.action_slot = slots[0]
            except Exception:  # noqa: BLE001
                pass
        start, end = (int(round(v)) for v in action.frame_range)
        worst = {}
        for frame in range(start, end + 1, max(1, (end - start) // 6)):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for group in obj.vertex_groups:
                if rest.get(group.name) is None:
                    continue
                center = evaluated_bounds(obj, mesh, group.index)
                if center is None:
                    continue
                delta = (center - rest[group.name]).length
                if delta > worst.get(group.name, 0.0):
                    worst[group.name] = delta
        print("\nCLIP %s frames=%d..%d" % (action.name, start, end))
        for name, delta in sorted(worst.items(), key=lambda kv: -kv[1]):
            print("  %-16s maxDelta=%6.3f m" % (name, delta))
    arm.animation_data.action = None
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
