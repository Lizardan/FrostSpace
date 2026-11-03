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
import level_parts as LP
import transport_parts as TP
import world_layout as W

import cell_props as CP

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
    profile = level_lib.outward_profile(profile)
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

#: Наносы по краям двора. Внутри двор чистит снегоубор R11, и всё, что не
#: убрано, лежит у полотна: две полосы — западнее бокового пути и восточнее
#: магистрали. Внутри коридоров наносов нет: там ходят составы, снег между
#: путями — помеха, а не украшение.
YARD_DRIFTS = (
    (20.5, 34.0, 3.4, 4.6, 1.05, 1.0),
    (21.0, 25.0, 3.0, 4.4, 1.15, 2.0),
    (20.0, -34.0, 3.2, 5.0, 1.05, 3.0),
    (21.5, -56.0, 3.0, 5.2, 0.95, 4.0),
    (20.5, -92.0, 3.4, 5.4, 1.10, 5.0),
    (19.5, -118.0, 3.0, 4.0, 0.85, 6.0),
    (-57.0, 36.0, 4.6, 4.2, 0.95, 7.0),
    (-59.0, 50.0, 5.0, 4.0, 1.00, 8.0),
    (-56.0, -60.0, 5.2, 4.6, 1.05, 9.0),
    (-54.0, -104.0, 5.0, 4.2, 0.90, 10.0),
    (-58.0, -122.0, 4.4, 3.8, 0.80, 11.0),
)

#: Груз на платформе: штабель запасных шпал у северного торца, где состав не
#: стоит. Платформа — единственное ровное место двора, и груз на ней читается
#: сразу. Именно штабель шпал, а не ящики: у готовых ящиков и бочек из
#: `level_parts` есть детали тоньше 6 см (заклёпки, кнопки приборов), а
#: проверка стиля такие оболочки в ассете уровня не пропускает.
PLATFORM_CARGO = ((-2.6, 30.0, 0.0), (-1.4, 32.4, 38.0))
CARGO_PILE = (-2.9, 34.0)

#: Зарядный узел: по канону они есть в депо и на станции, и между ними — ничего
#: (`WORLD.md`). Стоит у западного края платформы, спиной к путям.
CHARGE_POST = (2.4, 24.0)

#: Портальный кран над путём. Севернее платформы: южнее она подходит к пути
#: вплотную (габарит кузова), и опора встала бы в вагон. Западная опора — между
#: магистралью и боковым путём, восточная — на открытом дворе. Высота моста
#: 9,2 м: состав проходит под крюком с запасом, а сам кран читается силуэтом
#: с платформы.
CRANE_X = (-19.0, 3.0)
CRANE_Z = (43.0, 53.0)
CRANE_RISE = 9.2

#: Крытый ангар для груза. Восточнее платформы и севернее навеса: там открытый
#: двор, ангар не задевает ни габарит вагона, ни рампу. Западная сторона
#: открыта — груз заезжает с платформы.
HANGAR = (4.5, 18.5, 34.0, 56.0)
HANGAR_EAVE = 5.4
HANGAR_RIDGE = 1.6


