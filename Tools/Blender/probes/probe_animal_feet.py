"""Покадровый разбор стойки стопы в готовом FBX: высота, наклон, вес контакта.

Нужен, когда `check_animal_clips.py` ругается на наклон подошвы: видно, в каких
кадрах нога стоит, а в каких переносится, и какой вес им даёт общее с сборкой
определение стойки (`animal_lib.stance_weights`). Формат строки — `высота/наклон/вес`,
высота отсчитана от медианы сустава (уровня стойки).

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_feet.py -- Wolf
"""

from __future__ import annotations

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(TOOLS, "..", ".."))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import bpy  # noqa: E402

import animal_lib  # noqa: E402


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = argv[0] if argv else "Wolf"
    model = os.path.join(ROOT, "Assets", "_Project", "Content", "Models",
                         "RobotAnimals", species, "RA_%s.fbx" % species)
    with open(os.path.join(TOOLS, "animal_manifest.json"), "r",
              encoding="utf-8") as handle:
        entry = next(e for e in json.load(handle) if e["species"] == species)
    body = float(entry["source_size"])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=model)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    action = next(a for a in bpy.data.actions if "move_loop" in a.name)
    arm.animation_data_create()
    arm.animation_data.action = action
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    start, end = (int(round(v)) for v in action.frame_range)
    print("%s body %.3f frames %d..%d" % (species, body, start, end))

    feet = animal_lib.roles(arm).get("feet", [])
    samples = animal_lib.foot_samples(arm, feet, start, end)
    straight = [name for name in feet
                if samples[name] and animal_lib.lie_flat_along_floor(samples[name])]
    print("прямые стопы", straight)
    weights = animal_lib.stance_weights(samples, straight, body)
    for name in feet:
        heights = [head.z for head, _tail in samples[name]]
        level = sorted(heights)[len(heights) // 2]
        row = []
        for (head, tail), height, weight in zip(samples[name], heights,
                                                weights.get(name, [])):
            direction = tail - head
            tilt = math.degrees(math.atan2(abs(direction.z),
                                           math.hypot(direction.x, direction.y)))
            row.append("%.3f/%.0f/%.1f" % (height - level, tilt, weight))
        print(name, " ".join(row))
    return 0


if __name__ == "__main__":
    sys.exit(main())
