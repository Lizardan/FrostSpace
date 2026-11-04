"""Станция «Север-7» в клетке (0,0): комплекс отсеков, платформа и гигантский ангар.

Зачем это в чанке. Узел мира — часть клетки, а не набор префабов в сцене
(ADR 0037, ADR 0039): меши живут в локальных осях клетки, отметки берутся от
пола узла, и весь узел выходит частями `Station_*_0_0.fbx`, из которых
`sync_cell_prefabs.cs` собирает префаб.

Что здесь происходит по замыслу:

- **Пол узла держит одну отметку** (ADR 0041): `PAD_LEVEL0["station"]`.
  Магистраль приходит двор по оси x = 0 и обрывается буфером у северного края
  платформы, поэтому посадочная платформа лежит вдоль пути, как в депо, а не
  поперёк. До правки 2026-11-04 нить шла через двор по диагонали 52° и резала
  и платформу, и угол ремзоны — выпрямлена в `world_layout.RAIL`.
- **Станция — комплекс, а не изба** (WORLD.md): несколько отсеков, связанных
  коридорами. Медблок, диспетчерская, склад, жилая секция и ремзона набраны
  одним модулем: наружные стены, перегородки, кровля.
- **Ремзона вскрыта.** Комнаты без входа в кадр — работа, которой никто не
  видит: южная стена и кровля ремзоны сняты, и с платформы видно станки,
  верстаки и стеллажи. Тот же приём, что у коробки депо с сорванной кровлей.
- **Гигантский ангар** держит обломок космического корабля: пролёт 70 × 80 м,
  высота до конька 19 м, западные ворота раскрыты. Корпус собран здесь же
  (`Station_Wreck`), движок приезжает меню-моделью; геометрия ангара — часть
  клетки.
- **Огорожен только жилой комплекс.** Периметр на весь двор (204 × 144) не
  сходился ни с одной из пяти нитей, которые входят в узел: на него пришлось
  бы пять створов. Стены держат западный комплекс с двумя воротами — на дорогу
  с запада и на переход к платформе, — а грузовой двор с ангаром остаётся
  открытым, как двор депо.

Запуск (штатно — из пайплайна):

    python Tools/Blender/build_all.py --asset cells --check --preview

Координаты — локальные оси клетки: X на восток, Y в Blender = −z мира, вверх Z.
"""

from __future__ import annotations

import math

import level_lib
import level_parts as LP
import world_layout as W

import cell_props as CP

#: Узел станции: якорь «Север-7» в мире (0, 0), клетка (0, 0).
STATION_NODE = W.node("station")

#: Клетка станции и её центр в мире.
CELL = W.cell_of(STATION_NODE["x"], STATION_NODE["z"])

#: Пол узла: одна отметка на весь двор (ADR 0041).
FLOOR = W.PAD_LEVEL0["station"]

#: Верх настила платформы: плита 0,90 плюс снеговая шапка 0,14. Всё, что
#: стоит на платформе (навесы, скамейки, ящики, фонари), меряется отсюда,
#: а не от снега — тот же урок, что депо поплатило за плиту вместо шапки.
DECK = FLOOR + 0.90 + 0.14

#: Посадочная платформа вдоль главного пути: путь идёт по x = 0, платформа —
#: восточнее, как грузовой перрон депо. Отметка настила +0,90 над полом узла:
#: выше пол вагона не пустит (вагон стоит на головке, пол на 1,00 над ней).
#: Южный край — там, где кривая кончилась и путь лёг на ось (замер: −50 м по z);
#: северный — у буферного упора, дальше пути нет.
PLATFORM = (6.0, 26.0, -50.0, 45.0)
PLATFORM_RISE = 0.90
CANOPY_Z = ((-42.0, -22.0), (-12.0, 8.0), (20.0, 40.0))

#: Буферный упор: северный конец магистрали во дворе станции.
BUFFER = (0.0, 45.0)

#: Комплекс отсеков: западная половина узла.
COMPLEX = (-96.0, -40.0, -56.0, 56.0)
WALL_T = 0.34
PART_T = 0.24
ROOM_H = 4.40

#: (x0, x1, z0, z1, имя, какие стены снять, есть ли кровля)
ROOMS = (
    (-94.0, -68.0, 22.0, 54.0, "диспетчерская", (), True),
    (-66.0, -42.0, 22.0, 54.0, "медблок", (), True),
    (-94.0, -68.0, -6.0, 20.0, "склад", (), True),
    (-66.0, -42.0, -6.0, 20.0, "жилая секция", (), True),
    (-94.0, -42.0, -54.0, -10.0, "ремзона", ("south", "roof"), True),
)

