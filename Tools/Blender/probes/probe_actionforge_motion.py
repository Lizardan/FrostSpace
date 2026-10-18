"""Проба движения корня в клипах ActionForge.

Печатает, на какой кости лежит перенос, где начинается/заканчивается клип и
как ведут себя стопы. Нужно, чтобы корректно сделать in-place версию и
посчитать скорость без скольжения.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_actionforge_motion.py -- \
        AssetSource/ActionForge/walk_loop.glb AssetSource/ActionForge/idle_loop.glb \
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


def fcurves(action):
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                yield from bag.fcurves
    if not getattr(action, "layers", []):
        yield from getattr(action, "fcurves", [])


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        return 1
    print("Blender", bpy.app.version_string)

    for path in args:
        for obj in list(bpy.data.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions):
            for item in list(block):
                block.remove(item)

        bpy.context.scene.render.fps = 30
        bpy.ops.import_scene.gltf(filepath=path)
        name = os.path.basename(path)
        print("\n===== %s =====" % name)

        arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
        if arm is None:
            print("нет арматуры")
            continue
        action = arm.animation_data.action if arm.animation_data else None
        print("action:", action.name if action else None,
              "range:", tuple(action.frame_range) if action else None)
        print("slot:", getattr(getattr(arm.animation_data, "action_slot", None),
                               "name_display", None) if arm.animation_data else None)
        for fc in fcurves(action) if action else []:
            if '"root"' in fc.data_path or '"pelvis"' in fc.data_path:
                if fc.data_path.endswith("location"):
                    vals = [kp.co[1] for kp in fc.keyframe_points]
                    print("  %-60s idx=%d keys=%d min=%.4f max=%.4f"
                          % (fc.data_path, fc.array_index, len(vals),
                             min(vals), max(vals)))
        print("  action slots:", [getattr(s, "name_display", str(s))
                                  for s in getattr(action, "slots", [])] if action else None)

        # Мировое положение root/pelvis по кадрам
        scene = bpy.context.scene
        for frame in (int(action.frame_range[0]), int(action.frame_range[1])):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            for bone_name in ("root", "pelvis"):
                bone = arm.pose.bones.get(bone_name)
                if bone:
                    pos = arm.matrix_world @ bone.head
                    print("  f=%-3d %-7s world=(%.4f %.4f %.4f)"
                          % (frame, bone_name, *pos))

        # Стопы по всем кадрам: минимум Z и диапазон Y (для контакта)
        scene.frame_set(int(action.frame_range[0]))
        for side in ("l", "r"):
            foot = arm.pose.bones.get("foot_" + side)
            ball = arm.pose.bones.get("ball_" + side)
            if not foot:
                continue
            ys, zs = [], []
            for frame in range(int(action.frame_range[0]), int(action.frame_range[1]) + 1):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                p = arm.matrix_world @ foot.head
                b = arm.matrix_world @ ball.head if ball else p
                ys.append((frame, p.y, b.y))
                zs.append((frame, min(p.z, b.z)))
            print("  %s foot Y %.4f..%.4f  minZ=%.4f at f=%d"
                  % (side, min(v[1] for v in ys), max(v[1] for v in ys),
                     min(v for _, v in zs), min(zs, key=lambda t: t[1])[0]))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
