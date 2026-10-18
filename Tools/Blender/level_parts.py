"""Дизайны статических ассетов уровня FrostSpace.

Террейн, дом, деревья, уличные пропы и интерьер. Все размеры — метры;
оси и палитра — как в `level_lib.py`. Каждая функция возвращает готовые
объекты Blender-сцены, экспорт — `build_level.py`.

Ключевое отличие дома от старой коробки: стены — венцы брёвен с проёмами
под дверь и окна, пол на 0.25 м (проходим для CharacterController), вход
смотрит в Blender +Y (в Unity — −Z, на юг к спавну). Крыша и снег на ней —
отдельные объекты: в Unity они станут прозрачными, когда игрок зайдёт.
"""

from __future__ import annotations

import math

from mathutils import Vector

import level_lib
import lowpoly

TAU = lowpoly.TAU


def frac(v: float) -> float:
    """Дробная часть числа — детерминированный «случай» без random."""
    return v - math.floor(v)


# ---------------------------------------------------------------- террейн

SIZE = 90.0        # сторона квадрата карты
CELL = 1.5         # шаг сетки
FLAT_R = 15.0      # до этого радиуса земля в центре практически плоская
WAVE_R = 32.0      # здесь волны выходят на полную амплитуду
EDGE_R = 37.0      # начало бортика у границы карты


def terrain_height(x: float, y: float, seed: float = 11.0) -> float:
    """Базовая высота снега без заносов: центр — плоско, к краям — волны."""
    r = max(abs(x), abs(y))
    w = level_lib.smoothstep(FLAT_R, WAVE_R, r)
    h = 0.22 * w * level_lib.noise2d(x * 0.052, y * 0.052, seed)
    h += 0.12 * w * level_lib.noise2d(x * 0.110 + 5.0, y * 0.110, seed + 3.0)
    h += 1.5 * level_lib.smoothstep(EDGE_R, 45.0, r)
    return h


def drift_height(x: float, y: float, drifts) -> float:
    """Вклад заносов: мягкие холмы там, где в старой сцене были сферы."""
    h = 0.0
    for (dx, dy, rx, ry, dz) in drifts:
        q = ((x - dx) / rx) ** 2 + ((y - dy) / ry) ** 2
        if q < 7.0:
            h += dz * math.exp(-q)
    return h


def build_terrain(materials, drifts):
    """Террейн: одна сетка, заносы вшиты буграми, у края — бортик.

    Материал грани выбирается по высоте вклада заносов: гребни получают
    «глубокий снег», подошвы — обычный. Это даёт читаемый рельеф без
    второго объекта.
    """
    b = level_lib.LevelBuilder(materials)
    n = int(round(SIZE / CELL))
    half = SIZE / 2.0
    base = len(b.mb.verts)
    index = b._index("Level_Snow")
    index_deep = b._index("Level_SnowDeep")

    dvals = {}
    for j in range(n + 1):
        for i in range(n + 1):
            x = -half + i * CELL
            y = -half + j * CELL
            d = drift_height(x, y, drifts)
            dvals[(i, j)] = d
            z = terrain_height(x, y) + d
            b.mb.verts.append(Vector((x, y, z)))

    for j in range(n):
        for i in range(n):
            a = base + j * (n + 1) + i
            d = (dvals[(i, j)] + dvals[(i + 1, j)] +
                 dvals[(i, j + 1)] + dvals[(i + 1, j + 1)]) * 0.25
            mat = index_deep if d > 0.05 else index
            b.mb.add_face((a, a + 1, a + n + 2, a + n + 1), mat, "surface")

    return b.build("SnowTerrain", recalc=False)


# ---------------------------------------------------------------- дом

HX = 3.5           # полудлина корпуса по X (осевая линия стены)
HY = 2.75          # полуглубина по Y; +Y — вход (в Unity это юг, −Z)
PLANK_H = 0.30     # высота ряда обшивки
PLANK_N = 11       # число рядов до карниза
PLANK_CORE = 0.20  # толщина сплошного ядра стены (сквозных щелей нет)
PLANK_THIN = 0.08  # толщина наружной доски обшивки
CORNER = 0.30      # сечение углового столба
FLOOR_Z = 0.25     # верх пола внутри дома
EAVE_Y = 3.20      # край скатов по Y
EAVE_Z = FLOOR_Z + PLANK_H * PLANK_N   # 3.55 — карниз на верхнем ряду
RIDGE_Z = 5.05     # высота конька
ROOF_T = 0.14
SNOW_T = 0.16
SLOPE = math.degrees(math.atan2(RIDGE_Z - EAVE_Z, EAVE_Y))

DOOR_X = (-2.05, -0.75)             # проём двери по X (юг)
DOOR_Z = (FLOOR_Z - 0.02, 2.90)     # проектный интервал двери по высоте
WIN_Z = (1.25, 2.55)                # окна: проектный высотный интервал
WIN_S = (1.05, 2.45)                # юг: интервал по X
WIN_N = (-1.30, 0.10)               # север: интервал по X
WIN_W = (-0.10, 1.30)               # запад: интервал по Y
WIN_E = (-1.30, 0.10)               # восток: интервал по Y

CHIMNEY = (2.10, -1.10)             # труба: X, Y (северо-восток дома)
PORCH_CX = (DOOR_X[0] + DOOR_X[1]) * 0.5


def _cut_span(z0: float, z1: float):
    """Фактический вертикальный проём в рядах обшивки для выреза [z0, z1]."""
    cut = [k for k in range(PLANK_N)
           if min(FLOOR_Z + (k + 1) * PLANK_H, z1)
           - max(FLOOR_Z + k * PLANK_H, z0) > 0.04]
    return (FLOOR_Z + min(cut) * PLANK_H,
            FLOOR_Z + (max(cut) + 1) * PLANK_H)


