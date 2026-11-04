"""Проверяет инварианты края мира: раскладку, рельеф, путь и меши после сборки.

Запуск (штатно — из `build_all.py --asset frontier --check`, per = file):

    blender --background --factory-startup --python Tools/Blender/check_frontier.py -- \
        "Assets/_Project/Content/Models/Level/Frontier_Track.fbx"

Здесь проверяется то, что глазами на превью не ловится и что нельзя починить
«на глаз потом»:

* раскладка — ни один кластер, пролёт или указатель не встал на путь, на
  платформу, в ворота или поперёк коридора дороги (`in_keep_out`);
* рельеф — под депо плоско, дно выемки на одной отметке на всех 340 м, шпала
  целиком лежит на дне, а южный конец линии тонет в тумане (96 %), то есть
  обрыва не видно не «на глаз», а по формуле тумана Unity Exp²;
* меши — габариты против таблицы, головка рельса на y = 0, борт заноса выше
  площадки, минимальное тело не тоньше 6 см и нет копланарных пар граней из
  разных оболочек (те же причины мерцания, что у вагона).

Ошибка — ненулевой код возврата, как у остальных `check_*` пайплайна.
"""

from __future__ import annotations

import math
import os
import re
import sys
import traceback

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import check_train  # noqa: E402
import frontier_layout as L  # noqa: E402
import world_layout as W  # noqa: E402
import lowpoly  # noqa: E402

#: Плотность тумана, с которой собран край мира (`RenderSettings.fogColor` и
#: `m_FogDensity` в `Frontier.unity`). Проверка южного конца линии считается
#: по той же формуле, что и движок: Exp², visibility = exp(-(d·density)^2).
FOG_DENSITY = 0.006

#: Что обязан показать меш линии: головка рельса, гребень борта, верх знака.
RAIL_HEAD_TOL = 0.001
MIN_DIM = 0.06              # «ничего тоньше 6 см» из docs/STYLE.md
MIN_DIM_SHELL_TOL = 0.005   # допуск на фаску и плоские грани


def fog_visibility(distance: float) -> float:
    """Доля цвета, которую съел туман, по формуле Unity ExponentialSquared."""
    return 1.0 - math.exp(-((distance * FOG_DENSITY) ** 2))


def _check_layout(problems: list[str]) -> None:
    """Раскладка против запретных зон и против самой себя."""
    x0, x1, z0, z1 = L.DEPOT_PAD
    worst = 0.0
    for i in range(0, 41):
        for j in range(0, 41):
            x = x0 + (x1 - x0) * i / 40.0
            z = z0 + (z1 - z0) * j / 40.0
            if L.corridor_mask(x, z) > 0.0:
                continue
            worst = max(worst, abs(L.ground_height(x, z) - L.PAD_Y))
    if worst > L.PAD_FLAT_TOL:
        problems.append("под депо не плоско: отклонение %.3f м > %.3f"
                        % (worst, L.PAD_FLAT_TOL))

    # Дно выемки — одна отметка на всём протяжении, иначе прямой путь либо
    # висит над прогибом, либо тонет в гребне.
    drift = 0.0
    for i in range(0, 181):
        z = L.TRACK_MAIN_SOUTH + (L.TRACK_MAIN[-1][1] - L.TRACK_MAIN_SOUTH) * i / 180.0
        drift = max(drift, abs(L.ground_height(L.TRACK_X, z) - L.TRACK_FLOOR_Y))
    if drift > 0.005:
        problems.append("дно коридора гуляет на %.3f м" % drift)

    # Шпала длиной 8.4 м обязана целиком лежать на плоском дне.
    half = L.SLEEPER_SIZE[0] * 0.5 - 0.1
    for (cx, cz) in L.TRACK_MAIN + L.TRACK_STUB:
        for edge in (cx - half, cx + half):
            if L.corridor_mask(edge, cz) < 1.0:
                problems.append("край шпалы (%.2f, %.2f) вне плоского дна"
                                % (edge, cz))

    for (name, x, z, _yaw) in L.SCENE_INSTANCES:
        if L.in_keep_out(x, z):
            problems.append("%s в (%.0f, %.0f) стоит в запретной зоне"
                            % (name, x, z))
    for (_a, x, z, _yaw) in L.telegraph_instances():
        if L.in_track_cut(x, z):
            problems.append("пролёт в (%.1f, %.1f) стоит в выемке пути"
                            % (x, z))

    # Обрыв южного конца не виден: 95 % тумана на дальности конца линии.
    vis = fog_visibility(abs(L.TRACK_MAIN_SOUTH))
    if vis < 0.95:
        problems.append("южный конец линии виден: туман %.0f %% < 95 %%"
                        % (vis * 100.0))


