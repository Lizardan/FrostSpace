"""Проверяет готовый FBX ходуна против `mech_manifest.json`.

Зачем: ходун — это транспорт с кабиной, и цена ошибки выше, чем у декоративного
робота: игрок садится внутрь, стопа обязана не скользить, а модель не должна
разваливаться на шаге. Поэтому проверяется сам артефакт из `Assets/`, а не
память о сборке.

Жёсткие инварианты (провал = код 1):

  1. в файле есть арматура и меш;
  2. есть три клипа (`idle_loop`/`walk_loop`/`run_loop`), их длина совпадает
     с манифестом;
  3. у всех вершин есть вес — иначе часть корпуса встанет в rest-позу;
  4. меш стоит на полу (min Z ≈ 0), а в походке ступни не проваливаются;
  5. кабина пуста: ни одна вершина не заходит в `cockpit.cavity` — там сидит
     игрок (это же число читает префаб);
  6. ход корня за цикл ≈ 0: клипы in-place (ADR 0022/0023), актёра двигает
     `Mech`;
  7. скорость земли из клипа совпадает с `clip_speed` манифеста — по ней
     `Mech` подгоняет `Animator.speed`, иначе стопа скользит.

Запуск:
    blender --background --factory-startup --python Tools/Blender/check_mech_clips.py -- <fbx> [<fbx> ...]
"""

from __future__ import annotations

import json
import os
import statistics
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

MANIFEST = os.path.join(HERE, "mech_manifest.json")

#: Клипы и порядок проверки: idle неподвижен, walk/run меряются по стопе.
CLIP_KEYS = ("idle", "walk", "run")

#: Допуск на скорость стопы: замер идёт по сэмплам, а не по ключам.
SPEED_TOLERANCE = 0.25

#: Клипы in-place: горизонтальный ход корня за цикл, м.
IN_PLACE_TOLERANCE = 0.02

#: Пол: меш не выше и не ниже этого допуска в rest-позе, м.
FLOOR_TOLERANCE = 0.03

#: Провал геометрии в походке: ниже этого — брак, м. Носок длинного
#: «ботинка» на перекате касается пола — это нормально, утопание корпуса нет.
SINK_TOLERANCE = 0.12

#: Отрыв стопы от голени, м. Стопа в риге пака — отдельная кость без родителя
#: (IK-цель): на замахе она уходит вперёд до 0.5 м, и это её штатное поведение.
#: Брак — когда она уезжает на метры: так выглядел сломанный rest (ступня
#: отлетала от ноги на 2.4 м). В покое стопа стоит на голени, там порог жёсткий.
FOOT_GAP_MAX = 0.60
FOOT_GAP_IDLE = 0.05


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def load_manifest() -> dict:
    if not os.path.exists(MANIFEST):
        raise RuntimeError("нет манифеста %s — сначала build_mechs.py" % MANIFEST)
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


def world_vertices(obj) -> list:
    return [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]


def inside(point, box) -> bool:
    x0, x1, y0, y1, z0, z1 = box
    return (x0 <= point.x <= x1 and y0 <= point.y <= y1
            and z0 <= point.z <= z1)


def foot_gap(arm, action) -> float:
    """Максимальный отрыв стопы от голени за клип, м.

    Стопа в риге пака — кость **без родителя** (IK-цель), её положение задаётся
    ключами локации, а не цепочкой ног. Любое расхождение rest-позы и ключей
    локтей локации разводит стопу с голенью: ступня «отлетает» от ноги.
    """
    activate(arm, action)
    start, end = (int(round(v)) for v in action.frame_range)
    worst = 0.0
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for side in ("L", "R"):
            foot = arm.pose.bones.get("Foot." + side)
            shin = arm.pose.bones.get("LowerLeg." + side)
            if foot is None or shin is None:
                continue
            worst = max(worst, (foot.head - shin.tail).length)
    return worst