def _platform_level(lz: float) -> float:
    """Отметка настила платформы в локальной точке двора."""
    return head(0.0, lz) + PLATFORM_RISE


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

    # -- кран над путём и ангар для груза ------------------------------------
    _crane(b)
    _hangar(b, materials)

    # -- отделка, свет и обстановка ------------------------------------------
    _shed_detail(b)
    _platform_detail(b)
    _crane_detail(b)
    _hangar_detail(b)
    _road_exit(b)
    _yard_props(b, materials)

    # -- ворота, забор и мачта ----------------------------------------------
    _gate(b)
    _mast(b, W.DEPOT_MAST[0] - W.DEPOT[0], W.DEPOT_MAST[1] - W.DEPOT[1])

    # -- обстановка двора ----------------------------------------------------
    # Наносы по краям, груз на платформе, зарядный узел. Всё вливается в тот
    # же объект: двор — одна формация, и десяток отдельных объектов ломал бы
    # именно то, ради чего чанк и делается одним куском.
    for (lx, lz, rx, ry, h, seed) in YARD_DRIFTS:
        CP.drift(b, lx, lz, yard(lx, lz), rx, ry, h, seed)
    for (lx, lz, rot) in PLATFORM_CARGO:
        CP.merge(b, LP.build_log_pile(materials), lx, lz, _platform_level(lz),
                 sink=-0.02, rot=rot)
    CP.merge(b, LP.build_log_pile(materials), CARGO_PILE[0], CARGO_PILE[1],
             _platform_level(CARGO_PILE[1]), sink=-0.02, rot=15.0)
    CP.charging_post(b, CHARGE_POST[0], CHARGE_POST[1],
                     _platform_level(CHARGE_POST[1]))
    return b.build("Depot_%d_%d" % (i, j), recalc=False)


def _crane(b):
    """Портальный кран над путём: опоры, мост, тележка, крюк и кабина.

    Кран стоит севернее платформы, где по обе стороны пути есть место под
    опоры. Южнее платформа подходит к пути вплотную (габарит кузова), и любая
    опора оказалась бы в вагоне. Мост идёт поперёк пути, тележка — над осью
    магистрали, крюк висит на высоте 6,3 м: состав проходит под ним с запасом.
    """
    (xw, xe) = CRANE_X
    (z0, z1) = CRANE_Z
    zc = (z0 + z1) * 0.5
    xc = (xw + xe) * 0.5
    top = yard(xc, zc) + CRANE_RISE

    for lx in (xw, xe):
        for lz in (z0, z1):
            # Пятка уходит в грунт: плита не лежит на рельефе плоскостью.
            _box(b, lx, lz, yard(lx, lz) + 0.02, (1.10, 1.10, 0.30),
                 "Level_Stone", bevel=0.04)
            _leg(b, lx, lz, top, (0.55, 0.55), "Level_Metal", bevel=0.03)

    # Два моста по сторонам площадки и торцевые связи: тележка едет между ними.
    for lz in (z0, z1):
        _box(b, xc, lz, top + 0.25, (xe - xw + 0.90, 0.42, 0.50),
             "Level_Metal", bevel=0.02)
    for lx in (xw, xe):
        _box(b, lx, zc, top + 0.25, (0.42, z1 - z0 + 0.90, 0.50),
             "Level_Metal", bevel=0.02)

    # Тележка над осью пути, трос и крюк. Трос — 10 см: тоньше стиль не
    # пропускает, а толще читался бы балкой.
    track_x = W.DEPOT_MAIN_X - W.DEPOT[0]
    _box(b, track_x, zc, top - 0.10, (2.40, 2.00, 0.70), "Level_Rust",
         bevel=0.03)
    _box(b, track_x, zc, top - 1.60, (0.10, 0.10, 2.20), "Level_Metal",
         bevel=0.0)
    _box(b, track_x, zc, top - 2.85, (0.55, 0.55, 0.55), "Level_Rust",
         bevel=0.03)

    # Кабина оператора на западной опоре: окно проходит сквозь стену, поэтому
    # его грань не совпадает с гранью кабины.
    cab_z = z0 + 1.8
    cab_floor = yard(xw, cab_z)
    _box(b, xw, cab_z, cab_floor + 4.60, (2.00, 2.40, 2.20), "Level_Metal",
         bevel=0.03)
    _box(b, xw + 1.02, cab_z, cab_floor + 4.90, (0.08, 1.20, 0.90),
         "Level_Window", bevel=0.0)
    # Маячок на восточной опоре: единственный тёплый огонь крана.
    _box(b, xe, z0, top + 0.62, (0.22, 0.22, 0.24), "Level_Orange", bevel=0.0)


