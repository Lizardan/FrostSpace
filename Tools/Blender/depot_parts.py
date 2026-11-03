"""Депо одной клеткой: двор, боковой путь, платформа, коробка, ворота и мачта.

Запуск (штатно — из пайплайна вместе с землёй клетки):

    blender --background --factory-startup --python Tools/Blender/build_world_cells.py -- -2:-2

Депо стоит в клетке (−2,−2) и авторится в локальных осях клетки, как и всё
остальное: Blender-x = мир x − центр клетки, Blender-y = −(мир z − центр),
Blender-z = абсолютная отметка. Центр клетки и есть ноль двора (`DEPOT`),
поэтому локальные числа здесь читаются как «метры от двора», а не как
абстракция. Наружный интерфейс модуля — те же локальные оси; пересчёт в клеточные
делают `_sweep`, `_tube` и `_box`.

Почему меши, а не примитивы сцены. Прежнее депо было черновиком: кубы,
масштабированные `Cube` и меши меню, поставленные на авторскую площадку
1200×1200 м, которая выходила за размер чанка. Депо теперь обычный чанк, и его
содержимое приезжает из Blender тем же путём, что перегон: один FBX на клетку,
общая палитра `Level_*`, численная проверка и превью.

Главное число двора — отметка пола. Двор не плоский блин: магистраль идёт через
него под 4,7 % и падает с юга на север на 9 м. Пол двора — стол хозяйской линии
минус постель (`W.pad_level`), поэтому всё стоящее во дворе опускается на
фактический рельеф (`yard`). Отсюда два правила сборки:

* элементы, вытянутые **вдоль** пути (платформа, коробка, боковой путь),
  тянутся профилем по пути: коробка на уклоне даёт ступеньку на каждом стыке,
  а профиль повторяет уклон непрерывно;
* элементы, вытянутые **поперёк** пути, стоят на ровном полу, и их можно
  набирать коробками: перепада поперёк двора нет ни на сантиметр.

Второе правило сборки — никакого совпадения граней. Опора, поставленная основанием
ровно на рельеф, даёт копланарную грань с землёй, а две детали на одной плоскости
мерцают (STYLE.md). Поэтому каждая стойка уходит в грунт на `LEG_DEPTH`, а
накладная деталь на стене проходит сквозь стену, а не лежит на ней.

Габариты берутся только из `world_layout` (`DEPOT_*`): ни одного числа «на
глаз», иначе двор и путь разъедутся и состав встанет не над рельсом.
"""

from __future__ import annotations

import math

import frontier_layout as FL
import frontier_parts as FP
import level_lib
import transport_parts as TP
import world_layout as W

#: Шаг станций вдоль бокового пути — тот же, что у магистрали.
STEP = FL.SLEEPER_STEP

#: На сколько юбка длинного элемента уходит в грунт: перекрывает и его
#: собственную глубину, и перепад пола поперёк. Под постройкой не должно быть
#: видно неба, и проверять приходится одно число вместо сотни вершин.
BURY = 2.0

#: Запас погружения: нижняя грань обязана быть ниже рельефа хотя бы на столько.
#: Десяток сантиметров — это то, что остаётся после заносов и волн плиты.
BURY_MIN = 0.10

#: Насколько уходит в грунт каждая стойка, тумба и мачта.
LEG_DEPTH = 0.45

#: Отметка верха платформы над головкой рельса — тот же порог двери, что в
#: принятом кадре прибытия: низ колеса на головке, ступенька в вагон 0,60 м.
PLATFORM_RISE = 0.60

#: Сечения двора. Платформа и её снежный вал набираются из `DEPOT_PLATFORM`
#: (см. `build_depot`): западный край платформы держит габарит кузова, восточный
#: — ширину прохода, и оба числа живут в раскладке, а не здесь. Вал шире плиты
#: на 0,4 м с каждой стороны и ниже её верха на 6 см — так он не делит с плитой
#: плоскость и читается заносом по краю.
PLATFORM_CAP_W = 0.40
BAR_PROFILE = [(-0.06, -0.06), (0.06, -0.06), (0.06, 0.06), (-0.06, 0.06)]
RAMP_HALF = 4.0
RAMP_DECK = [(-RAMP_HALF, 0.0), (RAMP_HALF, 0.0),
             (RAMP_HALF, -BURY), (-RAMP_HALF, -BURY)]