def speed(arm, feet, action, fps: float) -> tuple[float, int]:
    """Скорость земли из клипа: медиана перемещения стопы в стойке, м/с.

    Клип in-place, поэтому земля «едет» назад ровно со скоростью стопы на
    полу. Стойка — кадры, где стопа не выше зоны контакта; перенос стопы
    делится на длительность отрезка, а не на весь цикл.
    """
    activate(arm, action)
    start, end = (int(round(v)) for v in action.frame_range)
    positions = {name: [] for name in feet}
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in feet:
            bone = arm.pose.bones.get(name)
            if bone is None:
                continue
            positions[name].append(
                arm.matrix_world @ ((bone.head + bone.tail) * 0.5))

    samples = []
    for points in positions.values():
        if len(points) < 4:
            continue
        floor = min(p.z for p in points)
        span = max(p.z for p in points) - floor
        zone = max(0.02, min(0.08, span * 0.15))
        runs, current = [], []
        for index, point in enumerate(points):
            if point.z - floor <= zone:
                current.append(index)
            elif current:
                runs.append(current)
                current = []
        if current:
            runs.append(current)
        for run in runs:
            # У бега стойка короткая (бывает два кадра): меньше этого замер
            # состоит из шума.
            if len(run) < 2:
                continue
            travel = points[run[-1]].y - points[run[0]].y
            if travel <= 0.0:
                continue
            samples.append(travel / ((len(run) - 1) / fps))
    if not samples:
        return 0.0, 0
    return statistics.median(samples), len(samples)