def _shells(obj):
    """Границы связных оболочек импортированного меша, в метрах.

    `matrix_world` уже содержит масштаб импорта (FBX приходит в сантиметрах с
    scale 0.01 на объекте), поэтому второй раз масштаб не применяем.
    """
    out = []
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    islands = check_train._islands(bm)
    groups: dict[int, list] = {}
    for face in bm.faces:
        groups.setdefault(islands[face.index], []).append(face)
    for faces in groups.values():
        lo = [1e9, 1e9, 1e9]
        hi = [-1e9, -1e9, -1e9]
        for face in faces:
            for vert in face.verts:
                p = obj.matrix_world @ vert.co
                for k, value in enumerate((p.x, p.y, p.z)):
                    lo[k] = min(lo[k], value)
                    hi[k] = max(hi[k], value)
        out.append((lo, hi))
    bm.free()
    return out


def to_unity(p: Vector) -> Vector:
    """Точка мира Blender -> координаты Unity.

    Импортёр Blender не снимает зеркало по X, которое `export_static` ставит
    навстречу `bake_space_transform`: в Blender меш приходит отражённым
    относительно того, что увидит Unity (проверено на `TrainCar`: локальный
    x ∈ [-4.80, +4.09] против юнитовского [-4.09, +4.80]). Поэтому все
    численные проверки пути и земли считаются здесь в осях Unity — иначе
    проверка «путь на x = -8» ловила бы собственное зеркало.
    """
    return Vector((-p.x, p.z, -p.y))


def unity_bounds(obj) -> tuple[Vector, Vector]:
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    bpy.context.view_layer.update()
    for corner in obj.bound_box:
        u = to_unity(obj.matrix_world @ Vector(corner))
        lo = Vector((min(lo.x, u.x), min(lo.y, u.y), min(lo.z, u.z)))
        hi = Vector((max(hi.x, u.x), max(hi.y, u.y), max(hi.z, u.z)))
    return lo, hi


def unity_heights(obj) -> list:
    """Высоты (Unity y) всех вершин меша, в метрах."""
    bpy.context.view_layer.update()
    mw = obj.matrix_world
    return [(mw @ v.co).z for v in obj.data.vertices]


def _check_track(problems, meshes) -> None:
    line = meshes.get("Track_Line")
    if line is None:
        problems.append("нет объекта Track_Line")
        return
    lo, hi = unity_bounds(line)
    # Южный конец — за проектной отметкой (там линия тонет в тумане), северный
    # — за буферным упором, но не дальше борта заноса на стрелке.
    check_train.check_range("line.south", lo.z, L.TRACK_MAIN_SOUTH - 0.6,
                            L.TRACK_MAIN_SOUTH + 0.1, problems)
    check_train.check_range("line.north", hi.z, 26.0, 32.0, problems)
    # Западная кромка — угол борта заноса на стрелке: упор на x = -26.5, а
    # нормаль последней ветки уводит внешний край постели до -32.5.
    check_train.check_range("line.west", lo.x, -33.0, -32.0, problems)
    check_train.check_range("line.east", hi.x, -0.9, 0.0, problems)

    zs = unity_heights(line)
    head = [z for z in zs if abs(z - L.RAIL_HEAD_Y) <= RAIL_HEAD_TOL]
    if len(head) < 8:
        problems.append("головки рельса нет в меше (вершин на y=0: %d)"
                        % len(head))
    crest = [z for z in zs if abs(z - L.BANK_TOP_Y) <= 0.002]
    if len(crest) < 8:
        problems.append("борта заноса не выше площадки (вершин на y=%.2f: %d)"
                        % (L.BANK_TOP_Y, len(crest)))
    if hi.y > 1.6:
        problems.append("что-то выше 1.6 м над рельсом: %.2f" % hi.y)
    # Самое глубокое в линии — не постель (-0.78), а стойка путевого знака:
    # она уходит в снег на -0.85, чтобы не висеть на внешнем склоне борта.
    if lo.y < -0.90:
        problems.append("что-то глубже -0.90: %.2f" % lo.y)
    if abs(lo.y - (-0.85)) > 0.06:
        problems.append("низ линии не на стойке знака: %.2f" % lo.y)
    term = meshes.get("Track_Terminus")
    if term is None:
        problems.append("нет объекта Track_Terminus")
        return
    tlo, thi = unity_bounds(term)
    if not (tlo.x < L.BUFFER_STOP[0] < thi.x):
        problems.append("буферный упор не над точкой раскладки по x")
    if not (tlo.z < L.BUFFER_STOP[1] < thi.z):
        problems.append("буферный упор не над точкой раскладки по z")


