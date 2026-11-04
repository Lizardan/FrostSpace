"""Поселение роботов в клетке (0,-2): изба, загоны, метеопост, электроподстанция.

Зачем это в чанке, а не в сцене. Депо уже показывает, как надо: узел мира —
это часть клетки, а не набор префабов, раскиданных руками (`depot_parts`).
Поселение собрано так же: меши живут в локальных осях клетки, отметки берутся
от пола двора узла, и весь узел выходит частями `Hut_*_0_-2.fbx` вместе с
землёй и дорогами, а префаб собирает `sync_cell_prefabs.cs`.

Что здесь происходит по замыслу (ADR 0041: пол узла держит одну отметку):

- **Две дороги, а не одна.** `road 0` приходит с запада и кончается в узле,
  `road 1` уходит на север. Обе протыкают ограду: западная линия стоит с
  воротами на въезд, северная — с воротами на выезд. Живой угол ног дороги в
  локальных осях (0, 0), поэтому двор обнят вокруг него, а не вокруг дома.
- **Ограда красивая, а не «три лажи».** Каменный цоколь, столбы каждые 3 м, две
  лажи, угловые тумбы 1,62 м с металлическим оголовком, пилоны 2,30 м у ворот.
  Прежний забор читался изгородью огорода, а узел обязан выглядеть постаментом
  Пояса, который держат и током, и погодой, и приютом.
- **Три зоны внутри:** загоны для переделанных машин (юго-запад), электропост
  с трансформаторами и кабелем (северо-запад), метеоплощадка с цоколем под
  вышку (северо-восток). Дом и мастерская — восточная половина.

Запуск (штатно — из пайплайна, отдельного шага нет):

    python Tools/Blender/build_all.py --asset cells --check --preview

Координаты — локальные оси клетки: X на восток, Y в Blender = −z мира, вверх Z.
"""

from __future__ import annotations

import math

import level_lib
import level_parts as LP
import world_layout as W

import cell_props as CP

#: Клетка поселения и её центр в мире.
CELL = W.cell_of(*W.SETTLEMENT)

#: Двор узла в локальных осях: тёплая площадка `rect` мира (−60…60, −55…55),
#: из которой оставлен запас на съезд. Ограда стоит внутри двора: если её
#: выставить на край, цоколь сядет на съезд и поплывёт на стыке клеток.
PARK = (-6.0, 54.0, -8.0, 52.0)

#: Дороги протыкают двор: `road 0` приходит с запада по линии z = 0 и кончается
#: в узле, `road 1` уходит на север по линии x = 0. Обе держат выемку
#: полушириной до 9 м, поэтому весь жилой и хозяйственный смысл узла стоит
#: восточнее x = 10: иначе забор, загон и трансформатор встали бы в канаве.
#: Дом: северо-восточная половина, порог смотрит на юг.
HOUSE = (40.0, 40.0)

#: Мастерская приюта: юго-восточный угол, рядом с загонами — именно там
#: переделывают машины, а не живут.
SHOP = (46.0, 8.0)

#: Ворота: въезд с запада по `road 0` (створ поперёк линии z = 0) и выезд на
#: север по `road 1` (створ поперёк линии x = 0). Ширина 12 м — пропускать обоз
#: с аккумуляторами, и ровно столько, чтобы полотно не упиралось в тумбы.
GATE_W = (-6.0, 6.0)        # проём в западной линии, абсолютный z
GATE_N = (-6.0, 6.0)        # проём в северной линии, абсолютный x

#: Загоны: три секции восточной половины, вода и сено — в каждой свой угол.
PENS = ((12.0, 22.0, -4.0, 10.0), (12.0, 22.0, 12.0, 26.0),
        (26.0, 38.0, 12.0, 26.0))
TROUGH = ((17.0, 3.0), (17.0, 20.0), (32.0, 20.0))
HAY = ((20.5, 24.0, 0.0), (13.5, 8.0, 90.0), (36.0, 14.0, 90.0))

