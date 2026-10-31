"""Проверяет инварианты ассетов вагона и груза после сборки.

Кабина — нос глубиной 4,2 м (`NOSE_TIP = -16,10`), длина вагона ~28,9 м.

Запуск (штатно — из `build_all.py`, per = file):

    blender --background --factory-startup --python Tools/Blender/check_train.py -- \
        "Assets/_Project/Content/Models/Train/TrainCar.fbx"

Проверяется то, что нельзя увидеть на превью одним взглядом: габарит, низ на
полу (пивот пропов), наличие створок и их размеры, отсутствие копланарных
граней. Копланарность — главная причина «мерцания»: две грани в одной
плоскости дают z-fighting, и вагон рябит. Ошибка — ненулевой код возврата,
как у остальных `check_*` пайплайна.
"""

from __future__ import annotations

import os
import sys
import traceback

import bmesh
import bpy
from mathutils import Vector


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def bounds(obj) -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for corner in obj.bound_box:
        p = obj.matrix_world @ Vector(corner)
        lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
        hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def check_range(name: str, value: float, lo: float, hi: float,
                problems: list[str]) -> None:
    if not (lo <= value <= hi):
        problems.append("%s=%.3f вне [%.3f, %.3f]" % (name, value, lo, hi))


def _islands(bm) -> dict[int, int]:
    """Номер связного острова для каждой грани (по общим рёбрам)."""
    island: dict[int, int] = {}
    current = 0
    for face in bm.faces:
        if face.index in island:
            continue
        island[face.index] = current
        stack = [face]
        while stack:
            cur = stack.pop()
            for edge in cur.edges:
                for other in edge.link_faces:
                    if other.index not in island:
                        island[other.index] = current
                        stack.append(other)
        current += 1
    return island


def _aabb(face) -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for vert in face.verts:
        p = vert.co
        lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
        hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def _overlap(a, b, eps: float = 0.002) -> bool:
    for axis in range(3):
        if a[1][axis] - eps <= b[0][axis] or b[1][axis] - eps <= a[0][axis]:
            return False
    return True