#: Гигантский ангар: восточная половина. Пролёт держит корабль, поэтому стены
#: не нужны — крышу несут фермы, как в депо. Сдвинут на 12 м к северу: южнее
#: его стены через двор идёт нога дороги на склад, и прежний край стены встал
#: бы в снежный вал полотна.
HANGAR = (34.0, 104.0, -28.0, 52.0)
HANGAR_WALL_H = 8.20
HANGAR_RISE = 19.00
HANGAR_TRUSS_SPAN = 6.0
HANGAR_DOOR = (34.0, 34.6)      # раскрытые западные ворота: x стены, x створа
HANGAR_DOOR_Z = (-4.0, 28.0)    # створ против платформы: 32 м проёма

#: Центр корабля под фермами и его ось: 34° к северу, нос к воротам.
WRECK_CENTER = (66.0, 12.0)
WRECK_AXIS_DEG = 34.0

#: Ограда жилого комплекса: стены держат только западную половину узла, грузовой
#: двор с ангаром открыт. Ворота — на западную ногу дороги (она приходит к двору
#: на отметке z ≈ 20) и на пешеходный проход к платформе: он у северного торца,
#: потому что переход через путь возможен только севернее буферного упора.
COMPOUND = (-102.0, -32.0, -62.0, 62.0)
GATE_W = (10.0, 30.0)
GATE_E = (30.0, 50.0)
PLINTH = (0.42, 0.64)
POST = (0.14, 0.14, 1.62)
RAIL_Z = (0.94, 1.46)
PILLAR = (0.52, 0.52, 2.05)
PIER = (0.60, 0.60, 2.70)

#: Декор узла: наносы у стен и платформ, заносы по горизонту, камни, шипы.
#: Ни одна точка не стоит в коридоре пути (ось x = 0 до буфера) и не легла на
#: полотно дороги, которое идёт через юго-восток двора.
DRIFTS = ((-90.0, 58.0, 4.2, 3.0, 1.00, 1.0), (-40.0, 58.0, 3.8, 2.6, 0.95, 2.0),
          (-38.0, -58.0, 4.6, 3.2, 1.10, 3.0), (30.0, 66.0, 3.6, 2.8, 0.90, 4.0),
          (28.0, -44.0, 4.0, 2.6, 1.00, 5.0), (108.0, 30.0, 4.4, 3.0, 1.05, 6.0),
          (2.0, 66.0, 3.4, 2.6, 0.85, 7.0), (2.0, -66.0, 3.2, 2.4, 0.80, 8.0))
BILLOWS = ((-60.0, 74.0, 8.4), (60.0, 72.0, 9.2), (112.0, -30.0, 7.6),
           (-108.0, -30.0, 8.0), (150.0, -70.0, 6.8))
ROCKS = ((-34.0, 40.0, 0.4), (30.0, 20.0, 2.7), (-100.0, -64.0, 1.1),
         (110.0, 44.0, 2.2), (30.0, -20.0, 0.7), (6.0, 52.0, 1.9))
SHARDS = ((-36.0, 24.0, 1.35), (30.0, -50.0, 1.10), (106.0, 12.0, 1.50))
PINES = ((-120.0, 40.0, 0.6), (-118.0, -20.0, 2.4), (120.0, 60.0, 3.1),
         (-110.0, 70.0, 4.8), (118.0, -60.0, 5.5), (-124.0, -60.0, 6.9),
         (90.0, 78.0, 7.5), (-70.0, 78.0, 8.2), (40.0, -78.0, 9.6),
         (-46.0, -78.0, 10.4))

#: Наполнение вскрытой ремзоны: станки, верстаки, стеллажи, ящики.
BENCHES = ((-88.0, -46.0, 0.0), (-88.0, -34.0, 0.0), (-88.0, -22.0, 0.0),
           (-56.0, -46.0, 90.0), (-56.0, -30.0, 90.0))
SHELVES = ((-92.0, -14.0, 0.0), (-46.0, -14.0, 0.0), (-92.0, -52.0, 0.0))
CRATES = ((-70.0, -20.0), (-64.0, -26.0), (-76.0, -40.0), (-50.0, -50.0),
          (-58.0, -18.0))
#: Ящики у торцов платформы: груз, который ждут с поезда.
PLATFORM_CRATES = ((14.0, 38.0, DECK), (14.0, -44.0, DECK), (18.0, 26.0, DECK))
STOOLS = ((-84.0, -46.0), (-84.0, -34.0), (-52.0, -30.0))

#: Наполнение закрытых отсеков виден только через окна — поэтому там то, что
#: даёт свет и тень в проёмах: печи и столы стоят у оконных стен.
STOVES = ((-70.0, 50.0), (-46.0, 50.0))
TABLES = ((-52.0, 6.0), (-88.0, 6.0), (-80.0, 34.0))

#: На сколько усадить основание в снег, чтобы под плитой не было щели.
SINK = 0.10


def _place(lx: float, lz: float):
    """Локальная точка клетки -> оси Blender для этой же клетки."""
    return (lx, -lz)


