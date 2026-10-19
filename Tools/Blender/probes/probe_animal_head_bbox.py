"""Габарит органической головы в осях кости головы.

Нужен, чтобы робо-череп гарантированно накрывал органическую голову:
если коробка построена по этому bbox, голова не торчит из неё и щели на
шее не возникает (органическая шея остаётся целой).

Печатает для порогов веса 0.5/0.75/0.9: число вершин, размер и центр
bbox в осях «вбок / вверх / вперёд» от основания кости головы, и bbox
в мировых осях.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_head_bbox.py -- Wolf Cow Eagle
"""

from __future__ import annotations

import os
import sys
import traceback

from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, ".."))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import animal_designs  # noqa: E402
import animal_lib  # noqa: E402
import robot_lib  # noqa: E402


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        arm, mesh, meta = animal_lib.load(item)
        roles = meta["roles"]
        R = robot_lib.rig_points(arm)
        h0, _h1, hd, hl = animal_designs._head_frame(R, roles)
        right = hd.cross(Vector((0.0, 0.0, 1.0)))
        if right.length < 1e-6:
            right = Vector((1.0, 0.0, 0.0))
        right.normalize()
        up = right.cross(hd).normalized()
        group = mesh.vertex_groups.get(roles["head"])
        print("=" * 70)
        print("SPECIES", item, "head_len=%.3f" % hl)
        for threshold in (0.5, 0.75, 0.9):
            points = []
            for vert in mesh.data.vertices:
                weight = 0.0
                for item_group in vert.groups:
                    if item_group.group == group.index:
                        weight = item_group.weight
                        break
                if weight >= threshold:
                    points.append(mesh.matrix_world @ vert.co)
            if not points:
                print("  thr=%.2f verts=0" % threshold)
                continue
            local = []
            for p in points:
                rel = p - h0
                local.append((rel.dot(right), rel.dot(up), rel.dot(hd)))
            lo = [min(v[i] for v in local) for i in range(3)]
            hi = [max(v[i] for v in local) for i in range(3)]
            world_lo = Vector((min(p.x for p in points),
                               min(p.y for p in points),
                               min(p.z for p in points)))
            world_hi = Vector((max(p.x for p in points),
                               max(p.y for p in points),
                               max(p.z for p in points)))
            print("  thr=%.2f verts=%3d size=%s center=%s world=%s" % (
                threshold, len(points),
                tuple(round(hi[i] - lo[i], 3) for i in range(3)),
                tuple(round((hi[i] + lo[i]) * 0.5, 3) for i in range(3)),
                tuple(round(v, 3) for v in (world_hi - world_lo))))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
