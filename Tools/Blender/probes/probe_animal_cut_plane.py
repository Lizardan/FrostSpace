"""Проба среза головы по плоскости + закрытие отверстий.

Сравнивает три варианта среза:
1. только по весам (текущий);
2. по весам и плоскости на базе черепа;
3. по весам и плоскости с запасом назад.

Печатает размеры контура среза в осях кости головы и число открытых рёбер
после `holes_fill`.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_cut_plane.py -- Wolf Cow Eagle
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


def cut(mesh, roles, R, back):
    h0, _h1, hd, hl = animal_designs._head_frame(R, roles)
    plane = h0 - hd * hl * back
    group = mesh.vertex_groups.get(roles["head"])
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    layer = bm.verts.layers.deform.active
    mw = mesh.matrix_world
    doomed = []
    for vert in bm.verts:
        if layer is None or group is None:
            continue
        if vert[layer].get(group.index, 0.0) <= 0.25:
            continue
        if (mw @ vert.co - plane).dot(hd) > 0.0:
            doomed.append(vert)
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    return bm


def loop_extents(bm, h0, hd):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    if not edges:
        return None, 0
    right = hd.cross(Vector((0.0, 0.0, 1.0)))
    if right.length < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(hd).normalized()
    values = [[], [], []]
    for edge in edges:
        for vert in edge.verts:
            rel = vert.co - h0
            values[0].append(rel.dot(right))
            values[1].append(rel.dot(up))
            values[2].append(rel.dot(hd))
    sizes = tuple(round(max(v) - min(v), 3) for v in values)
    return sizes, len(edges)


def fill(bm):
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sum(1 for e in bm.edges if len(e.link_faces) == 1)


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
        print("=" * 70)
        print("SPECIES", item, "head_len=%.3f" % hl)
        for label, back in (("weights", -1.0), ("plane_0", 0.0),
                            ("plane_10", 0.10), ("plane_20", 0.20)):
            bm = cut(mesh, roles, R, back)
            if back < 0:
                # только веса
                pass
            sizes, count = loop_extents(bm, h0, hd)
            before_faces = len(bm.faces)
            after_boundary = fill(bm)
            added = len(bm.faces) - before_faces
            print("  %-9s cut_edges=%3d size=%s added_faces=%2d boundary=%d"
                  % (label, count, sizes, added, after_boundary))
            bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