def _ground(lx: float, lz: float) -> float:
    """Отметка снега под локальной точкой: внутри двора это пол узла."""
    return W.world_height(STATION_NODE["x"] + lx, STATION_NODE["z"] + lz)


def _slab(b, x0, x1, z0, z1, z_base, thickness, mat, bevel=0.02):
    """Плита: коробка по осям клетки (центр и размер из границ)."""
    (px, py) = _place((x0 + x1) * 0.5, (z0 + z1) * 0.5)
    b.box((px, py, z_base + thickness * 0.5),
          (x1 - x0, z1 - z0, thickness), mat, bevel=bevel)


def _wall(b, x0, x1, z0, z1, base, top, thickness, mat="Level_Stone"):
    """Стена по отрезку: коробка между двумя точками, толщиной `thickness`."""
    length = math.hypot(x1 - x0, z1 - z0)
    if length < 0.2:
        return
    rot = math.degrees(math.atan2(-(x1 - x0), -(z1 - z0)))
    (px, py) = _place((x0 + x1) * 0.5, (z0 + z1) * 0.5)
    b.box_rot((px, py, (base + top) * 0.5), (thickness, length, top - base),
              (0.0, 0.0, rot), mat, bevel=0.02)


def _post(b, lx, lz, height, size, mat, rot=0.0, sink=0.30, level=None):
    """Столб или тумба: основание утоплено, верх — `height` над опорой.

    `level` — отметка той поверхности, на которой стоит столб. По умолчанию
    это снег, но стойки навеса стоят на настиле платформы (+1,04 над снегом),
    и без этого навес вырастал бы на метр ниже балки, которую должен держать.
    """
    (px, py) = _place(lx, lz)
    g = _ground(lx, lz) if level is None else level
    base = g - sink
    b.box_rot((px, py, (base + g + height) * 0.5),
              (size[0], size[1], height + sink), (0.0, 0.0, rot), mat, bevel=0.02)


def _scaled(obj, k: float):
    """Разуть модель на месте: общие пропы собраны из деталей 5 см.

    `Prop_Workbench`, `Prop_Stool` и `Prop_Stove` живут как отдельные ассеты
    уровня `Game`, где их толщина не мерилась. В чанке клетки их меряет
    `check_frontier` (MIN_DIM 6 см), и честный ответ — не ослаблять проверку, а
    взять предмет крупнее: станок в 1,3 раза всё ещё станок, а 6,5 см стенки
    читаются и в WebGL.
    """
    for v in obj.data.vertices:
        v.co.x *= k
        v.co.y *= k
        v.co.z *= k
    return obj


def _rack(b, lx, lz, rot):
    """Стеллаж ремзоны: свои доски, а не `Prop_Shelf`.

    Полка общего ассета собрана из досок 8 см с фаской 1,2 см, и на сгибе
    оболочка выходит 4 см — `check_frontier` зовёт это «тоньше стиля» (MIN_DIM
    6 см). Вузлу станции нужен тот же предмет, но прочитанный проверкой,
    поэтому здесь доска 12 см без фаски: визуально та же полка, размер честный.
    """
    (px, py) = _place(lx, lz)
    g = _ground(lx, lz) + 0.26
    for z in (0.42, 1.02, 1.62):
        b.box_rot((px, py, g + z), (1.80, 0.40, 0.12), (0.0, 0.0, rot),
                  "Level_Wood", bevel=0.0)
    for sx in (-0.84, 0.84):
        b.box_rot((px + sx * math.cos(math.radians(rot)),
                   py + sx * math.sin(math.radians(rot)), g + 0.90),
                  (0.12, 0.40, 1.80), (0.0, 0.0, rot), "Level_WoodDark",
                  bevel=0.0)


