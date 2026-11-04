"""Путь и дороги одной клетки: куски мировой линии, уложенные на стол.

Запуск (штатно — из пайплайна):

    blender --background --factory-startup --python Tools/Blender/build_transport.py

Почему куски, а не одна длинная лента. Чанк клетки обязан быть самостоятельным
(в этом весь смысл сетки), поэтому линия режется по рёбрам решётки `line_spans`,
и каждая клетка получает свой отрезок. Чтобы соседние отрезки сошлись, держатся
три правила:

1. **Отметка берётся из стола, а не из рельефа.** `transport_floor` даёт свою
   высоту в любой точке, и земля слоя C будет прорезана под неё. Если бы путь
   ложился на готовый рельеф, стык зависел бы от того, как сетка террейна
   пересекла откос.
2. **Фазы считаются по всей трассе.** Шпалы и путевые знаки ставятся по
   глобальной длине дуги, а не «от начала куска»: иначе у двух соседей шаг
   шпал разъехался бы на величину до 2 м ровно на границе клеток.
3. **Конец куска — это точка стыка.** Кусок заканчивается ровно на ребре
   решётки, поэтому у обеих клеток там вершина, и проверка меряет расстояние
   между ними, а не «на глаз по превью».

Профиль пути не переизобретён: тот же набор сечений, что у принятого двора
депо (`frontier_parts.BED_PROFILE`, `RAIL_PROFILE`, шпалы и снежные шапки),
просто повёрнутый на отметку стола. Коридор рельсовой линии специально равен
`frontier_layout.CORRIDOR_FLAT`/`CORRIDOR_HALF`, чтобы путь на новой карте
выглядел тем же путём, который уже принят кадром прибытия.
"""

from __future__ import annotations

import math

from mathutils import Vector

import frontier_layout as FL
import frontier_parts as FP
import level_lib
import world_layout as W

#: Шаг станций вдоль линии — тот же, что в принятом депо: 2,0 м.
STATION_STEP = FL.SLEEPER_STEP

#: Смещение высот: стол теперь даёт **поверхность** линии (головку рельса), а
#: профиль депо набран ровно от неё же — поэтому смещение ноль. Осталась
#: константой, чтобы стек пути читался от головки, как в `frontier_parts`.
SHIFT = 0.0

#: Путевые знаки: ритм по принятому депо — один знак на 24 м.
SIGN_EVERY = FP.SIGN_EVERY

#: Полотно дороги: 8 м шириной, верх вровень со столом (+0,02, чтобы не слиться
#: с прорезанной землёй), низ уходит **ниже дна выемки**. Держать нижнюю грань
#: внутри коридора нельзя: откос выемки проходит через каждую промежуточную
#: отметку, и где-нибудь полотно легло бы вровень с гранью земли — это мерцание
#: на кромке дороги, которое видно в редакторе и не видно внутри одного меша.
PAVE_HALF = 4.0

#: На сколько уводить нижние грани за дно коридора. 20 см мало: там, где
#: коридор идёт насыпью, плита лежит ниже дна, и нижняя грань юбки ложится
#: вровень именно с ней — те 1-2 см, которые ловит межобъектная проверка.
#: 1,6 м перекрывает любой провал плиты у дороги. Рельеф от глубины не портится,
#: потому что видимая часть обочины держится сверху, а вниз уходит только
#: погребённый край.
BURY = 1.60

def pave_profile(crown: float) -> list:
    """Полотно дороги: плита покрытия, положенная на полку коридора.

    Верх на `crown` над столом, низ уходит на юбку `BURY`: между снегом и
    льдом остаётся 34 см, и ни при каком весе схода на двор пара «дорога/земля»
    не ложится в одну плоскость. Обход — против часовой стрелки, иначе верх
    полотна смотрит вниз и меш рисуется наизнанку
    (`Tools/Blender/check_sweep_orientation.py`).
    """
    return [(-PAVE_HALF, -BURY), (PAVE_HALF, -BURY),
            (PAVE_HALF, crown), (-PAVE_HALF, crown)]


