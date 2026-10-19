"""Проба «пауз» в клипах движения: где в Move-анимации кости не двигаются.

Импортирует собранный FBX из Assets (или исходный из `AssetSource/_staging`)
и по кадрам считает максимальное смещение костей за кадр. Кадры с нулевым
смещением — простой анимации; в игре это читается как «два шага, потом ноги
стоят». Для ходьбы печатается профиль смещений и список провалов.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_cycles.py
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_cycles.py -- Wolf Cow
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_cycles.py -- --source
"""

from __future__ import annotations

import os
import statistics
import sys
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(BLENDER, "..", ".."))
if BLENDER not in sys.path:
    sys.path.insert(0, BLENDER)

import animal_lib  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "_Project", "Content", "Models",
                   "RobotAnimals")
STAGING = os.path.join(ROOT, "AssetSource", "_staging")
FPS = 24.0


def clear() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials):
        for item in list(block):
            block.remove(item)


def find_action(token: str, exact: str | None = None):
    for action in bpy.data.actions:
        base = action.name.rsplit("|", 1)[-1]
        if exact is not None and base == exact:
            return action
        if exact is None and token in base.lower():
            return action
    return None


def profile(arm, action) -> tuple[list[float], float, list[str]]:
    """Смещение костей за кадр, замыкание петли и участники движения."""
    for track in (arm.animation_data.nla_tracks
                  if arm.animation_data else []):
        track.mute = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    start, end = (int(round(v)) for v in action.frame_range)

    frames = list(range(start, end + 1))
    poses = []
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        poses.append({pb.name: arm.matrix_world @ ((pb.head + pb.tail) * 0.5)
                      for pb in arm.pose.bones})

    deltas, leaders = [], []
    for i in range(1, len(poses)):
        worst, who = 0.0, "-"
        for name, point in poses[i].items():
            shift = (point - poses[i - 1][name]).length
            if shift > worst:
                worst, who = shift, name
        deltas.append(worst)
        leaders.append(who)

    seam = max((poses[0][n] - poses[-1][n]).length for n in poses[0])
    return deltas, seam, leaders


def runs(deltas: list[float], threshold: float, minimum: int = 2):
    result, current = [], []
    for index, delta in enumerate(deltas, start=1):
        if delta <= threshold:
            current.append(index)
        else:
            if len(current) >= minimum:
                result.append((current[0], current[-1] + 1))
            current = []
    if len(current) >= minimum:
        result.append((current[0], current[-1] + 1))
    return result


def probe(name: str, path: str, tokens: dict) -> None:
    print("\n===== %s (%s)" % (name, os.path.basename(path)))
    clear()
    bpy.context.scene.render.fps = int(FPS)
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not arms:
        print("  нет арматуры")
        return
    arm = arms[0]
    print("  actions=%s" % sorted((a.name, tuple(round(v, 1) for v in
                                                  a.frame_range))
                                  for a in bpy.data.actions))
    for key in ("idle", "move", "bite"):
        exact = tokens.get(key) if key != "bite" else None
        action = find_action(key, exact)
        if action is None:
            continue
        deltas, seam, leaders = profile(arm, action)
        start, end = (int(round(v)) for v in action.frame_range)
        seconds = (end - start) / FPS
        median = statistics.median(deltas) if deltas else 0.0
        peaks = sorted(zip(deltas, leaders), reverse=True)[:3]
        print("  %s frames=%d..%d (%.2f s) median=%.1f mm max=%.1f mm "
              "seam=%.1f mm"
              % (action.name, start, end, seconds, median * 1000,
                 (max(deltas) if deltas else 0.0) * 1000, seam * 1000))
        print("    top= %s" % ", ".join("%s %.1f mm" % (who, d * 1000)
                                        for d, who in peaks))
        if key == "move":
            print("    profile mm:")
            print("     " + " ".join("%.1f" % (d * 1000) for d in deltas))
        threshold = max(1e-5, (max(deltas) if deltas else 0.0) * 1e-3)
        holes = runs(deltas, threshold)
        if holes:
            print("    HOLDS (стоит): %s" % ", ".join(
                "%d..%d (%.2f s)" % (a + start, b + start, (b - a) / FPS)
                for a, b in holes))
        else:
            print("    HOLDS: нет")


def main() -> int:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    source = "--source" in args
    wanted = [a for a in args if not a.startswith("--")]
    selected = {a.lower() for a in wanted}

    targets = []
    for species, cfg in animal_lib.SPECIES.items():
        if selected and species.lower() not in selected:
            continue
        if source:
            targets.append((species, animal_lib.extract(species),
                            {"idle": cfg["idle"], "move": cfg["move"]}))
        else:
            path = os.path.join(OUT, species, "RA_%s.fbx" % species)
            targets.append((species, path, {}))
    if not source and (not selected or "spider" in selected):
        targets.append(("Spider", os.path.join(OUT, "Spider", "RA_Spider.fbx"),
                        {}))

    for name, path, tokens in targets:
        try:
            probe(name, path, tokens)
        except Exception:  # noqa: BLE001
            print("FAILED", name)
            traceback.print_exc()
    return 0


if __name__ == "__main__":
    sys.exit(main())
