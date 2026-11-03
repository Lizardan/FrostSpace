"""Проверка целостности мешей: дырки, non-manifold и перевёрнутые нормали.

Три вопроса к каждому мешу:

1. **Дырки.** Ребро, у которого одна грань вместо двух, — открытая граница.
   У земли клетки такая граница — это шов с соседней клеткой, и она по замыслу;
   у коробки это дыра в геометрии.
2. **Non-manifold.** Ребро, у которого больше двух граней, — меш нельзя
   построить вокруг: при заливке и при физике он ведёт себя непредсказуемо.
3. **Перевёрнутые нормали.** Признак без знаковых соглашений: **куда смотрит
   верхняя поверхность меша**. Если верхние грани направлены вниз, меш
   отрисован наизнанку: с backface culling сверху видна только его изнанка
   (или ничего), и такой меш «прячется» под землёй или просвечивает насквозь.

Проверка верхней поверхности не опирается на знак объёма, потому что знак
меняется от того, как чтец (Blender, Unity, FBX) переворачивает оси при
импорте: один и тот же меш в разных чтецах имеет противоположный знак.

Запуск:
    blender --background --factory-startup --python check_mesh_integrity.py -- <file.fbx> [...]
    blender --background --factory-startup --python check_mesh_integrity.py -- --dir <папка>
"""

from __future__ import annotations

import os
import sys

import bmesh
import bpy

#: Насколько ниже самой верхней грани считать «верхней поверхностью», м.
TOP_BAND = 0.05


def _shells(bm):
    """Связные оболочки меша: каждая проверяется отдельно."""
    out = []
    seen = set()
    for f in bm.faces:
        if f.index in seen:
            continue
        stack = [f]
        seen.add(f.index)
        faces = []
        while stack:
            cur = stack.pop()
            faces.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index)
                        stack.append(nf)
        out.append(faces)
    return out


def mesh_report(obj) -> list:
    """Проблемы меша: перевёрнутый верх, дырки, non-manifold."""
    me = obj.data
    mw = obj.matrix_world
    scale = mw.to_scale()
    # Метры: FBX клеток приходит в сантиметрах, и без масштаба «верх» и «низ»
    # считались бы в разных единицах.
    unit = max(abs(scale.x), abs(scale.y), abs(scale.z)) or 1.0

    bm = bmesh.new()
    bm.from_mesh(me)
    issues = []
    for faces in _shells(bm):
        boundary = 0
        non_manifold = 0
        for f in faces:
            for e in f.edges:
                n = len(e.link_faces)
                if n == 1:
                    boundary += 1
                elif n > 2:
                    non_manifold += 1
        tris = sum(max(len(f.verts) - 2, 0) for f in faces)
        if non_manifold:
            issues.append("non-manifold рёбер: %d" % non_manifold)
        if boundary:
            issues.append("открытая граница: %d рёбер (%d трис)" % (boundary, tris))
    bm.free()

    # Верхняя поверхность: где смотрят самые высокие грани.
    up = 0
    down = 0
    top = None
    for poly in me.polygons:
        cen = mw @ poly.center
        if top is None or cen.z > top:
            top = cen.z
    band = TOP_BAND * unit
    for poly in me.polygons:
        cen = mw @ poly.center
        if top is None or cen.z < top - band:
            continue
        nrm = (mw.to_3x3() @ poly.normal).normalized()
        if nrm.z > 0.5:
            up += 1
        elif nrm.z < -0.5:
            down += 1
    if down > up and down > 0:
        issues.append("ВЕРХ СМОТРИТ ВНИЗ (верхних граней вниз %d, вверх %d) — меш вывернут"
                      % (down, up))
    return issues


def check_file(path: str) -> list:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    problems = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        issues = mesh_report(obj)
        if issues:
            problems.append("%s: %s" % (obj.name, "; ".join(issues)))
    return problems


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после -- или --dir <папка>")
        return 2
    if argv[0] == "--dir":
        root = argv[1]
        files = []
        for dirpath, _dirs, names in os.walk(root):
            for n in sorted(names):
                if n.lower().endswith(".fbx"):
                    files.append(os.path.join(dirpath, n))
    else:
        files = argv

    bad = 0
    for path in files:
        problems = check_file(path)
        base = os.path.basename(path)
        if problems:
            bad += 1
            print("ПРОБЛЕМА %s" % base)
            for p in problems:
                print("   - %s" % p)
        else:
            print("OK       %s" % base)
    print()
    print("Файлов с проблемами: %d из %d" % (bad, len(files)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