def _segments(lo, hi, cuts):
    """Отрезки [lo, hi] без интервалов cuts (вырезы окон/дверей)."""
    segs = [(lo, hi)]
    for (c0, c1) in sorted(cuts):
        out = []
        for (s0, s1) in segs:
            if c1 <= s0 or c0 >= s1:
                out.append((s0, s1))
                continue
            if c0 - s0 > 0.05:
                out.append((s0, c0))
            if s1 - c1 > 0.05:
                out.append((c1, s1))
        segs = out
    return segs


def _wbox(b, along_x: bool, coord: float, z: float, s0: float, s1: float,
          height: float, depth: float, mat: str, bevel: float = 0.0):
    """Коробка вдоль стены: coord — поперёк, [s0, s1] — вдоль стены."""
    if along_x:
        b.box(((s0 + s1) * 0.5, coord, z), (s1 - s0, depth, height), mat,
              bevel=bevel)
    else:
        b.box((coord, (s0 + s1) * 0.5, z), (depth, s1 - s0, height), mat,
              bevel=bevel)


def _wall_planks(b, along_x: bool, wall: float, out_sign: float, cuts):
    """Стена: сплошное ядро рядами + наружные доски с тёмными швами.

    Ядро исключает сквозные щели (жалоба на старые венцы), доски дают
    читаемый дощатый фасад: шов 6 см, доска 24 см, редкие светлые доски —
    «латаная изба». out_sign ведёт наружу от оси стены.
    """
    ext = 0.14
    half = (HX if along_x else HY) + ext
    board_coord = (wall + out_sign * (PLANK_CORE * 0.5 + PLANK_THIN * 0.5))
    rows = []
    for k in range(PLANK_N):
        z0 = FLOOR_Z + k * PLANK_H
        z1 = z0 + PLANK_H
        active = [(c0, c1) for (c0, c1, cz0, cz1) in cuts
                  if min(z1, cz1) - max(z0, cz0) > 0.04]
        rows.append((z0, z1, active))
    for k, (z0, z1, active) in enumerate(rows):
        zc = (z0 + z1) * 0.5
        for (s0, s1) in _segments(-half, half, active):
            _wbox(b, along_x, wall, zc, s0, s1, PLANK_H + 0.006, PLANK_CORE,
                  "Level_WoodDark")
        mat = "Level_Wood" if k % 4 == 2 else "Level_Roof"
        for (s0, s1) in _segments(-half, half, active):
            if s1 - s0 < 0.26:
                continue
            _wbox(b, along_x, board_coord, zc, s0 + 0.03, s1 - 0.03,
                  PLANK_H - 0.06, PLANK_THIN, mat, bevel=0.012)


def _prism_yz(b, x0, x1, points, mat: str):
    """Призма: профиль в плоскости YZ, вытянутый по X от x0 до x1."""
    n = len(points)
    base = len(b.mb.verts)
    idx = b._index(mat)
    for x in (x0, x1):
        for (y, z) in points:
            b.mb.verts.append(Vector((x, y, z)))
    for i in range(n):
        j = (i + 1) % n
        b.mb.add_face((base + i, base + j, base + n + j, base + n + i), idx)
    b.mb.add_face(tuple(reversed(range(base, base + n))), idx, "cap")
    b.mb.add_face(tuple(range(base + n, base + 2 * n)), idx, "cap")


def _roof_slopes(b, mat: str, lift: float, thickness: float):
    """Два ската крыши. lift — смещение от плоскости ската по нормали."""
    length = math.hypot(EAVE_Y, RIDGE_Z - EAVE_Z)
    ny = math.sin(math.radians(SLOPE))
    nz = math.cos(math.radians(SLOPE))
    for sign in (1.0, -1.0):
        cy = sign * (EAVE_Y * 0.5 + ny * lift)
        cz = (EAVE_Z + RIDGE_Z) * 0.5 + nz * lift
        b.box_rot((0.0, cy, cz), (2 * HX + 0.7, length + 0.06, thickness),
                  (-sign * SLOPE, 0.0, 0.0), mat, bevel=0.01)


def _window(b, axis: str, wall: float, span, inward: int):
    """Окно: крупный наличник со «бровями», стекло, переплёт.

    axis — 'x' (стена идёт по X) или 'y'. Всё нарочно толстое: мелкие детали
    в рамке выбиваются из «варкрафтовского» масштаба уровня.
    """
    z0, z1 = _cut_span(*WIN_Z)
    c0, c1 = span
    cz = (z0 + z1) * 0.5
    height = z1 - z0
    mid = (c0 + c1) * 0.5
    along_x = axis == "x"
    t = 0.17                    # толщина стоек наличника
    depth = 0.46                # вылет наличника из стены
    for cs in (c0 + t * 0.5, c1 - t * 0.5):
        _wbox(b, along_x, wall, cz, cs - t * 0.5, cs + t * 0.5,
              height + 0.34, depth, "Level_WoodDark", bevel=0.02)
    _wbox(b, along_x, wall, z0 - 0.09, c0, c1, 0.20, depth, "Level_WoodDark",
          bevel=0.02)
    _wbox(b, along_x, wall, z1 + 0.09, c0, c1, 0.20, depth, "Level_WoodDark",
          bevel=0.02)
    # подоконник и козырёк — «брови» наличника
    _wbox(b, along_x, wall, z0 - 0.25, c0 - 0.18, c1 + 0.18, 0.20, 0.54,
          "Level_WoodDark", bevel=0.03)
    _wbox(b, along_x, wall, z1 + 0.26, c0 - 0.24, c1 + 0.24, 0.18, 0.52,
          "Level_WoodDark", bevel=0.03)
    # стекло и переплёт крестовиной
    glass = wall + 0.03 * inward
    _wbox(b, along_x, glass, cz, c0 + t, c1 - t, height - 0.12, 0.06,
          "Level_Window")
    _wbox(b, along_x, glass, cz + 0.14, c0 + t, c1 - t, 0.16, 0.14,
          "Level_WoodDark")
    _wbox(b, along_x, glass, cz, mid - 0.08, mid + 0.08, height - 0.12, 0.14,
          "Level_WoodDark")