#: Стены и кровля коробки — два профиля, а не один: у кровли свой материал, и
#: её нижняя грань входит в стены на 0,3 м, чтобы не делить с ними плоскость.
#: Цоколь шире стен на 0,35 м и короче юбкой, чем стены, — иначе его нижняя
#: грань совпала бы с дном стен и далась копланарной парой.
SHED_WALL = [(-14.0, -BURY), (14.0, -BURY), (14.0, 6.2), (-14.0, 6.2)]
SHED_ROOF = [(-14.6, 5.90), (14.6, 5.90), (14.6, 6.15), (0.0, 8.70),
             (-14.6, 6.15)]
SHED_RIDGE = [(-1.6, 8.30), (1.6, 8.30), (1.6, 8.92), (-1.6, 8.92)]
SHED_PLINTH = [(-14.35, -BURY + 0.4), (14.35, -BURY + 0.4),
               (14.35, 0.55), (-14.35, 0.55)]

#: Проём ворот: ширина вдоль пути и высота от пола. Створка и обвязка проходят
#: сквозь стену насквозь (0,16 м при стене в нулевой толщине профиля), поэтому
#: ни одна их грань не совпадает со стеной.
SHED_DOOR = (12.0, 5.40)
DOOR_PROUD = 0.06
SKIN = 0.16

#: Высота кровли навеса от пола двора и её уклон на восток — от платформы,
#: чтобы с кровли шло прочь от вагона.
CANOPY_RISE = 4.30
CANOPY_TILT_DEG = 12.0

#: Забор: столько звеньев в обе стороны от ворот, и обрыв в поле. Двор отсыпан
#: шире, чем огорожен, — это хозяйство, которое не достроили, а не забытая до
#: конца ограда.
FENCE_BAYS = 6
FENCE_SPAN = 4.0

#: Боковой путь в локальных осях двора: съезд с оси магистрали, дуга и прямая
#: до буферного упора. Концы взяты из раскладки: начало съезда — в 18 м южнее
#: хвоста бокового пути (состав не должен вставать на стрелку), ось и упор —
#: `DEPOT_SIDING_X` и `DEPOT_SIDING_Z`. Промежуточные точки — форма дуги, её
#: в раскладке держать нечем.
SIDING_LOCAL = (
    (W.DEPOT_MAIN_X - W.DEPOT[0],
     W.DEPOT_SIDING_Z[0] - W.DEPOT[1] - 18.0),
    (-9.5, -100.0),
    (-17.0, -84.0),
    (W.DEPOT_SIDING_X - W.DEPOT[0], -74.0),
    (W.DEPOT_SIDING_X - W.DEPOT[0], W.DEPOT_SIDING_Z[1] - W.DEPOT[1]),
)
SIDING_FILLET = 22.0


# ---------------------------------------------------------------- измерения


def yard(lx: float, lz: float) -> float:
    """Отметка пола двора под локальной точкой: фактический рельеф клетки."""
    return W.world_height(*W.to_world(lx, lz))


def head(lx: float, lz: float) -> float:
    """Головка рельса магистрали напротив локальной точки.

    Берётся проекцией на магистраль, а не `yard + 0,6`: у южного съезда,
    который выглядывает за границу двора, эти две точки различаются, а боковой
    путь обязан лежать вровень с главным — иначе платформа, общая для двух
    путей, одному из них не подходит.
    """
    ln = TP.line_of("rail")
    (x, z) = W.to_world(lx, lz)
    (_d, s, _dir) = W.project_to_line(x, z, ln["pts"])
    return W.level_at(ln["table"], s)


def local(rect):
    """Прямоугольник раскладки в мире -> те же четыре числа в осях двора."""
    return (rect[0] - W.DEPOT[0], rect[1] - W.DEPOT[0],
            rect[2] - W.DEPOT[1], rect[3] - W.DEPOT[1])


def _span(a: float, b: float, step: float):
    """Равные отсечки отрезка [a, b] включительно с обоих концов."""
    n = max(1, int(round((b - a) / step)))
    return [a + (b - a) * k / n for k in range(n + 1)]