def _check_ground(problems, meshes) -> None:
    ground = meshes.get("Frontier_Ground")
    if ground is None:
        problems.append("нет объекта Frontier_Ground")
        return
    lo, hi = unity_bounds(ground)
    check_train.check_range("ground.size.x", hi.x - lo.x, 1199.0, 1201.0,
                            problems)
    check_train.check_range("ground.size.z", hi.z - lo.z, 1199.0, 1201.0,
                            problems)

    # Земля депо обязана быть мировым полем высот, а не полем сцены: край
    # авторского квадрата стыкуется с клетками, и расхождение в 8-31 м было бы
    # обрывом, а не швом. Проверка покрывает вершины меша по всей площади.
    bpy.context.view_layer.update()
    mw = ground.matrix_world
    worst = 0.0
    where = ("", 0.0, 0.0)
    floor = 1e9
    crest = -1e9
    for v in ground.data.vertices:
        u = to_unity(mw @ v.co)
        want = W.world_height(*W.to_world(u.x, u.z))
        d = abs(u.y - want)
        if d > worst:
            worst = d
            where = ("вершина", u.x, u.z)
        r = math.hypot(u.x, u.z)
        # Дно и гребень меряются там, где они определены раскладкой: коридор
        # пути и стена терминуса лежат внутри 300 м. За этой границей начинается
        # макрельеф плиты, и его пики не имеют отношения ни к тому, ни к другому.
        if abs(u.x - L.TRACK_X) < 12.0 and r < 300.0:
            floor = min(floor, u.y)
        if r < 300.0:
            crest = max(crest, u.y)
    if worst > 0.02:
        problems.append("земля депо не совпадает с мировым полем: %.3f м в (%.1f, %.1f)"
                        % (worst, where[1], where[2]))
    check_train.check_range("ground.floor", floor, L.TRACK_FLOOR_Y - 0.02,
                            L.TRACK_FLOOR_Y + 0.02, problems)
    # Стена терминуса обязана быть выше крыши вагона (y = 4.76), иначе из
    # кадра прибытия видно небо вместо края мира.
    check_train.check_range("ground.crest", crest, 9.6, 12.0, problems)
    if "Frontier_Ground_Collider" not in meshes:
        problems.append("нет прокси-коллайдера Frontier_Ground_Collider")


def _check_min_dim(obj, problems) -> None:
    for lo, hi in _shells(obj):
        dims = [hi[k] - lo[k] for k in range(3)]
        smallest = min(dims)
        if smallest < MIN_DIM - MIN_DIM_SHELL_TOL:
            problems.append("%s: оболочка на (%.1f, %.1f) тоньше стиля: %.3f м"
                            % (obj.name, lo[0], lo[1], smallest))


#: Порог площади грани для проверки копланарности. Дефолт `check_train`
#: (50 см²) выбран для вагона, где faces мелкие. На 340-метровом пути в него
#: попадают 110-см² фаски шпал и торцы рельсов: две грани в одной плоскости
#: в пяти метрах друг от друга, ни одна из которых игрок не видит под одним
#: ракурсом. 0.05 м² — это грань от 22×22 см: мерцает именно она.
COPANAR_MIN_AREA = 0.05

#: Минимальное пятно перекрытия двух граней, при котором копланарность
#: считается видимой: 20 × 20 см.
COPANAR_MIN_PATCH = 0.04


def _patch_area(a, b, scale: float) -> float:
    """Площадь перекрытия двух AABB по двум наибольшим измерениям, м².

    `_overlap` у `check_train` отвечает только «пересеклись ли», и для вагона
    этого довольно: там грани крупные. В лесу две двухметровые грани ярусов
    могут касаться полоской в сантиметр — тест глубины на ней решается
    случайно, но глаз этого не видит. Мерцает пятно, поэтому порог именно
    по площади пятна.
    """
    dims = []
    for axis in range(3):
        span = min(a[1][axis], b[1][axis]) - max(a[0][axis], b[0][axis])
        dims.append(max(span, 0.0) * scale)
    dims.sort()
    return dims[1] * dims[2]


def _faces_overlap_in_plane(fa, fb, normal, eps: float) -> bool:
    """Пересекаются ли сами две копланарные грани, а не их AABB (SAT).

    У скошенной грани AABB заметно шире её самой: две колеи одной дороги на
    изломе дают «пятно» в полметра, хотя узкие полосы разнесены на 2,5 м и не
    касаются друг друга. То же замечание уже есть у `COPANAR_PATCH_SIDE` в
    межобъектной проверке; здесь оно решается честно — проекцией граней в их
    общую плоскость и поиском разделяющей оси. Выпуклости достаточно: все
    грани пайплайна — треугольники и квады.
    """
    seed = Vector((1.0, 0.0, 0.0)) if abs(normal.x) < 0.9 else Vector((0.0, 1.0, 0.0))
    u = seed - normal * seed.dot(normal)
    if u.length < 1e-6:
        return True
    u.normalize()
    v = normal.cross(u).normalized()

    def project(face):
        return [(vert.co.dot(u), vert.co.dot(v)) for vert in face.verts]

    pa, pb = project(fa), project(fb)
    for poly in (pa, pb):
        for k in range(len(poly)):
            (x0, y0) = poly[k]
            (x1, y1) = poly[(k + 1) % len(poly)]
            (ax, ay) = (-(y1 - y0), x1 - x0)
            length = math.hypot(ax, ay)
            if length < 1e-9:
                continue
            ax, ay = ax / length, ay / length
            va = [px * ax + py * ay for (px, py) in pa]
            vb = [px * ax + py * ay for (px, py) in pb]
            if min(va) > max(vb) + eps or min(vb) > max(va) + eps:
                return False
    return True