#: Электропост: северная полоса восточнее дороги. Домик машины, три
#: трансформаторных постамента, кабельная канавка и три столба с траверсами.
POWER = (12.0, 30.0, 30.0, 48.0)
POWER_HOUSE = (17.0, 42.0)
TRANSFORMERS = ((20.0, 44.0), (23.5, 44.0), (27.0, 44.0))
CABLE_RUN = ((14.0, 34.0), (28.0, 34.0))
CABLE_POLES = ((15.0, 32.0), (20.0, 32.0), (25.0, 32.0))

#: Метеоплощадка: северо-восточный угол, чтобы вышку было видно и с въезда, и
#: с дороги на север. Цоколь под `MenuStationMast` (модель ставится в сцене,
#: как вышка депо), приборные ящики и анемометр на шесте.
MAST = (48.0, 46.0)
INSTRUMENTS = ((44.0, 34.0), (48.0, 34.0))

#: Тропа от въезда к порогу и к подстанции: снег утоптан, поэтому темнее и
#: лежит поверх земли, а не вровень с ней.
PATH = ((6.0, 4.0), (24.0, 8.0), (40.0, 26.0), (40.0, 33.0), (40.0, 36.0))
PATH_POWER = ((6.0, 4.0), (10.0, 30.0), (14.0, 34.0))

#: Дрова: изба с печью, значит поленница у стены и вязанка у порога.
LOGS = ((33.5, 45.0, 0.0), (33.5, 47.2, 90.0))
BUNDLE = (37.0, 35.6)

#: Наносы у стен и у цоколей: снег прилип к углу, к северной стене и к тумбам.
DRIFTS = ((35.5, 45.5, 3.2, 2.2, 0.85, 1.0),
          (45.5, 44.5, 3.0, 2.0, 0.80, 2.0),
          (46.0, 34.0, 2.6, 2.6, 0.70, 3.0),
          (30.0, 28.0, 3.0, 2.2, 0.75, 4.0),
          (32.0, 4.0, 2.8, 2.4, 0.70, 5.0))

#: Заносы по горизонту двора — крупные, чтобы огороженный участок не выглядел
#: вытоптанным плацем (тот же язык, что в рощах депо).
BILLOWS = ((34.0, 50.0, 6.2), (50.0, 20.0, 5.6),
           (30.0, 4.0, 6.0), (44.0, 16.0, 4.6))

#: Камни и шипы: движение по вертикали там, где взгляд уходит от заборов.
ROCKS = ((28.0, 6.0, 0.4), (36.0, 28.0, 2.7), (50.0, 30.0, 0.9),
         (22.0, 28.0, 2.2), (48.0, 12.0, 1.4))
SHARDS = ((32.0, 10.0, 1.15), (26.0, 22.0, 0.95), (50.0, 42.0, 1.30))

#: Сосны: вне обеих ног дороги и вне ограды, но в поле клетки.
PINES = ((-44.0, 46.0, 0.4), (-38.0, -44.0, 2.1), (58.0, 12.0, 3.8),
         (54.0, 44.0, 4.6), (-30.0, 8.0, 5.9), (18.0, 66.0, 7.2),
         (-20.0, 60.0, 8.4), (64.0, 40.0, 9.1), (60.0, -20.0, 10.3))

#: Габарит ограды: цоколь, столб, две лажи, тумба, пилон ворот.
PLINTH = (0.40, 0.62)       # полуширина и высота цоколя над землёй
POST = (0.13, 0.13, 1.48)
RAIL_H = (0.07, 0.07)
RAIL_Z = (0.86, 1.34)
PILLAR = (0.46, 0.46, 1.62)
PIER = (0.52, 0.52, 2.30)
PEN_POST = (0.10, 0.10, 1.05)

#: На сколько усадить основание в снег, чтобы под плитой не было щели.
SINK = 0.08


def _place(lx: float, lz: float):
    """Локальная точка клетки -> оси Blender для этой же клетки."""
    return (lx, -lz)


def _ground(lx: float, lz: float) -> float:
    """Отметка пола двора поселения под локальной точкой."""
    return W.world_height(W.SETTLEMENT[0] + lx, W.SETTLEMENT[1] + lz)