def bank_profile(crown: float) -> list:
    """Муфта: узкий занос по кромке полотна, как в `MenuRideSegment_C`.

    Прежняя форма раскатывала снег плоским плечом до края коридора (±16 м), и
    на перегоне дорога тонула в тридцатиметровой ленте: полотно, канал, плато.
    Теперь борт занимает 1,2 м: шапка на 22 см выше полотна, наружный склон
    мягкий, внутрь борт на 10 см влезает в плиту — две грани в одной плоскости
    там не появляются, а стык льда и снега не читается швом. Наружная стена
    отвесная и уходит в грунт: пологое плечо нашло бы склон коридора
    параллельным и дало бы мерцание ровно там, где оно ловилось и раньше.
    """
    top = crown + 0.22
    return [(PAVE_HALF + 1.2, -BURY),
            (PAVE_HALF + 1.2, crown + 0.04),
            (PAVE_HALF + 0.30, top), (PAVE_HALF - 0.10, top),
            (PAVE_HALF - 0.10, crown + 0.02),
            (-PAVE_HALF + 0.10, crown + 0.02),
            (-PAVE_HALF + 0.10, top), (-PAVE_HALF - 0.30, top),
            (-PAVE_HALF - 1.2, crown + 0.04),
            (-PAVE_HALF - 1.2, -BURY)]

_STATIONS: dict = {}


def line_of(key: str) -> dict:
    """Запись `W.TRANSPORT` по ключу (`rail`, `road 0`, ...)."""
    for ln in W.TRANSPORT:
        if ln["k"] == key:
            return ln
    raise KeyError(key)


def _rot_at(pts, i: int) -> float:
    """Поворот станции в градусах вокруг Blender-Z (тот же знак, что в `_stations`)."""
    a = pts[max(i - 1, 0)]
    b = pts[min(i + 1, len(pts) - 1)]
    return math.degrees(math.atan2(-(b[0] - a[0]), -(b[1] - a[1])))


def stations(key: str, step: float = STATION_STEP):
    """Глобальные станции трассы: (x, z, путь от начала, поворот, индекс).

    Считаются один раз на всю линию и кэшируются: у клетки нет права начинать
    отсчёт шпал заново, иначе фаза на стыке разъедется.
    """
    cache_key = (key, step)
    if cache_key in _STATIONS:
        return _STATIONS[cache_key]
    ln = line_of(key)
    pts = ln["pts"]
    out = []
    acc = 0.0
    next_at = 0.0
    prev_rot = None
    for i in range(len(pts) - 1):
        (ax, az) = pts[i]
        (bx, bz) = pts[i + 1]
        seg = math.hypot(bx - ax, bz - az)
        if seg <= 1e-6:
            continue
        (tx, tz) = ((bx - ax) / seg, (bz - az) / seg)
        rot = math.degrees(math.atan2(-tx, -tz))
        if prev_rot is not None:
            while rot - prev_rot > 180.0:
                rot -= 360.0
            while rot - prev_rot < -180.0:
                rot += 360.0
        while next_at <= acc + seg + 1e-6:
            t = (next_at - acc) / seg
            out.append((ax + tx * seg * t, az + tz * seg * t, next_at, rot, i))
            next_at += step
        acc += seg
        prev_rot = rot
    out.append((pts[-1][0], pts[-1][1], acc, prev_rot or 0.0, len(pts) - 2))
    _STATIONS[cache_key] = out
    return out


def cell_stations(key: str, i: int, j: int, step: float = STATION_STEP):
    """Станции, которые принадлежат клетке, вместе с их глобальным индексом."""
    return [(idx, s) for (idx, s) in enumerate(stations(key, step))
            if W.cell_of(s[0], s[1]) == (i, j)]