def build_cabin(materials):
    """Дом: фундамент, пол, стены, крыша, снег, труба, крыльцо, окна, дверь."""
    objs = []
    ln = math.hypot(EAVE_Y, RIDGE_Z - EAVE_Z)   # длина ската по наклону

    # --- фундамент: плита и опоясывающие камни
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.10), (2 * HX + 0.6, 2 * HY + 0.6, 0.20), "Level_Stone",
          bevel=0.03)
    for i in range(6):
        x = -3.0 + i * 1.2
        b.box((x, HY + 0.36, 0.075), (0.75, 0.24, 0.15), "Level_Stone",
              bevel=0.025)
        b.box((x, -HY - 0.36, 0.075), (0.75, 0.24, 0.15), "Level_Stone",
              bevel=0.025)
    for j in range(4):
        y = -2.0 + j * 1.35
        b.box((HX + 0.36, y, 0.075), (0.24, 0.8, 0.15), "Level_Stone",
              bevel=0.025)
    objs.append(b.build("Cabin_Foundation"))

    # --- пол: плита-основание и доски
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.16), (2 * HX + 0.1, 2 * HY + 0.1, 0.08), "Level_Wood")
    for k in range(7):
        x = -3.0 + k * 1.0
        b.box((x, 0.0, 0.225), (0.98, 2 * HY - 0.06, 0.05), "Level_Wood",
              bevel=0.008)
    objs.append(b.build("Cabin_Floor"))

    # --- стены: сплошное ядро в рядах, наружные доски, угловые столбы
    b = level_lib.LevelBuilder(materials)
    _wall_planks(b, True, HY, 1.0, [(*DOOR_X, *DOOR_Z), (*WIN_S, *WIN_Z)])
    _wall_planks(b, True, -HY, -1.0, [(*WIN_N, *WIN_Z)])
    _wall_planks(b, False, -HX, -1.0, [(*WIN_W, *WIN_Z)])
    _wall_planks(b, False, HX, 1.0, [(*WIN_E, *WIN_Z)])

    # угловые столбы
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((sx * (HX + 0.055), sy * (HY + 0.055),
                   (FLOOR_Z - 0.12 + EAVE_Z) * 0.5),
                  (CORNER, CORNER, EAVE_Z - FLOOR_Z + 0.12),
                  "Level_WoodDark", bevel=0.02)

    # ржавая юбка по низу стен (дверной проём пропускаем)
    skirt = FLOOR_Z + 0.085
    for (along_x, wall, out) in ((True, HY, 1.0), (True, -HY, -1.0),
                                 (False, -HX, -1.0), (False, HX, 1.0)):
        half = (HX if along_x else HY) + 0.10
        cuts = [(*DOOR_X,)] if (along_x and out > 0) else []
        for (s0, s1) in _segments(-half, half, cuts):
            _wbox(b, along_x, wall + out * (PLANK_CORE * 0.5 + 0.03), skirt,
                  s0, s1, 0.19, 0.10, "Level_Rust", bevel=0.015)

    # фронтоны (запад и восток): треугольники под скатами и круглые порты
    for sx in (-1.0, 1.0):
        _prism_yz(b, sx * HX - 0.09, sx * HX + 0.09,
                  [(-HY - 0.15, EAVE_Z), (HY + 0.15, EAVE_Z),
                   (0.0, RIDGE_Z - 0.07)], "Level_Roof")
        b.tube([(sx * HX - 0.17, 0.0, 4.00), (sx * HX + 0.17, 0.0, 4.00)],
               [(0.30, 0.30)] * 2, "Level_Window", n=10)

    # балки перекрытия и коньковый прогон
    for bx in (-1.75, 1.75):
        b.box((bx, 0.0, EAVE_Z + 0.05), (0.24, 2 * HY + 0.5, 0.26),
              "Level_WoodDark")
    b.box((0.0, 0.0, RIDGE_Z - 0.24), (2 * HX, 0.24, 0.24), "Level_WoodDark")

    # неоновые вставки по бокам двери — тёплые «витрины» из референса
    neon_y = HY + PLANK_CORE * 0.5 + PLANK_THIN + 0.04
    for nx in (DOOR_X[0] - 0.32, DOOR_X[1] + 0.32):
        b.box((nx, neon_y, 1.62), (0.16, 0.10, 2.40), "Level_Window",
              bevel=0.012)

    # пульт у двери: панель, цветные кнопки, светящаяся полоса
    panel_y = HY + PLANK_CORE * 0.5 + PLANK_THIN + 0.08
    px = DOOR_X[1] + 1.02
    b.box((px, panel_y, 1.46), (0.46, 0.14, 0.36), "Level_Metal",
          bevel=0.015)
    for i, mat in enumerate(("Level_Orange", "Level_Pine", "Level_Window")):
        b.box((px - 0.15 + i * 0.15, panel_y + 0.08, 1.37),
              (0.09, 0.06, 0.09), mat, bevel=0.01)
    b.box((px, panel_y + 0.08, 1.57), (0.32, 0.06, 0.09), "Level_Window",
          bevel=0.01)

    # труба-водосток по западной стене с хомутами
    pipe_x = -(HX + PLANK_CORE * 0.5 + PLANK_THIN + 0.09)
    b.tube([(pipe_x, -1.55, 0.30), (pipe_x, -1.55, 3.35)],
           [(0.085, 0.085)] * 2, "Level_Metal", n=8)
    for zc in (0.95, 2.75):
        b.box((pipe_x + 0.07, -1.55, zc), (0.18, 0.09, 0.13), "Level_Rust",
              bevel=0.012)

    # заплата из гофрированного ржавого листа на восточной стене
    patch_x = HX + PLANK_CORE * 0.5 + PLANK_THIN + 0.06
    for i in range(3):
        b.box((patch_x, 1.05 + i * 0.15, 1.72), (0.12, 0.13, 1.24),
              "Level_Rust", bevel=0.012)
    objs.append(b.build("Cabin_Walls"))

    # --- крыша: скаты и конёк (в Unity этот объект станет прозрачным)
    b = level_lib.LevelBuilder(materials)
    _roof_slopes(b, "Level_Roof", 0.0, ROOF_T)
    b.box((0.0, 0.0, RIDGE_Z + 0.02), (2 * HX + 0.7, 0.26, 0.22),
          "Level_WoodDark")
    # ветровые доски по скатам на торцах
    for sx in (-1.0, 1.0):
        for sign in (1.0, -1.0):
            b.box_rot((sx * (HX + 0.12), sign * EAVE_Y * 0.5,
                       (EAVE_Z + RIDGE_Z) * 0.5 - 0.06),
                      (0.14, ln + 0.06, 0.28), (-sign * SLOPE, 0.0, 0.0),
                      "Level_WoodDark", bevel=0.012)
    # заплата из ржавого листа на северном скате
    lift = ROOF_T * 0.5 + 0.02
    ny = math.sin(math.radians(SLOPE))
    nz = math.cos(math.radians(SLOPE))
    b.box_rot((1.25, -(EAVE_Y * 0.38 + ny * lift),
               EAVE_Z + (RIDGE_Z - EAVE_Z) * 0.62 + nz * lift),
              (1.05, 0.76, 0.06), (SLOPE, 0.0, 0.0), "Level_Rust",
              bevel=0.012)
    objs.append(b.build("Cabin_Roof"))

    # --- снег на крыше: плиты, языки на карнизах, конёк
    b = level_lib.LevelBuilder(materials)
    _roof_slopes(b, "Level_Snow", ROOF_T * 0.5 + SNOW_T * 0.5 + 0.005, SNOW_T)
    b.box((0.0, 0.0, RIDGE_Z + 0.21), (2 * HX + 0.8, 0.42, 0.20),
          "Level_Snow", bevel=0.03)
    for i in range(10):
        x = -3.60 + i * 0.80
        depth = 0.32 + 0.16 * frac(i * 4.13)
        drop = 0.02 + 0.03 * frac(i * 2.71 + 1.0)
        for sign in (1.0, -1.0):
            b.box_rot((x, sign * (EAVE_Y - 0.06), EAVE_Z - 0.045 - drop),
                      (0.86, depth, SNOW_T * 0.9),
                      (-sign * SLOPE, 0.0, 0.0), "Level_Snow", bevel=0.015)
    objs.append(b.build("Cabin_RoofSnow"))

    # --- труба: ствол, пояс, оголовок, снежная шапка
    b = level_lib.LevelBuilder(materials)
    chx, chy = CHIMNEY
    b.box((chx, chy, 4.60), (0.95, 0.95, 3.20), "Level_Stone", bevel=0.02)
    b.box((chx, chy, 5.55), (1.12, 1.12, 0.14), "Level_Stone", bevel=0.02)
    b.box((chx, chy, 6.28), (1.15, 1.15, 0.16), "Level_Stone", bevel=0.02)
    b.box((chx, chy, 6.45), (1.26, 1.26, 0.18), "Level_Snow", bevel=0.03)
    # мачта с антенной и маячком
    b.tube([(chx, chy, 6.30), (chx, chy, 7.60)], [(0.05, 0.05)] * 2,
           "Level_Metal", n=6)
    for i, (w, az) in enumerate(((0.44, 6.95), (0.30, 7.22), (0.18, 7.45))):
        size = (w, 0.06, 0.06) if i % 2 == 0 else (0.06, w, 0.06)
        b.box((chx, chy, az), size, "Level_Metal", bevel=0.012)
    b.box((chx, chy, 7.64), (0.13, 0.13, 0.13), "Level_Window",
          bevel=0.02)
    # провисшие провода к крыльцу
    for dz, sag in ((0.0, 0.50), (-0.14, 0.62)):
        start = Vector((chx, chy, 6.86 + dz))
        end = Vector((PORCH_CX + 1.55, HY + 1.35, 3.02))
        pts = []
        for i in range(5):
            t = i / 4.0
            p = start.lerp(end, t)
            p.z -= sag * math.sin(math.pi * t)
            pts.append(p)
        b.tube(pts, [(0.035, 0.035)] * 5, "Level_Metal", n=4)
    objs.append(b.build("Cabin_Chimney"))

    # --- крыльцо: настил, столбы, навес, снег
    b = level_lib.LevelBuilder(materials)
    b.box((PORCH_CX, HY + 0.80, 0.115), (3.4, 1.55, 0.15), "Level_Wood",
          bevel=0.01)
    for px in (PORCH_CX - 1.55, PORCH_CX + 1.55):
        b.box((px, HY + 1.35, 1.50), (0.28, 0.28, 2.80), "Level_WoodDark")
        b.box((px, HY + 1.35, 2.96), (0.42, 0.42, 0.17), "Level_Snow",
              bevel=0.03)
    b.box_rot((PORCH_CX, HY + 0.85, 3.02), (4.0, 1.80, 0.12),
              (-9.5, 0.0, 0.0), "Level_Roof", bevel=0.01)
    b.box_rot((PORCH_CX, HY + 0.85, 3.16), (4.1, 1.80, 0.13),
              (-9.5, 0.0, 0.0), "Level_Snow", bevel=0.015)
    # неоновая лампа под навесом (по переднему краю — видно от крыльца)
    b.box((PORCH_CX, HY + 1.62, 2.76), (2.95, 0.10, 0.10), "Level_Window",
          bevel=0.015)
    objs.append(b.build("Cabin_Porch"))

    # --- окна: четыре фасада
    b = level_lib.LevelBuilder(materials)
    _window(b, "x", HY, WIN_S, -1)
    _window(b, "x", -HY, WIN_N, 1)
    _window(b, "y", -HX, WIN_W, 1)
    _window(b, "y", HX, WIN_E, -1)
    objs.append(b.build("Cabin_Windows"))

    # --- дверь: наличники снаружи, порог, распахнутое внутрь полотно
    b = level_lib.LevelBuilder(materials)
    d_lo, d_hi = _cut_span(*DOOR_Z)
    c0, c1 = DOOR_X
    for cx in (c0 - 0.10, c1 + 0.10):
        b.box((cx, HY + 0.19, (d_lo + d_hi) * 0.5),
              (0.22, 0.10, d_hi - d_lo + 0.44), "Level_Wood", bevel=0.02)
    b.box(((c0 + c1) * 0.5, HY + 0.19, d_hi + 0.14),
          (c1 - c0 + 0.60, 0.10, 0.22), "Level_Wood", bevel=0.02)
    b.box(((c0 + c1) * 0.5, HY + 0.14, d_lo + 0.05),
          (c1 - c0 + 0.40, 0.34, 0.12), "Level_WoodDark", bevel=0.02)
    # полотно у внутренней стены, петля у правой стойки проёма
    hinge = Vector((c1 - 0.02, HY - 0.16, 0.0))
    beta = math.radians(152.0)
    door_w = c1 - c0 - 0.10
    door_h = d_hi - d_lo - 0.08
    direction = Vector((-math.cos(beta), -math.sin(beta), 0.0))
    center = hinge + direction * (door_w * 0.5)
    b.box_rot((center.x, center.y, d_lo + door_h * 0.5),
              (door_w, 0.10, door_h), (0.0, 0.0, math.degrees(beta)),
              "Level_WoodDark", bevel=0.012)
    # ржавые поперечины и оранжевая ручка — «собран из мусора»
    for off in (-0.26, 0.30):
        b.box_rot((center.x + direction.x * off,
                   center.y + direction.y * off, d_lo + door_h * 0.5),
                  (door_w - 0.20, 0.06, 0.14), (0.0, 0.0, math.degrees(beta)),
                  "Level_Rust", bevel=0.012)
    handle = center + direction * (door_w * 0.5 - 0.16)
    b.box((handle.x, handle.y, 1.15), (0.11, 0.11, 0.24), "Level_Orange",
          bevel=0.015)
    objs.append(b.build("Cabin_Door"))

    return objs