def _platform(materials):
    """Посадочная платформа, навесы и разметка: всё, что касается пути."""
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = PLATFORM
    deck = DECK
    # Плита настила: нижняя грань в грунте, верх — +0,90 над полом узла.
    _slab(b, x0, x1, z0, z1, FLOOR - 0.60, PLATFORM_RISE + 0.60, "Level_Stone")
    # Снеговая шапка настила, 14 см. Нижней гранью она входит в плиту на 5 см:
    # вровень с плитой легла бы вторая плоскость, и `check_frontier` считает
    # это мерцанием, а не снегом.
    _slab(b, x0 + 0.5, x1 - 0.5, z0 + 0.5, z1 - 0.5, FLOOR + PLATFORM_RISE - 0.05,
          0.19, "Level_Snow")
    # Линия безопасности: жёлтая полоса в 1,8 м от края, ближе к пути. Она
    # утоплена в шапку и выступает на 4 см — иначе крышка легла бы на снег
    # той же плоскостью.
    _slab(b, x0 + 1.4, x0 + 1.9, z0 + 1.0, z1 - 1.0, deck - 0.12, 0.16,
          "Level_Orange", bevel=0.0)
    # Навесы над тремя скамейками: стойки + балка + лист кровли.
    for (cz0, cz1) in CANOPY_Z:
        mid = (cz0 + cz1) * 0.5
        for edge in (cz0 + 1.0, cz1 - 1.0):
            _post(b, x0 + 3.2, edge, 3.10, (0.24, 0.24), "Level_Metal",
                  sink=0.10, level=deck)
        (px, py) = _place(x0 + 3.2, mid)
        # Балка входит в стойки верхом на 11 см: её нижняя грань не делит
        # плоскость с верхом столба.
        b.box((px, py, deck + 3.10), (3.60, cz1 - cz0 - 1.4, 0.22),
              "Level_Metal", bevel=0.02)
        # Снег на балке: нижняя грань внутри металла, верх — чистая кровля.
        b.box((px, py, deck + 3.26), (4.20, cz1 - cz0 - 0.8, 0.16),
              "Level_Snow", bevel=0.02)
        # Скамейка под навесом: сиденье на 0,60 над настилом, ножки утоплены.
        for off in (-1.2, 1.2):
            (sx, sy) = _place(x0 + 5.6, mid + off)
            b.box((sx, sy, deck + 0.60), (1.10, 0.44, 0.10), "Level_Wood",
                  bevel=0.01)
            for (dx, dz) in ((-0.5, -0.16), (0.5, -0.16), (-0.5, 0.16), (0.5, 0.16)):
                _post(b, x0 + 5.6 + dx, mid + off + dz, 0.55, (0.10, 0.10),
                      "Level_WoodDark", sink=0.05, level=deck)
    # Торцевые сходы: две ступени вниз к снегу с каждого конца платформы.
    # Ступени уходят НАРУЖУ от настила: внутрь они легли бы в одну плоскость
    # с плитой на том же уровне.
    for (end, sign) in ((z0, -1.0), (z1, 1.0)):
        _slab(b, x0 + 2.0, x1 - 2.0, end, end + sign * 1.4, FLOOR + 0.30, 0.60,
              "Level_Stone")
        _slab(b, x0 + 2.0, x1 - 2.0, end + sign * 1.4, end + sign * 2.8,
              FLOOR - 0.20, 0.65, "Level_Stone")
    # Буферный упор на конце пути: северный край двора. Без него тупик
    # читается как оборвавшиеся шпалы, а с ним видно, что поезд здесь встаёт
    # и дальше линии нет (инвариант «магистраль обрывается во дворе станции»).
    (bx, bz) = BUFFER
    for off in (-1.7, 1.7):
        _post(b, bx + off, bz + 0.6, 0.86, (0.36, 0.36), "Level_Rust",
              sink=0.12)
    (px, py) = _place(bx, bz + 1.0)
    b.box((px, py, FLOOR + 0.86), (4.40, 0.30, 0.44), "Level_Rust", bevel=0.02)
    # Шапка снега на балке: нижняя грань входит в металл, верх — выше его.
    b.box((px, py, FLOOR + 1.10), (4.40, 0.36, 0.10), "Level_Snow", bevel=0.01)
    return b.build("Station_Pad", recalc=False)