def _hangar(b, materials):
    """Крытый ангар для груза: плита, столбы, двускатная кровля и снег.

    Стоит восточнее платформы и севернее навеса, на открытом дворе. Западная
    сторона открыта — груз заезжает с платформы; восточная и северная стены
    закрыты, чтобы ангар читался складом, а не ещё одним навесом.
    """
    (x0, x1, z0, z1) = HANGAR
    xc = (x0 + x1) * 0.5
    zc = (z0 + z1) * 0.5
    half = (x1 - x0) * 0.5
    zs = _span(z0, z1, 4.0)

    # Плита: верх на 0,15 м над двором, юбка в грунт. Верх не совпадает с
    # рельефом, поэтому копланарной пары с землёй нет.
    floor_profile = [(-half, 0.15), (half, 0.15), (half, -BURY),
                     (-half, -BURY)]
    _sweep(b, [(xc, z) for z in zs], floor_profile, "Level_Stone",
           [yard(xc, z) for z in zs], "плита ангара")

    for lx in (x0 + 0.7, x1 - 0.7):
        for lz in (z0 + 0.7, zc, z1 - 0.7):
            _leg(b, lx, lz, yard(lx, lz) + HANGAR_EAVE + 0.25,
                 (0.34, 0.34), "Level_WoodDark", bevel=0.02)

    # Двускатная кровля: профиль поперёк, протяжка вдоль. Снег лежит на ней
    # отдельным профилем с зазором в 2 см — иначе грани совпали бы.
    roof = [(-half - 0.50, HANGAR_EAVE), (half + 0.50, HANGAR_EAVE),
            (half + 0.50, HANGAR_EAVE + 0.30),
            (0.0, HANGAR_EAVE + HANGAR_RIDGE),
            (-half - 0.50, HANGAR_EAVE + 0.30)]
    _sweep(b, [(xc, z) for z in zs], roof, "Level_Roof",
           [yard(xc, z) for z in zs], "кровля ангара", buried=False)

    snow = [(-half - 0.20, HANGAR_EAVE + 0.28),
            (half + 0.20, HANGAR_EAVE + 0.28),
            (half + 0.20, HANGAR_EAVE + 0.50),
            (0.0, HANGAR_EAVE + HANGAR_RIDGE + 0.18),
            (-half - 0.20, HANGAR_EAVE + 0.50)]
    _sweep(b, [(xc, z) for z in zs], snow, "Level_Snow",
           [yard(xc, z) for z in zs], "снег на ангаре", buried=False)

    # Восточная и северная стены. Низ стены на плите, верх входит в кровлю.
    wall_h = HANGAR_EAVE + 0.15
    _box(b, x1 - 0.10, zc, yard(xc, zc) + 0.15 + wall_h * 0.5,
         (0.20, z1 - z0, wall_h), "Level_Metal", bevel=0.02)
    _box(b, xc, z1 - 0.10, yard(xc, zc) + 0.15 + wall_h * 0.5,
         (x1 - x0, 0.20, wall_h), "Level_Metal", bevel=0.02)

    # Груз внутри и одна лампа под кровлей: двор не гирлянда, но ангар не
    # должен быть чёрной дырой.
    CP.merge(b, LP.build_log_pile(materials), xc - 2.0, zc,
             yard(xc, zc) + 0.15, sink=-0.02, rot=10.0)
    _box(b, xc, zc, yard(xc, zc) + HANGAR_EAVE - 0.55, (0.08, 0.08, 0.60),
         "Level_Metal", bevel=0.0)
    _box(b, xc, zc, yard(xc, zc) + HANGAR_EAVE - 0.95, (0.50, 0.50, 0.24),
         "Level_Window", bevel=0.06)