def _split_kinks(chunk, pts, gap: float = 0.001):
    """Разрезать внутренний угол трассы на два кольца.

    `tube` строит рамку кольца по соседним точкам пути, поэтому в вершине
    ломаной рамка усредняет два колена и разворачивается на пол-угла поворота.
    Для постели шириной 15,2 м это подъём внешнего края на метр: путь на углу
    вспухал над столом.

    Угол решается удвоением точки, а не сдвигом на «немного»: рамка первой
    копии смотрит вдоль входящего колена (её сосед — предыдущая точка того же
    колена и копия в той же позиции), рамка второй — вдоль исходящего. Зазор
    оставлен миллиметром, чтобы не плодить нулевые грани, и этого хватает:
    остаточный наклон рамки 0,04°, а не 8°, как было при зазоре 0,25 м на шаге
    2 м.

    Первый и последний точки куска не трогаются: это узлы стыка с соседней
    клеткой, и сдвинуть их нельзя, иначе два куска перестанут иметь общее
    сечение на границе.
    """
    verts = set(pts)
    last = len(chunk) - 1
    out = []
    for (n, (x, z, s)) in enumerate(chunk):
        if n == 0 or n == last or (x, z) not in verts:
            out.append((x, z, s))
            continue
        (bx, bz, _bs) = out[-1]
        if (x, z) == pts[-1]:
            out.append((x, z, s))
            continue
        (fx, fz) = pts[pts.index((x, z)) + 1]
        din = (x - bx, z - bz)
        dout = (fx - x, fz - z)
        lin = math.hypot(*din) or 1.0
        lout = math.hypot(*dout) or 1.0
        out.append((x - din[0] / lin * gap, z - din[1] / lin * gap, s - gap))
        out.append((x + dout[0] / lout * gap, z + dout[1] / lout * gap, s + gap))
    return out


def cell_paths(key: str, i: int, j: int, step: float):
    """Куски трассы в клетке, уплотнённые до шага станции: [[(x, z, путь)], ...].

    Уплотнение нужно потому, что узлы ломаной стоят в десятки метров друг от
    друга, а профиль протягивается между ними: без деления шпала на середине
    прямого участка легла бы на хорду.
    """
    ln = line_of(key)
    out = []
    for span in W.line_spans(ln["pts"], i, j):
        chunk = []
        for (a, b) in zip(span, span[1:]):
            d = math.hypot(b[0] - a[0], b[1] - a[1])
            n = max(1, int(d / step + 0.5))
            for k in range(n):
                t = k / n
                chunk.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
        chunk.append(span[-1])
        pts = [(x, z, W.project_to_line(x, z, ln["pts"])[1]) for (x, z) in chunk]
        out.append(_split_kinks(pts, ln["pts"]))
    return out


def _levels(key: str, path):
    """Отметка стола в каждой точке куска."""
    ln = line_of(key)
    return [(x, z, W.level_at(ln["table"], s)) for (x, z, s) in path]


def _bank_runs(key: str, fine):
    """Куски пути, где дорога ещё на своём ложе, — только там и есть занос.

    У тупика дорога сходит на двор (`W.flush_blend`), и земля под ней
    поднимается к полу двора. Снежная обочина там не нужна дважды: двор
    расчищен, а полоса профиля прошла бы в трёх сантиметрах над землёй (замер
    на узле склада), и пара «дорога/земля» мерцала бы. Куски от двух точек:
    одиночная станция трубой не становится.
    """
    ln = line_of(key)
    out = []
    cur = []
    for p in fine:
        s = W.project_to_line(p[0], p[1], ln["pts"])[1]
        if W.flush_blend(key, s) >= 1.0 - 1e-6:
            cur.append(p)
            continue
        if len(cur) > 1:
            out.append(cur)
        cur = []
    if len(cur) > 1:
        out.append(cur)
    return out


def _sweep(b, fine, profile, mat, center, z_level):
    """Протянуть профиль по куску клетки в локальных осях.

    `z_level` — отметка, к которой прибавляются высоты профиля: в депо они
    набраны от головки рельса, здесь — от стола.
    """
    (cx, cz) = center
    path = [(x - cx, -(z - cz), level + z_level) for (x, z, level) in fine]
    profile = level_lib.outward_profile(profile)
    b.tube(path, [(1.0, 1.0)] * len(path), mat, n=4, profile=profile,
           ref=(0.0, 0.0, 1.0), fan_caps=True)