def _complex(materials):
    """Отсеки комплекса: плиты, наружные стены, перегородки, кровля, окна, двери."""
    b = level_lib.LevelBuilder(materials)
    (cx0, cx1, cz0, cz1) = COMPLEX
    g = FLOOR
    # Общая плита комплекса: 0,2 над снегом, чтобы порог не тонул в наносах.
    _slab(b, cx0 - 1.2, cx1 + 1.2, cz0 - 1.2, cz1 + 1.2, g - 0.40, 0.60,
          "Level_Stone")
    for (x0, x1, z0, z1, name, stripped, has_roof) in ROOMS:
        base = g + 0.20
        top = base + ROOM_H
        sides = {
            "north": (x0, x1, z1, z1),
            "south": (x0, x1, z0, z0),
            "west": (x0, x0, z0, z1),
            "east": (x1, x1, z0, z1),
        }
        # Наружные стены отсека; снятые стороны остаются проёмами — это и есть
        # вскрытая ремзона.
        for side, (wx0, wx1, wz0, wz1) in sides.items():
            if side in stripped:
                continue
            _wall(b, wx0, wx1, wz0, wz1, base, top, WALL_T, "Level_Stone")
        # Перегородки между отсеками: тоньше наружных, чтобы читался план.
        if "south" not in stripped:
            _wall(b, x0 + WALL_T, x1 - WALL_T, z0 + WALL_T, z0 + WALL_T,
                  base, top, PART_T, "Level_Metal")
        # Пол отсека: бетон по плите, в ремзоне — тот же бетон, он виден.
        _slab(b, x0 + WALL_T, x1 - WALL_T, z0 + WALL_T, z1 - WALL_T,
              g + 0.20, 0.06, "Level_Stone")
        # Кровля: лист с снеговой шапкой, у вскрытого отсека — снята.
        if "roof" not in stripped and has_roof:
            _slab(b, x0 - 0.5, x1 + 0.5, z0 - 0.5, z1 + 0.5, top, 0.26,
                  "Level_Roof")
            _slab(b, x0 + 0.4, x1 - 0.4, z0 + 0.4, z1 - 0.4, top + 0.26, 0.12,
                  "Level_Snow")
        # Окна: тёмные проёмы с тёплым стеклом, по два на длинную стену.
        if "south" not in stripped:
            for t in (0.30, 0.70):
                wx = x0 + (x1 - x0) * t
                (px, py) = _place(wx, z0)
                b.box((px, py, base + 2.05), (1.30, 0.12, 1.10),
                      "Level_Window", bevel=0.0)
        # Дверь в северной стене жилой секции и медблока.
        if name in ("жилая секция", "медблок", "ремзона"):
            (px, py) = _place(x0 + (x1 - x0) * 0.5, z1)
            b.box((px, py, base + 1.10), (1.10, 0.16, 2.20),
                  "Level_WoodDark", bevel=0.01)
    # Вскрытая ремзона: станки, верстаки, стеллажи, ящики и табуреты — то, что
    # видно с платформы через снятую стену.
    for (lx, lz, rot) in BENCHES:
        CP.merge(b, _scaled(LP.build_workbench(materials), 1.3), lx, lz,
                 _ground(lx, lz) + 0.26, sink=0.0, rot=rot)
    for (lx, lz, rot) in SHELVES:
        _rack(b, lx, lz, rot)
    for (lx, lz) in STOOLS:
        CP.merge(b, _scaled(LP.build_stool(materials), 1.3), lx, lz,
                 _ground(lx, lz) + 0.26, sink=0.0, rot=20.0)
    for (lx, lz) in CRATES:
        CP.merge(b, _scaled(LP.build_crate(materials), 1.3), lx, lz,
                 _ground(lx, lz) + 0.26, sink=0.0, rot=15.0)
    for (lx, lz) in STOVES:
        CP.merge(b, _scaled(LP.build_stove(materials), 1.3), lx, lz,
                 _ground(lx, lz) + 0.26, sink=0.0, rot=0.0)
    for (lx, lz) in TABLES:
        CP.merge(b, _scaled(LP.build_workbench(materials), 1.3), lx, lz,
                 _ground(lx, lz) + 0.26, sink=0.0, rot=90.0)
    return b.build("Station_Complex", recalc=False)


def _hangar(materials):
    """Гигантский ангар: плита, стены, раскрытые ворота и фермы под коньком."""
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = HANGAR
    base = FLOOR - 0.30
    top = base + HANGAR_WALL_H
    # Пол ангара: бетон с шапкой снега у ворот, чтобы обломки не лежали на голом
    # камне, и чтобы кромка не сливалась с плитой комплекса.
    _slab(b, x0, x1, z0, z1, base, 0.44, "Level_Stone")
    _slab(b, x0 + 1.2, x0 + 12.0, z0 + 1.2, z1 - 1.2, base + 0.44, 0.10,
          "Level_Snow")
    # Стены: север, юг, восток — глухие; запад — с раскрытым створом ворот.
    _wall(b, x0, x1, z1, z1, base, top, 0.50, "Level_Stone")
    _wall(b, x0, x1, z0, z0, base, top, 0.50, "Level_Stone")
    _wall(b, x1, x1, z0, z1, base, top, 0.50, "Level_Stone")
    (door_z0, door_z1) = HANGAR_DOOR_Z
    _wall(b, x0, x0, z0, door_z0, base, top, 0.50, "Level_Stone")
    _wall(b, x0, x0, door_z1, z1, base, top, 0.50, "Level_Stone")
    # Перемычка над створом и её опоры: ворота читаются проёмом, а не дырой.
    _wall(b, x0, x0, door_z0, door_z1, top - 1.60, top, 0.50, "Level_Metal")
    for zz in (door_z0, door_z1):
        _post(b, x0, zz, HANGAR_WALL_H + 0.40, (0.70, 0.70), "Level_Stone",
              rot=0.0, sink=0.40)
    # Обвязка по верху стен: лист металла, он держит кровлю и даёт тень.
    for (wx0, wx1, wz0, wz1) in ((x0, x1, z1, z1), (x0, x1, z0, z0),
                                 (x1, x1, z0, z1)):
        _wall(b, wx0, wx1, wz0, wz1, top, top + 0.34, 0.70, "Level_Metal")
    # Фермы: набор поперёк пролёта с шагом, а не непрерывный прогон — иначе
    # кровля снова станет V-образным панцирем без дыры (см. депо, ADR 0040).
    prof = [(-1.0, 0.0), (1.0, 0.0), (1.0, -0.22), (-1.0, -0.22)]
    zs = []
    z = z0 + 3.0
    while z <= z1 - 3.0:
        zs.append(z)
        z += HANGAR_TRUSS_SPAN
    for tz in zs:
        pts = [(x0 + 0.6, tz), (x1 - 0.6, tz)]
        levels = [top + 0.34] * 2
        b.tube([(p[0], -p[1], levels[0]) for p in pts],
               [(0.30, 0.30)] * 2, "Level_Metal", n=4, profile=prof)
    # Кровля: два ската до конька плюс снежная шапка. Скат — коробка, повёрнутая
    # вокруг Blender-X: локальная Y меша это −z клетки, поэтому поворот с знаком
    # `sign` поднимает скат к коньку ровно с той стороны, с которой он идёт.
    # Прежняя версия не поворачивала скаты вовсе: два горизонтальных листа
    # вставали на одной высоте друг над другом, и изнутри ангар выглядел
    # связкой балок вместо кровли.
    midz = (z0 + z1) * 0.5
    ridge = base + HANGAR_RISE
    half = (z1 - z0) * 0.5
    rise = ridge - top
    angle = math.degrees(math.atan2(rise, half))
    length = math.hypot(half, rise) + 0.8
    for sign in (-1.0, 1.0):
        cz = midz + sign * half * 0.5
        (px, py) = _place((x0 + x1) * 0.5, cz)
        b.box_rot((px, py, (top + ridge) * 0.5),
                  (x1 - x0 + 2.8, length, 0.24),
                  (sign * angle, 0.0, 0.0), "Level_Roof", bevel=0.02)
        # Шапка снега лежит НА листе: её нижняя грань входит в кровлю на 8 см,
        # иначе две плоскости на одном уровне дают мерцание.
        b.box_rot((px, py, (top + ridge) * 0.5 + 0.16),
                  (x1 - x0 + 1.6, length - 1.4, 0.16),
                  (sign * angle, 0.0, 0.0), "Level_Snow", bevel=0.02)
    b.box((*_place((x0 + x1) * 0.5, midz), ridge + 0.10),
          (x1 - x0 + 2.8, 1.2, 0.30), "Level_Metal", bevel=0.02)
    return b.build("Station_Hangar", recalc=False)