def _shed_detail(b):
    """Отделка коробки: рёбра стен, вентиляция, труба, фонарь и вывеска.

    Коробка без отделки читается параллелепипедом: у неё нет масштаба.
    Рёбра дают вертикальный ритм, вентиляция и труба — силуэт, фонарь и
    вывеска — точку, куда смотрит человек у ворот.
    """
    (sx0, sx1, sz0, sz1) = local(W.DEPOT_SHED)
    shaxis = (sx0 + sx1) * 0.5
    face = sx1 + DOOR_PROUD
    dz = (sz0 + sz1) * 0.5 + 6.0

    # Вертикальные рёбра на восточной стене, кроме проёма ворот.
    z = sz0 + 2.0
    while z < sz1 - 1.0:
        if abs(z - dz) > SHED_DOOR[0] * 0.5 + 1.2:
            g = yard(sx1, z)
            _box(b, sx1 + 0.06, z, g + 3.10, (0.12, 0.12, 6.20),
                 "Level_Metal", bevel=0.0)
        z += 3.2

    # Вентиляция на коньке: колпак и плита поверх него.
    for vz in (sz0 + 18.0, (sz0 + sz1) * 0.5, sz1 - 18.0):
        g = yard(shaxis, vz)
        _box(b, shaxis, vz, g + 8.75, (1.30, 1.10, 0.55), "Level_Metal",
             bevel=0.03)
        _box(b, shaxis, vz, g + 9.06, (1.55, 1.35, 0.12), "Level_Roof",
             bevel=0.03)

    # Труба котельной: ствол, юбка и оголовок.
    cx = shaxis + 6.0
    cz = sz0 + 12.0
    g = yard(cx, cz)
    _tube(b, [(cx, cz, g + 8.0), (cx, cz, g + 11.2)],
          [(0.38, 0.38), (0.32, 0.32)], "Level_Rust", n=10)
    _box(b, cx, cz, g + 11.35, (0.90, 0.90, 0.18), "Level_Metal", bevel=0.03)

    # Фонарь над воротами и вывеска.
    gy = yard(shaxis, dz)
    _box(b, face + 0.30, dz, gy + 6.30, (0.60, 0.18, 0.18), "Level_Metal",
         bevel=0.0)
    _box(b, face + 0.62, dz, gy + 6.18, (0.42, 0.42, 0.26), "Level_Window",
         bevel=0.06)
    _box(b, face + 0.10, dz, gy + 6.85, (0.14, 6.20, 0.90), "Level_Orange",
         bevel=0.02)
    _box(b, face + 0.20, dz, gy + 6.85, (0.10, 5.40, 0.30), "Level_Metal",
         bevel=0.0)

    # Южный торец смотрит на въезд: у него должен быть фасад, а не глухая
    # стена. Ворота, окна и вывеска дают коробке лицо со стороны дороги.
    sz = sz0
    g = yard(shaxis, sz)
    _box(b, shaxis, sz - 0.10, g + 2.80, (12.00, 0.12, 5.40), "Level_Metal",
         bevel=0.02)
    _box(b, shaxis, sz - 0.05, g + 5.62, (13.00, 0.30, 0.40), "Level_Rust",
         bevel=0.02)
    for sx in (-6.20, 6.20):
        _box(b, shaxis + sx, sz - 0.05, g + 2.80, (0.40, 0.30, 5.40),
             "Level_Rust", bevel=0.02)
    _box(b, shaxis, sz - 0.16, g + 1.10, (11.60, 0.06, 0.30), "Level_Orange",
         bevel=0.0)
    for sx in (-5.0, 5.0):
        _box(b, shaxis + sx, sz - 0.10, g + 4.10, (1.60, 0.10, 1.00),
             "Level_Window", bevel=0.0)
    _box(b, shaxis, sz - 0.12, g + 6.55, (6.00, 0.12, 0.80), "Level_Orange",
         bevel=0.02)
    _box(b, shaxis, sz - 0.20, g + 6.55, (5.20, 0.06, 0.28), "Level_Metal",
         bevel=0.0)