def coplanar_visible_pairs(obj, tol: float = 0.002,
                           min_area: float = COPANAR_MIN_AREA,
                           min_patch: float = COPANAR_MIN_PATCH):
    """Пары сопадающих граней из разных оболочек **и разных материалов**.

    Отличие от `check_train.coplanar_pairs` одно и оно принципиально: две
    грани одного цвета в одной плоскости мерцают невидимо — чем бы ни
    разрешился тест глубины, пиксель остаётся тем же. Видимый дефект
    а видимый дефект появляется только на стыке разных материалов (хвоя о
    снег, рельс о шпалу), и именно его ловит эта проверка. На кластере из 20
    сосен двухметровые грани ярусов ложатся в одну плоскость — это свойство
    плотного леса, а не баг меша.
    """
    scale = max(abs(s) for s in obj.matrix_world.to_scale()) or 1.0
    area_scale = scale * scale
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    islands = check_train._islands(bm)
    groups: dict = {}
    for face in bm.faces:
        if face.calc_area() * area_scale < min_area:
            continue
        normal = face.normal.copy()
        # Грань, смотрящая вниз, утоплена в грунт или в свою же плиту и не
        # конкурирует за пиксель: межобъектная проверка (`COPANAR_UP`) это
        # правило уже соблюдает, внутримешная — нет, поэтому низы двух ярусов
        # сосен и донца двух колей давали ложные пары. Нормаль переводим в
        # мировые оси: FBX поворачивает меш, и локальный «низ» — не мировой.
        world_normal = (obj.matrix_world.to_3x3() @ normal).normalized()
        if world_normal.z < COPANAR_UP:
            continue
        key = ((round(normal.x, 2), round(normal.y, 2), round(normal.z, 2)),
               round(normal.dot(face.calc_center_median()) * scale / tol),
               face.material_index)
        groups.setdefault(key, []).append(face)
    pairs = []
    for faces in groups.values():
        if len(faces) < 2:
            continue
        boxes = [check_train._aabb(f) for f in faces]
        for i in range(len(faces)):
            for j in range(i + 1, len(faces)):
                if islands[faces[i].index] == islands[faces[j].index]:
                    continue
                if check_train._overlap(boxes[i], boxes[j], tol / scale):
                    if _patch_area(boxes[i], boxes[j], scale) < min_patch:
                        continue
                    if not _faces_overlap_in_plane(faces[i], faces[j],
                                                   faces[i].normal,
                                                   tol / scale):
                        continue
                    pairs.append((faces[i].calc_center_median(),
                                  faces[j].calc_center_median()))
    bm.free()
    return pairs


#: Минимальный зазор между параллельными гранями **разных объектов и разных
#: материалов**: 6 см, тот же порог, что «ни мельче 6 см» в STYLE.md. Пары
#: ближе этого зазора глаз читает как одну плоскость, и тест глубины решает её
#: случайно — это мерцание, а не деталь.
COPANAR_GAP = 0.06

#: Сколько соседей в окне зазора успевает проверить одна грань. Пол двора —
#: сотни квадов в одной плоскости, и перебирать их попарно бессмысленно:
#: соседство решает порядок, а не геометрия.
COPANAR_WINDOW = 64


#: Насколько нормали двух граней должны совпадать, чтобы пару считать
#: сопадающей: 0.9994 — это около 2°. Наклонный квад земли пересекает
#: горизонтальную грань шпалы по одной линии, расходится вдоль поверхности и
#: не мерцает; мерцают именно параллельные плоскости, идущие вровень.
COPANAR_NORMAL_DOT = 0.9994


#: Сторона честного пятна перекрытия, при котором две параллельные грани
#: действительно борются за пиксель: 20 см по обеим осям. Произведение само по
#: себе обманчиво: у скошенной грани AABB шире самой грани, и два листа,
#: касающиеся ребром, дают «пятно» в 0,04 м² при реальном перекрытии в ноль.
COPANAR_PATCH_SIDE = 0.20


def _patch_sides(a, b) -> list:
    """Два наибольших размера перекрытия AABB, м."""
    dims = []
    for axis in range(3):
        span = min(a[1][axis], b[1][axis]) - max(a[0][axis], b[0][axis])
        dims.append(max(span, 0.0))
    dims.sort()
    return dims[1:]