# ---------------------------------------------------------------- деревья


def _tier(b, z0: float, r0: float, z1: float, mat: str, seed: float,
          n: int = 10, bite: float = 0.22, top_r: float = 0.10):
    """Один ярус хвои: зубчатое нижнее кольцо и малое верхнее."""
    base = len(b.mb.verts)
    idx = b._index(mat)
    low = []
    for i in range(n):
        a = TAU * i / n + math.pi / n
        wobble = 1.0 + bite * math.sin(i * 4.7 + seed)
        zz = z0 + 0.10 * math.cos(i * 3.1 + seed * 2.0)
        low.append(Vector((math.cos(a) * r0 * wobble,
                           math.sin(a) * r0 * wobble, zz)))
    top = []
    for i in range(n):
        a = TAU * i / n + math.pi / n
        top.append(Vector((math.cos(a) * top_r, math.sin(a) * top_r, z1)))
    b.mb.verts.extend(low)
    b.mb.verts.extend(top)
    for i in range(n):
        j = (i + 1) % n
        b.mb.add_face((base + i, base + j, base + n + j, base + n + i), idx,
                      "surface")
    b.mb.add_face(tuple(range(base + n, base + 2 * n)), idx, "cap")
    b.mb.add_face(tuple(reversed(range(base, base + n))), idx, "cap")