def _platform_detail(b):
    """Платформа: линия безопасности, фонари, скамьи и вывеска.

    Платформа была ровной плитой с валом. Линия вдоль края даёт габарит,
    фонари — вертикали, скамьи — масштаб человека: без них восемьдесят
    метров настила читаются как взлётная полоса.
    """
    (px0, px1, pz0, pz1) = local(W.DEPOT_PLATFORM)

    # Линия безопасности у западного края: прорезана в настиле, не лежит на нём.
    zs = _span(pz0, pz1, 4.0)
    profile = [(-0.07, -0.03), (0.07, -0.03), (0.07, 0.05), (-0.07, 0.05)]
    _sweep(b, [(px0 + 0.45, z) for z in zs], profile, "Level_Orange",
           [_platform_level(z) for z in zs], "линия безопасности",
           buried=False)

    # Фонари: стойка, кронштейн в сторону пути и головка.
    for lz in (pz0 + 8.0, pz0 + 28.0, pz0 + 48.0, pz0 + 68.0):
        top = _platform_level(lz) + 4.10
        _leg(b, px1 - 0.55, lz, top, (0.14, 0.14), "Level_Metal", bevel=0.0)
        _box(b, px1 - 1.30, lz, top - 0.06, (1.50, 0.12, 0.12),
             "Level_Metal", bevel=0.0)
        _box(b, px1 - 2.00, lz, top - 0.22, (0.42, 0.42, 0.24),
             "Level_Window", bevel=0.06)

    # Скамьи: сиденье, спинка и две ножки.
    for lz in (pz0 + 20.0, pz0 + 44.0):
        g = _platform_level(lz)
        _box(b, px1 - 1.40, lz, g + 0.44, (0.62, 1.80, 0.10),
             "Level_Wood", bevel=0.03)
        _box(b, px1 - 1.05, lz, g + 0.74, (0.10, 1.70, 0.42),
             "Level_Wood", bevel=0.03)
        for lz2 in (lz - 0.85, lz + 0.85):
            _box(b, px1 - 1.40, lz2, g + 0.22, (0.10, 0.10, 0.44),
                 "Level_Metal", bevel=0.0)

    # Вывеска на двух стойках у южного конца платформы.
    sz = pz0 + 3.0
    g = _platform_level(sz)
    for sx in (-1.0, 1.0):
        _leg(b, px1 - 0.80 + sx * 0.90, sz, g + 2.90, (0.10, 0.10),
             "Level_Metal", bevel=0.0)
    _box(b, px1 - 0.80, sz, g + 3.05, (2.10, 0.12, 0.85), "Level_Rust",
         bevel=0.02)
    _box(b, px1 - 0.80, sz - 0.07, g + 3.05, (1.80, 0.06, 0.55),
         "Level_Window", bevel=0.0)


def _crane_detail(b):
    """Кран: раскосы, лестница и полосы на пятках опор.

    Без раскосов кран — две вертикальные палки и перекладина; раскосы дают
    ему ферму и читаемый силуэт. Лестница объясняет, как на него попадают.
    """
    (xw, xe) = CRANE_X
    (z0, z1) = CRANE_Z
    base = yard(xw, (z0 + z1) * 0.5)

    for lz in (z0, z1):
        for (ax, az, bx, bz) in ((xw, 1.8, xe, 7.0), (xe, 1.8, xw, 7.0)):
            _tube(b, [(ax, lz, base + az), (bx, lz, base + bz)],
                  [(0.07, 0.07)] * 2, "Level_Metal", n=6)

    # Лестница на западной опоре: два поручня и перекладины.
    for lz in (z0 - 0.26, z0 + 0.26):
        _box(b, xw - 0.33, lz, base + 4.60, (0.06, 0.06, 8.60),
             "Level_Metal", bevel=0.0)
    for i in range(18):
        _box(b, xw - 0.33, z0, base + 0.60 + i * 0.45, (0.06, 0.46, 0.06),
             "Level_Metal", bevel=0.0)

    # Полосы на пятках: опору видно в метели.
    for lx in (xw, xe):
        for lz in (z0, z1):
            _box(b, lx, lz, yard(lx, lz) + 0.42, (0.62, 0.62, 0.45),
                 "Level_Orange", bevel=0.0)