def _wreck(materials):
    """Обломок корабля внутри ангара: фюзеляж с разрывом, два крыла и киль.

    Меню-модели `MenuWreck*` для этого узла малы: движок 3,7 м, крыло 9,3 м —
    в пролёте 70 × 80 они читались бы мусором, а владелец просил гигантский
    ангар со сломанным кораблём. Поэтому корпус собран здесь: 44 м фюзеляжа
    с разрывом посередине, шесть си шпангоутов в дыре, два крыла (одно
    сломано и лежит на килю) и оперение. Движок остаётся меню-моделью, но
    в 2,2 раза крупнее: он виден из ворот и даёт кадру масштаб человека.
    """
    b = level_lib.LevelBuilder(materials)
    floor = FLOOR + 0.14
    axis = math.radians(WRECK_AXIS_DEG)
    ux, uz = math.sin(axis), math.cos(axis)      # единичный вектор вдоль корпуса

    def along(t):
        """Точка на оси корпуса: t в метрах от центра `WRECK_CENTER`."""
        return (WRECK_CENTER[0] + ux * t, WRECK_CENTER[1] + uz * t)

    # Фюзеляж: две половины с разрывом 7 м, диаметр 6,8 м, к хвосту тоньше.
    for (t0, t1, r0, r1) in ((-22.0, -3.5, 3.40, 3.00), (3.5, 22.0, 2.90, 2.10)):
        pts = []
        radii = []
        n = 7
        for k in range(n + 1):
            t = t0 + (t1 - t0) * k / n
            pts.append((along(t)[0], -along(t)[1], floor + 3.30))
            r = r0 + (r1 - r0) * k / n
            radii.append((r, r * 0.92))
        b.tube(pts, radii, "Level_Metal", n=10)
        # Обшивка на разрыве: тёмное нутро видно с ворот.
        for t in (t1 if t0 < 0 else t0,):
            (px, py) = _place(along(t)[0], along(t)[1])
            b.ico((px, py, floor + 3.30), 2.60, "Level_Rust",
                  subdivisions=1, scale=(0.9, 0.9, 1.0))
    # Шпангоуты в дыре: шесть колец, они и есть «силовой набор», видимый с пола.
    for k in range(6):
        t = -3.2 + k * 1.3
        (px, py) = _place(along(t)[0], along(t)[1])
        b.band((px, py), 3.05 - abs(t) * 0.03, floor + 1.10, floor + 5.60,
               "Level_Metal", n=12, squash=(0.94, 1.0), thickness=0.22)
    # Перегородки внутри половин: палуба, которая читается сквозь разрыв.
    for t in (-14.0, 14.0):
        (px, py) = _place(along(t)[0], along(t)[1])
        b.box_rot((px, py, floor + 2.10), (5.20, 0.30, 0.16),
                  (0.0, 0.0, math.degrees(axis)), "Level_Stone", bevel=0.0)
    # Крыло целое: 26 м, тонет в снегу на 40 см у законцовки.
    (wx, wz) = (along(-9.0)[0] - 11.0, along(-9.0)[1] + 5.0)
    (px, py) = _place(wx, wz)
    b.box_rot((px, py, floor + 2.60), (26.0, 6.40, 0.34),
              (0.0, 0.0, math.degrees(axis) - 8.0), "Level_Metal", bevel=0.03)
    b.box_rot((px, py, floor + 2.86), (24.0, 5.60, 0.16),
              (0.0, 0.0, math.degrees(axis) - 8.0), "Level_Snow", bevel=0.02)
    # Крыло сломанное: лежит отдельно, подмято, законцовка в снегу.
    (fx, fz) = (along(11.0)[0] + 9.0, along(11.0)[1] - 12.0)
    (px, py) = _place(fx, fz)
    b.box_rot((px, py, floor + 1.30), (21.0, 5.80, 0.34),
              (0.0, 0.0, math.degrees(axis) + 46.0), "Level_Rust", bevel=0.03)
    b.box_rot((px, py, floor + 0.40), (9.0, 5.20, 0.30),
              (0.0, 0.0, math.degrees(axis) + 46.0), "Level_Metal", bevel=0.02)
    # Киль: плита 9 × 6,5, легшая на бок у хвоста.
    (kx, kz) = (along(19.0)[0] + 3.0, along(19.0)[1] + 9.0)
    (px, py) = _place(kx, kz)
    b.box_rot((px, py, floor + 3.20), (0.40, 6.60, 8.20),
              (math.degrees(axis) - 12.0, 0.0, 0.0), "Level_Metal", bevel=0.03)
    # Опоры ангара под корпусом: корабль лежит на стапелях, а не на бетоне.
    for t in (-16.0, -6.0, 8.0, 18.0):
        (px, py) = _place(along(t)[0], along(t)[1])
        b.box((px, py, floor + 0.55), (1.40, 5.20, 1.10), "Level_WoodDark",
              bevel=0.02)
    return b.build("Station_Wreck", recalc=False)


