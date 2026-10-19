"""Проверка «здоровья» мешей робо-животных: щели, изнанка, пустые места.

Отчёт по каждому мешу FBX:
- открытые границы (`boundary_edges`) и их контуры: где именно дыра;
- неманифолдные рёбра (граница из 3+ граней);
- грани нулевой площади с материалом и координатами — «мусор», который
  в Unity может читаться как артефакт;
- замкнутые оболочки с отрицательным объёмом — вывернутые наружу изнанкой;
- вершины без веса и группы без костей (деталь «зависнет» в rest-позе);
- габарит меша.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_mesh_health.py -- <file.fbx> [<file.fbx> ...]

Код возврата 1, если найден хотя бы один дефект уровня ERROR: открытая
граница у меша, который обязан быть замкнутым, вывернутая оболочка,
вершины без веса. Это позволяет использовать пробу как гейт приёмки.
"""

from __future__ import annotations

import os
import sys
import traceback

import bmesh
import bpy
from mathutils import Vector

ERRORS = []


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def bbox(points):
    lo = Vector((min(p.x for p in points), min(p.y for p in points),
                 min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points),
                 max(p.z for p in points)))
    return lo, hi


def face_components(bm):
    """Связные компоненты граней: список списков BMFace."""
    seen = set()
    result = []
    for face in bm.faces:
        if face.index in seen:
            continue
        stack = [face]
        seen.add(face.index)
        group = []
        while stack:
            cur = stack.pop()
            group.append(cur)
            for edge in cur.edges:
                for neighbor in edge.link_faces:
                    if neighbor.index not in seen:
                        seen.add(neighbor.index)
                        stack.append(neighbor)
        result.append(group)
    return result


def boundary_loops(bm, boundary_edges):
    """Собрать открытые рёбра в контуры (по связности вершин)."""
    by_vert = {}
    for edge in boundary_edges:
        for vert in edge.verts:
            by_vert.setdefault(vert.index, []).append(edge)
    seen = set()
    loops = []
    for edge in boundary_edges:
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
    return loops


def report_mesh(obj) -> None:
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    boundary = [e for e in bm.edges if len(e.link_faces) == 1]
    non_manifold = [e for e in bm.edges if len(e.link_faces) > 2]
    loose = [e for e in bm.edges if not e.link_faces]
    zero = [f for f in bm.faces if f.calc_area() < 1e-9]
    print("MESH", obj.name, "verts=%d" % len(mesh.vertices),
          "faces=%d" % len(mesh.polygons),
          "boundary_edges=%d" % len(boundary),
          "non_manifold=%d" % len(non_manifold),
          "loose=%d" % len(loose),
          "zero_area=%d" % len(zero))
    slots = [m.name if m else "<None>" for m in mesh.materials]
    if any(m is None for m in mesh.materials):
        ERRORS.append("%s: есть слот без материала" % obj.name)
        print("  ERROR empty material slot")
    used = {}
    for poly in mesh.polygons:
        used[poly.material_index] = used.get(poly.material_index, 0) + 1
    for index, name in enumerate(slots):
        print("  slot %2d %-30s faces=%d" % (index, name,
                                             used.get(index, 0)))
    out_of_range = [i for i in used if i >= len(slots) or i < 0]
    if out_of_range:
        ERRORS.append("%s: индексы материалов вне диапазона %s" %
                      (obj.name, out_of_range))
        print("  ERROR out_of_range material indices", out_of_range)

    for loop in boundary_loops(bm, boundary):
        points = [v.co for e in loop for v in e.verts]
        lo, hi = bbox(points)
        closed = all(len(v.link_edges) > 0 for e in loop for v in e.verts)
        print("  boundary loop edges=%-3d closed=%s center=%s size=%s" % (
            len(loop), closed,
            tuple(round(v, 3) for v in ((lo + hi) * 0.5)),
            tuple(round(v, 3) for v in (hi - lo))))
        ERRORS.append("%s: открытая граница (%d рёбер) в %s" % (
            obj.name, len(loop),
            tuple(round(v, 2) for v in ((lo + hi) * 0.5))))

    if zero:
        groups = {}
        for face in zero:
            mat = slots[face.material_index] if 0 <= face.material_index < len(slots) else "?"
            groups[mat] = groups.get(mat, 0) + 1
        print("  zero_area by material:", groups)
        for face in zero[:5]:
            print("    zero face mat=%s center=%s" % (
                slots[face.material_index]
                if 0 <= face.material_index < len(slots) else "?",
                tuple(round(v, 3) for v in face.calc_center_median())))

    for index, group in enumerate(face_components(bm)):
        closed = all(len(e.link_faces) == 2 for f in group for e in f.edges)
        if not closed:
            continue
        volume = sum(f.calc_area() * f.normal.dot(f.calc_center_median())
                     for f in group) / 3.0
        if volume < -1e-9:
            points = [v.co for f in group for v in f.verts]
            lo, hi = bbox(points)
            ERRORS.append("%s: вывернутая оболочка #%d volume=%.5f" % (
                obj.name, index, volume))
            print("  ERROR inverted shell #%d volume=%.5f center=%s size=%s"
                  % (index, volume,
                     tuple(round(v, 3) for v in ((lo + hi) * 0.5)),
                     tuple(round(v, 3) for v in (hi - lo))))

    unweighted = sum(1 for v in mesh.vertices if not v.groups)
    print("  unweighted_verts=%d groups=%d" % (unweighted,
                                               len(obj.vertex_groups)))
    if unweighted:
        ERRORS.append("%s: %d вершин без веса" % (obj.name, unweighted))

    lo, hi = bbox([obj.matrix_world @ v.co for v in bm.verts])
    print("  bbox center=%s size=%s" % (
        tuple(round(v, 3) for v in ((lo + hi) * 0.5)),
        tuple(round(v, 3) for v in (hi - lo))))
    bm.free()


def check_bone_groups(obj, arm) -> None:
    if arm is None:
        return
    bones = {bone.name for bone in arm.data.bones}
    unknown = sorted(g.name for g in obj.vertex_groups if g.name not in bones)
    if unknown:
        ERRORS.append("%s: группы без костей %s" % (obj.name, unknown))
        print("  ERROR groups without bones", unknown)


def main() -> int:
    configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    files = [os.path.abspath(a) for a in args if not a.startswith("--")]
    if not files:
        print("укажи FBX после --")
        return 2
    for path in files:
        print("=" * 70)
        print("FILE", path)
        reset()
        bpy.ops.import_scene.fbx(filepath=path)
        arm = next((o for o in bpy.context.scene.objects
                    if o.type == "ARMATURE"), None)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        print("ARMATURE", arm.name if arm else "<none>",
              "meshes=%d" % len(meshes))
        for obj in sorted(meshes, key=lambda o: o.name):
            report_mesh(obj)
            check_bone_groups(obj, arm)
    print("=" * 70)
    if ERRORS:
        print("RESULT FAIL errors=%d" % len(ERRORS))
        for message in ERRORS:
            print(" -", message)
        return 1
    print("RESULT PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