def coplanar_pairs(obj, tol: float = 0.002,
                   min_area: float = 0.005) -> list[tuple[Vector, Vector]]:
    """Пары граней из разных оболочек, лежащих в одной плоскости.

    Сравниваются только грани площадью больше `min_area` (в м²): фаски
    размером в сантиметр дают микроскопические треугольники, которые не
    мерцают. Меш импортированного FBX лежит в сантиметрах, поэтому площадь
    и смещение плоскости переводятся в метры через масштаб объекта.
    """
    scale = obj.matrix_world.to_scale()
    linear = max(abs(scale.x), 0.000001)
    area_scale = linear * linear

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    islands = _islands(bm)

    groups: dict[tuple, list] = {}
    for face in bm.faces:
        if face.calc_area() * area_scale < min_area:
            continue
        normal = face.normal.copy()
        key_n = (round(normal.x, 2), round(normal.y, 2), round(normal.z, 2))
        distance = normal.dot(face.calc_center_median()) * linear
        key_d = round(distance / tol)
        groups.setdefault((key_n, key_d), []).append(face)

    pairs: list[tuple[Vector, Vector]] = []
    for faces in groups.values():
        if len(faces) < 2:
            continue
        boxes = [_aabb(f) for f in faces]
        for i in range(len(faces)):
            for j in range(i + 1, len(faces)):
                if islands[faces[i].index] == islands[faces[j].index]:
                    continue
                if _overlap(boxes[i], boxes[j], tol / linear):
                    pairs.append((faces[i].calc_center_median(),
                                  faces[j].calc_center_median()))
    bm.free()
    return pairs


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    path = argv[0]
    base = os.path.splitext(os.path.basename(path))[0]
    if not os.path.exists(path):
        print("CHECK %s FAIL нет файла" % base)
        return 1

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = {obj.name: obj for obj in bpy.context.scene.objects
              if obj.type == "MESH"}
    problems: list[str] = []

    if base == "TrainCar":
        body = meshes.get("TrainBody")
        door_l = meshes.get("TrainDoorL")
        door_r = meshes.get("TrainDoorR")
        left = meshes.get("TrainSideDoorL")
        right = meshes.get("TrainSideDoorR")
        for name, obj in (("TrainBody", body), ("TrainDoorL", door_l),
                          ("TrainDoorR", door_r), ("TrainSideDoorL", left),
                          ("TrainSideDoorR", right)):
            if obj is None:
                problems.append("нет объекта %s" % name)
        if body is not None:
            lo, hi = bounds(body)
            size = hi - lo
            check_range("body.width", size.x, 8.40, 8.90, problems)
            check_range("body.length", size.y, 28.75, 29.15, problems)
            check_range("body.front", lo.y, -16.45, -16.15, problems)
            check_range("body.top", hi.z, 3.30, 3.78, problems)
            check_range("body.bottom", lo.z, -1.20, -0.80, problems)
        # Торцевая дверь — две створки на разных дорожках.
        for door, center_y in ((door_l, -11.88), (door_r, -11.94)):
            if door is None:
                continue
            lo, hi = bounds(door)
            size = hi - lo
            check_range(door.name + ".width", size.x, 1.50, 1.65, problems)
            check_range(door.name + ".height", size.z, 2.50, 2.62, problems)
            check_range(door.name + ".bottom", lo.z, -0.04, 0.02, problems)
            # X в импорте зеркален Unity — проверяем модуль центра.
            check_range(door.name + ".x", abs((lo.x + hi.x) * 0.5),
                        0.70, 0.82, problems)
            if abs((lo.y + hi.y) * 0.5 - center_y) > 0.05:
                problems.append("%s не у торца: y=%.2f"
                                % (door.name, (lo.y + hi.y) * 0.5))
        if door_l is not None and door_r is not None:
            ll, lh = bounds(door_l)
            rl, rh = bounds(door_r)
            overlap = min(lh.x, rh.x) - max(ll.x, rl.x)
            if overlap < 0.03:
                problems.append("щель между створками двери: %.3f м" % overlap)
            if abs((ll.y + lh.y) * 0.5 - (rl.y + rh.y) * 0.5) < 0.03:
                problems.append("створки двери на одной дорожке")
            union = max(lh.x, rh.x) - min(ll.x, rl.x)
            if union > 3.16:
                problems.append("створки шире проёма: %.3f" % union)
        for leaf, center in ((left, -1.63), (right, -0.37)):
            if leaf is None:
                continue
            lo, hi = bounds(leaf)
            size = hi - lo
            check_range(leaf.name + ".thickness", size.x, 0.08, 0.30, problems)
            check_range(leaf.name + ".width", size.y, 1.20, 1.40, problems)
            check_range(leaf.name + ".height", size.z, 2.40, 2.70, problems)
            # В импортированном FBX ось X зеркальна Unity: в сцене створки
            # стоят на +X, здесь — на −X.
            check_range(leaf.name + ".x", (lo.x + hi.x) * 0.5,
                        -4.24, -4.02, problems)
            if abs((lo.y + hi.y) * 0.5 - center) > 0.05:
                problems.append("%s не на месте: y=%.2f"
                                % (leaf.name, (lo.y + hi.y) * 0.5))
        if left is not None and right is not None:
            lx = (bounds(left)[0].x + bounds(left)[1].x) * 0.5
            rx = (bounds(right)[0].x + bounds(right)[1].x) * 0.5
            if abs(lx - rx) < 0.05:
                problems.append("створки на одном рельсе: dx=%.3f"
                                % abs(lx - rx))
            # Створки обязаны перекрываться по длине: зазор в 4 см пропускает
            # луч и читается как щель в закрытом вагоне.
            ll, lh = bounds(left)
            rl, rh = bounds(right)
            overlap = min(lh.y, rh.y) - max(ll.y, rl.y)
            if overlap < 0.005:
                problems.append("щель между створками: %.3f м" % overlap)

        # Копланарные грани: главный источник мерцания. Для вагона — FAIL.
        for obj in (body, door_l, door_r, left, right):
            if obj is None:
                continue
            pairs = coplanar_pairs(obj)
            print("COPLANAR %s pairs=%d" % (obj.name, len(pairs)))
            for a, b in pairs[:12]:
                print("  %.2f,%.2f,%.2f  <->  %.2f,%.2f,%.2f"
                      % (a.x, a.y, a.z, b.x, b.y, b.z))
            if pairs:
                problems.append("%s: копланарных пар %d" % (obj.name, len(pairs)))

    elif base == "Prop_Battery":
        if len(meshes) != 1:
            problems.append("ожидался один меш, найдено %d" % len(meshes))
        obj = next(iter(meshes.values()), None)
        if obj is not None:
            lo, hi = bounds(obj)
            size = hi - lo
            check_range("battery.width", size.x, 0.9, 1.1, problems)
            check_range("battery.depth", size.y, 0.6, 0.8, problems)
            check_range("battery.height", size.z, 0.85, 1.05, problems)
            check_range("battery.bottom", lo.z, -0.02, 0.02, problems)

    elif base == "Prop_GravCart":
        if len(meshes) != 1:
            problems.append("ожидался один меш, найдено %d" % len(meshes))
        obj = next(iter(meshes.values()), None)
        if obj is not None:
            lo, hi = bounds(obj)
            size = hi - lo
            check_range("cart.width", size.x, 2.3, 2.5, problems)
            check_range("cart.depth", size.y, 1.8, 2.0, problems)
            check_range("cart.height", size.z, 1.05, 1.25, problems)
            check_range("cart.bottom", lo.z, -0.02, 0.02, problems)
            # Днище гнёзд мерцало: верх плиты и дно гнезда были на 0,55.
            pairs = coplanar_pairs(obj)
            print("COPLANAR %s pairs=%d" % (obj.name, len(pairs)))
            for a, b in pairs[:12]:
                print("  %.2f,%.2f,%.2f  <->  %.2f,%.2f,%.2f"
                      % (a.x, a.y, a.z, b.x, b.y, b.z))
            if pairs:
                problems.append("%s: копланарных пар %d" % (obj.name, len(pairs)))
    else:
        print("CHECK %s SKIP неизвестный ассет" % base)
        return 0

    if problems:
        print("CHECK %s FAIL: %s" % (base, "; ".join(problems)))
        return 1
    print("CHECK %s PASS" % base)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