def _hangar_detail(b):
    """Ангар: стеллажи, две лампы, верстак и вывеска над проёмом."""
    (x0, x1, z0, z1) = HANGAR
    xc = (x0 + x1) * 0.5
    zc = (z0 + z1) * 0.5
    floor = yard(xc, zc) + 0.15

    # Стеллажи у восточной стены: стойки и три полки.
    for rz in (zc - 4.0, zc + 4.0):
        for rx in (x1 - 2.6, x1 - 1.2):
            for ry in (rz - 2.2, rz + 2.2):
                _leg(b, rx, ry, floor + 3.00, (0.10, 0.10),
                     "Level_Metal", bevel=0.0)
        for h in (0.90, 1.90, 2.80):
            _box(b, x1 - 1.90, rz, floor + h, (1.40, 4.60, 0.08),
                 "Level_WoodDark", bevel=0.0)

    # Две лампы под кровлей.
    for lz in (zc - 6.0, zc + 6.0):
        _box(b, xc, lz, floor + 4.45, (0.08, 0.08, 0.70), "Level_Metal",
             bevel=0.0)
        _box(b, xc, lz, floor + 4.05, (0.45, 0.45, 0.22), "Level_Window",
             bevel=0.06)

    # Верстак у западного края.
    _box(b, x0 + 1.20, zc, floor + 0.88, (1.00, 3.20, 0.12),
         "Level_Wood", bevel=0.02)
    for lz in (zc - 1.40, zc + 1.40):
        _box(b, x0 + 1.20, lz, floor + 0.42, (0.10, 0.10, 0.84),
             "Level_Metal", bevel=0.0)

    # Вывеска над открытым западным краем.
    _box(b, x0 - 0.10, zc, floor + HANGAR_EAVE + 0.55, (0.12, 4.20, 0.80),
         "Level_Orange", bevel=0.02)
    _box(b, x0 - 0.18, zc, floor + HANGAR_EAVE + 0.55, (0.08, 3.60, 0.28),
         "Level_Metal", bevel=0.0)


def _road_exit(b):
    """Дворовая дорога от ворот внутрь и будка у выезда.

    Снаружи дорога 0 подходит к границе двора, а внутри неё не было: ворота
    стояли в снегу, и выезд читался как дыра в заборе. Дорога идёт от створа
    ворот вглубь двора и кончается площадкой у коробки; будка объясняет, кто
    эти ворота открывает.
    """
    ln = TP.line_of("road 0")
    (wx, wz) = _cross_x(ln["pts"], W.DEPOT_YARD[1])
    (gx, gz) = (wx - W.DEPOT[0], wz - W.DEPOT[1])
    (tx, tz) = _dir_at(ln["pts"], (wx, wz))

    pts = [(gx - tx * d, gz - tz * d) for d in (0.0, 4.0, 9.4)]
    profile = [(-4.60, 0.07), (4.60, 0.07), (4.60, -BURY), (-4.60, -BURY)]
    _sweep(b, pts, profile, "Level_Stone",
           [yard(x, z) for (x, z) in pts], "дворовая дорога")

    # Снеговые бровки по краям: дорога прорезана, а не положена на снег.
    bank = [(-0.35, -0.20), (0.35, -0.20), (0.35, 0.30), (-0.35, 0.30)]
    for side in (-1.0, 1.0):
        edge = [(x + tz * side * 4.95, z - tx * side * 4.95)
                for (x, z) in pts]
        _sweep(b, edge, bank, "Level_Snow",
               [yard(x, z) for (x, z) in edge], "бровка дороги")

    # Будка у выезда: стены, окно на дорогу, дверь, кровля и лампа. Стоит в
    # предворотной площадке севернее дороги, вне рампы: рампа занимает
    # z −20…20, и будка в её габарите встала бы на наклонный настил.
    (bx, bz) = (gx - tx * 4.0 + tz * 12.0, gz - tz * 4.0 - tx * 12.0)
    g = yard(bx, bz)
    _box(b, bx, bz, g + 1.35, (3.00, 3.00, 2.70), "Level_WoodDark",
         bevel=0.03)
    _box(b, bx, bz, g + 2.78, (3.60, 3.60, 0.22), "Level_Roof", bevel=0.04)
    _box(b, bx, bz, g + 2.96, (3.20, 3.20, 0.12), "Level_Snow", bevel=0.03)
    _box(b, bx - 1.52, bz, g + 1.70, (0.10, 1.40, 0.90), "Level_Window",
         bevel=0.0)
    _box(b, bx, bz - 1.52, g + 1.10, (0.90, 0.10, 2.00), "Level_Metal",
         bevel=0.0)
    _box(b, bx + 1.70, bz, g + 2.30, (0.40, 0.18, 0.18), "Level_Metal",
         bevel=0.0)
    _box(b, bx + 1.92, bz, g + 2.18, (0.30, 0.30, 0.22), "Level_Window",
         bevel=0.06)