def _move(obj, dx: float, dy: float, dz: float) -> None:
    """Перенести меш объекта на месте: объекты дома собраны вокруг нуля."""
    for v in obj.data.vertices:
        v.co.x += dx
        v.co.y += dy
        v.co.z += dz


def build_house(materials):
    """Изба и мастерская: секции `build_cabin`, переведённые в клетку."""
    objs = LP.build_cabin(materials)
    (px, py) = _place(HOUSE[0], HOUSE[1])
    z = _ground(HOUSE[0], HOUSE[1]) - SINK
    for obj in objs:
        _move(obj, px, py, z)
        obj.name = "Hut_" + obj.name.split("_")[-1]
    # Мастерская — тот же сруб, но без трубы и чуть меньше: приют чинит машины
    # в отдельном доме, а не в жилом.
    shop = LP.build_cabin(materials)
    (sx, sy) = _place(SHOP[0], SHOP[1])
    sz = _ground(SHOP[0], SHOP[1]) - SINK
    for obj in shop:
        if "Chimney" in obj.name:
            continue
        _move(obj, sx, sy, sz)
        obj.name = "Hut_Shop_" + obj.name.split("_")[-1]
        objs.append(obj)
    return objs


def _post(b, lx: float, lz: float, height: float, rot: float,
          size=None, mat: str = "Level_WoodDark") -> None:
    """Столб или тумба: основание утоплено на 0,25 м, верх — `height` над землёй."""
    (px, py) = _place(lx, lz)
    size = size or POST
    base = _ground(lx, lz) - 0.25
    top = _ground(lx, lz) + height
    b.box_rot((px, py, (base + top) * 0.5),
              (size[0], size[1], top - base), (0.0, 0.0, rot), mat, bevel=0.02)


def _run(b, a, c, *, gate=None, plinth=True, rails=True, post_h=None):
    """Пролёт ограды от `a` до `c`: цоколь, столбы каждые 3 м, две лажи.

    `gate` — отрезок вдоль пролёта, где нет ни цоколя, ни лаж: там ворота.
    Цоколь и лажи не сливаются в одну плоскость ни друг с другом, ни с землёй:
    верх цоколя 0,52, нижняя лажа 0,68 — зазор 16 см, и `check_frontier`
    считает это гранями на разных высотах, а не мерцанием.
    """
    (ax, az) = a
    (cx, cz) = c
    length = math.hypot(cx - ax, cz - az)
    rot = math.degrees(math.atan2(-(cx - ax), -(cz - az)))
    n = max(1, int(round(length / 3.0)))
    post_h = post_h or POST[2]

    def skip(t: float) -> bool:
        if not gate:
            return False
        return gate[0] <= t * length <= gate[1]

    if plinth:
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
            base = g - 0.30
            b.box_rot((px, py, (base + g + PLINTH[1]) * 0.5),
                      (PLINTH[0], seg, g + PLINTH[1] - base),
                      (0.0, 0.0, rot), "Level_Stone", bevel=0.02)
            k = k1 + 1
    for k in range(n + 1):
        t = k / n
        # У ворот столбы остаются: створ обязан выглядеть проёмом в линии, а не
        # дырой в траве, поэтому столбы ставим только на границах проёма.
        if gate and skip(t) and min(abs(t * length - gate[0]),
                                   abs(t * length - gate[1])) > 0.75:
            continue
        (lx, lz) = (ax + (cx - ax) * t, az + (cz - az) * t)
        _post(b, lx, lz, post_h, rot)
    if not rails:
        return
    for z_off in RAIL_Z:
        for k in range(n):
            if skip((k + 0.5) / n):
                continue
            t0 = k / n
            t1 = (k + 1) / n
            mx = ax + (cx - ax) * (t0 + t1) * 0.5
            mz = az + (cz - az) * (t0 + t1) * 0.5
            seg = length / n
            (px, py) = _place(mx, mz)
            b.box_rot((px, py, _ground(mx, mz) + z_off),
                      (RAIL_H[0], seg, RAIL_H[1]), (0.0, 0.0, rot),
                      "Level_WoodDark", bevel=0.01)