def build_pine(materials, seed: float = 0.0, name: str = "Tree_Pine"):
    """Сосна: ствол с лёгким изгибом, ярусы хвои, снежные чехлы."""
    b = level_lib.LevelBuilder(materials)
    height = 5.6 + 0.9 * frac(seed * 0.731 + 0.11)
    tiers = 4 + int(frac(seed * 0.417 + 0.3) * 3.0)
    lean_x = 0.28 * (frac(seed * 1.31) - 0.5)
    lean_y = 0.28 * (frac(seed * 2.17 + 4.0) - 0.5)

    pts = []
    radii = []
    for k in range(4):
        t = k / 3.0
        pts.append(Vector((lean_x * t * t, lean_y * t * t, height * t * 1.02)))
        radii.append((0.26 * (1.0 - t * 0.80), 0.26 * (1.0 - t * 0.80)))
    b.tube(pts, radii, "Level_WoodDark", n=6)

    z = 1.05 + 0.25 * frac(seed * 3.7)
    step = (height - 1.35) / tiers
    for k in range(tiers):
        t = k / max(tiers - 1, 1)
        r0 = (2.05 - 1.35 * t) * (0.90 + 0.20 * frac(seed + k * 1.7))
        z0 = z + step * k
        z1 = z0 + step * 1.25
        cx = lean_x * (z0 / height)
        cy = lean_y * (z0 / height)
        before = len(b.mb.verts)
        _tier(b, z0, r0, z1, "Level_Pine", seed + k * 1.9)
        _tier(b, z0 + step * 0.30, r0 * 0.86, z1 + step * 0.06,
              "Level_PineSnow", seed + k * 1.9 + 0.5)
        for v in b.mb.verts[before:]:
            v.x += cx
            v.y += cy

    # верхушка
    before = len(b.mb.verts)
    _tier(b, height * 0.92, 0.42, height + 0.28, "Level_Pine", seed + 9.1,
          n=8, bite=0.16, top_r=0.03)
    _tier(b, height * 0.94, 0.34, height + 0.26, "Level_PineSnow", seed + 9.6,
          n=8, bite=0.14, top_r=0.03)
    for v in b.mb.verts[before:]:
        v.x += lean_x
        v.y += lean_y
    return b.build(name)