def siding_line():
    """Боковой путь в локальных осях: скруглённый съезд и прямая до упора."""
    return W.fillet(list(SIDING_LOCAL), SIDING_FILLET)


# ---------------------------------------------------------------- примитивы


def _path(pts):
    """Локальные оси двора -> клеточные Blender-оси: `(x, z, h)` или `(x, z)`."""
    center = W.cell_center(*W.DEPOT_CELL)

    def one(p):
        if len(p) == 2:
            (lx, lz) = p
            return (W.DEPOT[0] + lx - center[0], -(W.DEPOT[1] + lz - center[1]))
        (lx, lz, h) = p
        return (W.DEPOT[0] + lx - center[0], -(W.DEPOT[1] + lz - center[1]), h)

    return [one(p) for p in pts]


def _sweep(b, pts, profile, mat, levels, name, buried=True):
    """Протянуть профиль по локальным точкам `(x, z)` на их собственных отметках.

    Это единственный способ положить длинный элемент на уклон двора без
    ступеньки: `levels` — абсолютная отметка каждой станции, кольцо идёт
    перпендикулярно пути, и потому верх платформы, кровля коробки и головка
    бокового пути остаются параллельными столу магистрали.

    `buried` включает проверку погружения: нижняя грань профиля обязана быть
    ниже рельефа хотя бы на `BURY_MIN`. Проверка падает сразу и с именем
    элемента, а не превращается в «под стеной видно небо» на превью.
    """
    path = [(x, z, y) for ((x, z), y) in zip(_path(pts), levels)]
    if buried:
        lo = min(py for (_px, py) in profile)
        for ((lx, lz), y) in zip(pts, levels):
            d = (y + lo) - yard(lx, lz)
            if d > -BURY_MIN:
                raise RuntimeError(
                    "%s: низ на %.2f м над рельефом в (%.1f, %.1f) — элемент "
                    "висит, нужно уводить юбку ниже" % (name, -d, lx, lz))
    b.tube(path, [(1.0, 1.0)] * len(path), mat, n=4, profile=profile,
           ref=(0.0, 0.0, 1.0), fan_caps=True)


def _tube(b, pts, radii, mat, n=6):
    """Труба по локальным точкам `(x, z, отметка)`: сечение лежит в плане.

    `_sweep` для вертикального пути не годится: там кольцо ставится
    перпендикулярно оси, а у стойки, мачты и провода ось вертикальна.
    """
    b.tube(_path(pts), radii, mat, n=n)


def _box(b, lx, lz, z, size, mat, rot=(0.0, 0.0, 0.0), bevel=0.03):
    """Коробка в локальных осях двора: центр `(lx, lz, z)`, поворот в градусах.

    Поворот — вокруг Blender-Z, тот же знак, что у шпал и путевых знаков в
    `transport_parts`: локальная X смотрит поперёк направления, локальная Y —
    вдоль него.
    """
    (px, py) = _path([(lx, lz)])[0]
    b.box_rot((px, py, z), size, rot, mat, bevel=bevel)


def _leg(b, lx, lz, top, size, mat, rot=(0.0, 0.0, 0.0), bevel=0.02):
    """Опора от `top` вниз в грунт: основание никогда не лежит на рельефе.

    `size` — габарит по X и Y; высота считается сама как `top − (yard −
    LEG_DEPTH)`, поэтому стойка на уклоне двора всегда входит в снег на
   половину метра, а не стоит на нём касанием.
    """
    g = yard(lx, lz) - LEG_DEPTH
    _box(b, lx, lz, (g + top) * 0.5, (size[0], size[1], top - g), mat,
         rot=rot, bevel=bevel)


def _stations(pts, step: float = STEP):
    """Станции локальной ломаной: (x, z, поворот вокруг Blender-Z, путь)."""
    out = []
    acc = 0.0
    nxt = 0.0
    for (a, b) in zip(pts, pts[1:]):
        ln = math.hypot(b[0] - a[0], b[1] - a[1])
        if ln <= 1e-6:
            continue
        (tx, tz) = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
        rot = math.degrees(math.atan2(-tx, -tz))
        if out:
            while rot - out[-1][2] > 180.0:
                rot -= 360.0
            while rot - out[-1][2] < -180.0:
                rot += 360.0
        while nxt <= acc + ln + 1e-6:
            t = (nxt - acc) / ln
            out.append((a[0] + tx * ln * t, a[1] + tz * ln * t, rot, nxt))
            nxt += step
        acc += ln
    return out