def _fence(materials):
    """Ограда двора: цоколь, лажи, угловые тумбы и пилоны двух ворот."""
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = PARK
    _run(b, (x0, z0), (x0, z1), gate=(GATE_W[0] - z0, GATE_W[1] - z0))
    _run(b, (x1, z0), (x1, z1))
    _run(b, (x0, z1), (x1, z1), gate=(GATE_N[0] - x0, GATE_N[1] - x0))
    _run(b, (x0, z0), (x1, z0))
    # Тумбы: четыре угла и средины длинных сторон. Ограда в 60 м с камнем
    # только по углам читается лентой, а тумба каждые 30 м даёт глазу масштаб
    # и держит линию там, где к ней примыкает загон или пост.
    for (lx, lz) in ((x0, z0), (x1, z0), (x0, z1), (x1, z1),
                     (x0, (z0 + z1) * 0.5), (x1, (z0 + z1) * 0.5),
                     ((x0 + x1) * 0.5, z0), ((x0 + x1) * 0.5, z1)):
        _post(b, lx, lz, PILLAR[2], 45.0, size=PILLAR, mat="Level_Stone")
        (px, py) = _place(lx, lz)
        b.box_rot((px, py, _ground(lx, lz) + PILLAR[2] + 0.09),
                  (0.30, 0.30, 0.18), (0.0, 0.0, 45.0), "Level_Metal", bevel=0.02)
    # Пилоны ворот: каменный столб с металлическим оголовком и фонарём на кронштейне.
    for (axis, at, span) in (("z", x0, GATE_W), ("x", z1, GATE_N)):
        for edge in span:
            (gx, gz) = (at, edge) if axis == "z" else (edge, at)
            _post(b, gx, gz, PIER[2], 0.0, size=PIER, mat="Level_Stone")
            (px, py) = _place(gx, gz)
            top = _ground(gx, gz) + PIER[2]
            b.box_rot((px, py, top + 0.10), (0.34, 0.34, 0.20),
                      (0.0, 0.0, 0.0), "Level_Metal", bevel=0.02)
            (ox, oy) = (0.31, 0.0) if axis == "z" else (0.0, -0.31)
            b.box_rot((px + ox, py + oy, top + 0.36), (0.10, 0.10, 0.10),
                      (0.0, 0.0, 0.0), "Level_Metal", bevel=0.01)
            b.ico((px + ox * 2.0, py + oy * 2.0, top + 0.32), 0.16,
                  "Level_Metal", subdivisions=1)
    return b.build("Hut_Fence", recalc=False)


def _pens(materials):
    """Загоны приюта: низкие секции, корыто и сено — в каждой свой угол."""
    b = level_lib.LevelBuilder(materials)
    for (px0, px1, pz0, pz1) in PENS:
        _run(b, (px0, pz0), (px1, pz0), plinth=False, post_h=PEN_POST[2])
        _run(b, (px0, pz1), (px1, pz1), plinth=False, post_h=PEN_POST[2])
        _run(b, (px0, pz0), (px0, pz1), plinth=False, post_h=PEN_POST[2])
        _run(b, (px1, pz0), (px1, pz1), plinth=False, post_h=PEN_POST[2])
    for (lx, lz) in TROUGH:
        (bx, by) = _place(lx, lz)
        g = _ground(lx, lz)
        b.box_rot((bx, by, g + 0.24), (1.60, 0.70, 0.42), (0.0, 0.0, 0.0),
                  "Level_Metal", bevel=0.02)
        b.box_rot((bx, by, g + 0.33), (1.44, 0.54, 0.06), (0.0, 0.0, 0.0),
                  "Level_Ice", bevel=0.0)
    for (lx, lz, rot) in HAY:
        CP.merge(b, LP.build_log_pile(materials), lx, lz, _ground(lx, lz),
                 sink=-0.02, rot=rot)
    return b.build("Hut_Pen", recalc=False)