# ---------------------------------------------------------------- пропы


def build_barrel(materials, name: str = "Prop_Barrel"):
    """Ржавая бочка: пузатый корпус, обручи, крышка, снежная шапка."""
    b = level_lib.LevelBuilder(materials)
    b.tube([(0.0, 0.0, 0.06), (0.0, 0.0, 0.32), (0.0, 0.0, 0.58),
            (0.0, 0.0, 0.84)],
           [(0.285, 0.285), (0.355, 0.355), (0.355, 0.355), (0.285, 0.285)],
           "Level_Rust", n=12)
    b.band((0.0, 0.0), 0.345, 0.16, 0.24, "Level_Metal", n=12, thickness=0.035)
    b.band((0.0, 0.0), 0.345, 0.66, 0.74, "Level_Metal", n=12, thickness=0.035)
    b.tube([(0.0, 0.0, 0.83), (0.0, 0.0, 0.90)], [(0.30, 0.30), (0.30, 0.30)],
           "Level_Metal", n=12)
    b.tube([(0.0, 0.0, 0.90), (0.0, 0.0, 0.98)], [(0.26, 0.26), (0.255, 0.255)],
           "Level_Snow", n=12)
    # приборная панель с кнопками — «техно-мусор»
    b.box((0.0, 0.36, 0.62), (0.30, 0.14, 0.26), "Level_Metal", bevel=0.015)
    for i, mat in enumerate(("Level_Orange", "Level_Pine", "Level_Window")):
        b.box((-0.09 + i * 0.09, 0.44, 0.57), (0.07, 0.05, 0.07), mat,
              bevel=0.01)
    b.box((0.0, 0.44, 0.70), (0.22, 0.05, 0.06), "Level_Window", bevel=0.01)
    # вентиль на крышке
    b.tube([(0.0, 0.0, 0.98), (0.0, 0.0, 1.10)], [(0.055, 0.055)] * 2,
           "Level_Metal", n=6)
    b.box((0.0, 0.0, 1.13), (0.22, 0.09, 0.06), "Level_Rust", bevel=0.012)
    return b.build(name)


def build_crate(materials, name: str = "Prop_Crate"):
    """Ящик: доски, ржавые рёбра, светящаяся наклейка и снег на крышке."""
    b = level_lib.LevelBuilder(materials)
    s = 0.72
    b.box((0.0, 0.0, s * 0.5), (s, s, s), "Level_Roof", bevel=0.015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * s * 0.46, sy * s * 0.46, s * 0.5),
                  (0.13, 0.13, s + 0.04), "Level_Rust", bevel=0.012)
    for sz in (0.11, s - 0.11):
        for sy in (-1, 1):
            b.box((0.0, sy * s * 0.46, sz), (s + 0.04, 0.10, 0.12),
                  "Level_WoodDark", bevel=0.012)
        for sx in (-1, 1):
            b.box((sx * s * 0.46, 0.0, sz), (0.10, s + 0.04, 0.12),
                  "Level_WoodDark", bevel=0.012)
    # наклейка-индикатор и заклёпки на «лицевой» грани (−Y)
    b.box((-0.10, -0.375, 0.42), (0.30, 0.04, 0.20), "Level_Metal",
          bevel=0.01)
    b.box((-0.10, -0.392, 0.42), (0.24, 0.04, 0.14), "Level_Window",
          bevel=0.01)
    for sx in (-1, 1):
        for sz in (0.16, s - 0.16):
            b.box((sx * 0.28, -0.385, sz), (0.07, 0.04, 0.07), "Level_Metal",
                  bevel=0.008)
    b.box((0.0, 0.0, s + 0.045), (s + 0.06, s + 0.06, 0.11), "Level_Snow",
          bevel=0.035)
    return b.build(name)


def build_rock(materials, seed: float = 0.0, name: str = "Prop_Rock"):
    """Фасетный валун со снежной шапкой."""
    b = level_lib.LevelBuilder(materials)
    b.ico((0.0, 0.0, 0.34), 0.55, "Level_Stone", subdivisions=1,
          scale=(1.25, 0.80, 1.02), jitter=0.34, seeds=seed)
    b.ico((0.03, -0.02, 0.68), 0.50, "Level_Snow", subdivisions=1,
          scale=(1.18, 0.78, 0.34), jitter=0.22, seeds=seed + 2.0)
    return b.build(name)


