"""Проба импорта ActionForge GLB в Blender: скелет, клипы, стопы.

Зачем: перед постройкой роботов нужно знать фактические имена костей, масштаб,
длительность клипов и положение стоп в кадры контакта. Скрипт ничего не меняет,
только импортирует файлы и печатает факты.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_actionforge.py -- \
        AssetSource/ActionForge/rig-human.glb \
        AssetSource/ActionForge/walk_loop.glb \
        AssetSource/ActionForge/idle_loop.glb \
        AssetSource/ActionForge/sprint_loop.glb
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


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials):
        for item in list(block):
            block.remove(item)


def layer_fcurves(action) -> int:
    total = 0
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                total += len(bag.fcurves)
    return total


def world_bbox(objs):
    low = high = None
    for obj in objs:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low = point.copy() if low is None else Vector((
                min(low.x, point.x), min(low.y, point.y), min(low.z, point.z)))
            high = point.copy() if high is None else Vector((
                max(high.x, point.x), max(high.y, point.y), max(high.z, point.z)))
    return low, high


def print_scene(label: str) -> None:
    print("\n===== %s =====" % label)
    print("fps", bpy.context.scene.render.fps)
    low, high = world_bbox(list(bpy.context.scene.objects))
    if low is not None:
        size = high - low
        print("WORLD_BBOX min=(%.4f %.4f %.4f) max=(%.4f %.4f %.4f) size=(%.4f %.4f %.4f)"
              % (*low, *high, *size))
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            print("MESH %-24s verts=%-5d polys=%-4d dims=(%.4f %.4f %.4f) loc=(%.3f %.3f %.3f) scale=(%.4f %.4f %.4f)"
                  % (obj.name, len(obj.data.vertices), len(obj.data.polygons),
                     *obj.dimensions, *obj.location, *obj.scale))
            print("  materials:", [m.name if m else None for m in obj.data.materials])
        elif obj.type == "ARMATURE":
            print("ARMATURE %s bones=%d scale=(%.4f %.4f %.4f) loc=(%.3f %.3f %.3f)"
                  % (obj.name, len(obj.data.bones), *obj.scale, *obj.location))
            print("  bones:", ",".join(b.name for b in obj.data.bones))
            ad = obj.animation_data
            print("  action:", ad.action.name if ad and ad.action else None,
                  "nla_tracks:", len(ad.nla_tracks) if ad else 0)
            for track in (ad.nla_tracks if ad else []):
                print("   nla", track.name, "strips", [(s.name, s.frame_start, s.frame_end) for s in track.strips])

    for action in bpy.data.actions:
        print("ACTION %-24s range=%s fcurves=%d slots=%s"
              % (action.name, tuple(action.frame_range), layer_fcurves(action),
                 [getattr(s, "name_display", str(s)) for s in getattr(action, "slots", [])]))


def foot_positions(label: str) -> None:
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        return
    print("FOOT_REST %s:" % label)
    for bone_name in ("pelvis", "thigh_l", "calf_l", "foot_l", "ball_l",
                      "thigh_r", "calf_r", "foot_r", "ball_r"):
        bone = arm.data.bones.get(bone_name)
        if bone:
            head = arm.matrix_world @ bone.head_local
            tail = arm.matrix_world @ bone.tail_local
            print("  %-10s head=(%.4f %.4f %.4f) tail=(%.4f %.4f %.4f)"
                  % (bone_name, *head, *tail))


def sample_feet(label: str, frames_by_side: dict[str, list[tuple[int, int]]]) -> None:
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        return
    scene = bpy.context.scene
    print("FOOT_SAMPLES %s:" % label)
    for side, ranges in frames_by_side.items():
        for start, end in ranges:
            for frame in (start, end):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                for bone_name in ("foot_" + side, "ball_" + side):
                    bone = arm.pose.bones.get(bone_name)
                    if bone:
                        pos = arm.matrix_world @ bone.head
                        print("  %-7s f=%-3d %-7s (%.4f %.4f %.4f)"
                              % (side, frame, bone_name, *pos))


def probe(path: str) -> None:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=path)
    print_scene(os.path.basename(path))
    foot_positions(os.path.basename(path))


def main() -> int:
    configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("нет файлов для пробы")
        return 1

    contact = {
        "walk_loop.glb": {"l": [(39, 23)], "r": [(19, 3)]},
        "sprint_loop.glb": {"l": [(20, 4)], "r": [(11, 14)]},
    }

    for path in args:
        probe(path)
        name = os.path.basename(path)
        if name in contact:
            sample_feet(name, contact[name])
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