def build_rail(materials, i: int, j: int):
    """Кусок магистрали в клетке: None, если линия её не касается."""
    key = "rail"
    paths = cell_paths(key, i, j, STATION_STEP)
    if not paths:
        return None
    ln = line_of(key)
    center = W.cell_center(i, j)
    b = level_lib.LevelBuilder(materials)

    for path in paths:
        fine = _levels(key, path)
        _sweep(b, fine, FP.BED_PROFILE, "Level_SnowDeep", center, SHIFT)
        for side in (-1.0, 1.0):
            off = FP._offset([(x, z) for (x, z, _l) in fine],
                             side * FL.GAUGE * 0.5)
            _sweep(b, [(o[0], o[1], p[2]) for (o, p) in zip(off, fine)],
                   FP.RAIL_PROFILE, "Level_Metal", center,
                   (FL.RAIL_HEAD_Y + FL.RAIL_FOOT_Y) * 0.5 + SHIFT)

    return b.build("Rail_%d_%d" % (i, j), recalc=False)


def build_ties(materials, i: int, j: int):
    """Шпалы и снежные шапки между ними — отдельным объектом.

    Разделение не для красоты: стык двух чанков мерится поперечным сечением
    метлы, а шпала — коробка, которая стоит в полуметре от границы и попадает
    в сечение только с одной стороны. Смешивать их в одном мешe значит ловить
    «расхождение стыка» там, где расходятся шпала и балласт.
    """
    key = "rail"
    st = cell_stations(key, i, j)
    if not st:
        return None
    ln = line_of(key)
    center = W.cell_center(i, j)
    all_st = stations(key)
    b = level_lib.LevelBuilder(materials)
    for (idx, (ux, uz, s, rot, _seg)) in st:
        level = W.level_at(ln["table"], s)
        b.box_rot((ux - center[0], -(uz - center[1]),
                   level + (FL.SLEEPER_TOP_Y - FL.SLEEPER_DEPTH * 0.5) + SHIFT),
                  (FL.SLEEPER_SIZE[0], FL.SLEEPER_SIZE[1], FL.SLEEPER_DEPTH),
                  (0.0, 0.0, rot), "Level_WoodDark", bevel=0.02)
        nxt = all_st[idx + 1] if idx + 1 < len(all_st) else None
        if nxt is None or W.cell_of(nxt[0], nxt[1]) != (i, j):
            continue
        nrot = nxt[3]
        rot2 = rot if abs(nrot - rot) <= 4.0 else (rot + nrot) * 0.5
        cap_z = level + (FL.CAP_TOP_Y - FL.CAP_DEPTH * 0.5) + SHIFT
        b.box_rot(((ux + nxt[0]) * 0.5 - center[0],
                   -((uz + nxt[1]) * 0.5 - center[1]), cap_z),
                  (4.6, STATION_STEP - FL.SLEEPER_SIZE[1], FL.CAP_DEPTH),
                  (0.0, 0.0, rot2), "Level_Snow", bevel=0.03)

    return b.build("Ties_%d_%d" % (i, j), recalc=False)