def build_fence_post(materials, name: str = "Prop_FencePost"):
    """Столб изгороди со снежной шапкой. Толстый: мелочь выбивается из стиля."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.70), (0.26, 0.26, 1.40), "Level_WoodDark", bevel=0.03)
    b.box((0.0, 0.0, 1.48), (0.36, 0.36, 0.16), "Level_Snow", bevel=0.04)
    return b.build(name)


def build_fence_rail(materials, name: str = "Prop_FenceRail"):
    """Жердь изгороди (центр — середина пролёта, низ на z = 0)."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.10), (2.30, 0.20, 0.20), "Level_Wood", bevel=0.025)
    b.box((0.0, 0.0, 0.245), (2.20, 0.15, 0.09), "Level_Snow", bevel=0.02)
    return b.build(name)


def build_log_pile(materials, name: str = "Prop_LogPile"):
    """Поленница: штабель брёвен со снегом поверху."""
    b = level_lib.LevelBuilder(materials)
    r = 0.115
    for ri, count in enumerate((4, 3, 2, 1)):
        dz = 0.20 * ri
        off = (count - 1) * 0.5
        for i in range(count):
            y = (i - off) * 0.235
            mat = "Level_WoodDark" if ((i + ri) % 3 == 0) else "Level_Wood"
            b.tube([(-0.95, y, r + dz), (0.95, y, r + dz)],
                   [(r, r), (r, r)], mat, n=6)
    b.box((0.0, 0.0, 0.89), (1.90, 0.98, 0.12), "Level_Snow", bevel=0.04)
    # ржавые стяжки поверх снега и по фасаду штабеля
    for sx in (-0.62, 0.62):
        b.box((sx, 0.48, 0.46), (0.10, 0.06, 0.90), "Level_Rust", bevel=0.012)
        b.box((sx, 0.0, 0.955), (0.10, 0.96, 0.06), "Level_Rust", bevel=0.012)
    return b.build(name)


# ---------------------------------------------------------------- интерьер


def build_stove(materials, name: str = "Prop_Stove"):
    """Буржуйка: чугунный корпус, дверца с огнём, труба под потолок."""
    b = level_lib.LevelBuilder(materials)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.tube([(sx * 0.16, sy * 0.16, 0.0), (sx * 0.16, sy * 0.16, 0.42)],
                   [(0.05, 0.05)] * 2, "Level_Metal", n=6)
    b.tube([(0.0, 0.0, 0.40), (0.0, 0.0, 0.45)], [(0.34, 0.34)] * 2,
           "Level_Metal", n=12)
    b.tube([(0.0, 0.0, 0.45), (0.0, 0.0, 1.22)], [(0.30, 0.30)] * 2,
           "Level_Rust", n=12)
    b.tube([(0.0, 0.0, 1.22), (0.0, 0.0, 1.30)], [(0.34, 0.34)] * 2,
           "Level_Metal", n=12)
    # дверца топки с окошком огня
    b.box((0.0, 0.31, 0.86), (0.42, 0.10, 0.36), "Level_Metal", bevel=0.015)
    b.box((0.0, 0.35, 0.86), (0.28, 0.07, 0.24), "Level_Window", bevel=0.012)
    b.box((0.25, 0.35, 0.90), (0.09, 0.09, 0.17), "Level_Metal", bevel=0.012)
    # поддувало
    b.box((0.0, 0.30, 0.58), (0.32, 0.09, 0.18), "Level_Metal", bevel=0.012)
    # труба с воротником
    b.tube([(0.0, 0.0, 1.28), (0.0, 0.0, 1.42)], [(0.16, 0.16)] * 2,
           "Level_Metal", n=10)
    b.tube([(0.0, 0.0, 1.40), (0.10, 0.06, 2.70)], [(0.12, 0.12)] * 2,
           "Level_Metal", n=10)
    return b.build(name)


def build_bed(materials, name: str = "Prop_Bed"):
    """Койка: рама, тюфяк, оранжевое одеяло, подушка, спинка."""
    b = level_lib.LevelBuilder(materials)
    # ножки и рама
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.93, sy * 0.40, 0.17), (0.12, 0.12, 0.34),
                  "Level_WoodDark")
    for sy in (-1, 1):
        b.box((0.0, sy * 0.40, 0.30), (1.96, 0.11, 0.12), "Level_WoodDark")
    b.box((-0.98, 0.0, 0.30), (0.11, 0.88, 0.12), "Level_WoodDark")
    b.box((0.98, 0.0, 0.30), (0.11, 0.88, 0.12), "Level_WoodDark")
    for k in range(3):
        b.box((-0.63 + k * 0.63, 0.0, 0.33), (0.60, 0.80, 0.05), "Level_Wood",
              bevel=0.008)
    # тюфяк, одеяло, подушка
    b.box((0.0, 0.0, 0.435), (1.90, 0.82, 0.16), "Level_Snow", bevel=0.04)
    b.box((0.26, 0.0, 0.545), (1.30, 0.86, 0.09), "Level_Orange", bevel=0.03)
    b.box((0.86, 0.0, 0.54), (0.14, 0.88, 0.11), "Level_Orange", bevel=0.03)
    b.box((-0.68, 0.0, 0.565), (0.48, 0.36, 0.13), "Level_Snow", bevel=0.045)
    # спинка
    for sy in (-1, 1):
        b.box((-0.98, sy * 0.40, 0.72), (0.12, 0.12, 0.90), "Level_WoodDark")
    b.box((-0.98, 0.0, 1.10), (0.12, 0.88, 0.12), "Level_WoodDark")
    b.box((-0.98, 0.0, 0.92), (0.10, 0.80, 0.09), "Level_WoodDark")
    return b.build(name)


