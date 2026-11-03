"""Знак объёма каждой замкнутой оболочки меша: какие вывернуты наизнанку.

`check_mesh_integrity.py` смотрит только на верхнюю поверхность, поэтому
перевёрнутая **стена** (вертикальная грань) для него невидима: сверху у неё
нормали в порядке, а снаружи она просвечивает. Здесь проверяется каждая
замкнутая оболочка отдельно по знаку объёма: у правильно собранной оболочки
нормали смотрят наружу, и объём положителен. Оболочки с отрицательным объёмом
— вывернутые.

Открытые оболочки (земля клетки, лист, шов) пропускаются: у них объём не
определён, и судить по нему нельзя.

Запуск:
    blender --background --factory-startup --python check_shell_normals.py -- <file.fbx> [...]

Выход: список вывернутых оболочек с габаритом и материалом; код возврата 1,
если такие есть.
"""

from __future__ import annotations

import os
import sys

import bmesh
import bpy


def imported(path: str):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def shells_of(obj):
    """Разбить объект на связные оболочки и вернуть их как отдельные объекты."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate()
    dup = bpy.context.active_object
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    return [o for o in bpy.context.selected_objects if o.type == "MESH"]


def describe(o) -> str:
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    mats = set()
    for poly in o.data.polygons:
        if poly.material_index >= len(o.material_slots):
            continue
        slot = o.material_slots[poly.material_index]
        if slot.material:
            mats.add(slot.material.name)
    return ("габарит=(%.1f..%.1f, %.1f..%.1f, %.1f..%.1f) материал=%s"
            % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
               ",".join(sorted(mats))))


def main() -> int:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("нужен файл .fbx")
        return 2

    bad_total = 0
    for path in args:
        if not os.path.exists(path):
            print("НЕТ ФАЙЛА", path)
            continue
        print("ФАЙЛ", os.path.basename(path))
        objects = imported(path)
        for obj in objects:
            shells = shells_of(obj)
            bad = []
            for sh in shells:
                bm = bmesh.new()
                bm.from_mesh(sh.data)
                closed = all(e.is_manifold for e in bm.edges)
                if not closed:
                    bm.free()
                    continue

                # Объём считается относительно центра оболочки: меши клетки
                # стоят в сотнях метров от нуля, и сумма по граням в мировых
                # координатах теряет точность — знак становится случайным.
                # Перенос начала в центр оболочки убирает это полностью.
                if bm.verts:
                    cx = sum(v.co.x for v in bm.verts) / len(bm.verts)
                    cy = sum(v.co.y for v in bm.verts) / len(bm.verts)
                    cz = sum(v.co.z for v in bm.verts) / len(bm.verts)
                    for v in bm.verts:
                        v.co.x -= cx
                        v.co.y -= cy
                        v.co.z -= cz

                vol = bm.calc_volume(signed=True)
                bm.free()
                if vol < -1e-6:
                    bad.append((vol, sh))
            if bad:
                bad_total += len(bad)
                print("  %s: вывернутых оболочек %d из %d"
                      % (obj.name, len(bad), len(shells)))
                for vol, sh in sorted(bad, key=lambda p: p[0])[:20]:
                    print("    объём=%.3f %s" % (vol, describe(sh)))
            else:
                print("  %s: вывернутых нет (оболочек %d)" % (obj.name, len(shells)))
        bpy.ops.wm.read_factory_settings(use_empty=True)

    print("ИТОГ: вывернутых оболочек", bad_total)
    return 1 if bad_total else 0


if __name__ == "__main__":
    sys.exit(main())
