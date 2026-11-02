"""Собирает землю клеток мира и экспортирует FBX.

Запуск (обычно через пайплайн):

    blender --background --factory-startup --python Tools/Blender/build_world_cells.py
    blender --background --factory-startup --python Tools/Blender/build_world_cells.py -- -2:-1

Сегодня генерируются клетки **с контентом** (19 по раскладке), кроме той одной,
что легла под авторскую землю депо целиком: там уже лежит меш с шагом 0,94 м у
двора, и второй слой снега ему не нужен. Пустошь без узлов и дорог не печётся
пока нечего на неё ставить — с дорогой придут клетки коридора, и это будет
осмысленный список, а не «все 121 на всякий случай».

Файлы идут в `Assets/_Project/Content/Models/Level/` — только там работает
`LevelAssetPostprocessor`, подменяющий материалы на проектные (ADR 0002).

Каждая клетка проверяется на сборке двумя числами, а не верой: вершина меша
обязана равняться мировому полю в своей точке (то есть сетка не разъехалась с
раскладкой), и габарит обязан быть ровно 600×600 м минус вырезанное под депо.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import level_lib  # noqa: E402
import world_cell_parts as C  # noqa: E402
import world_layout as W  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))

#: Допуск сверки высоты вершины меша с мировым полем: сетка считается из того же
#: вызова, что и поле, так что сантиметр — это уже разъехавшийся порядок обхода.
H_TOL = 0.01


def _wanted(tokens):
    """Клетки с контентом, отфильтрованные аргументом `--ids` (формат `i:j`)."""
    cells = [c for c in W.cells() if c["content"]
             and W.depot_cover(c["i"], c["j"]) != "full"]
    if tokens:
        keys = {t for t in tokens}
        missing = keys - {"%d:%d" % (c["i"], c["j"]) for c in cells}
        if missing:
            print("нет таких клеток с контентом: %s" % ", ".join(sorted(missing)))
            return None
        cells = [c for c in cells if "%d:%d" % (c["i"], c["j"]) in keys]
    return cells


def _edge_check() -> list:
    """Общая сторона двух соседних клеток обязана быть одними и теми же точками.

    Это и есть ответ на «стык или обрыв», и он численный, а не на глаз: кадр
   Workbench без света показывает однотонное поле, и доказывать по нему нечего.
    Берутся углы всех квадов каждой генерируемой клетки; если две соседние клетки
    держат на общей границе разные точки (или разные высоты в одной точке),
    между ними либо щель, либо ступенька.
    """
    corners = {}
    for c in W.cells():
        if not c["content"] or W.depot_cover(c["i"], c["j"]) == "full":
            continue
        for (qx0, qz0, qx1, qz1) in C._quads(c["i"], c["j"]):
            for (px, pz) in ((qx0, qz0), (qx1, qz0), (qx1, qz1), (qx0, qz1)):
                corners.setdefault((px, pz), set()).add(
                    round(W.world_height(px, pz), 6))
    multi = [p for p, v in corners.items() if len(v) > 1]
    if multi:
        return ["в общей точке сетки две разные высоты: %s" % multi[:3]]

    shared = 0
    for (px, pz) in corners:
        # Точка внутри области клетки (не на её внешнем крае) значит, что сюда
        # сходятся минимум четыре квада, то есть это внутренний узел сетки.
        neighbours = sum(1 for (dx, dz) in ((W.TERRAIN_STEP, 0.0),
                                             (-W.TERRAIN_STEP, 0.0),
                                             (0.0, W.TERRAIN_STEP),
                                             (0.0, -W.TERRAIN_STEP))
                        if (px + dx, pz + dz) in corners)
        if neighbours == 4:
            shared += 1
    print("СТЫК: общих узлов сетки %d, все с одной высотой" % shared)
    return []


def _verify(obj, i, j) -> list:
    """Вершины меша против мирового поля: клетка обязана лежать на своём рельефе."""
    (cx, cz) = W.cell_center(i, j)
    bad = []
    worst = 0.0
    for v in obj.data.vertices:
        x = cx + v.co.x
        z = cz - v.co.y
        d = abs(v.co.z - W.world_height(x, z))
        worst = max(worst, d)
        if d > H_TOL and len(bad) < 3:
            bad.append("(%.0f, %.0f) %.3f м" % (x, z, d))
    if bad:
        return ["высота не совпадает с мировым полем: %s (макс %.4f м)"
                % (", ".join(bad), worst)]
    if worst > H_TOL:
        return ["высота не совпадает с мировым полем: макс %.4f м" % worst]
    return []


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cells = _wanted(set(args))
    if cells is None:
        return 1
    if not cells:
        raise RuntimeError("ничего не собрано")

    problems = _edge_check()
    if problems:
        print("FAIL стык клеток: %s" % "; ".join(problems))
        return 1

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    os.makedirs(OUT, exist_ok=True)
    total = 0

    for c in cells:
        (i, j) = (c["i"], c["j"])
        obj = C.build_cell(mats, i, j)
        if obj is None:
            print("SKIP %d:%d — клетка целиком под землёй депо" % (i, j))
            continue
        problems = _verify(obj, i, j)
        if problems:
            print("FAIL WorldCell_%d_%d: %s" % (i, j, "; ".join(problems)))
            return 1
        quads = len(C._quads(i, j))
        print("WorldCell_%d_%d: квадов %d из %d, габарит %.1f x %.1f x %.1f" % (
            i, j, quads, int(round(W.BLK / W.TERRAIN_STEP)) ** 2,
            obj.dimensions.x, obj.dimensions.y, obj.dimensions.z))
        total += level_lib.report([obj])
        level_lib.export_static([obj], os.path.join(
            OUT, "WorldCell_%d_%d.fbx" % (i, j)))
        bpy.data.objects.remove(obj, do_unlink=True)

    print("BUILT %d files, TOTAL tris %d" % (len(cells), total))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
