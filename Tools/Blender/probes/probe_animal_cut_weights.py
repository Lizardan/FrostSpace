"""Срез головы плоскостью у основания: контур в мировых осях и веса.

Ключевые вопросы перед правкой дизайна:
- сохраняют ли новые вершины среза веса (иначе они «зависнут» в rest-позе);
- какой габарит у заглушки в мировых осях — от него зависит размер черепа;
- какой габарит у шеи сразу за срезом.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_cut_weights.py -- Wolf Cow Eagle
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
        torso = animal_designs._shell_metrics(mesh, roles)["torso"]
        mw = mesh.matrix_world
        inv = mw.inverted()
        plane_co = inv @ h0
        plane_no = (inv.to_3x3() @ hd).normalized()
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        before_verts = len(bm.verts)
        bmesh.ops.bisect_plane(
            bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
            dist=1e-6, plane_co=plane_co, plane_no=plane_no,
            clear_outer=True, clear_inner=False)
        edges = [e for e in bm.edges if len(e.link_faces) == 1]
        result = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        new_verts = [v for v in bm.verts if v.index >= before_verts]
        layer = bm.verts.layers.deform.active
        unweighted = 0
        for vert in bm.verts:
            if layer is None or not vert[layer]:
                unweighted += 1
        points = [mw @ v.co for v in new_verts]
        print("=" * 70)
        print("SPECIES", item, "head_len=%.3f" % hl,
              "cut_verts=%d filled=%d boundary=%d unweighted=%d" % (
                  len(new_verts), len(result.get("faces", [])), boundary,
                  unweighted))
        if torso is not None:
            print("  torso world size=%s" % (
                tuple(round(v, 3) for v in torso),))
        if points:
            lo = Vector((min(p.x for p in points), min(p.y for p in points),
                         min(p.z for p in points)))
            hi = Vector((max(p.x for p in points), max(p.y for p in points),
                         max(p.z for p in points)))
            print("  cap world size=%s center=%s" % (
                tuple(round(v, 3) for v in (hi - lo)),
                tuple(round(v, 3) for v in ((hi + lo) * 0.5))))
        bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
