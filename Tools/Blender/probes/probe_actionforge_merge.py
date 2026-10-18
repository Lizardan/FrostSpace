"""Проба объединения клипов ActionForge и экспорта трёх NLA-клипов в FBX.

Проверяет три вещи, без которых нельзя строить пайплайн роботов:

1. Клипы из разных GLB можно назначить одной арматуре (имена костей совпадают).
2. Смещение корня по Y можно обнулить, получив in-place клип.
3. FBX-экспорт с NLA даёт три отдельных клипа с ожидаемыми именами.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_actionforge_merge.py
"""

from __future__ import annotations

import os
import sys
import tempfile
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
Samples = os.path.join(HERE, "..", "..", "..", "AssetSource", "ActionForge")
SAMPLES = os.path.abspath(Samples)


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions):
        for item in list(block):
            block.remove(item)


def fcurves(action):
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                yield from bag.fcurves
    if not getattr(action, "layers", []):
        yield from getattr(action, "fcurves", [])


def zero_root_forward(action) -> int:
    changed = 0
    for fc in fcurves(action):
        if fc.data_path == 'pose.bones["root"].location' and fc.array_index == 1:
            for kp in fc.keyframe_points:
                kp.co[1] = 0.0
                kp.handle_left[1] = 0.0
                kp.handle_right[1] = 0.0
            fc.update()
            changed += 1
    return changed


def main() -> int:
    configure_stdout()
    print("Blender", bpy.app.version_string)
    clear_scene()
    bpy.context.scene.render.fps = 30

    base = None
    actions = {}
    for clip in ("walk_loop", "idle_loop", "sprint_loop"):
        bpy.ops.import_scene.gltf(filepath=os.path.join(SAMPLES, clip + ".glb"))
        arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
        arm = arms[-1]
        action = arm.animation_data.action if arm.animation_data else None
        print("imported", clip, "armature", arm.name, "action", action.name if action else None)
        if base is None:
            base = arm
        else:
            # Оставляем только арматуру; меши и лишние арматуры удаляем.
            for obj in list(bpy.context.scene.objects):
                if obj.type == "MESH":
                    bpy.data.objects.remove(obj, do_unlink=True)
            if arm is not base:
                bpy.data.objects.remove(arm, do_unlink=True)
        if action:
            actions[clip] = action

    # В базовой сцене остаётся base + её Icosphere
    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    base.animation_data_create()
    print("base bones", len(base.data.bones), "actions", list(actions))

    # Назначаем idle и смотрим, меняется ли поза
    scene = bpy.context.scene
    base.animation_data.action = actions["idle_loop"]
    for frame in (0, 37):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        p = base.pose.bones["pelvis"].head
        print("idle assigned f=%d pelvis=(%.4f %.4f %.4f)" % (frame, *p))

    # In-place и NLA
    base.animation_data.action = None
    for track in list(base.animation_data.nla_tracks):
        base.animation_data.nla_tracks.remove(track)
    for clip in ("walk_loop", "idle_loop", "sprint_loop"):
        action = actions[clip]
        print("zero root", clip, zero_root_forward(action))
        track = base.animation_data.nla_tracks.new()
        track.name = clip
        strip = track.strips.new(clip, int(action.frame_range[0]), action)
        strip.name = clip
        track.mute = False

    for track in base.animation_data.nla_tracks:
        print("NLA", track.name, [(s.name, s.frame_start, s.frame_end, s.action.name) for s in track.strips])

    bpy.ops.object.select_all(action="DESELECT")
    base.select_set(True)
    bpy.context.view_layer.objects.active = base
    fbx = os.path.join(tempfile.gettempdir(), "actionforge_merge_probe.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx,
        use_selection=True,
        object_types={"ARMATURE"},
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
    )
    print("exported", fbx, os.path.getsize(fbx))

    clear_scene()
    bpy.ops.import_scene.fbx(filepath=fbx)
    for action in bpy.data.actions:
        print("FBX ACTION", action.name, tuple(action.frame_range), len(list(fcurves(action))))
    for obj in bpy.context.scene.objects:
        if obj.animation_data:
            print("FBX NLA", obj.name, [(t.name, [(s.name, s.frame_start, s.frame_end) for s in t.strips])
                                          for t in obj.animation_data.nla_tracks])
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