def _face_height(nrm, cen, x: float, y: float) -> float:
    """Отметка плоскости грани в точке (x, y).

    Плоскость грани задана нормалью `nrm` и точкой на ней `cen`. Z — высота,
    X и Y — горизонтальные оси. Отметка отличается от смещения плоскости от
    начала координат именно тем, что не зависит от того, где плоскость стоит
    относительно нуля: на углу клетки это сотни метров, и разница нормалей
    двух граней, умноженная на такое расстояние, съедает настоящий зазор.
    """
    return (nrm.dot(cen) - nrm.x * x - nrm.y * y) / nrm.z


#: Окно поиска кандидатов по отметке грани. Шире порога `COPANAR_GAP`: у двух
#: почти параллельных граней, стоящих в разных местах, отметки центров
#: расходятся ещё и на уклоне — на 2° и десяти метрах это уже треть метра.
#: Окно считается от уклона и размера грани, а не берётся с потолка.
def _search_window(nrm, radius: float, rad_max: float, gap: float) -> float:
    slope = math.hypot(nrm.x, nrm.y) / max(nrm.z, 1e-6)
    return gap + slope * (radius + rad_max) + 0.05


#: Наклон грани, выше которого она считается «поверхностной»: cos 45°. Проверка
#: смотрит только такие пары — именно их видит игрок сверху, и именно они
#: мерцают на земле. Торцевые крышки кусков, отвесные стенки юбок и внутренние
#: грани балласта скрыты в грунте или соседним куском, и их сопадение
#: невидимо: ловить его значит плодить ложные отказы.
COPANAR_UP = 0.70

#: Шпала вложена в балласт по замыслу: её верхняя грань примыкает к плечу
#: балласта **по линии**, а не лежит на нём площадью. Проверка меряет плоскости,
#: и такая линия даёт зазор в сантиметры — при том, что глаз не видит ничего:
#: кадры депо и перегона показывают чистые шпалы. Борьба плоскостей — это когда
#: совпадает площадь, а не когда одно тело вложено в другое. Поэтому шпалы и
#: путевая структура, в которую они вложены, из проверки исключены.
EMBEDDED_IN_TRACK = ("Ties", "Rail", "Siding")


def _is_designed_contact(na: str, nb: str) -> bool:
    """Шпала и путевая структура, в которую она вложена, — не борьба плоскостей."""
    for a, b in ((na, nb), (nb, na)):
        if a.startswith(EMBEDDED_IN_TRACK) and b.startswith(EMBEDDED_IN_TRACK):
            return True
    return False


