"""Подбор порога весов головы: какой кусок шеи реально срезается.

Печатает для порогов 0.25/0.5/0.75/0.9 число удалённых вершин, размеры
контура среза в осях кости головы и его смещение от основания черепа.
Нужно, чтобы череп шириной ~0.6 торса перекрывал срез, а не половину шеи.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_head_threshold.py -- Wolf Cow Eagle
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
        group = mesh.vertex_groups.get(roles["head"])
        right = hd.cross(Vector((0.0, 0.0, 1.0)))
        if right.length < 1e-6:
            right = Vector((1.0, 0.0, 0.0))
        right.normalize()
        up = right.cross(hd).normalized()
        print("=" * 70)
        print("SPECIES", item, "head_len=%.3f" % hl)
        for threshold in (0.25, 0.5, 0.75, 0.9):
            bm = bmesh.new()
            bm.from_mesh(mesh.data)
            layer = bm.verts.layers.deform.active
            doomed = []
            if layer is not None:
                for vert in bm.verts:
                    if vert[layer].get(group.index, 0.0) > threshold:
                        doomed.append(vert)
            if not doomed:
                print("  thr=%.2f removed=0" % threshold)
                bm.free()
                continue
            bmesh.ops.delete(bm, geom=doomed, context="VERTS")
            edges = [e for e in bm.edges if len(e.link_faces) == 1]
            points = [mesh.matrix_world @ v.co for e in edges for v in e.verts]
            result = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
            boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
            if points:
                values = [[], [], []]
                for p in points:
                    rel = p - h0
                    values[0].append(rel.dot(right))
                    values[1].append(rel.dot(up))
                    values[2].append(rel.dot(hd))
                size = tuple(round(max(v) - min(v), 3) for v in values)
                center = tuple(round((max(v) + min(v)) * 0.5, 3)
                               for v in values)
            else:
                size = center = None
            print("  thr=%.2f removed=%3d edges=%3d filled=%2d boundary=%d "
                  "size=%s center=%s" % (
                      threshold, len(doomed), len(edges),
                      len(result.get("faces", [])), boundary, size, center))
            bm.free()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