def _fence(materials):
    """Ограда жилого комплекса: цоколь, лажи, тумбы и пилоны двух ворот."""
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = COMPOUND

    def run(a, c, gate=None):
        (ax, az) = a
        (cx, cz) = c
        length = math.hypot(cx - ax, cz - az)
        n = max(1, int(round(length / 3.2)))
        rot = math.degrees(math.atan2(-(cx - ax), -(cz - az)))

        def skip(t):
            return bool(gate) and gate[0] <= t * length <= gate[1]

        k = 0
        while k < n:
            if skip((k + 0.5) / n):
                k += 1
                continue
            k1 = k
            while k1 + 1 < n and not skip((k1 + 1.5) / n):
                k1 += 1
            t0, t1 = k / n, (k1 + 1) / n
            mx = ax + (cx - ax) * (t0 + t1) * 0.5
            mz = az + (cz - az) * (t0 + t1) * 0.5
            seg = length * (t1 - t0)
            (px, py) = _place(mx, mz)
            g = _ground(mx, mz)
            b.box_rot((px, py, (g - 0.30 + g + PLINTH[1]) * 0.5),
                      (PLINTH[0], seg, PLINTH[1] + 0.30), (0.0, 0.0, rot),
                      "Level_Stone", bevel=0.02)
            k = k1 + 1
        for k in range(n + 1):
            t = k / n
            if gate and skip(t) and min(abs(t * length - gate[0]),
                                        abs(t * length - gate[1])) > 0.8:
                continue
            _post(b, ax + (cx - ax) * t, az + (cz - az) * t, POST[2], POST,
                  "Level_WoodDark")
        for z_off in RAIL_Z:
            for k in range(n):
                if skip((k + 0.5) / n):
                    continue
                t0, t1 = k / n, (k + 1) / n
                mx = ax + (cx - ax) * (t0 + t1) * 0.5
                mz = az + (cz - az) * (t0 + t1) * 0.5
                (px, py) = _place(mx, mz)
                b.box_rot((px, py, _ground(mx, mz) + z_off),
                          (0.08, length / n, 0.08), (0.0, 0.0, rot),
                          "Level_WoodDark", bevel=0.01)

    run((x0, z0), (x0, z1), gate=(GATE_W[0] - z0, GATE_W[1] - z0))
    run((x1, z0), (x1, z1), gate=(GATE_E[0] - z0, GATE_E[1] - z0))
    run((x0, z0), (x1, z0))
    run((x0, z1), (x1, z1))
    for (lx, lz) in ((x0, z0), (x1, z0), (x0, z1), (x1, z1),
                     (x0, (z0 + z1) * 0.5), (x1, (z0 + z1) * 0.5),
                     ((x0 + x1) * 0.5, z0), ((x0 + x1) * 0.5, z1)):
        _post(b, lx, lz, PILLAR[2], PILLAR, "Level_Stone", rot=45.0)
        (px, py) = _place(lx, lz)
        b.box_rot((px, py, _ground(lx, lz) + PILLAR[2] + 0.10),
                  (0.34, 0.34, 0.20), (0.0, 0.0, 45.0), "Level_Metal", bevel=0.02)
    for (axis, at, span) in (("z", x0, GATE_W), ("z", x1, GATE_E)):
        for edge in span:
            (gx, gz) = (at, edge)
            _post(b, gx, gz, PIER[2], PIER, "Level_Stone")
            (px, py) = _place(gx, gz)
            top = _ground(gx, gz) + PIER[2]
            b.box_rot((px, py, top + 0.12), (0.38, 0.38, 0.24),
                      (0.0, 0.0, 0.0), "Level_Metal", bevel=0.02)
    return b.build("Station_Fence", recalc=False)