def cross_object_coplanar(meshes, gap: float = COPANAR_GAP,
                          min_area: float = COPANAR_MIN_AREA,
                          min_patch: float = COPANAR_MIN_PATCH):
    """Пары сопадающих граней **между разными объектами** одного файла.

    `coplanar_visible_pairs` смотрит один меш: он ловит шпалу о шпалу и ярус
    сосны о ярус. А мерцание, которое видит игрок на земле, живёт на стыке
    объектов: юбка дороги легла вровень с квадом земли, край балласта — вровень
    с полотном, кровля — вровень со снежной шапкой соседней коробки. Внутри
    каждого из этих мешей порядок, и файл проходил зелёным ровно до того
    момента, пока два объекта не оказались в одной плоскости.

    Критерий: параллельные грани (нормаль совпадает), разные объекты, разные
    материалы (одинаковый цвет мерцает невидимо), перекрытие от 20×20 см и
    зазор меньше `COPANAR_GAP`. Грани с противоположными нормалями не считаются:
    это контакт, а не конкуренция плоскостей.
    """
    buckets: dict = {}
    boxes: dict = {}
    rad_max = 0.0
    for (name, obj) in meshes.items():
        if name.endswith("_Collider"):
            continue
        mw = obj.matrix_world
        scale = max(abs(s) for s in mw.to_scale()) or 1.0
        area_scale = scale * scale
        lo = [1e9] * 3
        hi = [-1e9] * 3
        mats = [s.material.name if s.material else "?"
                for s in obj.material_slots]
        for poly in obj.data.polygons:
            if poly.area * area_scale < min_area:
                continue
            nrm = (mw.to_3x3() @ poly.normal).normalized()
            if nrm.z < COPANAR_UP:
                continue        # смотрим только на обращённые вверх поверхности
            cen = mw @ poly.center
            corners = [mw @ obj.data.vertices[i].co for i in poly.vertices]
            f_lo = [min(c[a] for c in corners) for a in range(3)]
            f_hi = [max(c[a] for c in corners) for a in range(3)]
            for a in range(3):
                lo[a] = min(lo[a], f_lo[a])
                hi[a] = max(hi[a], f_hi[a])
            mat = (mats[poly.material_index]
                   if poly.material_index < len(mats) else "?")
            key = (round(nrm.x, 1), round(nrm.y, 1), round(nrm.z, 1))
            # Ключ сортировки — отметка грани по центру, а не смещение плоскости
            # от начала координат. Смещение зависит от того, где плоскость
            # стоит относительно нуля, и на двух гранях с чуть разными
            # нормалями (склон земли против склона рельса) разница нормалей
            # умножается на расстояние до нуля: в углу клетки это сотни метров,
            # и настоящий зазор в полметра превращается в сантиметры. Отметка
            # от этого свободна — это высота поверхности в её точке.
            buckets.setdefault(key, []).append(
                (_face_height(nrm, cen, cen.x, cen.y), name, mat,
                 (f_lo, f_hi), nrm, cen))
            rad = max(f_hi[0] - f_lo[0], f_hi[1] - f_lo[1]) * 0.5
            if rad > rad_max:
                rad_max = rad
        boxes[name] = ((lo, hi) if lo[0] < hi[0] else None)

    pairs = []
    for faces in buckets.values():
        if len(faces) < 2:
            continue
        faces.sort()
        for i in range(len(faces)):
            (oi, na, ma, ba, va, ca) = faces[i]
            if boxes.get(na) is None:
                continue
            seen = 0
            j = i + 1
            radius = max(ba[1][0] - ba[0][0], ba[1][1] - ba[0][1]) * 0.5
            window = _search_window(va, radius, rad_max, gap)
            while j < len(faces) and faces[j][0] - oi < window:
                (oj, nb, mb, bb, vb, cb) = faces[j]
                j += 1
                if na == nb or ma == mb or seen > COPANAR_WINDOW:
                    continue
                if _is_designed_contact(na, nb):
                    continue
                if va.dot(vb) < COPANAR_NORMAL_DOT:
                    continue
                seen += 1
                if boxes.get(nb) is None:
                    continue
                if not check_train._overlap(boxes[na], boxes[nb], gap):
                    continue
                # Пересечение именно граней, а не «почти дотронулись»: две
                # параллельные плоскости в 5 см, чьи проекции только касаются,
                # не конкурируют за пиксель. Пятно перекрытия считается по
                # честным границам без раздувания, иначе касание общим ребром
                # проходит как копланарность.
                if not check_train._overlap(ba, bb, 0.0):
                    continue
                if min(_patch_sides(ba, bb)) < COPANAR_PATCH_SIDE:
                    continue
                # Настоящий зазор: расстояние от точки поверхности A в пятне
                # перекрытия до плоскости B, а не разница смещений. Смещение
                # плоскости от нуля для этого не годится — оно зависит от
                # положения грани относительно начала координат и от того,
                # насколько нормали двух граней расходятся (см. сортировку выше).
                #
                # Зазор считается не в одной точке перекрытия, а по всему
                # пятну: если плоскости совпадают, зазор мал в любой его точке,
                # а если грани лишь задевают друг друга (шпала вложена в
                # балласт по замыслу), зазор мал только посередине и растёт к
                # краям. Борьба плоскостей за пиксель — это когда плоскости
                # совпадают на всей площади, а не когда одна проходит сквозь
                # другую по краю.
                ox0 = max(ba[0][0], bb[0][0])
                ox1 = min(ba[1][0], bb[1][0])
                oy0 = max(ba[0][1], bb[0][1])
                oy1 = min(ba[1][1], bb[1][1])
                px = (ox0 + ox1) * 0.5
                py = (oy0 + oy1) * 0.5
                far = 0.0
                for (qx, qy) in ((px, py), (ox0, oy0), (ox1, oy1), (ox0, oy1), (ox1, oy0)):
                    qz = _face_height(va, ca, qx, qy)
                    d = abs(vb.x * qx + vb.y * qy + vb.z * qz - vb.dot(cb))
                    if d > far:
                        far = d
                if far >= gap:
                    continue
                pairs.append((na, nb, ba[0], bb[0]))
                if len(pairs) >= 4:
                    return pairs
    return pairs
#: кластер», а молча провалившаяся сборка: `_scatter` на залипшем счётчике
#: однажды вернул ноль точек, и весь пайплайн прошёл зелёным.
#: Минимум треугольников и оболочек в кластере. Пустой меш — не «лёгкий
#: кластер», а молча провалившаяся сборка: `_scatter` на залипшем счётчике
#: однажды вернул ноль точек, и весь пайплайн прошёл зелёным.
GROVE_MIN_TRIS = 2000
GROVE_MIN_SHELLS = 40
ROCKS_MIN_TRIS = 200
ROCKS_MIN_SHELLS = 10