def build_workbench(materials, name: str = "Prop_Workbench"):
    """Верстак: столешница, полка, тиски, инструменты."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.86), (1.80, 0.72, 0.10), "Level_Wood", bevel=0.015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.80, sy * 0.28, 0.41), (0.13, 0.13, 0.82),
                  "Level_WoodDark")
    b.box((0.0, 0.0, 0.28), (1.64, 0.60, 0.08), "Level_Wood", bevel=0.012)
    # тиски
    b.box((0.56, 0.16, 0.96), (0.26, 0.20, 0.18), "Level_Metal", bevel=0.015)
    b.box((0.56, 0.30, 1.00), (0.14, 0.14, 0.20), "Level_Metal", bevel=0.012)
    # ящик с инструментом и молоток
    b.box((-0.48, 0.02, 0.98), (0.50, 0.32, 0.20), "Level_WoodDark",
          bevel=0.02)
    b.box((-0.48, 0.02, 1.10), (0.20, 0.07, 0.07), "Level_Metal", bevel=0.01)
    b.tube([(-0.02, -0.14, 0.94), (0.30, -0.16, 0.95)], [(0.035, 0.035)] * 2,
           "Level_Wood", n=6)
    b.box((0.33, -0.16, 0.955), (0.16, 0.09, 0.09), "Level_Metal",
          bevel=0.012)
    b.box((-0.06, -0.22, 0.94), (0.54, 0.08, 0.07), "Level_Metal", bevel=0.012)
    # терминал-монитор с кабелем
    b.box_rot((0.10, -0.20, 1.06), (0.32, 0.10, 0.24), (-16.0, 0.0, 0.0),
              "Level_Metal", bevel=0.015)
    b.box_rot((0.10, -0.252, 1.07), (0.24, 0.05, 0.17), (-16.0, 0.0, 0.0),
              "Level_Window", bevel=0.01)
    b.tube([(0.16, -0.20, 0.95), (0.34, -0.06, 0.925), (0.20, 0.10, 0.925)],
           [(0.03, 0.03)] * 3, "Level_Metal", n=4)
    return b.build(name)


def build_stool(materials, name: str = "Prop_Stool"):
    """Табурет."""
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.43), (0.42, 0.42, 0.06), "Level_Wood", bevel=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.16, sy * 0.16, 0.20), (0.06, 0.06, 0.40),
                  "Level_WoodDark")
    for sy in (-1, 1):
        b.box((0.0, sy * 0.16, 0.18), (0.36, 0.05, 0.05), "Level_WoodDark")
    return b.build(name)


def build_shelf(materials, name: str = "Prop_Shelf"):
    """Полка с банками и канистрами (вешается на стену)."""
    b = level_lib.LevelBuilder(materials)
    for z in (1.18, 1.62):
        b.box((0.0, 0.15, z), (1.60, 0.34, 0.08), "Level_Wood", bevel=0.012)
        for sx in (-1, 1):
            b.box((sx * 0.72, 0.14, z - 0.16), (0.09, 0.30, 0.34),
                  "Level_WoodDark")
    for i in range(4):
        x = -0.52 + i * 0.34
        mat = "Level_Metal" if i % 2 == 0 else "Level_Rust"
        b.tube([(x, 0.16, 1.22), (x, 0.16, 1.37)], [(0.075, 0.075)] * 2,
               mat, n=8)
        b.tube([(x, 0.16, 1.37), (x, 0.16, 1.41)], [(0.085, 0.085)] * 2,
               "Level_Metal", n=8)
    b.box((0.30, 0.16, 1.76), (0.20, 0.14, 0.26), "Level_Orange", bevel=0.025)
    b.box((0.56, 0.16, 1.75), (0.20, 0.14, 0.24), "Level_Orange", bevel=0.025)
    b.box((-0.45, 0.16, 1.72), (0.34, 0.24, 0.18), "Level_WoodDark",
          bevel=0.02)
    return b.build(name)


def build_lamp(materials, name: str = "Prop_Lamp"):
    """Подвесная лампа: шнур вниз от крюка, жестяной абажур, тёплая лампа."""
    b = level_lib.LevelBuilder(materials)
    b.tube([(0.0, 0.0, 0.0), (0.0, 0.0, -0.30), (0.0, 0.0, -0.56)],
           [(0.03, 0.03)] * 3, "Level_WoodDark", n=6)
    b.tube([(0.0, 0.0, -0.52), (0.0, 0.0, -0.72)],
           [(0.07, 0.07), (0.25, 0.25)], "Level_Metal", n=12,
           caps=(True, False))
    b.ico((0.0, 0.0, -0.66), 0.10, "Level_Window", subdivisions=1)
    b.tube([(0.0, 0.0, 0.0), (0.0, 0.0, 0.06)], [(0.07, 0.07)] * 2,
           "Level_Metal", n=8)
    return b.build(name)


def build_log_bundle(materials, name: str = "Prop_LogBundle"):
    """Вязанка дров у печи."""
    b = level_lib.LevelBuilder(materials)
    r = 0.11
    for row, count in ((0, 3), (1, 2), (2, 1)):
        off = (count - 1) * 0.5
        for i in range(count):
            y = (i - off) * 0.24
            mat = "Level_WoodDark" if (i + row) % 2 else "Level_Wood"
            b.tube([(-0.42, y, r + row * 0.215), (0.42, y, r + row * 0.215)],
                   [(r, r), (r, r)], mat, n=6)
    return b.build(name)