def _lamp(b, lx, lz, level):
    """Фонарный столб узла: свои детали, а не `Prop_Lamp`.

    Общий проп — подвесная лампа со шнуром 3 см и жестью 2,6 см: на уровне
    `Game` это висюлька, в чанке клетки та же толщина не проходит MIN_DIM.
    Узлу станции нужен столб с фонарём, поэтому он собран здесь: стойка
    14 см, кронштейн, короб 40 см и лампа-икоcаэдр 24 см.
    """
    (px, py) = _place(lx, lz)
    g = level
    b.box((px, py, g + 2.00), (0.14, 0.14, 4.00), "Level_Metal", bevel=0.02)
    b.box((px, py, g + 4.12), (0.30, 0.30, 0.24), "Level_Metal", bevel=0.02)
    b.box((px + 0.42, py, g + 4.30), (0.86, 0.30, 0.16), "Level_Metal", bevel=0.02)
    b.box((px + 0.80, py, g + 4.10), (0.44, 0.40, 0.24), "Level_Roof", bevel=0.02)
    b.ico((px + 0.80, py, g + 3.96), 0.12, "Level_Window", subdivisions=1)


def _dressing(materials):
    """Обитание узла: наносы, заносы, камни, шипы и фонари у периметра."""
    b = level_lib.LevelBuilder(materials)
    for (lx, lz, rx, ry, h, seed) in DRIFTS:
        CP.drift(b, lx, lz, _ground(lx, lz), rx, ry, h, seed)
    for (lx, lz, r) in BILLOWS:
        CP.billow(b, lx, lz, _ground(lx, lz), r, seed=lx * 0.3 + lz)
    for (lx, lz, seed) in ROCKS:
        CP.merge(b, LP.build_rock(materials, 0.4 if seed % 2 else 2.7), lx, lz,
                 _ground(lx, lz), sink=-0.18, rot=seed * 41.0)
    for (lx, lz, h) in SHARDS:
        CP.shard(b, lx, lz, _ground(lx, lz), h, seed=lz * 0.4 + lx)
    # Фонари: три на платформе, два у ворот комплекса, два у створа ангара.
    # Стопки ставятся по фактическому полу: настил платформы выше снега на
    # 1,04, и фонарь, посаженный от снега, утонул бы в настиле.
    for (lx, lz, floor) in PLATFORM_CRATES:
        CP.merge(b, _scaled(LP.build_crate(materials), 1.3), lx, lz, floor,
                 sink=0.0, rot=20.0)
    for (lx, lz, floor) in ((10.0, -44.0, DECK), (10.0, 0.0, DECK),
                            (10.0, 40.0, DECK),
                            (-102.0, 6.0, FLOOR), (-102.0, 34.0, FLOOR),
                            (34.6, -10.0, FLOOR + 0.14),
                            (34.6, 34.0, FLOOR + 0.14)):
        _lamp(b, lx, lz, floor)
    return b.build("Station_Dressing", recalc=False)


def build_cell_station(materials, i: int, j: int):
    """Объекты станции клетки: пустой список, если клетка не станции."""
    if (i, j) != CELL:
        return []
    objs = [_platform(materials), _complex(materials), _hangar(materials),
            _wreck(materials), _fence(materials), _dressing(materials)]
    for (n, (lx, lz, seed)) in enumerate(PINES):
        tree = LP.build_pine(materials, seed, name="Station_Pine_%d" % n)
        (px, py) = _place(lx, lz)
        for v in tree.data.vertices:
            v.co.x += px
            v.co.y += py
            v.co.z += _ground(lx, lz) - 0.15
        objs.append(tree)
    # Часть живёт своим FBX и обязана носить клетку в имени: по суффиксу
    # `<i>_<j>` префаб клетки собирается из частей (`sync_cell_prefabs.cs`).
    for obj in objs:
        obj.name = "%s_%d_%d" % (obj.name, i, j)
    return objs