def _power(materials):
    """Электропост: домик машины, постаменты трансформаторов, кабель и столбы.

    Поселение держит узел теплом — это сюжетная точка (тепло дают дороги и
    площадки), поэтому у подстанции свой двор с цоколями, а не три ящика на
    снегу. Крышка кабельной канавки лежит на 3 см НИЖЕ уровня земли: вровень
    она слилась бы с покрытием в одну плоскость, и `check_frontier` посчитал бы
    это мерцанием.
    """
    b = level_lib.LevelBuilder(materials)
    (x0, x1, z0, z1) = POWER
    _run(b, (x0, z0), (x1, z0), plinth=False)
    _run(b, (x0, z1), (x1, z1), plinth=False)
    _run(b, (x0, z0), (x0, z1), plinth=False)
    _run(b, (x1, z0), (x1, z1), plinth=False)
    (hx, hz) = POWER_HOUSE
    (bx, by) = _place(hx, hz)
    g = _ground(hx, hz)
    b.box_rot((bx, by, g + 0.12), (5.60, 4.40, 0.30), (0.0, 0.0, 0.0),
              "Level_Stone", bevel=0.02)
    b.box_rot((bx, by, g + 1.55), (5.00, 3.80, 2.60), (0.0, 0.0, 0.0),
              "Level_Roof", bevel=0.03)
    b.box_rot((bx, by, g + 3.10), (5.40, 4.20, 0.30), (0.0, 0.0, 0.0),
              "Level_Metal", bevel=0.02)
    b.box_rot((bx - 1.60, by + 1.10, g + 3.90), (0.44, 0.44, 1.60),
              (0.0, 0.0, 0.0), "Level_Rust", bevel=0.02)
    for (ox, oy, ow, oh) in ((0.0, -1.94, 1.00, 1.90), (-1.60, -1.94, 0.80, 0.80),
                             (1.60, -1.94, 0.80, 0.80)):
        b.box_rot((bx + ox, by + oy, g + 0.30 + oh * 0.5), (ow, 0.10, oh),
                  (0.0, 0.0, 0.0), "Level_Window", bevel=0.0)
    for (tx, tz) in TRANSFORMERS:
        (px, py) = _place(tx, tz)
        tg = _ground(tx, tz)
        b.box_rot((px, py, tg + 0.20), (2.20, 2.20, 0.40), (0.0, 0.0, 0.0),
                  "Level_Stone", bevel=0.02)
        b.band((px, py), 0.62, tg + 0.40, tg + 1.70, "Level_Roof", n=10)
        for k in (-0.34, 0.0, 0.34):
            b.tube([(px + k, py - 0.30, tg + 1.70), (px + k, py - 0.30, tg + 2.24)],
                   [(0.13, 0.13), (0.08, 0.08)], "Level_PineSnow")
    (cx0, cz0) = CABLE_RUN[0]
    (cx1, _cz1) = CABLE_RUN[1]
    (mx, my) = _place((cx0 + cx1) * 0.5, cz0)
    mg = _ground((cx0 + cx1) * 0.5, cz0)
    b.box_rot((mx, my, mg - 0.03), (cx1 - cx0, 0.80, 0.34), (0.0, 0.0, 0.0),
              "Level_Window", bevel=0.0)
    for (px, pz) in CABLE_POLES:
        (bx2, by2) = _place(px, pz)
        pg = _ground(px, pz)
        b.box_rot((bx2, by2, pg + 2.55), (0.24, 0.24, 5.30), (0.0, 0.0, 0.0),
                  "Level_WoodDark", bevel=0.02)
        b.box_rot((bx2, by2, pg + 4.90), (1.60, 0.18, 0.16), (0.0, 0.0, 0.0),
                  "Level_Metal", bevel=0.02)
    return b.build("Hut_Power", recalc=False)