def _check_not_empty(meshes, problems) -> None:
    for name, obj in sorted(meshes.items()):
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        if tris == 0:
            problems.append("%s: пустой меш (%d вершин)"
                            % (name, len(obj.data.vertices)))
        elif tris < 8:
            problems.append("%s: в меше %d трис — это не ассет" % (name, tris))


#: Что в клетке бывает полом: рельеф, полотна дорог и пути, боковой путь и
#: плиты двора. Мерить опору по всем объектам нельзя: основание постройки
#: лежит под её же стеной, стена под кровлей, и «опорой» стала бы крыша в пяти
#: метрах над полом.
FLOOR_KINDS = ("Ground", "DepotApron", "DepotPlatform", "Station_Pad", "Road_",
               "Rail_", "Siding_")
#: Декор, которого касается проверка опоры, и то, из чего он сделан.
#: Поселение меряется теми же правилами, что и депо: секции сруба (`Hut_Walls`,
#: `Hut_Roof`, `Hut_Chimney`, `Hut_Shop_*`) в список не входят намеренно — у
#: здания подошвой является фундамент, а трубы и окна висят по замыслу.
DECOR_KINDS = ("Grove", "Yard", "Posts", "Signs", "Rocks", "Drift", "Prop",
               "Hut_Fence", "Hut_Pen", "Hut_Power", "Hut_Mast", "Hut_Pine",
               "Hut_Dressing", "Station_Fence", "Station_Dressing",
               "Station_Pine")
#: Провал проверять только у тех предметов, у которых основание видно: у сосны
#: и валуна метр под снегом — это пропавший предмет, у столба и знака под
#: снегом стоит стойка, и два метра для них норма. Наносы (`Drift`,
#: `Hut_Dressing`, дворовые `Yard`) — не исключение, а правило: у снежного
#: вала основание и должно уходить в снег на два его метра, иначе проверка
#: требует от снега быть камнем.
DECOR_SUNK_KINDS = ("Grove", "Rocks", "Drift", "Prop", "Hut_Pine",
                    "Station_Pine")
#: Зазоры. Висеть декор не может больше 12 см: это видно и в кадре, и при
#: попытке поставить рядом предмет. Проваливаться — больше метра (двух у
#: столбов).
CONTACT_FLOAT = 0.12
CONTACT_SUNK = 1.00
CONTACT_SUNK_POSTS = 2.00
#: Подошва предмета: вершины в 35 см от самой низкой точки. Выше — это уже
#: корпус, а не основание.
CONTACT_BAND = 0.35
#: Отступ от края клетки: за ним опора у соседнего чанка, а его в этом прогоне
#: нет.
CONTACT_EDGE = W.BLK * 0.5


def _floor_top(meshes, skip, x: float, y: float, z: float):
    """Самая верхняя грань пола над точкой: луч сверху клетки вниз."""
    best = None
    for (name, other) in meshes.items():
        if other is skip or not any(k in name for k in FLOOR_KINDS):
            continue
        inv = other.matrix_world.inverted()
        origin = inv @ Vector((x, y, z + 60.0))
        direction = (inv.to_3x3() @ Vector((0, 0, -1))).normalized()
        hit, loc, _n, _i = other.ray_cast(origin, direction)
        if not hit:
            continue
        wz = (other.matrix_world @ loc).z
        if best is None or wz > best:
            best = wz
    return best


def _check_ground_contact(meshes, problems) -> None:
    """Ни одного висячего и ни одного провалившегося предмета декора.

    Причина, по которой проверка стала шагом пайплайна: до ровной базы мира
    такие дефекты прятались в уклоне, а зеркальная ось в `cell_props.merge`
    (предмет ставился в `+lz` там, где рельеф считает `−lz`) уводила сосны в
    чанке депо на 7 и 11 метров вниз — и ни одна прежняя проверка этого не
    видела, потому что меряла вершины земли и путь, а не посадку пропов.
    """
    for name, obj in sorted(meshes.items()):
        if not any(k in name for k in DECOR_KINDS):
            continue
        world = [obj.matrix_world @ v.co for v in obj.data.vertices]
        if not world:
            continue
        base = min(p.z for p in world)
        limit = (CONTACT_SUNK if any(k in name for k in DECOR_SUNK_KINDS)
                 else CONTACT_SUNK_POSTS)
        float_n = sunk_n = 0
        worst_f = 0.0
        worst_s = 0.0
        for wp in world:
            if (wp.z - base > CONTACT_BAND or abs(wp.x) > CONTACT_EDGE
                    or abs(wp.y) > CONTACT_EDGE):
                continue
            top = _floor_top(meshes, obj, wp.x, wp.y, wp.z)
            if top is None:
                continue
            gap = wp.z - top
            if gap > CONTACT_FLOAT:
                float_n += 1
                worst_f = max(worst_f, gap)
            elif gap < -limit:
                sunk_n += 1
                worst_s = min(worst_s, gap)
        if float_n or sunk_n:
            problems.append(
                "%s: %d точек подошвы висят (до %+.2f м над полом), %d "
                "провалились глубже %.1f м (до %.2f м)"
                % (name, float_n, worst_f, sunk_n, limit, worst_s))


