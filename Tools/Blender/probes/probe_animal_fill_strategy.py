"""Стратегии закрытия среза головы после среза по весам.

Для порога 0.5 проверяет:
- сколько открытых контуров осталось;
- закрывает ли `holes_fill`;
- если нет — закрывает ли `triangle_fill` (и сколько граней добавляет);
- остаётся ли открытая граница.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_fill_strategy.py -- Wolf Cow Eagle
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

import animal_lib  # noqa: E402
import robot_lib  # noqa: E402


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def loop_count(bm):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    by_vert = {}
    for edge in edges:
        for vert in edge.verts:
            by_vert.setdefault(vert.index, []).append(edge)
    seen = set()
    loops = 0
    for edge in edges:
        if edge.index in seen:
            continue
        loops += 1
        stack = [edge]
        seen.add(edge.index)
        while stack:
            cur = stack.pop()
            for vert in cur.verts:
                for other in by_vert.get(vert.index, []):
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
    return loops, len(edges)


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        arm, mesh, meta = animal_lib.load(item)
        group = mesh.vertex_groups.get(meta["roles"]["head"])
        print("=" * 70)
        print("SPECIES", item)
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        layer = bm.verts.layers.deform.active
        doomed = [v for v in bm.verts
                  if layer is not None and v[layer].get(group.index, 0.0) > 0.5]
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")
        loops, edges = loop_count(bm)
        print("  after cut: loops=%d boundary_edges=%d" % (loops, edges))
        boundary = [e for e in bm.edges if len(e.link_faces) == 1]
        before = len(bm.faces)
        result = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
        after_holes = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        print("  holes_fill: faces=%d boundary=%d" % (
            len(result.get("faces", [])), after_holes))
        if after_holes:
            boundary = [e for e in bm.edges if len(e.link_faces) == 1]
            result = bmesh.ops.triangle_fill(
                bm, use_beauty=True, use_dissolve=False, edges=boundary)
            after_tri = sum(1 for e in bm.edges if len(e.link_faces) == 1)
            print("  triangle_fill: faces=%d boundary=%d added=%d" % (
                len(result.get("geom", [])), after_tri,
                len(bm.faces) - before))
        bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
