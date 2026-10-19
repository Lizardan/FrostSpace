"""Срез головы плоскостью у основания черепа + закрытие отверстия.

Весовой срез удаляет всю шею (вес головы > 0.25 тянется до торса), из-за
чего череп оказывается в воздухе. Плоскость у основания кости головы режет
только череп и оставляет шею целой; отверстие лежит в плоскости и легко
закрывается.

Для смещений назад 0/5/10/15% длины головы печатает: сколько граней срезано,
размер контура, закрылся ли он `holes_fill`, и остался ли органический меш
впереди плоскости.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_bisect.py -- Wolf Cow Eagle
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
        right = hd.cross(Vector((0.0, 0.0, 1.0)))
        if right.length < 1e-6:
            right = Vector((1.0, 0.0, 0.0))
        right.normalize()
        up = right.cross(hd).normalized()
        print("=" * 70)
        print("SPECIES", item, "head_len=%.3f" % hl)
        for back in (-0.10, -0.20, -0.30, -0.40, -0.50, 0.0):
            bm = bmesh.new()
            bm.from_mesh(mesh.data)
            mw = mesh.matrix_world
            inv = mw.inverted()
            plane_co = h0 - hd * hl * back
            plane_co_local = inv @ plane_co
            plane_no_local = (inv.to_3x3() @ hd).normalized()
            before = len(bm.faces)
            bmesh.ops.bisect_plane(
                bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                dist=1e-6, plane_co=plane_co_local, plane_no=plane_no_local,
                clear_outer=True, clear_inner=False)
            removed = before - len(bm.faces)
            edges = [e for e in bm.edges if len(e.link_faces) == 1]
            result = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
            boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
            points = [mw @ v.co for e in edges for v in e.verts]
            if points:
                values = [[], [], []]
                for p in points:
                    rel = p - plane_co
                    values[0].append(rel.dot(right))
                    values[1].append(rel.dot(up))
                    values[2].append(rel.dot(hd))
                size = tuple(round(max(v) - min(v), 3) for v in values)
            else:
                size = None
            forward = max(((mw @ v.co - plane_co).dot(hd)
                           for v in bm.verts), default=0.0)
            print("  back=%.2f removed_faces=%3d cut_edges=%3d filled=%2d "
                  "boundary=%d size=%s forward=%.3f" % (
                      back, removed, len(edges),
                      len(result.get("faces", [])), boundary, size, forward))
            bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