def _yard_props(b, materials):
    """Топливный бак, катушки с кабелем и запас шпал: двор — хозяйство."""
    # Топливный бак на двух ложах, с трубой и вентилем.
    tx, tz = -46.0, -58.0
    g = yard(tx, tz)
    _tube(b, [(tx - 4.0, tz, g + 1.90), (tx + 4.0, tz, g + 1.90)],
          [(1.55, 1.55)] * 2, "Level_Metal", n=12)
    for sx in (-2.60, 2.60):
        _box(b, tx + sx, tz, g + 0.55, (1.20, 2.40, 1.10),
             "Level_Stone", bevel=0.03)
    _box(b, tx + 4.10, tz, g + 1.10, (0.30, 0.30, 2.20), "Level_Rust",
         bevel=0.0)
    _box(b, tx + 4.10, tz, g + 2.30, (0.62, 0.62, 0.18), "Level_Orange",
         bevel=0.0)

    # Катушки с кабелем: два диска на общей раме.
    rx, rz = -40.0, 44.0
    g = yard(rx, rz)
    for dz in (-1.10, 1.10):
        _tube(b, [(rx - 0.45, rz + dz, g + 1.25),
                  (rx + 0.45, rz + dz, g + 1.25)],
              [(1.15, 1.15)] * 2, "Level_WoodDark", n=12)
    _box(b, rx, rz, g + 0.55, (1.20, 3.20, 0.22), "Level_Metal", bevel=0.0)

    # Запас шпал у восточной стены коробки.
    for lz in (-44.0, -54.0):
        CP.merge(b, LP.build_log_pile(materials), -33.0, lz,
                 yard(-33.0, lz), sink=-0.02, rot=90.0)


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
        _leg(b, lx, lz, g + 4.10, (1.10, 1.10), "Level_Stone",
             rot=(0.0, 0.0, rot), bevel=0.04)
        _box(b, lx, lz, g + 4.18, (1.34, 1.34, 0.16), "Level_Snow",
             rot=(0.0, 0.0, rot), bevel=0.04)
    # Перемычка лежит поперёк дороги: длинная ось её сечения — местная X.
    gmax = max(yard(gx + perx * 3.4, gz + perz * 3.4),
               yard(gx - perx * 3.4, gz - perz * 3.4))
    _box(b, gx, gz, gmax + 4.30, (15.2, 0.34, 0.50), "Level_Rust",
         rot=(0.0, 0.0, rot), bevel=0.03)
    _box(b, gx, gz, gmax + 4.90, (6.4, 0.22, 0.86), "Level_Orange",
         rot=(0.0, 0.0, rot), bevel=0.0)
    # Тёмная полоса на вывеске: пустое оранжевое поле читается как заготовка.
    # Смещение — по локальной оси толщины (после поворота это world +x), иначе
    # полоса уезжает на торец щита и с дороги не видна.
    _box(b, gx + 0.12, gz, gmax + 4.90, (5.4, 0.06, 0.28), "Level_Metal",
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
