"""Вертикальный срез головы: плоскость поперёк корпуса у основания черепа.

Наклонная плоскость (перпендикулярно кости головы) у длинношеих режет шею
по касательной и даёт контур в 1.5–2 высоты торса. Вертикальная плоскость
поперёк оси животного отрезает голову «гильотиной»: контур — это сечение
головы, и череп-коробка встаёт на него без наклона.

Печатает для смещений 0/10/20/30% длины головы вперёд: удалено вершин,
контур (ширина × высота × глубина), сколько вершин осталось впереди
плоскости, закрылся ли срез.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_vertical_cut.py -- Wolf Cow Eagle
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

UP = Vector((0.0, 0.0, 1.0))


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
        forward = Vector((hd.x, hd.y, 0.0))
        if forward.length < 1e-6:
            forward = Vector((0.0, -1.0, 0.0))
        forward.normalize()
        right = forward.cross(UP).normalized()
        mw = mesh.matrix_world
        inv = mw.inverted()
        group = mesh.vertex_groups.get(roles["head"])
        print("=" * 70)
        print("SPECIES %s head_len=%.3f torso=%s forward=%s" % (
            item, hl, tuple(round(v, 3) for v in torso),
            tuple(round(v, 2) for v in forward)))
        for offset in (0.0, 0.10, 0.20, 0.30):
            plane_co = h0 + forward * hl * offset
            bm = bmesh.new()
            bm.from_mesh(mesh.data)
            layer = bm.verts.layers.deform.active
            bmesh.ops.bisect_plane(
                bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                dist=1e-6, plane_co=inv @ plane_co,
                plane_no=(inv.to_3x3() @ forward).normalized(),
                clear_outer=False, clear_inner=False)
            doomed = []
            for vert in bm.verts:
                if (mw @ vert.co - plane_co).dot(forward) <= 1e-6:
                    continue
                weights = vert[layer] if layer is not None else {}
                if weights.get(group.index, 0.0) > 0.25:
                    doomed.append(vert)
            bmesh.ops.delete(bm, geom=doomed, context="VERTS")
            edges = [e for e in bm.edges if len(e.link_faces) == 1]
            points = [mw @ v.co for e in edges for v in e.verts]
            result = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
            filled = len(result.get("faces", []))
            left = [e for e in bm.edges if len(e.link_faces) == 1]
            if left:
                extra = bmesh.ops.triangle_fill(
                    bm, use_beauty=True, use_dissolve=False, edges=left)
                filled += len(extra.get("geom", []))
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            after = sum(1 for e in bm.edges if len(e.link_faces) == 1)
            ahead = sum(1 for v in bm.verts
                        if (mw @ v.co - plane_co).dot(forward) > 1e-4)
            if points:
                values = [[], [], []]
                for p in points:
                    rel = p - plane_co
                    values[0].append(rel.dot(right))
                    values[1].append(rel.dot(UP))
                    values[2].append(rel.dot(forward))
                size = tuple(round(max(v) - min(v), 3) for v in values)
            else:
                size = None
            print("  off=%.2f removed=%3d edges=%3d filled=%2d boundary=%d "
                  "ahead=%d size=%s" % (
                      offset, len(doomed), len(edges), filled, after, ahead,
                      size))
            bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
