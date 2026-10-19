"""Геометрия контура среза головы в системе кости головы.

Для каждого вида: упорядоченный контур открытой границы после `_remove_head`,
его размеры в осях «вбок / вверх / вперёд» относительно направления кости
головы, число вершин и форма (прямоугольник, эллипс, седло).

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_cut.py -- Wolf Cow Eagle
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


def ordered_loop(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    if not edges:
        bm.free()
        return []
    by_vert = {}
    for edge in edges:
        for vert in edge.verts:
            by_vert.setdefault(vert.index, []).append(edge)
    start = edges[0]
    loop_verts = [start.verts[0], start.verts[1]]
    used = {start.index}
    current = start.verts[1]
    while True:
        nxt = None
        for edge in by_vert.get(current.index, []):
            if edge.index in used:
                continue
            nxt = edge
            break
        if nxt is None:
            break
        used.add(nxt.index)
        other = nxt.other_vert(current)
        if other.index == loop_verts[0].index:
            break
        loop_verts.append(other)
        current = other
    coords = [obj.matrix_world @ v.co for v in loop_verts]
    bm.free()
    return coords


def describe(species):
    arm, mesh, meta = animal_lib.load(species)
    roles = meta["roles"]
    R = robot_lib.rig_points(arm)
    h0, h1, hd, hl = animal_designs._head_frame(R, roles)
    # Срез делаем здесь, чтобы увидеть контур ДО заглушки: `_remove_head`
    # теперь закрывает отверстие и открытых рёбер не оставляет.
    group = mesh.vertex_groups.get(roles["head"])
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    layer = bm.verts.layers.deform.active
    doomed = [v for v in bm.verts
              if layer is not None and v[layer].get(group.index, 0.0) > 0.25]
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(mesh.data)
    bm.free()
    mesh.data.update()
    loop = ordered_loop(mesh)
    print("=" * 70)
    print("SPECIES", species, "loop_verts=%d head_len=%.3f" % (len(loop), hl))
    if not loop:
        print("  NO CUT LOOP")
        return
    right = hd.cross(Vector((0.0, 0.0, 1.0)))
    if right.length < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(hd).normalized()
    origin = h0
    local = []
    for point in loop:
        rel = point - origin
        local.append((rel.dot(right), rel.dot(up), rel.dot(hd)))
    for label, axis in (("right", 0), ("up", 1), ("forward", 2)):
        values = [item[axis] for item in local]
        print("  %-8s min=%7.3f max=%7.3f size=%6.3f" % (
            label, min(values), max(values), max(values) - min(values)))
    print("  plane_max_forward=%.3f plane_min_forward=%.3f" % (
        max(item[2] for item in local), min(item[2] for item in local)))
    print("  points:")
    for index, (px, py, pz) in enumerate(local):
        print("    %2d right=%7.3f up=%7.3f forward=%7.3f" % (index, px, py, pz))


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        describe(item)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
