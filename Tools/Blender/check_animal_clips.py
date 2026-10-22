"""Проверяет готовый FBX робо-животного против `animal_manifest.json`.

Зачем: у животных нет кости `ball_l`, поэтому `measure_locomotion.py` к ним не
применим, и до этой проверки готовые файлы проверялись только глазами по превью.
Скрипт смотрит сам артефакт (FBX из `Assets/`), а не память о сборке.

Жёсткие инварианты (провал = код 1):

  1. в файле есть арматура и меш;
  2. есть оба клипа (`idle_loop`, `move_loop`), и их длина совпадает с манифестом;
  3. у всех вершин есть вес — иначе животное в Unity встанет в rest-позу;
  4. скорость стопы в контакте совпадает с `clip_speed_source` — это и есть
     скорость, с которой животное обязано ехать в Unity, иначе стопа скользит.

Мягкие (WARN, код возврата не меняют): шов петли и габарит меша — это качество
анимации, а не сломанная конфигурация.

Запуск:
    blender --background --factory-startup --python Tools/Blender/check_animal_clips.py -- <fbx> [<fbx> ...]
"""

from __future__ import annotations

import json
import math
import os
import statistics
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import animal_lib  # noqa: E402

MANIFEST = os.path.join(HERE, "animal_manifest.json")

#: Кадры клипов: `robot_lib.export_fbx` ставит сцене 30 fps перед экспортом,
#: поэтому take в FBX и замер скорости живут в одних и тех же кадрах.
FPS = 30.0

#: Допуск на скорость стопы: замер идёт по сэмплам, а не по ключам.
SPEED_TOLERANCE = 0.25

#: Клипы, у которых замер скорости не имеет смысла.
NOMINAL_MODES = ("hop", "fly", "swim")

#: Шов длиннее этого — предупреждение о видимом рывке на стыке петли.
SEAM_WARN = 0.30


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def load_manifest() -> dict:
    if not os.path.exists(MANIFEST):
        raise RuntimeError("нет манифеста %s — сначала build_animals.py" % MANIFEST)
    with open(MANIFEST, "r", encoding="utf-8") as handle:
        entries = json.load(handle)
    return {entry["name"]: entry for entry in entries}


def find_action(token: str):
    return next((a for a in bpy.data.actions if token in a.name), None)


def activate(arm, action) -> None:
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    arm.animation_data.action = action
    for track in arm.animation_data.nla_tracks:
        track.mute = True


def pose_matrices(arm, action, frames) -> list:
    activate(arm, action)
    poses = []
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        poses.append({pb.name: pb.matrix.copy() for pb in arm.pose.bones})
    return poses


def seam_delta(arm, action, start: int, end: int) -> float:
    first, last = pose_matrices(arm, action, (start, end))
    worst = 0.0
    for name, matrix in first.items():
        other = last.get(name)
        if other is None:
            return 99.0
        for row in range(4):
            for col in range(4):
                worst = max(worst, abs(matrix[row][col] - other[row][col]))
    return worst


def speed(arm, action, feet, start: int, end: int, ground: float) -> tuple[float, int]:
    """Медианная скорость стопы в контакте, единицы файла в секунду.

    Тот же расчёт, что в `animal_lib.measure_speed`, поэтому результат обязан
    совпасть с записанным в манифест `clip_speed_source`.
    """
    activate(arm, action)
    positions = {name: [] for name in feet}
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in feet:
            bone = arm.pose.bones.get(name)
            if bone is None:
                continue
            positions[name].append(arm.matrix_world @ ((bone.head + bone.tail) * 0.5))

    samples = []
    for points in positions.values():
        if len(points) < 3:
            continue
        floor = min(p.z for p in points)
        for previous, current in zip(points, points[1:]):
            if previous.z - floor > ground or current.z - floor > ground:
                continue
            # Стойка: стопа едет назад — это +Y после нормализации осей.
            if current.y - previous.y <= 0.0:
                continue
            samples.append(math.hypot(current.x - previous.x,
                                      current.y - previous.y) * FPS)
    if not samples:
        return 0.0, 0
    return statistics.median(samples), len(samples)