def lowest_during(arm, meshes, action) -> float:
    """Самая низкая точка геометрии за клип — признак провала в пол.

    Меряется деформированный меш, а не кости: у ригов пака есть кости-цели IK
    (`PoleTarget`), они уходят глубоко под пол, и по костям проверка врала.
    """
    activate(arm, action)
    start, end = (int(round(v)) for v in action.frame_range)
    frames = sorted({start, (start + end) // 2, end,
                     start + (end - start) // 4, start + 3 * (end - start) // 4})
    lowest = 1e9
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        for obj in meshes:
            evaluated = obj.evaluated_get(depsgraph)
            data = evaluated.to_mesh()
            matrix = evaluated.matrix_world
            for vertex in data.vertices:
                lowest = min(lowest, (matrix @ vertex.co).z)
            evaluated.to_mesh_clear()
    return lowest


def check(path: str, manifest: dict) -> bool:
    base = os.path.splitext(os.path.basename(path))[0]
    entry = manifest.get(base)
    print("\n=== %s" % base)
    if entry is None:
        print("FAIL %s: нет записи в манифесте" % base)
        return False

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    fps = float(bpy.context.scene.render.fps or 30.0)
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if arm is None or not meshes:
        print("FAIL %s: в файле нет арматуры или меша" % base)
        return False
    failed = []

    # --- клипы ----------------------------------------------------------
    actions = {}
    for key in CLIP_KEYS:
        action = find_action(key + "_loop")
        if action is None:
            failed.append("нет клипа %s_loop" % key)
            continue
        actions[key] = action
        start, end = (int(round(v)) for v in action.frame_range)
        expected = entry["clips"].get(key, 0) + 1
        actual = end - start + 1
        ok = actual == expected
        print("%s %-10s кадров %d..%d (%d) манифест %d %s" % (
            "OK  " if ok else "FAIL", key + "_loop", start, end, actual,
            expected, "%.2f c" % (actual / fps)))
        if not ok:
            failed.append("%s_loop: %d кадров вместо %d" % (key, actual, expected))

    # --- веса -----------------------------------------------------------
    loose = sum(1 for obj in meshes for vertex in obj.data.vertices
                if not any(group.weight > 0.0 for group in vertex.groups))
    total = sum(len(obj.data.vertices) for obj in meshes)
    print("%s веса: без веса %d из %d" % ("OK  " if not loose else "FAIL",
                                          loose, total))
    if loose:
        failed.append("вершин без веса: %d" % loose)

    # --- пол в rest-позе -------------------------------------------------
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    rest_groups = []
    for obj in meshes:
        names = {group.index: group.name for group in obj.vertex_groups}
        for vertex in obj.data.vertices:
            group = max(vertex.groups, key=lambda g: g.weight,
                        default=None)
            rest_groups.append((obj.matrix_world @ vertex.co,
                                names.get(group.group, "-") if group else "-"))
    rest = [point for point, _ in rest_groups]
    low = min(point.z for point in rest)
    high = max(point.z for point in rest)
    floor_ok = abs(low) <= FLOOR_TOLERANCE
    print("%s пол: min Z %.3f (допуск ±%.3f), рост %.2f м" % (
        "OK  " if floor_ok else "FAIL", low, FLOOR_TOLERANCE, high - low))
    if not floor_ok:
        failed.append("меш не стоит на полу: min Z %.3f" % low)

    # --- кабина ---------------------------------------------------------
    cavity = entry.get("cockpit", {}).get("cavity")
    if not cavity:
        failed.append("в манифесте нет cockpit.cavity")
    else:
        inside_points = [(point, group) for point, group in rest_groups
                         if inside(point, cavity)]
        ok = not inside_points
        print("%s кабина: %d вершин в объёме %s" % (
            "OK  " if ok else "FAIL", len(inside_points),
            "[" + ", ".join("%.2f" % v for v in cavity) + "]"))
        if inside_points:
            for point, group in inside_points[:6]:
                print("     %.3f %.3f %.3f  %s" % (
                    point.x, point.y, point.z, group))
            failed.append("кабина занята: %d вершин" % len(inside_points))

    # --- скорость, корень, провал ---------------------------------------
    arm.data.pose_position = "POSE"
    feet = [name for name in ("Foot.L", "Foot.R") if name in arm.pose.bones]
    if not feet:
        failed.append("нет костей стоп Foot.L/Foot.R")
    lowest = 1e9
    for key in ("walk", "run"):
        action = actions.get(key)
        if action is None:
            continue
        measured, samples = speed(arm, feet, action, fps)
        expected = float(entry.get("clip_speed", {}).get(key, 0.0))
        if samples == 0:
            failed.append("%s: стопа ни разу не коснулась пола" % key)
            print("FAIL скорость %s: контакт не найден" % key)
            continue
        ratio = measured / expected if expected else 0.0
        ok = abs(ratio - 1.0) <= SPEED_TOLERANCE
        print("%s скорость %s: замер %.2f м/с манифест %.2f (%.0f%%) сэмплов %d" % (
            "OK  " if ok else "FAIL", key, measured, expected, ratio * 100.0,
            samples))
        if not ok:
            failed.append("%s: скорость %.2f против %.2f" % (key, measured, expected))
        # Только шаг: бег у пака с фазой полёта, и провал там не инвариант.
        if key == "walk":
            lowest = min(lowest, lowest_during(arm, meshes, action))

    if lowest < 1e8:
        sink_ok = lowest >= -SINK_TOLERANCE
        print("%s шаг: минимальная точка %.3f (провал до %.3f)" % (
            "OK  " if sink_ok else "FAIL", lowest, -SINK_TOLERANCE))
        if not sink_ok:
            failed.append("в походке уходит в пол: %.3f" % lowest)

    # --- стопа на голени -------------------------------------------------
    for key, action in actions.items():
        gap = foot_gap(arm, action)
        limit = FOOT_GAP_IDLE if key == "idle" else FOOT_GAP_MAX
        ok = gap <= limit
        print("%s стопа на голени %-5s отрыв %.3f м (порог %.2f)" % (
            "OK  " if ok else "FAIL", key, gap, limit))
        if not ok:
            failed.append("%s: стопа отошла от голени на %.2f м" % (key, gap))

    root = entry.get("root")
    if root and root in arm.pose.bones:
        for key, action in actions.items():
            activate(arm, action)
            start, end = (int(round(v)) for v in action.frame_range)
            points = []
            for frame in (start, end):
                bpy.context.scene.frame_set(frame)
                bpy.context.view_layer.update()
                points.append(arm.matrix_world @ arm.pose.bones[root].head)
            # Только горизонталь: вертикальный боб корня — это «вес» шага, а
            # не движение актёра.
            travel = (points[1].xy - points[0].xy).length
            ok = travel <= IN_PLACE_TOLERANCE
            print("%s %s: ход корня %.4f (in-place, порог %.3f)" % (
                "OK  " if ok else "FAIL", key, travel, IN_PLACE_TOLERANCE))
            if not ok:
                failed.append("%s: ход корня %.3f" % (key, travel))
    else:
        print("WARN ход корня: кости %s нет" % root)

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
    print("ПРОВЕРКА ХОДУНОВ PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