# ---------------------------------------------------------------- боковой путь


def build_siding(materials, i: int, j: int):
    """Боковой путь: постель, рельсы, шпалы, снежные шапки и буферный упор.

    Головка лежит на столе магистрали (`head`), поэтому состав здесь и состав у
    платформы стоят на одной отметке: платформа на оба пути одна, и только это
    делает её полезной. Профиль тот же, что у перегонной метлы
    (`frontier_parts.BED_PROFILE`), — путь во дворе обязан выглядеть тем же
    путём, что пришёл с юга.
    """
    fine = siding_line()
    levels = [head(lx, lz) for (lx, lz) in fine]
    b = level_lib.LevelBuilder(materials)

    _sweep(b, fine, FP.BED_PROFILE, "Level_SnowDeep", levels, "постель")
    for side in (-1.0, 1.0):
        off = FP._offset(fine, side * FL.GAUGE * 0.5)
        _sweep(b, off, FP.RAIL_PROFILE, "Level_Metal",
               [y + (FL.RAIL_HEAD_Y + FL.RAIL_FOOT_Y) * 0.5 for y in levels],
               "рельс", buried=False)

    st = _stations(fine)
    for (k, (lx, lz, rot, _s)) in enumerate(st):
        _box(b, lx, lz, head(lx, lz) + (FL.SLEEPER_TOP_Y - FL.SLEEPER_DEPTH * 0.5),
             (FL.SLEEPER_SIZE[0], FL.SLEEPER_SIZE[1], FL.SLEEPER_DEPTH),
             "Level_WoodDark", rot=(0.0, 0.0, rot), bevel=0.0)
        nxt = st[k + 1] if k + 1 < len(st) else None
        if nxt is None:
            continue
        rot2 = rot if abs(nxt[2] - rot) <= 4.0 else (rot + nxt[2]) * 0.5
        (mx, mz) = ((lx + nxt[0]) * 0.5, (lz + nxt[1]) * 0.5)
        _box(b, mx, mz, head(mx, mz) + (FL.CAP_TOP_Y - FL.CAP_DEPTH * 0.5),
             (4.6, STEP - FL.SLEEPER_SIZE[1], FL.CAP_DEPTH), "Level_Snow",
             rot=(0.0, 0.0, rot2), bevel=0.0)

    # -- буферный упор ------------------------------------------------------
    (bx, bz) = (W.DEPOT_BUFFER[0] - W.DEPOT[0], W.DEPOT_BUFFER[1] - W.DEPOT[1])
    rot = st[-1][2] if st else 0.0
    y = head(bx, bz)
    for side in (-1.0, 1.0):
        (ox, oz) = FP._offset([fine[-2], (bx, bz)], side * 1.7)[1]
        _box(b, ox, oz, y + 0.35, (0.34, 0.34, 2.10), "Level_Metal",
             rot=(0.0, 0.0, rot), bevel=0.02)
    _box(b, bx, bz, y + 0.60, (4.6, 0.5, 0.90), "Level_Rust",
         rot=(0.0, 0.0, rot), bevel=0.03)
    _box(b, bx, bz, y + 1.12, (3.4, 0.24, 0.30), "Level_Snow",
         rot=(0.0, 0.0, rot), bevel=0.05)
    return b.build("Siding_%d_%d" % (i, j), recalc=False)


# ---------------------------------------------------------------- двор