def _mast(materials):
    """Метеоплощадка: цоколь под вышку, приборные ящики и анемометр на шесте."""
    b = level_lib.LevelBuilder(materials)
    (mx, mz) = MAST
    (bx, by) = _place(mx, mz)
    g = _ground(mx, mz)
    b.box_rot((bx, by, g + 0.25), (3.20, 3.20, 0.56), (0.0, 0.0, 0.0),
              "Level_Stone", bevel=0.02)
    # Ступица, в которую встаёт модель вышки: низ `MenuStationMast` на −0,34,
    # поэтому посадка в сцене идёт от верха этого стакана, а не от земли.
    b.band((bx, by), 0.52, g + 0.53, g + 1.05, "Level_Metal", n=10)
    for (ix, iz) in INSTRUMENTS:
        (px, py) = _place(ix, iz)
        ig = _ground(ix, iz)
        b.box_rot((px, py, ig + 0.60), (0.90, 0.70, 1.20), (0.0, 0.0, 0.0),
                  "Level_Roof", bevel=0.02)
        b.box_rot((px, py, ig + 1.26), (0.98, 0.78, 0.10), (0.0, 0.0, 0.0),
                  "Level_Metal", bevel=0.0)
    (ax, az) = (mx - 3.20, mz - 1.20)
    (abx, aby) = _place(ax, az)
    ag = _ground(ax, az)
    b.box_rot((abx, aby, ag + 1.65), (0.10, 0.10, 3.50), (0.0, 0.0, 0.0),
              "Level_Metal", bevel=0.01)
    b.ico((abx, aby, ag + 3.46), 0.22, "Level_PineSnow", subdivisions=1)
    return b.build("Hut_Mast", recalc=False)


def _dressing(materials):
    """Обитание двора: тропы, дрова, наносы, камни и шипы — в одном объекте."""
    b = level_lib.LevelBuilder(materials)
    CP.trodden(b, PATH, _ground, width=1.8, mat="Level_SnowDeep", lift=0.05)
    CP.trodden(b, PATH_POWER, _ground, width=1.4, mat="Level_SnowDeep", lift=0.05)
    for (lx, lz, rx, ry, h, seed) in DRIFTS:
        CP.drift(b, lx, lz, _ground(lx, lz), rx, ry, h, seed)
    for (lx, lz, r) in BILLOWS:
        CP.billow(b, lx, lz, _ground(lx, lz), r, seed=lx * 0.7 + lz)
    for (lx, lz, seed) in ROCKS:
        CP.merge(b, LP.build_rock(materials, 0.4 if seed % 2 else 2.7), lx, lz,
                 _ground(lx, lz), sink=-0.18, rot=seed * 37.0)
    for (lx, lz, h) in SHARDS:
        CP.shard(b, lx, lz, _ground(lx, lz), h, seed=lz * 0.3 + lx)
    for (lx, lz, rot) in LOGS:
        CP.merge(b, LP.build_log_pile(materials), lx, lz, _ground(lx, lz),
                 sink=-0.02, rot=rot)
    CP.merge(b, LP.build_log_bundle(materials), BUNDLE[0], BUNDLE[1],
             _ground(*BUNDLE), sink=-0.02, rot=25.0)
    return b.build("Hut_Dressing", recalc=False)


def build_cell_settlement(materials, i: int, j: int):
    """Объекты поселения клетки: пустой список, если клетка не поселения."""
    if (i, j) != CELL:
        return []
    objs = build_house(materials) + [_fence(materials), _pens(materials),
                                     _power(materials), _mast(materials),
                                     _dressing(materials)]
    for (n, (lx, lz, seed)) in enumerate(PINES):
        tree = LP.build_pine(materials, seed, name="Hut_Pine_%d" % n)
        (px, py) = _place(lx, lz)
        _move(tree, px, py, _ground(lx, lz) - 0.15)
        objs.append(tree)
    # Часть живёт своим FBX и обязана носить клетку в имени: по суффиксу
    # `<i>_<j>` префаб клетки собирается из частей (`sync_cell_prefabs.cs`).
    # Без него «Hut_Chimney» попал бы в клетку по догадке, а не по имени.
    for obj in objs:
        obj.name = "%s_%d_%d" % (obj.name, i, j)
    return objs