def unweighted_vertices(meshes) -> int:
    loose = 0
    for obj in meshes:
        for vertex in obj.data.vertices:
            if not any(group.weight > 0.0 for group in vertex.groups):
                loose += 1
    return loose


def mesh_height(meshes) -> float:
    zs = [(obj.matrix_world @ vertex.co).z
          for obj in meshes for vertex in obj.data.vertices]
    if not zs:
        return 0.0
    return max(zs) - min(zs)


def check(path: str, manifest: dict) -> bool:
    base = os.path.splitext(os.path.basename(path))[0]
    entry = manifest.get(base)
    print("\n=== %s" % base)
    if entry is None:
        print("FAIL %s: нет записи %s в манифесте" % (base, base))
        return False

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.scene.render.fps = int(FPS)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if arm is None or not meshes:
        print("FAIL %s: в файле нет арматуры или меша" % base)
        return False

    failed = []

    for key in ("idle", "move"):
        action = find_action(key + "_loop")
        if action is None:
            failed.append("нет клипа %s_loop" % key)
            continue
        start, end = (int(round(v)) for v in action.frame_range)
        expected = entry["clips"].get(key, 0) + 1
        actual = end - start + 1
        ok = actual == expected
        print("%s %-10s кадров %d..%d (%d) манифест %d" %
              ("OK  " if ok else "FAIL", key + "_loop", start, end, actual, expected))
        if not ok:
            failed.append("%s_loop: %d кадров вместо %d" % (key, actual, expected))
        delta = seam_delta(arm, action, start, end)
        print("     шов петли %.5f%s" %
              (delta, "  WARN: виден рывок" if delta > SEAM_WARN else ""))

    loose = unweighted_vertices(meshes)
    print("%s веса: без веса %d из %d" %
          ("OK  " if loose == 0 else "FAIL", loose,
           sum(len(o.data.vertices) for o in meshes)))
    if loose:
        failed.append("вершин без веса: %d" % loose)

    mode = entry.get("mode", "walk")
    feet = animal_lib.roles(arm).get("feet", [])
    source_speed = float(entry.get("clip_speed_source", 0.0))
    action = find_action("move_loop")
    if mode in NOMINAL_MODES or source_speed < 0.1:
        print("OK   скорость: режим %s, замер не нужен" % mode)
    elif not feet:
        print("WARN скорость: кости стоп не найдены, замер пропущен")
    elif action is not None:
        # Порог контакта — как в сборке: 0.02 габарита источника.
        ground = max(0.03, 0.02 * float(entry.get("source_size", 5.0)))
        start, end = (int(round(v)) for v in action.frame_range)
        measured, count = speed(arm, action, feet, start, end, ground)
        if count == 0:
            failed.append("скорость: стопа ни разу не коснулась пола")
            print("FAIL скорость: контакт не найден")
        else:
            ratio = measured / source_speed if source_speed else 0.0
            ok = abs(ratio - 1.0) <= SPEED_TOLERANCE
            print("%s скорость: замер %.3f манифест %.3f (%.0f%%) сэмплов %d" %
                  ("OK  " if ok else "FAIL", measured, source_speed,
                   ratio * 100.0, count))
            if not ok:
                failed.append("скорость %.3f против %.3f в манифесте"
                              % (measured, source_speed))

    height = mesh_height(meshes)
    print("     габарит по Z %.3f (единицы источника)" % height)

    arm.animation_data.action = None
    if failed:
        print("FAIL %s: %s" % (base, "; ".join(failed)))
        return False
    print("PASS %s" % base)
    return True


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    missing = [path for path in argv if not os.path.exists(path)]
    if missing:
        print("НЕТ ФАЙЛА:", ", ".join(missing))
        return 2

    manifest = load_manifest()
    results = [(path, check(path, manifest)) for path in argv]
    bad = [path for path, ok in results if not ok]
    print("\nИТОГ: проверено %d, провал %d" % (len(results), len(bad)))
    for path in bad:
        print("  FAIL %s" % os.path.basename(path))
    if bad:
        return 1
    print("ПРОВЕРКА ЖИВОТНЫХ PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