def build_depot(materials, i: int, j: int):
    """Постройки двора: платформа, рампа, коробка, навес, ворота, мачта.

    Один объект на весь двор — это осознанно: чанк обязан открываться в
    Prefab Mode как кусок места, а не как пять объектов, которые кто-то должен
    помнить вместе.
    """
    b = level_lib.LevelBuilder(materials)

    # -- посадочная платформа -----------------------------------------------
    # Путь притянут к x = 0, а сечение набирается из краёв прямоугольника: так
    # западный грань платформы стоит ровно там, где её требует габарит кузова,
    # и правка `DEPOT_PLATFORM` в раскладке сама двигает и плиту, и вал.
    (px0, px1, pz0, pz1) = local(W.DEPOT_PLATFORM)
    plat_profile = [(px0, 0.0), (px1, 0.0), (px1, -BURY), (px0, -BURY)]
    cap_profile = [(px0 - PLATFORM_CAP_W, -0.06), (px1 + PLATFORM_CAP_W, -0.06),
                   (px1 + PLATFORM_CAP_W, 0.14), (px0 - PLATFORM_CAP_W, 0.14)]
    zs = _span(pz0, pz1, 5.0)
    pts = [(0.0, z) for z in zs]
    lvl = [head(0.0, z) + PLATFORM_RISE for z in zs]
    _sweep(b, pts, plat_profile, "Level_Stone", lvl, "платформа")
    _sweep(b, pts, cap_profile, "Level_Snow", lvl, "вал у платформы",
           buried=False)

    # -- погрузочная рампа ---------------------------------------------------
    (rx0, rx1, rz0, rz1) = local(W.DEPOT_RAMP)
    lz_mid = (rz0 + rz1) * 0.5
    top = head(rx0, lz_mid) + PLATFORM_RISE
    floor = yard(rx1, lz_mid)
    xs = _span(rx0, rx1, 2.0)
    # Настил держит уровень платформы до трети пролёта, дальше идёт съезд во
    # двор. Доля 0,35 выбрана по уклону: спуск 1,2 м на остальных двух третях
    # пролёта — это 11 %, круче тележку с аккумуляторами уже не толкать.
    deck = [top + (floor - top) *
            max(0.0, (x - (rx0 + (rx1 - rx0) * 0.35)) / ((rx1 - rx0) * 0.65))
            for x in xs]
    _sweep(b, [(x, lz_mid) for x in xs], RAMP_DECK, "Level_Stone", deck, "рампа")
    for sy in (-1.0, 1.0):
        _sweep(b, [(x, lz_mid + sy * RAMP_HALF) for x in xs], BAR_PROFILE,
               "Level_WoodDark", [v + 0.55 for v in deck], "ограждение рампы",
               buried=False)

    # -- депо-коробка --------------------------------------------------------
    (sx0, sx1, sz0, sz1) = local(W.DEPOT_SHED)
    shaxis = (sx0 + sx1) * 0.5
    spts = [(shaxis, z) for z in _span(sz0, sz1, 5.0)]
    slvl = [yard(shaxis, z) for z in _span(sz0, sz1, 5.0)]
    _sweep(b, spts, SHED_PLINTH, "Level_Stone", slvl, "цоколь коробки")
    _sweep(b, spts, SHED_WALL, "Level_Metal", slvl, "стены коробки")
    _sweep(b, spts, SHED_ROOF, "Level_Roof", slvl, "кровля коробки",
           buried=False)
    _sweep(b, spts, SHED_RIDGE, "Level_Snow", slvl, "снег по коньку",
           buried=False)

    # Створки ворот (DP-02) на восточной стене, проёмом на боковой путь.
    # Коробка тянется профилем и дырки в стене не имеет, поэтому проём набран
    # накладными деталями: тёмная створка в габарите проёма и ржавая обвязка
    # вокруг неё.
    face = sx1 + DOOR_PROUD
    dz = (sz0 + sz1) * 0.5 + 6.0
    floor_at = yard(shaxis, dz)
    for k in (-1.0, 1.0):
        zc = dz + k * (SHED_DOOR[0] * 0.25 + 0.15)
        _box(b, face, zc, floor_at + SHED_DOOR[1] * 0.5,
             (SKIN, SHED_DOOR[0] * 0.46, SHED_DOOR[1]), "Level_WoodDark",
             bevel=0.02)
        _box(b, face + 0.10, zc, floor_at + SHED_DOOR[1] * 0.5,
             (0.10, SHED_DOOR[0] * 0.40, SHED_DOOR[1] * 0.86), "Level_Rust",
             bevel=0.02)
    _box(b, face, dz, floor_at + SHED_DOOR[1] + 0.30,
         (0.30, SHED_DOOR[0] + 1.4, 0.60), "Level_Rust", bevel=0.03)
    for side in (-1.0, 1.0):
        zc = dz + side * (SHED_DOOR[0] * 0.5 + 0.5)
        _box(b, face, zc, yard(shaxis, zc) + 1.4, (0.44, 0.90, 3.4),
             "Level_Stone", bevel=0.03)
        # Окна по бокам проёма: тёплых точек в кадре депо намеренно мало
        # (`STYLE.md`), и это не гирлянда, а два окна там, где внутри кто-то
        # есть.
        zw = dz + side * 9.0
        _box(b, face, zw, yard(shaxis, zw) + 4.05, (SKIN, 2.2, 1.1),
             "Level_Window", bevel=0.0)
    # По окну на каждом фронте: торец коробки смотрит вдоль пути. Окно идёт
    # сквозь стену, поэтому его грани не совпадают ни с стеной, ни с кровлей.
    for side in (-1.0, 1.0):
        zc = (sz0 if side < 0 else sz1) - side * 0.05
        _box(b, shaxis, zc, yard(shaxis, zc) + 4.6, (2.0, SKIN, 1.0),
             "Level_Window", bevel=0.0)

    # -- навес над рампой ---------------------------------------------------
    (cx0, cx1, cz0, cz1) = local(W.DEPOT_CANOPY)
    (cxc, czc) = ((cx0 + cx1) * 0.5, (cz0 + cz1) * 0.5)
    roof_z = yard(cxc, czc) + CANOPY_RISE
    for lx in (cx0 + 0.6, cx1 - 0.6):
        for lz in (cz0 + 0.6, cz1 - 0.6):
            _leg(b, lx, lz, roof_z, (0.26, 0.26), "Level_WoodDark", bevel=0.0)
    _box(b, cxc, czc, roof_z + 0.12,
         (cx1 - cx0 + 1.6, cz1 - cz0 + 1.6, 0.24), "Level_Roof",
         rot=(0.0, CANOPY_TILT_DEG, 0.0), bevel=0.04)
    _box(b, cxc, czc, roof_z + 0.62,
         (cx1 - cx0 + 0.8, cz1 - cz0 - 1.0, 0.18), "Level_Snow",
         rot=(0.0, CANOPY_TILT_DEG, 0.0), bevel=0.04)
    # Лампа под навесом: единственная тёплая точка двора.
    lamp_x = cx0 + (cx1 - cx0) * 0.35
    _box(b, lamp_x, czc, roof_z - 0.55, (0.10, 0.10, 0.55), "Level_Metal",
         bevel=0.0)
    _box(b, lamp_x, czc, roof_z - 0.95, (0.62, 0.62, 0.30), "Level_Window",
         bevel=0.06)

    # -- ворота, забор и мачта ----------------------------------------------
    _gate(b)
    _mast(b, W.DEPOT_MAST[0] - W.DEPOT[0], W.DEPOT_MAST[1] - W.DEPOT[1])
    return b.build("Depot_%d_%d" % (i, j), recalc=False)


