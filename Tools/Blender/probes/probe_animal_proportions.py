"""Числа по пропорциям головы и местам разрывов оболочки.

Считает по ригу и по фактической геометрии, без рендера:
- размеры тела (ширина/высота/длина) в той же системе, что `_body_armor`;
- длину кости головы и текущий размер робо-черепа;
- контуры открытых границ исходной оболочки в мировых координатах и их
  положение относительно кости головы;
- габариты исходной оболочки и робо-брони.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_proportions.py -- Wolf Cow Eagle
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


def loops_of(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    by_vert = {}
    for edge in edges:
        for vert in edge.verts:
            by_vert.setdefault(vert.index, []).append(edge)
    seen = set()
    loops = []
    for edge in edges:
        if edge.index in seen:
            continue
        stack = [edge]
        seen.add(edge.index)
        group = []
        while stack:
            cur = stack.pop()
            group.append(cur)
            for vert in cur.verts:
                for other in by_vert.get(vert.index, []):
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        loops.append(group)
    result = []
    mw = obj.matrix_world
    for group in loops:
        points = [mw @ v.co for e in group for v in e.verts]
        lo = Vector((min(p.x for p in points), min(p.y for p in points),
                     min(p.z for p in points)))
        hi = Vector((max(p.x for p in points), max(p.y for p in points),
                     max(p.z for p in points)))
        result.append({"edges": len(group), "center": (lo + hi) * 0.5,
                       "size": hi - lo})
    bm.free()
    return result


def describe(species):
    print("=" * 70)
    print("SPECIES", species)
    arm, source_mesh, meta = animal_lib.load(species)
    roles = meta["roles"]
    R = robot_lib.rig_points(arm)
    cfg = dict(animal_designs.QUADRUPEDS.get(species, {}))
    if not cfg:
        cfg = dict(body_width=1.0, body_height=0.4)
    pelvis, neck, center, width, height, length = animal_designs._body_frame(
        R, roles, cfg)
    h0, h1, hd, hl = animal_designs._head_frame(R, roles)
    print("BODY width=%.3f height=%.3f length=%.3f center=%s" % (
        width, height, length, tuple(round(v, 3) for v in center)))
    print("HEAD bone len=%.3f h0=%s h1=%s" % (
        hl, tuple(round(v, 3) for v in h0), tuple(round(v, 3) for v in h1)))
    print("NECK head=%s tail=%s" % (
        tuple(round(v, 3) for v in R[roles["neck"]][0]),
        tuple(round(v, 3) for v in R[roles["neck"]][1])))
    metrics = animal_designs._shell_metrics(source_mesh, roles)
    print("TORSO size=%s" % (tuple(
        round(v, 3) for v in metrics.get("torso", Vector((0, 0, 0)))),))

    print("SHELL before cut:")
    for item in loops_of(source_mesh):
        rel = item["center"] - h0
        print("  loop edges=%d center=%s size=%s rel_head=%s" % (
            item["edges"],
            tuple(round(v, 3) for v in item["center"]),
            tuple(round(v, 3) for v in item["size"]),
            tuple(round(v, 3) for v in rel)))
    cut = animal_designs._remove_head(source_mesh, roles, R)
    box = animal_designs._head_box(R, roles, cut, metrics.get("torso"))
    print("SKULL size=(w, l, h)=%s ratio w/body=%.2f h/body=%.2f" % (
        (round(box["width"], 3), round(box["length"], 3),
         round(box["height"], 3)),
        box["width"] / max(width, 1e-6), box["height"] / max(height, 1e-6)))
    print("SHELL after cut:")
    for item in loops_of(source_mesh):
        rel = item["center"] - h0
        print("  loop edges=%d center=%s size=%s rel_head=%s" % (
            item["edges"],
            tuple(round(v, 3) for v in item["center"]),
            tuple(round(v, 3) for v in item["size"]),
            tuple(round(v, 3) for v in rel)))
    corners = [source_mesh.matrix_world @ Vector(c)
               for c in source_mesh.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    print("SHELL bbox size=%s" % (tuple(round(v, 3) for v in (hi - lo)),))


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