def build_signs(materials, i: int, j: int):
    """Путевые знаки клетки: стойка и табличка, ритм один на 24 м.

    Знаки нарочно не в мешe пути. Их высота — деталь на бровке, а не часть
    стела, и на повороте стойка попадает внутрь коридора соседнего колена, где
    проверка «вершина лежит на столе» к ней неприменима. Отдельный объект
    позволяет мерить путь строго, а знаки считать пропами слоя D.
    """
    key = "rail"
    st = cell_stations(key, i, j)
    if not st:
        return None
    ln = line_of(key)
    center = W.cell_center(i, j)
    b = level_lib.LevelBuilder(materials)
    placed = 0
    for (idx, (ux, uz, s, rot, _seg)) in st:
        if idx % SIGN_EVERY:
            continue
        level = W.level_at(ln["table"], s)
        side = 1.0 if (idx // SIGN_EVERY) % 2 == 0 else -1.0
        # Внутри тёплого двора восточное плечо занято посадочным перроном
        # (депо — ADR 0040, станция — слой E): стойка в 7,2 м от оси встала бы
        # внутри плиты настила — невидимая сама и ловимая проверкой как
        # провалившаяся. На открытой линии ритм остаётся двусторонним.
        if side > 0.0 and W.in_yard(ux, uz):
            continue
        placed += 1
        px = ux + side * (FL.CORRIDOR_FLAT + 2.9)
        b.box_rot((px - center[0], -(uz - center[1]), level + 0.125 + SHIFT),
                  (0.16, 0.16, 1.95), (0.0, 0.0, rot), "Level_Metal", bevel=0.01)
        b.box_rot((px - center[0], -(uz - center[1]), level + 1.24 + SHIFT),
                  (0.44, 0.10, 0.30), (0.0, 0.0, rot), "Level_Orange", bevel=0.0)
    if not placed:
        return None
    return b.build("Signs_%d_%d" % (i, j), recalc=False)


#: Накатанные полосы на полотне: где колёса протоптали лёд. Профиль лежит
#: внутри плиты (низ нулевой, верх +5 см): дно полосы не совпадает с верхом
#: полотна, и межобъектная проверка не видит пары «полоса/полотно».
TRACK_HALF = 0.275
TRACK_OFF = 1.25


def track_profile(crown: float) -> list:
    """Накатанные следы: два колеёных рубца над полотном на 8 см.

    Низ на 2 см утоплен в лёд, чтобы след не висел над ним отдельной плитой;
    верх на 8 см выше полотна — меньше порога мерцания нельзя, больше — следы
    начнут читаться рельсами, которых у дороги нет.
    """
    return [(-TRACK_HALF, crown - 0.02), (TRACK_HALF, crown - 0.02),
            (TRACK_HALF, crown + W.TRACK_CROWN),
            (-TRACK_HALF, crown + W.TRACK_CROWN)]


def road_crown(key: str) -> float:
    """Отметка полотна этой дороги: чётные на `PAVE_CROWN`, нечётные выше на
    `ROAD_CROWN_STEP`, чтобы две дороги в пересечении не легли в одну плоскость.
    """
    try:
        n = int(key.split()[-1])
    except (IndexError, ValueError):
        n = 0
    return W.PAVE_CROWN + (W.ROAD_CROWN_STEP if n % 2 else 0.0)

#: Столбики обочины: вид тот же, что у дороги в меню (`MenuRideSegment_B`),
#: но ритм реже: в меню полоса 32 м несёт восемь столбиков, а на карте это
#: тысячи штук. Шаг 24 м — как у путевых знаков: стойка с оранжевой табличкой
#: читает край дороги, не набивая его тыном (замер: 12 м давали +110 тыс. трис).
POST_EVERY = 24.0
POST_OUT = 5.65
#: Насколько далеко от границы узла ставится столбик. Стена двора стоит по
#: границе, а её тумба у проёма выступает на метр внутрь и наружу: первый
#: столбик дороги, посаженный ровно у старта нити, оказывался в тумбе ворот и
#: уходил под обвязку на 6,4 м — то есть не существовал.
POST_KEEP_OUT = 2.00


def _in_node(x: float, z: float) -> bool:
    """True, если точка внутри тёплого узла или в двух метрах от его стены."""
    for n in W.NODES:
        if n["rect"] and W._rect_dist(x, z, n["rect"]) < POST_KEEP_OUT:
            return True
    return False


def _road_posts(b, key: str, i: int, j: int) -> int:
    """Столбики на обеих обочинах; фаза — глобальная, по всей трассе.

    Считать их от начала куска нельзя? можно, но тогда на стыке двух клеток
    два столбика встанут рядом или один пропадёт. Фаза берётся из глобальной
    длины дуги — той же, что у шпал и знаков.
    """
    ln = line_of(key)
    center = W.cell_center(i, j)
    step = max(1, int(round(POST_EVERY / STATION_STEP)))
    placed = 0
    for (idx, (ux, uz, s, rot, _seg)) in cell_stations(key, i, j):
        if idx % step:
            continue
        level = W.level_at(ln["table"], s)
        pts = [(p[0], p[1]) for p in ln["pts"]]
        for side in (-1.0, 1.0):
            off = FP._offset([(ux, uz), (ux + 0.01, uz)], side * POST_OUT)[0]
            if _in_node(off[0], off[1]):
                continue        # во дворе своя обстановка, и стена здесь
            (bx, bz) = (off[0] - center[0], -(off[1] - center[1]))
            # База утоплена в кромку на 0,35 м: столбик читается вкопанным,
            # а не лежащим на снегу.
            b.box_rot((bx, bz, level + 0.35), (0.16, 0.16, 1.30),
                      (0.0, 0.0, rot), "Level_Metal", bevel=0.01)
            b.box_rot((bx, bz, level + 1.10), (0.46, 0.12, 0.26),
                      (0.0, 0.0, rot), "Level_Orange", bevel=0.0)
            placed += 1
    return placed


def build_road(materials, i: int, j: int, key: str):
    """Кусок дороги `key` в клетке: None, если дорога её не касается.

    Шаг уплотнения тот же, что у пути (2 м), а не «раз в десять метров»: рамка
    кольца наклоняется на уклон хорды, и при хорде 10 м две соседние клетки
    получают на общем ребре разные уклоны — полотно на стыке даёт ступеньку
    0,35 м. На хорде 2 м расхождение уходит в сотые доли миллиметра.
    """
    paths = cell_paths(key, i, j, STATION_STEP)
    if not paths:
        return None
    center = W.cell_center(i, j)
    b = level_lib.LevelBuilder(materials)
    # Оба профиля набраны от стола, который теперь и есть поверхность линии.
    # Порядок: кромка, потом ледовое полотно, потом накатанные полосы. Кромка
    # — снег, как у дороги в меню: она держит край и не даёт льду читаться
    # серой плитой, впаянной в снег.
    for path in paths:
        fine = _levels(key, path)
        crown = road_crown(key)
        for run in _bank_runs(key, fine):
            _sweep(b, run, bank_profile(crown), "Level_Snow", center, 0.0)
        _sweep(b, fine, pave_profile(crown), "Level_Ice", center, 0.0)
        for side in (-1.0, 1.0):
            off = FP._offset([(x, z) for (x, z, _l) in fine], side * TRACK_OFF)
            _sweep(b, [(o[0], o[1], p[2]) for (o, p) in zip(off, fine)],
                   track_profile(crown), "Level_SnowDeep", center, 0.0)
    return b.build("Road_%s_%d_%d" % (key.replace("road ", "").replace(" ", "_"),
                                      i, j), recalc=False)


def build_road_posts(materials, i: int, j: int, key: str):
    """Столбики обочины — отдельным объектом и отдельным файлом.

    Держать их в мешe полотна нельзя: полотно меряется правилом «вершина на
    столе» (`build_transport.measure`, `Z_HIGH = 0.48`), а столбик высотой в
    1,3 м — это проп у дороги, как путевые знаки у пути. Отдельный объект
    позволяет мерить полотно строго, а столбики считать пропом.
    """
    if not cell_paths(key, i, j, STATION_STEP):
        return None
    b = level_lib.LevelBuilder(materials)
    if not _road_posts(b, key, i, j):
        return None
    return b.build("Posts_%s_%d_%d" % (key.replace("road ", "").replace(" ", "_"),
                                       i, j), recalc=False)


def transport_cells():
    """Клетки, которых касается хоть одна транспортная линия."""
    out = set()
    for ln in W.TRANSPORT:
        for (i, j) in W.line_cells(ln["pts"]):
            out.add((i, j))
    return sorted(out)


def build_cell_transport(materials, i: int, j: int):
    """Все транспортные объекты клетки: путь, потом дороги."""
    objs = []
    rail = build_rail(materials, i, j)
    if rail is not None:
        objs.append(rail)
    ties = build_ties(materials, i, j)
    if ties is not None:
        objs.append(ties)
    for n in range(len(W.ROADS)):
        key = "road %d" % n
        road = build_road(materials, i, j, key)
        if road is not None:
            objs.append(road)
        posts = build_road_posts(materials, i, j, key)
        if posts is not None:
            objs.append(posts)
    signs = build_signs(materials, i, j)
    if signs is not None:
        objs.append(signs)
    return objs
