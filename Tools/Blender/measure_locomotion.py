"""Меряет горизонтальную скорость стопы в контакте для walk/sprint.

Зачем: в клипах ActionForge корневое смещение обнулено. Чтобы в Unity стопа
не скользила, скорость движения персонажа должна совпадать со скоростью, с
которой стопа уезжает назад в клипе. Этот скрипт считает её по позе, а не по
памяти о метаданных.

Запуск:
    blender --background --factory-startup --python Tools/Blender/measure_locomotion.py -- <fbx>
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


def set_action(arm, action):
    arm.animation_data_create()
    arm.animation_data.action = action
    slot = getattr(arm.animation_data, "action_slot", None)
    if slot is None and getattr(action, "slots", None):
        try:
            arm.animation_data.action_slot = action.slots[0]
        except Exception:
            pass


def measure(arm, action, bone="ball_l"):
    start, end = (int(round(v)) for v in action.frame_range)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = start, end
    set_action(arm, action)
    samples = []
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        depsgraph.update()
        pose_bone = arm.pose.bones.get(bone)
        if pose_bone is None:
            raise RuntimeError("нет кости %s" % bone)
        point = arm.matrix_world @ pose_bone.head
        samples.append((frame, point.copy()))

    ground = min(p.z for _, p in samples)
    contact = [(f, p) for f, p in samples if p.z <= ground + 0.04]
    speeds = []
    for (f0, p0), (f1, p1) in zip(contact, contact[1:]):
        if f1 != f0 + 1:
            continue
        speeds.append((p1.y - p0.y) * scene.render.fps / (f1 - f0))
    print("%-22s frames=%d..%d  ground=%.4f  contact=%d  speedY=%s"
          % (action.name, start, end, ground, len(contact),
             ("%.3f" % (sum(speeds) / len(speeds))) if speeds else "—"))
    for frame, point in samples:
        if frame % 5 == 0 or frame == end:
            print("  f%02d y=%.4f z=%.4f" % (frame, point.y, point.z))
    return speeds


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    path = argv[0]
    if not os.path.exists(path):
        print("НЕТ ФАЙЛА", path)
        return 2

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        raise RuntimeError("FBX без арматуры")
    bpy.context.scene.render.fps = 30

    actions = sorted(bpy.data.actions, key=lambda a: a.name)
    for action in actions:
        if "walk" in action.name or "sprint" in action.name:
            measure(arm, action)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
