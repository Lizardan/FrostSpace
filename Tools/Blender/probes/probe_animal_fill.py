"""Проверка закрытия среза головы: weight-cut + заливка отверстий.

Сравнивает два способа:
1. `triangle_fill` по всем открытым рёбрам;
2. `holes_fill`.
Печатает число открытых рёбер до/после и число новых граней.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_fill.py -- Wolf Cat Cow Eagle Piranha
"""

from __future__ import annotations

import os
import sys
import traceback

import bmesh
import bpy

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


def cut(mesh, roles):
    group = mesh.vertex_groups.get(roles["head"])
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    layer = bm.verts.layers.deform.active
    doomed = [v for v in bm.verts if v[layer].get(group.index, 0.0) > 0.25]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    return bm


def boundary_count(bm):
    return sum(1 for e in bm.edges if len(e.link_faces) == 1)


def try_triangle_fill(bm):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    before = len(bm.faces)
    bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False,
                            edges=edges)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return len(bm.faces) - before, boundary_count(bm)


def try_holes_fill(bm):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    before = len(bm.faces)
    bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return len(bm.faces) - before, boundary_count(bm)


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        arm, mesh, meta = animal_lib.load(item)
        roles = meta["roles"]
        print("=" * 60)
        print("SPECIES", item)
        for label, func in (("triangle_fill", try_triangle_fill),
                            ("holes_fill", try_holes_fill)):
            bm = cut(mesh, roles)
            before_faces = len(bm.faces)
            before_boundary = boundary_count(bm)
            added, after = func(bm)
            print("  %-14s faces_before=%d boundary_before=%d added=%d "
                  "boundary_after=%d" % (label, before_faces,
                                         before_boundary, added, after))
            bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