def _gate(b):
    """Створки выезда (DP-02): два тумба, перемычка с плашкой и обрыв забора.

    Ворота стоят ровно там, где дорога уходит за границу тёплого двора: она
    выходит из двора перпендикулярно главному пути, на восток, и пересекает
    границу площадки x = +24. Точка пересечения считается из раскладки, а не
    ставится руками, иначе створка разъедется с дорогой и машина выедет в тумбу.
    """
    ln = TP.line_of("road 0")
    (wx, wz) = _cross_x(ln["pts"], W.DEPOT_YARD[1])
    (gx, gz) = (wx - W.DEPOT[0], wz - W.DEPOT[1])
    (tx, tz) = _dir_at(ln["pts"], (wx, wz))
    rot = math.degrees(math.atan2(-tx, -tz))
    (perx, perz) = (tz, -tx)
    for side in (-1.0, 1.0):
        (lx, lz) = (gx + perx * side * 7.2, gz + perz * side * 7.2)
        g = yard(lx, lz)
        _leg(b, lx, lz, g + 2.90, (1.10, 1.10), "Level_Stone",
             rot=(0.0, 0.0, rot), bevel=0.04)
        _box(b, lx, lz, g + 2.98, (1.34, 1.34, 0.16), "Level_Snow",
             rot=(0.0, 0.0, rot), bevel=0.04)
    # Перемычка лежит поперёк дороги: длинная ось её сечения — местная X.
    gmax = max(yard(gx + perx * 3.4, gz + perz * 3.4),
               yard(gx - perx * 3.4, gz - perz * 3.4))
    _box(b, gx, gz, gmax + 4.30, (15.2, 0.34, 0.50), "Level_Rust",
         rot=(0.0, 0.0, rot), bevel=0.03)
    _box(b, gx, gz, gmax + 4.90, (6.4, 0.22, 0.86), "Level_Orange",
         rot=(0.0, 0.0, rot), bevel=0.0)
    # Забор: звенья в обе стороны от ворот вдоль границы двора, дальше обрыв.
    edge = W.DEPOT_YARD[1] - W.DEPOT[0]
    for side in (-1.0, 1.0):
        g = yard(edge, gz)
        for k in range(FENCE_BAYS):
            zc = gz + side * (9.0 + k * FENCE_SPAN)
            _leg(b, edge, zc, yard(edge, zc) + 1.40, (0.16, 0.16),
                 "Level_WoodDark", bevel=0.0)
            for hz in (1.05, 0.62):
                _box(b, edge, zc + side * FENCE_SPAN * 0.5, g + hz,
                     (0.10, FENCE_SPAN, 0.10), "Level_WoodDark", bevel=0.0)


