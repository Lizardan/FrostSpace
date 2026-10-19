"""Распределение весов головы и состояние исходной оболочки.

Печатает:
- открытые границы исходного меша до правок (должно быть 0);
- сколько вершин попадает в весовые корзины головы и где они;
- bbox вершин каждой корзины относительно кости головы.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_head_weights.py -- Wolf Cow Eagle
"""

from __future__ import annotations

import os
import sys
import traceback

import bmesh
import bpy
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


def bbox(points):
    if not points:
        return None
    lo = Vector((min(p.x for p in points), min(p.y for p in points),
                 min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points),
                 max(p.z for p in points)))
    return lo, hi


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        arm, mesh, meta = animal_lib.load(item)
        roles = meta["roles"]
        R = robot_lib.rig_points(arm)
        h0, h1, hd, hl = animal_designs._head_frame(R, roles)
        group = mesh.vertex_groups.get(roles["head"])
        print("=" * 70)
        print("SPECIES", item, "head_group", roles["head"],
              "head_len=%.3f" % hl)
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        print("  source boundary_edges=%d" % boundary)
        bm.free()

        buckets = [(0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.01)]
        for low, high in buckets:
            points = []
            for vert in mesh.data.vertices:
                weight = 0.0
                for item_group in vert.groups:
                    if item_group.group == group.index:
                        weight = item_group.weight
                        break
                if low <= weight < high:
                    points.append(mesh.matrix_world @ vert.co)
            lo_hi = bbox(points)
            if lo_hi is None:
                print("  weight %.2f..%.2f verts=0" % (low, high))
                continue
            lo, hi = lo_hi
            rel_lo = lo - h0
            rel_hi = hi - h0
            print("  weight %.2f..%.2f verts=%3d size=%s rel_center=%s" % (
                low, high, len(points),
                tuple(round(v, 3) for v in (hi - lo)),
                tuple(round(v, 3) for v in ((rel_lo + rel_hi) * 0.5))))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
