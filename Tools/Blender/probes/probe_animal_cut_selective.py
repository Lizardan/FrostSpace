"""Срез головы: плоскость у основания + удаление только головных вершин.

Чистый срез плоскостью режет всё, что оказалось впереди, включая петли шеи
у длинношеих; чистый срез по весам уносит всю шею. Комбинированный вариант:
плоскость в основании кости головы, затем удаляются только вершины впереди
плоскости, у которых доминирует группа головы. Шея остаётся целой.

Печатает: сколько удалено, сколько из них было с доминантой шеи (должно быть
0), число контуров и открытых рёбер до/после `holes_fill`/`triangle_fill`,
габарит контура среза.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_cut_selective.py -- Wolf Cow Eagle
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


def loops(bm):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    by_vert = {}
    for edge in edges:
        for vert in edge.verts:
            by_vert.setdefault(vert.index, []).append(edge)
    seen = set()
    count = 0
    for edge in edges:
        if edge.index in seen:
            continue
        count += 1
        stack = [edge]
        seen.add(edge.index)
        while stack:
            cur = stack.pop()
            for vert in cur.verts:
                for other in by_vert.get(vert.index, []):
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
    return count, len(edges)


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
        metrics = animal_designs._shell_metrics(mesh, roles)
        group = mesh.vertex_groups.get(roles["head"])
        neck_group = mesh.vertex_groups.get(roles.get("neck"))
        mw = mesh.matrix_world
        inv = mw.inverted()
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        layer = bm.verts.layers.deform.active
        bmesh.ops.bisect_plane(
            bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
            dist=1e-6, plane_co=inv @ h0,
            plane_no=(inv.to_3x3() @ hd).normalized(),
            clear_outer=False, clear_inner=False)
        doomed = []
        neck_dominant = 0
        for vert in bm.verts:
            if (mw @ vert.co - h0).dot(hd) <= 1e-6:
                continue
            weights = vert[layer] if layer is not None else {}
            if weights.get(group.index, 0.0) <= 0.25:
                continue
            doomed.append(vert)
            if neck_group is not None and len(weights) > 0:
                best = max(weights.items(), key=lambda kv: kv[1])[0]
                if best == neck_group.index:
                    neck_dominant += 1
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")
        count, edges = loops(bm)
        boundary = [e for e in bm.edges if len(e.link_faces) == 1]
        points = [mw @ v.co for e in boundary for v in e.verts]
        result = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
        filled = len(result.get("faces", []))
        left = [e for e in bm.edges if len(e.link_faces) == 1]
        if left:
            extra = bmesh.ops.triangle_fill(
                bm, use_beauty=True, use_dissolve=False, edges=left)
            filled += len(extra.get("geom", []))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        after = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        if points:
            cut = animal_designs._cut_extents(points, h0, hd)
            size = tuple(round(v, 3) for v in cut["size"])
        else:
            size = None
        torso = metrics.get("torso")
        print("SPECIES %-8s removed=%3d neck_dominant=%d loops=%d edges=%3d "
              "filled=%2d boundary=%d size=%s torso=%s" % (
                  item, len(doomed), neck_dominant, count, edges, filled,
                  after, size,
                  None if torso is None else
                  tuple(round(v, 3) for v in torso)))
        bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
