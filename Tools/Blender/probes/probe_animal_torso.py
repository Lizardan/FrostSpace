"""Замер реального торса по весам и поиск вырожденных граней.

Нужен, чтобы размеры брони и черепа считались от фактической оболочки,
а не от эвристики `body_width * length * 0.38`, которая занижает корпус
в 1.5–2 раза.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_torso.py -- Wolf Cat Eagle
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
    lo = Vector((min(p.x for p in points), min(p.y for p in points),
                 min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points),
                 max(p.z for p in points)))
    return lo, hi


def dominant_vertices(mesh, groups):
    names = {g.index: g.name for g in mesh.vertex_groups}
    wanted = {g.index for g in mesh.vertex_groups if g.name in groups}
    result = []
    for vert in mesh.data.vertices:
        if not vert.groups:
            continue
        best = max(vert.groups, key=lambda item: item.weight)
        if best.group in wanted:
            result.append(mesh.matrix_world @ vert.co)
    return result


def measure(species):
    arm, mesh, meta = animal_lib.load(species)
    roles = meta["roles"]
    torso_bones = {roles.get("body"), roles.get("pelvis"),
                   roles.get("chest")} - {None}
    points = dominant_vertices(mesh, torso_bones)
    lo, hi = bbox(points)
    print("TORSO %-8s verts=%4d size=%s center=%s" % (
        species, len(points),
        tuple(round(v, 3) for v in (hi - lo)),
        tuple(round(v, 3) for v in ((lo + hi) * 0.5))))
    neck = roles.get("neck")
    if neck:
        neck_points = dominant_vertices(mesh, {neck})
        if neck_points:
            nlo, nhi = bbox(neck_points)
            print("  NECK_SHELL size=%s center=%s" % (
                tuple(round(v, 3) for v in (nhi - nlo)),
                tuple(round(v, 3) for v in ((nlo + nhi) * 0.5))))
    head = roles.get("head")
    if head:
        head_points = dominant_vertices(mesh, {head})
        if head_points:
            hlo, hhi = bbox(head_points)
            print("  HEAD_SHELL size=%s center=%s" % (
                tuple(round(v, 3) for v in (hhi - hlo)),
                tuple(round(v, 3) for v in ((hlo + hhi) * 0.5))))
    all_points = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
    alo, ahi = bbox(all_points)
    print("  SHELL bbox size=%s" % (tuple(round(v, 3) for v in (ahi - alo)),))
    cfg = dict(animal_designs.QUADRUPEDS.get(species, {}) or
               dict(body_width=1.0, body_height=0.4))
    R = robot_lib.rig_points(arm)
    _, _, _, width, height, length = animal_designs._body_frame(
        R, roles, cfg)
    print("  OLD body width=%.3f height=%.3f length=%.3f" %
          (width, height, length))


def zero_faces(species):
    arm, mesh, meta = animal_lib.load(species)
    name, obj, mats, M = animal_designs.build_quadruped(species, arm, mesh)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    zero = [f for f in bm.faces if f.calc_area() < 1e-9]
    print("ZERO %-8s count=%d" % (species, len(zero)))
    by_mat = {}
    for face in zero:
        by_mat.setdefault(face.material_index, []).append(face)
    for index, faces in sorted(by_mat.items()):
        mat = mats[index].name if index < len(mats) else "?"
        print("  mat=%s faces=%d" % (mat, len(faces)))
        for face in faces[:3]:
            print("    center=%s normal=%s verts=%d" % (
                tuple(round(v, 3) for v in face.calc_center_median()),
                tuple(round(v, 2) for v in face.normal), len(face.verts)))
    bm.free()


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        print("=" * 70)
        measure(item)
        if item in animal_designs.QUADRUPEDS:
            zero_faces(item)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
