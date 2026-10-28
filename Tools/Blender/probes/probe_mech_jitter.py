"""Ищет разрывы в клипах ходуна: скачки поворота и позиции между соседними кадрами.

Зачем: «куски модели дёргаются» — это не вкус, а разрыв в кривой. Глазом его
видно как подрагивание накладки на суставе, а число показывает, на каком кадре и
на какой кости он сидит.

Меряется дважды:
  1. **локальный поворот кости** — скачок угла между кадрами `f` и `f+1`
     сравнивается с медианным шагом этой же кости за цикл (выброс = разрыв);
  2. **мировая позиция меша** — реальный сдвиг геометрии, чтобы не путать
     «кость крутится быстро» с «геометрия телепортируется».

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_mech_jitter.py -- \
        "Assets/_Project/Content/Models/Mechs/ME_PandaWalker.fbx"
"""

from __future__ import annotations

import math
import os
import statistics
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, os.path.dirname(HERE))

#: Во сколько раз шаг кадра должен превысить медиану, чтобы считаться разрывом.
SPIKE = 4.0


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX")
        return 2

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=argv[0])
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    fps = float(bpy.context.scene.render.fps or 30.0)
    arm.data.pose_position = "POSE"
    arm.animation_data_create()

    for action in bpy.data.actions:
        if not any(key in action.name for key in ("idle", "walk", "run")):
            continue
        arm.animation_data.action = action
        slots = list(getattr(action, "slots", []) or [])
        if slots and getattr(arm.animation_data, "action_slot", None) is None:
            try:
                arm.animation_data.action_slot = slots[0]
            except (AttributeError, TypeError):
                pass
        start, end = (int(round(v)) for v in action.frame_range)
        print("\n=== %s кадры %d..%d" % (action.name, start, end))

        # --- 1. Локальные повороты костей.
        bones = [b.name for b in arm.pose.bones]
        angles = {name: [] for name in bones}
        locations = {name: [] for name in bones}
        previous = {}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for name in bones:
                pb = arm.pose.bones[name]
                quat = pb.rotation_quaternion.copy()
                loc = pb.location.copy()
                if name in previous:
                    old_quat, old_loc = previous[name]
                    delta = quat.rotation_difference(old_quat).angle
                    angles[name].append(math.degrees(delta))
                    locations[name].append((loc - old_loc).length)
                previous[name] = (quat, loc)

        worst = []
        for name in bones:
            if not angles[name]:
                continue
            median = statistics.median(angles[name])
            peak = max(angles[name])
            index = angles[name].index(peak)
            # Порог: либо явный выброс против медианы, либо абсолютный скачок.
            if peak > max(SPIKE * median, 6.0):
                worst.append((peak / max(median, 1e-3), name, peak, median,
                              start + index, start + index + 1))
            move = max(locations[name]) if locations[name] else 0.0
            if move > 0.06:
                worst.append((move / 0.01, name + " (location)", move, 0.0,
                              start + locations[name].index(move),
                              start + locations[name].index(move) + 1))
        worst.sort(reverse=True)
        if not worst:
            print("  скачков нет (порог %.1fx медианы)" % SPIKE)
        for ratio, name, peak, median, a, b in worst[:12]:
            print("  СКАЧОК %-22s %.1f° между кадрами %d→%d (медиана %.1f°, x%.1f)"
                  % (name, peak, a, b, median, ratio))

        # --- 2. Мировая геометрия: сдвиг вершин между кадрами.
        depsgraph = bpy.context.evaluated_depsgraph_get()
        previous_points = None
        jumps = []
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            evaluated = mesh.evaluated_get(depsgraph)
            data = evaluated.to_mesh()
            matrix = evaluated.matrix_world
            points = [matrix @ v.co for v in data.vertices]
            evaluated.to_mesh_clear()
            if previous_points is not None:
                shift = max((a - b).length for a, b in zip(points, previous_points))
                jumps.append((shift, frame - 1, frame))
            previous_points = points
        jumps.sort(reverse=True)
        median = statistics.median([j[0] for j in jumps])
        print("  геометрия: медиана %.4f м/кадр, максимум %.4f м/кадр (кадры %d→%d)"
              % (median, jumps[0][0], jumps[0][1], jumps[0][2]))
        for shift, a, b in jumps[:5]:
            print("     %.4f м  кадры %d→%d  (x%.1f медианы)"
                  % (shift, a, b, shift / max(median, 1e-6)))
        _ = fps
    return 0


if __name__ == "__main__":
    sys.exit(main())
