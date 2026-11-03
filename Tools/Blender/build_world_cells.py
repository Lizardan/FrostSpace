"""Собирает клетки мира и экспортирует их единым префабом: земля плюс транспорт.

Запуск (обычно через пайплайн):

    blender --background --factory-startup --python Tools/Blender/build_world_cells.py
    blender --background --factory-startup --python Tools/Blender/build_world_cells.py -- -2:-1

Печатаются **все 121 клетка карты без исключений**. Раньше одна пропускалась —
она легла под квадрат авторской земли депо, и второй слой снега ей был не нужен.
С тех пор как депо стало обычным чанком, исключение исчезло вместе с землёй,
которая его породила: чанк имеет смысл, только когда он одинаков для всей карты,
а «любой кусок карты — префаб» иначе превращается в оговорку. Объём посчитан
заранее — ADR 0036: 121 клетка это 96 800 трис террейна, в памяти при радиусе 1
живёт 7200.

В файле клетки живёт всё, что в неё входит: земля (`world_cell_parts`), путь,
шпалы, путевые знаки и дороги (`transport_parts`), а в клетке депо — ещё и двор
с боковым путём (`depot_parts`). Один файл — один префаб — одна кнопка выгрузки:
править перегон, станцию или депо значит открыть один префаб, а не искать по
сцене пять объектов.

Файлы идут в `Assets/_Project/Content/Models/Level/` — только там работает
`LevelAssetPostprocessor`, подменяющий материалы на проектные (ADR 0002).

Числа, которыми клетка сдаётся, а не верой:

* вершина земли обязана равняться `world_height` в своей точке (допуск 1 см);
* в общей точке сетки у обоих соседей обязана быть одна высота — это стык чанков;
* узел решётки на дне коридора обязан лежать на столе: это доказательство, что
  рельеф уступил дороге, а не дорога рельефу.
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
import depot_parts as DP  # noqa: E402
import settlement_parts as SP  # noqa: E402
import transport_parts as TP  # noqa: E402
import world_cell_parts as C  # noqa: E402
import world_layout as W  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Level"))

#: Допуск сверки высоты вершины меша с мировым полем: сетка считается из того же
#: вызова, что и поле, так что сантиметр — это уже разъехавшийся порядок обхода.
H_TOL = 0.01

#: Допуск «рельеф уступил коридору»: дно выемки обязано совпасть со столом.
#: Полумиллиметра хватает, потому что оба числа берутся из одного вызова.
FLOOR_TOL = 0.0005


def _wanted(tokens):
    """Клетки карты, отфильтрованные аргументом `--ids` (формат `i:j`)."""
    cells = list(W.cells())
    if tokens:
        keys = {t for t in tokens}
        missing = keys - {"%d:%d" % (c["i"], c["j"]) for c in cells}
        if missing:
            print("нет таких клеток: %s" % ", ".join(sorted(missing)))
            return None
        cells = [c for c in cells if "%d:%d" % (c["i"], c["j"]) in keys]
    return cells


def _edge_check() -> list:
    """Общая сторона двух соседних клеток обязана быть одними и теми же точками.

    Это и есть ответ на «стык или обрыв», и он численный, а не на глаз: кадр
    Workbench без света показывает однотонное поле, и доказывать по нему нечего.
    Берутся углы всех квадов каждой клетки; если две соседние клетки держат на
    общей границе разные точки (или разные высоты в одной точке), между ними либо
    щель, либо ступенька.
    """
    corners = {}
    for c in W.cells():
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


def _corridor_check() -> list:
    """Рельеф обязан уступить коридору: узел сетки на дне выемки лежит на столе.

    Проверка живёт здесь, а не в `invariants()`, потому что мерит она поле в
    точке генерации: стол — функция раскладки, `world_height` — та функция, из
    которой строится меш. Если порядок слоёв когда-нибудь поедет (сначала
    коридор, потом заносы — ровно та ошибка, что уже была найдена в
    `plate_height`), здесь полезут сантиметры, и поймаются они до экспорта.

    Точки под площадкой узла тоже меряются: коридор режет двор, а не двор
    перекрывает коридор, и именно это проверка и ловит, если порядок слоёв
    поедет обратно.
    """
    bad = []
    checked = 0
    onpad = 0
    step = W.TERRAIN_STEP
    for c in W.cells():
        (x0, x1, z0, z1) = W.cell_bounds(c["i"], c["j"])
        px = x0
        while px <= x1 + 1e-6:
            pz = z0
            while pz <= z1 + 1e-6:
                f = W.transport_floor(px, pz)
                if f is not None and f["mask"] > 0.999:
                    checked += 1
                    if W.pad_blend(px, pz) > 1e-6:
                        onpad += 1
                    # Ждём не голое дно, а смешанную отметку: доля 0,999 — это
                    # не единица, и плита вносит оставшийся миллиметр. Сверять
                    # с дном в допуске полмиллиметра значит ловить собственную
                    # формулу смешивания, а не дефект рельефа.
                    floor = f["level"] - W.CUT_DEPTH[f["kind"]]
                    plate = W.pad_height(px, pz)
                    want = plate + (floor - plate) * f["mask"]
                    d = abs(W.world_height(px, pz) - want)
                    if d > FLOOR_TOL and len(bad) < 4:
                        bad.append("(%.0f, %.0f) %.4f м от дна коридора"
                                   % (px, pz, d))
                pz += step
            px += step
    print("КОРИДОР: узлов на дне %d, все на отметке стола минус постель; из них "
          "%d внутри тёплых площадок" % (checked, onpad))
    return bad


def _verify(obj, i, j) -> list:
    """Вершины земли против мирового поля: клетка обязана лежать на своём рельефе."""
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


def _bounds_check(pieces, label: str, i: int, j: int) -> list:
    """Узел обязан влезть в клетку и стоять на её полу.

    Критерий приёмки «депо целиком внутри клетки (−2,−2)» до этой проверки был
    словом, а не числом: чанк клетки с габаритом 600×600 м и постройкой,
    выставленной за ребро, выглядит в редакторе нормально, а на стыке с
    соседней клеткой даёт висящий в воздухе фасад. Меряются вершины всех
    объектов узла, а не только земли: земля в клетку ложится по построению,
    а постройка — нет.
    """
    bad = []
    half = W.BLK * 0.5 + 0.5      # полметра допуска: вершина на ребре клетки
    # есть вершина на ребре, и в float это 300,0000001, а не 300.
    # Отметка самой высокой точки рельефа именно ЭТОЙ клетки: от неё меряется,
    # насколько далеко вверх уходят постройки. Двор наклонён вдоль линии,
    # поэтому «пол двора» — не одно число, и верхняя граница честнее нижней.
    # Центр берётся из индексов, а не из нуля сцены: у депо они совпадают, у
    # поселения — нет, и прошлый вариант мерил рельеф депо для чужой клетки.
    (cx, cz) = W.cell_center(i, j)
    hi = max(W.world_height(cx + lx, cz + lz)
             for lx in (-300.0, 0.0, 300.0) for lz in (-300.0, 0.0, 300.0))
    reach = 0.0
    for obj in pieces:
        for v in obj.data.vertices:
            if abs(v.co.x) > half or abs(v.co.y) > half:
                bad.append("%s: вершина %.1f, %.1f м вне клетки"
                           % (obj.name, v.co.x, v.co.y))
                if len(bad) > 2:
                    return bad[:3]
            reach = max(reach, v.co.z - hi)
    if reach > 12.0:
        bad.append("%s поднимается над рельефом на %.1f м — выше любого "
                   "габарита узла" % (label, reach))
    print("%s: объектов %d, вершин %d, над верхней точкой рельефа максимум "
          "%.1f м, габарит клетки %.0f x %.0f м" % (
              label.upper(), len(pieces),
              sum(len(o.data.vertices) for o in pieces), reach, W.BLK, W.BLK))
    return bad


def main() -> int:
    level_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cells = _wanted(set(args))
    if cells is None:
        return 1
    if not cells:
        raise RuntimeError("ничего не собрано")

    problems = _edge_check() + _corridor_check()
    if problems:
        print("FAIL клетка: %s" % "; ".join(problems))
        return 1

    level_lib.clear_scene()
    mats = level_lib.make_level_materials()
    os.makedirs(OUT, exist_ok=True)
    total = 0

    for c in cells:
        (i, j) = (c["i"], c["j"])
        ground = C.build_cell(mats, i, j)
        problems = _verify(ground, i, j)
        if problems:
            print("FAIL WorldCell_%d_%d: %s" % (i, j, "; ".join(problems)))
            return 1
        pieces = [ground] + TP.build_cell_transport(mats, i, j)
        depot = DP.build_cell_depot(mats, i, j)
        pieces += depot
        if depot:
            problems = _bounds_check(depot, "депо", i, j)
            if problems:
                print("FAIL депо: %s" % "; ".join(problems))
                return 1
        hut = SP.build_cell_settlement(mats, i, j)
        pieces += hut
        if hut:
            problems = _bounds_check(hut, "поселение", i, j)
            if problems:
                print("FAIL поселение: %s" % "; ".join(problems))
                return 1
        tris = sum(len(p.vertices) - 2 for o in pieces for p in o.data.polygons)
        print("WorldCell_%d_%d: квадов %d, объектов %d, трис %d%s" % (
            i, j, len(C._quads(i, j)), len(pieces), tris,
            " · контент" if c["content"] else ""))
        total += level_lib.report(pieces)
        level_lib.export_static(pieces, os.path.join(
            OUT, "WorldCell_%d_%d.fbx" % (i, j)))
        for obj in pieces:
            me = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if me is not None and me.users == 0:
                bpy.data.meshes.remove(me)   # не копить сотни осиротевших меш-блоков

    print("BUILT %d файлов, TOTAL трис %d" % (len(cells), total))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