def _check_clusters(meshes, base, problems) -> None:
    if base.startswith("Frontier_Grove"):
        min_tris, min_shells, kind = GROVE_MIN_TRIS, GROVE_MIN_SHELLS, "кластер леса"
    elif base.startswith("Frontier_Rocks"):
        min_tris, min_shells, kind = ROCKS_MIN_TRIS, ROCKS_MIN_SHELLS, "гряду валунов"
    else:
        return
    for name, obj in sorted(meshes.items()):
        stats = lowpoly.mesh_stats(obj)
        if stats["tris"] < min_tris:
            problems.append("%s: %d трис, %s обязана быть от %d"
                            % (name, stats["tris"], kind, min_tris))
        if stats["shells"] < min_shells:
            problems.append("%s: %d оболочек, в %s ждали от %d"
                            % (name, stats["shells"], kind, min_shells))


def _label(paths: list[str]) -> str:
    """Имя проверки: клетка, если все файлы — её части, иначе имя первого файла.

    Клетка живёт в префабе, а не в одном FBX: части земли, пути и двора лежат
    отдельными файлами. Мерить их по одному бессмысленно — шов «дорога/земля»
    и «плита/платформа» живёт именно между двумя файлами.
    """
    cells = set()
    for path in paths:
        mt = re.search(r"_(-?\d+)_(-?\d+)$",
                       os.path.splitext(os.path.basename(path))[0])
        if not mt:
            return os.path.splitext(os.path.basename(paths[0]))[0]
        cells.add((mt.group(1), mt.group(2)))
    if len(cells) != 1:
        return os.path.splitext(os.path.basename(paths[0]))[0]
    (i, j) = cells.pop()
    return "WorldCell_%s_%s" % (i, j)


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после -- (можно несколько: все части одной клетки)")
        return 2
    base = _label(argv)
    missing = [p for p in argv if not os.path.exists(p)]
    if missing:
        print("CHECK %s FAIL нет файла: %s" % (base, missing[0]))
        return 1

    check_train.configure_stdout()
    problems: list[str] = []
    _check_layout(problems)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    for path in argv:
        bpy.ops.import_scene.fbx(filepath=path)
    meshes = {obj.name: obj for obj in bpy.context.scene.objects
              if obj.type == "MESH"}

    _check_not_empty(meshes, problems)
    _check_clusters(meshes, base, problems)

    if base.startswith("WorldCell"):
        # Клетка мерится целиком: опора предмета может быть плитой соседней
        # части, и по одному файлу этого не увидеть.
        _check_ground_contact(meshes, problems)

    if base == "Frontier_Ground":
        _check_ground(problems, meshes)
    if base == "Frontier_Track":
        _check_track(problems, meshes)

    for name, obj in sorted(meshes.items()):
        if name.endswith("_Collider"):
            continue                      # прокси не для глаз: толщина не важна
        _check_min_dim(obj, problems)
        pairs = coplanar_visible_pairs(obj)
        if not pairs:
            continue
        line = ("%s: копланарных пар %d, первая %s / %s"
                % (name, len(pairs),
                   tuple(round(v, 2) for v in pairs[0][0]),
                   tuple(round(v, 2) for v in pairs[0][1])))
        # Узкое исключение, а не общий порог: у кластера леса сотни
        # горизонтальных «блинов» ярусов, и два из них случайно попадают в
        # одну плоскость с разницей в 2 мм (120 граней дают ~7000 пар). Это
        # свойство плотного леса, а не дефект сборки. Путь, земля и пропы
        # такого оправдания не имеют: там копланарность = видимое мерцание.
        if name.startswith("Frontier_Grove") or name.startswith("Frontier_Rocks"):
            print("  ПРЕДУПРЕЖДЕНИЕ (кластер): %s" % line)
        else:
            problems.append(line)

    # Стыки между объектами одного файла: земля клетки против юбки дороги,
    # балласт против полотна, кровля против шапки снега. Внутри каждого меша
    # порядок, а на глаз мерцает именно шов.
    cross = cross_object_coplanar(meshes)
    for (na, nb, alo, blo) in cross:
        problems.append("%s и %s: грани в одной плоскости у %s / %s — мерцание"
                        % (na, nb, tuple(round(v, 2) for v in alo),
                           tuple(round(v, 2) for v in blo)))

    if problems:
        print("CHECK %s FAIL" % base)
        for p in problems:
            print("  - %s" % p)
        return 1
    print("CHECK %s OK (%d объектов)" % (base, len(meshes)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