def _mast(b, lx, lz):
    """Сигнальная мачта у горловины: основание, мачта, траверса с линзой.

    Стоит южнее съезда бокового пути: прибывающий поезд идёт с юга, и мачта —
    первое, что он видит во дворе, а не последний кадр уезжающего вида.
    """
    g = yard(lx, lz)
    _leg(b, lx, lz, g + 0.70, (1.10, 1.10), "Level_Rust", bevel=0.03)
    _box(b, lx, lz, g + 0.78, (0.72, 0.72, 0.14), "Level_Snow", bevel=0.03)
    _tube(b, [(lx, lz, g + 0.60), (lx, lz, g + 3.60), (lx, lz, g + 7.10)],
          [(0.19, 0.19), (0.145, 0.145), (0.11, 0.11)], "Level_Metal", n=6)
    for hz in (2.30, 3.85, 5.30):
        _box(b, lx, lz, g + hz, (0.62, 0.10, 0.09), "Level_Rust", bevel=0.0)
    _box(b, lx + 0.42, lz, g + 6.30, (1.15, 0.12, 0.12), "Level_Metal",
         bevel=0.02)
    _tube(b, [(lx + 0.86, lz, g + 6.30), (lx + 0.86, lz, g + 6.72)],
          [(0.17, 0.17), (0.17, 0.17)], "Level_Window", n=8)
    _box(b, lx + 0.86, lz, g + 6.90, (0.40, 0.34, 0.12), "Level_Snow",
         bevel=0.03)
    _box(b, lx, lz, g + 7.16, (0.30, 0.30, 0.12), "Level_Snow", bevel=0.03)


def _cross_x(pts, x: float):
    """Точка, где ломаная пересекает прямую x = const: (x, z) в мире."""
    for (a, b) in zip(pts, pts[1:]):
        (ax, az) = a
        (bx, bz) = b
        if (ax - x) * (bx - x) > 0.0:
            continue
        if abs(bx - ax) < 1e-9:
            return (x, max(az, bz))
        t = (x - ax) / (bx - ax)
        return (x, az + (bz - az) * t)
    raise RuntimeError("линия не пересекает x = %.1f" % x)


def _dir_at(pts, at):
    """Направление ломаной в точке `at`: единичный вектор (x, z) в мире."""
    best = None
    for (a, b) in zip(pts, pts[1:]):
        ln = math.hypot(b[0] - a[0], b[1] - a[1])
        if ln <= 1e-6:
            continue
        t = max(0.0, min(1.0, ((at[0] - a[0]) * (b[0] - a[0]) +
                               (at[1] - a[1]) * (b[1] - a[1])) / (ln * ln)))
        p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        d = math.hypot(p[0] - at[0], p[1] - at[1])
        if best is None or d < best[0]:
            best = (d, ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln))
    return best[1]


def build_cell_depot(materials, i: int, j: int):
    """Объекты депо клетки: пустой список, если клетка не депо."""
    if (i, j) != W.DEPOT_CELL:
        return []
    return [build_depot(materials, i, j), build_siding(materials, i, j)]
