"""Дизайн ездовых ходунов FrostSpace на ригах Ultimate Space Kit.

Пак Quaternius — это «мех-костюм с открытым креслом»: пилот сидит в чаше
посреди корпуса, ноги у меха длинные и обратноколенные, на плечах блоки с
выхлопами. Мы повторяем **анатомию** (кресло, кабина, длинные ноги, наплечные
блоки), но корпус строим свой — фасетный, из крупных масс, в палитре
`robot_lib.PALETTE` с подъёмом `lift_for_game()` (STYLE.md, ADR 0024).

Правила те же, что у роботов и животных:
- вперёд −Y, вверх +Z, вбок +X;
- каждая деталь весит ровно в одну кость (`PartMesh`), меш не гнётся;
- ничего мельче ~0.06 м, плоские нормали, никаких булевых операций.

**Кабина.** Объём `CAVITY` — место, куда садится игровой робот. Дизайн обязан
держать его пустым: `check_mech_clips.py` проверяет это по вершинам готового
FBX, а не «на глаз». Якоря посадки в префабе (`Seat`, `Seat Point`, `Grip L/R`,
`Foot L/R`) ставятся по числам `COCKPIT`.
"""

from __future__ import annotations

import math

from robot_lib import PartMesh, make_materials, rig_points

#: Точки посадки в локальных координатах ходуна, м.
#: seat — середина подушки, seat_point — куда встаёт таз наездника,
#: grips — рукоятки перед наездником, feet — упоры под ступни.
COCKPIT = {
    "seat": (0.0, 0.02, 1.36),
    "seat_point": (0.0, 0.02, 1.45),
    "grips": 0.24,
    "grip": (-0.40, 1.72),
    "foot": (-0.44, 1.00),
}

#: Пустой объём кабины: (x0, x1, y0, y1, z0, z1). Всё, что внутри, — брак:
#: там сидит игрок (корпус и голова, выше подушки и выше тазового блока).
#: Проверяется по вершинам готового FBX.
CAVITY = (-0.24, 0.24, -0.32, 0.18, 1.44, 2.15)

class _Shift:
    """Обёртка над `PartMesh`: переносит точки на (dy, dz).

    Кресло и консоль описаны числами для рига `mech` (кость Torso на 1.357 м).
    У другой высоты корпуса кабина строится в тех же числах, но сдвинутой на
    разницу опорной кости — иначе кресло уезжает в таз.
    """

    def __init__(self, part, offset):
        self._p = part
        self._o = offset

    def _point(self, point):
        return (point[0], point[1] + self._o[1], point[2] + self._o[2])

    def box(self, center, size, *args, **kwargs):
        return self._p.box(self._point(center), size, *args, **kwargs)

    def beam(self, start, end, *args, **kwargs):
        return self._p.beam(self._point(start), self._point(end), *args, **kwargs)

    def tube(self, points, *args, **kwargs):
        return self._p.tube([self._point(pt) for pt in points], *args, **kwargs)

    def disc(self, center, *args, **kwargs):
        return self._p.disc(self._point(center), *args, **kwargs)


def _bolts(p, points, mat, bone, size=0.02):
    for point in points:
        p.box(point, (size, size, size), mat, bone, bevel=0.0)


def _rivet_ring(p, center, radius, mat, bone, axis="X", count=8, size=0.026):
    for i in range(count):
        angle = math.tau * i / count
        if axis == "X":
            point = (center[0], center[1] + math.cos(angle) * radius,
                     center[2] + math.sin(angle) * radius)
        else:
            point = (center[0] + math.cos(angle) * radius, center[1],
                     center[2] + math.sin(angle) * radius)
        p.box(point, (size, size, size), mat, bone, bevel=0.0)


def _hazard(p, center, size, mat, bone, count=3, gap=0.07, rot=None):
    """Косая маркировка: несколько наклонённых полос."""
    cx, cy, cz = center
    for i in range(count):
        p.box((cx + (i - (count - 1) * 0.5) * gap, cy, cz),
              size, mat, bone, bevel=0.0, rot=rot)


# ------------------------------------------------------------------ кабина

def _cockpit(p, M, seat_bone, bulk=1.0):
    """Кресло, каркас, консоль, подножка и дуга над головой наездника."""
    base, panel, dark = M["base"], M["panel"], M["dark"]
    metal = M["metal"]
    accent = M["highlight"]

    # Подушка и спинка: наездник сидит в чаше, спина закрыта.
    p.box((0, 0.02, 1.32), (0.52 * bulk, 0.46, 0.09), dark, seat_bone,
          bevel=0.02)
    p.box((0, 0.02, 1.39), (0.46 * bulk, 0.40, 0.06), panel, seat_bone,
          bevel=0.015)
    p.box((0, 0.30, 1.83), (0.54 * bulk, 0.14, 0.74), base, seat_bone,
          bevel=0.03)
    p.box((0, 0.26, 1.80), (0.42 * bulk, 0.07, 0.58), panel, seat_bone,
          bevel=0.02)
    p.box((0, 0.30, 2.12), (0.34, 0.14, 0.18), dark, seat_bone, bevel=0.03)

    # Боковины-подлокотники: между ними сидит наездник.
    for sx in (-1, 1):
        p.box((sx * 0.34, 0.0, 1.56), (0.10, 0.56, 0.26), base, seat_bone,
              bevel=0.02)
        p.box((sx * 0.34, 0.0, 1.70), (0.12, 0.52, 0.05), panel, seat_bone,
              bevel=0.01)
        # Вентиляционная решётка на внешней стороне подлокотника.
        for i in range(3):
            p.box((sx * 0.395, -0.12 + i * 0.08, 1.52),
                  (0.014, 0.05, 0.14), dark, seat_bone, bevel=0.0)
        _bolts(p, [(sx * 0.395, 0.20, 1.68), (sx * 0.395, 0.20, 1.42)],
               metal, seat_bone)

    # Консоль перед наездником: экран, тумблеры и рукоятки.
    p.box((0, -0.42, 1.62), (0.46 * bulk, 0.10, 0.22), base, seat_bone,
          bevel=0.02, rot=(18, 0, 0))
    p.box((0, -0.465, 1.655), (0.30, 0.03, 0.10), M["glow"], seat_bone,
          bevel=0.0, rot=(18, 0, 0))
    _hazard(p, (0, -0.46, 1.53), (0.05, 0.02, 0.07), accent, seat_bone,
            count=3, gap=0.09)
    for sx in (-1, 1):
        p.beam((sx * COCKPIT["grips"], -0.40, 1.72),
               (sx * COCKPIT["grips"], -0.50, 1.69),
               0.06, 0.06, 0.05, 0.05, dark, seat_bone)
        p.box((sx * COCKPIT["grips"], -0.52, 1.68), (0.07, 0.05, 0.07),
              metal, seat_bone, bevel=0.012)

    # Подножка: на неё встают ступни наездника.
    p.box((0, -0.44, 0.96), (0.50 * bulk, 0.30, 0.07), base, seat_bone,
          bevel=0.02)
    for sx in (-1, 1):
        p.box((sx * 0.19, -0.48, 1.01), (0.16, 0.22, 0.05), dark,
              seat_bone, bevel=0.01)
        # Наклонный щиток от грязи перед ступнями.
        p.beam((sx * 0.22, -0.58, 0.98), (sx * 0.22, -0.62, 1.34),
               0.10, 0.06, 0.08, 0.05, panel, seat_bone, roll=sx * 6)
    p.beam((0, -0.58, 0.98), (0, -0.63, 1.36), 0.34, 0.05, 0.30, 0.05,
           panel, seat_bone, roll=0)

    # Дуга над наездником: страховка на случай тряски.
    p.tube([(-0.36, 0.26, 1.74), (-0.27, 0.30, 2.20),
            (0.0, 0.31, 2.30), (0.27, 0.30, 2.20),
            (0.36, 0.26, 1.74)],
           [(0.045, 0.045)] * 5, metal, seat_bone, n=6, fan_caps=True)


# ------------------------------------------------------------------ корпус

def _hips(p, M, seat_bone, hip_point, bulk=1.0):
    """Тазовый блок под креслом: на нём стоит кабина, к нему идут бёдра."""
    base, panel, dark = M["base"], M["panel"], M["dark"]
    _, hy, hz = hip_point
    # Блок держится ниже подушки (z 1.44) — в объём кабины он не заходит.
    p.box((0, hy - 0.04, hz - 0.33), (0.70 * bulk, 0.46, 0.30), base,
          seat_bone, bevel=0.03)
    # Наклонная опора спинки: не дальше спинки кресла, иначе она входит
    # в объём кабины у ригов с другой геометрией таза (ловит чекер).
    p.box((0, max(hy + 0.15, 0.31), hz - 0.10), (0.52 * bulk, 0.18, 0.44),
          panel, seat_bone, bevel=0.02)
    for sx in (-1, 1):
        p.disc((sx * 0.33 * bulk, hy - 0.02, hz - 0.33), 0.13, 0.10, dark,
               seat_bone, axis="X", n=8)
        _rivet_ring(p, (sx * 0.36 * bulk, hy - 0.02, hz - 0.33), 0.10,
                    M["metal"], seat_bone, axis="X", count=6)


def _legs(p, M, R, bulk=1.0, boot=1.0, brace=False):
    """Ноги ходуна: бедро, голень и плоская механическая ступня.

    После `straighten_stance` нога — колонна с коленом, вынесенным вперёд на
    0.40 м: бедро идёт вниз-вперёд, голень — вниз-назад. Броня строится ровно
    по этим костям (`rig_points`), поэтому новая стойка и походка держатся без
    правки чисел здесь.
    """
    base, panel, dark = M["base"], M["panel"], M["dark"]
    metal, accent = M["metal"], M["highlight"]

    for side, out in (("L", 1.0), ("R", -1.0)):
        # Точки костей уже несут сторону −X/+X, поэтому боковое зеркало к ним
        # не применяется (иначе обе ноги встают в одну); `out` — только для
        # смещений деталей наружу.
        sx = 1.0
        upper, lower, foot = ("UpperLeg." + side, "LowerLeg." + side,
                              "Foot." + side)
        hip, knee = R[upper][0], R[upper][1]
        knee_l, ankle = R[lower][0], R[lower][1]
        ankle_f, toe = R[foot][0], R[foot][1]

        # --- бедро: короб вдоль кости + щиток снаружи.
        p.beam((sx * hip.x, hip.y - 0.04, hip.z),
               (sx * knee.x, knee.y + 0.02, knee.z),
               0.30 * bulk, 0.34 * bulk, 0.24 * bulk, 0.26 * bulk,
               base, upper)
        p.beam((hip.x + out * 0.14, hip.y - 0.12, hip.z - 0.02),
               (knee.x + out * 0.10, knee.y + 0.06, knee.z + 0.02),
               0.06, 0.24 * bulk, 0.05, 0.18 * bulk, panel, upper)
        p.box((sx * hip.x, hip.y - 0.02, hip.z + 0.02),
              (0.22, 0.26, 0.16), panel, upper, bevel=0.03, rot=(18, 0, 0))

        # --- колено: диск и накладка.
        p.disc((sx * knee.x, knee.y + 0.02, knee.z), 0.15 * bulk, 0.14, dark,
               lower, axis="X", n=8)
        p.box((sx * knee.x, knee.y - 0.06, knee.z + 0.06),
              (0.20, 0.14, 0.16), panel, lower, bevel=0.02)

        # --- голень: короб + гидравлика вдоль кости.
        p.beam((sx * knee_l.x, knee_l.y + 0.02, knee_l.z),
               (sx * ankle.x, ankle.y - 0.02, ankle.z + 0.10),
               0.26 * bulk, 0.30 * bulk, 0.20 * bulk, 0.22 * bulk,
               base, lower)
        p.beam((knee_l.x + out * 0.10, knee_l.y - 0.02, knee_l.z - 0.04),
               (ankle.x + out * 0.09, ankle.y + 0.02, ankle.z + 0.16),
               0.05, 0.06, 0.05, 0.06, metal, lower)
        p.tube([(knee_l.x + out * 0.10, knee_l.y - 0.02, knee_l.z - 0.04),
                (ankle.x + out * 0.09, ankle.y + 0.02, ankle.z + 0.16)],
               [(0.034, 0.034), (0.034, 0.034)], dark, lower, n=6)
        p.box((knee_l.x + out * 0.02, knee_l.y + 0.12, (knee_l.z + ankle.z) * 0.5),
              (0.22 * bulk, 0.10, 0.30), panel, lower, bevel=0.02)

        if brace:
            # Тонконогий фламинго: дополнительная распорка голени.
            p.beam((sx * knee_l.x, knee_l.y - 0.02, knee_l.z + 0.02),
                   (sx * ankle.x, ankle.y - 0.04, ankle.z + 0.34),
                   0.07, 0.07, 0.06, 0.06, accent, lower)

        # --- ступня: узкий башмак с плоской опорной плитой и пяткой.
        # Высоты здесь фиксированные, а не × boot: ширина/длина растут с
        # размером юнита, а подошва обязана остаться на z=0 — иначе ступня
        # проваливается в пол (ловит check_mech_clips).
        #
        # Пальцев-когтей нет: подошва в походке параллельна полу весь шаг, и
        # клинья первые втыкались бы в снег на перекате, а ступня читалась бы
        # лапой, а не опорой механизма.
        heel = ankle_f.y + 0.20
        tip = toe.y - 0.04
        sole_len = heel - tip
        sole_y = (heel + tip) * 0.5
        p.box((sx * (ankle_f.x + toe.x) * 0.5, sole_y, 0.055),
              (0.30 * boot, sole_len, 0.09), base, foot, bevel=0.015)
        p.box((sx * (ankle_f.x + toe.x) * 0.5, sole_y, 0.01),
              (0.26 * boot, sole_len - 0.04, 0.02), dark, foot, bevel=0.004)
        # Пятка: упор сзади, чтобы ступня не читалась одним носком.
        p.box((sx * ankle_f.x, heel - 0.09, 0.145),
              (0.26 * boot, 0.18, 0.16), dark, foot, bevel=0.02)
        # Носок: короткая накладка на плите.
        p.box((sx * toe.x, tip + 0.10, 0.13),
              (0.24 * boot, 0.20, 0.10), panel, foot, bevel=0.02)
        # Привод голеностопа — короб, а не диск: у человеческого рига кость
        # стопы всего на 0.036, и диск радиусом 0.10 уходил под пол.
        p.box((sx * ankle_f.x, ankle_f.y, max(ankle_f.z, 0.12) + 0.07),
              (0.22, 0.24, 0.20), metal, foot, bevel=0.02)
        p.beam((sx * ankle_f.x, ankle_f.y - 0.02, 0.16),
               (sx * toe.x, toe.y + 0.06, 0.11),
               0.07, 0.07, 0.06, 0.06, dark, foot)


def _shoulders(p, M, chest_bone, chest_point, pods=2, bulk=1.0, stacks=False):
    """Наплечные блоки с выхлопами: главный силуэт пака, но нашими массами."""
    base, panel, dark = M["base"], M["panel"], M["dark"]
    metal, accent = M["metal"], M["highlight"]
    _, cy, cz = chest_point

    # Спинка за креслом — общий корпус кабины. Глубина не от кости груди:
    # у разных ригов она разная, и плита заезжала в объём кабины (у пчелы
    # грудь на 0.03, у меха на 0.12). Держим её за спинкой кресла.
    back = max(cy + 0.24, 0.31)
    p.box((0, back, cz - 0.06), (0.62 * bulk, 0.20, 0.52), base,
          chest_bone, bevel=0.03)
    p.box((0, back + 0.10, cz - 0.02), (0.44, 0.08, 0.34), panel,
          chest_bone, bevel=0.02)
    _hazard(p, (0, back + 0.14, cz - 0.14), (0.05, 0.02, 0.09), accent,
            chest_bone, count=3, gap=0.09)

    for sx in (-1, 1):
        p.box((sx * 0.42 * bulk, cy + 0.06, cz + 0.10),
              (0.26 * bulk, 0.40, 0.36), base, chest_bone, bevel=0.035,
              rot=(0, sx * 6, 0))
        p.box((sx * 0.44 * bulk, cy - 0.10, cz + 0.16),
              (0.22 * bulk, 0.12, 0.26), panel, chest_bone, bevel=0.02)
        _rivet_ring(p, (sx * 0.44 * bulk, cy - 0.17, cz + 0.16), 0.09,
                    metal, chest_bone, axis="Y", count=6)

        # Выхлопы: трубы вверх-назад, по две на плечо.
        for index in range(pods):
            offset = (index - (pods - 1) * 0.5) * 0.13
            p.tube([(sx * (0.44 * bulk + offset * 0.4), cy + 0.16, cz + 0.22),
                    (sx * (0.44 * bulk + offset * 0.4), cy + 0.34, cz + 0.44),
                    (sx * (0.44 * bulk + offset * 0.4), cy + 0.46, cz + 0.56)],
                   [(0.075, 0.075), (0.072, 0.072), (0.078, 0.078)],
                   dark, chest_bone, n=6)
            p.tube([(sx * (0.44 * bulk + offset * 0.4), cy + 0.45, cz + 0.55),
                    (sx * (0.44 * bulk + offset * 0.4), cy + 0.52, cz + 0.61)],
                   [(0.09, 0.09), (0.09, 0.09)], metal, chest_bone, n=6)
        _bolts(p, [(sx * 0.56 * bulk, cy + 0.06, cz + 0.22),
                   (sx * 0.56 * bulk, cy + 0.06, cz - 0.02)], metal, chest_bone)

    if stacks:
        # Тяжёлый вариант: два вертикальных выхлопа за спиной.
        for sx in (-1, 1):
            p.tube([(sx * 0.20, cy + 0.30, cz + 0.10),
                    (sx * 0.20, cy + 0.34, cz + 0.58)],
                   [(0.085, 0.085), (0.075, 0.075)], dark, chest_bone, n=6)
            p.tube([(sx * 0.20, cy + 0.34, cz + 0.56),
                    (sx * 0.20, cy + 0.35, cz + 0.64)],
                   [(0.10, 0.10), (0.10, 0.10)], metal, chest_bone, n=6)


# ------------------------------------------------------------------ дизайны

def _frog(p, M, R, roles, meta):
    """Лягушачий ходун: грязевые щитки над коленями, катушки и стропы."""
    accent = M["highlight"]
    for out, side in ((1.0, "L"), (-1.0, "R")):
        knee = R["LowerLeg." + side][0]
        # Крыло-грязевик над коленом.
        p.box((knee.x, knee.y - 0.10, knee.z + 0.20),
              (0.30, 0.34, 0.07), M["panel"], "LowerLeg." + side,
              bevel=0.02, rot=(14, 0, out * 8))
        p.box((knee.x, knee.y - 0.22, knee.z + 0.24),
              (0.24, 0.08, 0.05), accent, "LowerLeg." + side, bevel=0.0)
        hip = R["UpperLeg." + side][0]
        # Буксировочная петля на бедре.
        p.tube([(hip.x + out * 0.16, hip.y + 0.06, hip.z - 0.02),
                (hip.x + out * 0.20, hip.y - 0.10, hip.z - 0.10),
                (hip.x + out * 0.16, hip.y - 0.24, hip.z - 0.16)],
               [(0.03, 0.03)] * 3, M["rope"], "UpperLeg." + side, n=4)
    # Катушка со стропой на спине.
    p.disc((0.0, R["Chest"][0].y + 0.44, R["Chest"][0].z - 0.10), 0.16, 0.12,
           M["rope"], "Chest", axis="X", n=8)
    p.box((0.0, R["Chest"][0].y + 0.44, R["Chest"][0].z + 0.06),
          (0.06, 0.10, 0.18), accent, "Chest", bevel=0.01)


def _bee(p, M, R, roles, meta):
    """Пчелиный ходун: жалюзи-крылья и предупреждающие полосы."""
    accent = M["highlight"]
    chest = R["Chest"][0]
    for sx in (-1, 1):
        # Жалюзи-«крыло» на спине.
        for i in range(3):
            p.box((sx * (0.30 + i * 0.06), chest.y + 0.34,
                   chest.z + 0.26 + i * 0.04),
                  (0.16, 0.26, 0.04), M["panel"], "Chest",
                  bevel=0.0, rot=(0, 0, sx * (18 + i * 6)))
        p.box((sx * 0.34, chest.y + 0.34, chest.z + 0.12),
              (0.10, 0.28, 0.05), M["dark"], "Chest", bevel=0.0)
    # Полосы — на груди под кабиной: в самом проёме их быть не может.
    _hazard(p, (0.0, chest.y - 0.16, chest.z - 0.55), (0.06, 0.03, 0.16),
            accent, "Chest", count=3, gap=0.13)


def _panda(p, M, R, roles, meta):
    """Медвежий ходун: тяжёлые наплечники, выхлопные стойки, широкие ступни."""
    accent = M["highlight"]
    chest = R["Chest"][0]
    for sx in (-1, 1):
        p.box((sx * 0.52, chest.y + 0.04, chest.z + 0.04),
              (0.18, 0.34, 0.30), M["base"], "Chest", bevel=0.03,
              rot=(0, sx * 10, 0))
        p.box((sx * 0.55, chest.y - 0.12, chest.z + 0.06),
              (0.10, 0.12, 0.20), accent, "Chest", bevel=0.0)
        # Широкая накладка на ступне.
        side = "L" if sx > 0 else "R"
        toe = R["Foot." + side][1]
        p.box((toe.x * 0.6, toe.y + 0.16, 0.12), (0.34, 0.22, 0.06),
              M["panel"], "Foot." + side, bevel=0.02)


def _flamingo(p, M, R, roles, meta):
    """Ходуля-фламинго: поручни по бортам и кольца на голени."""
    accent = M["highlight"]
    for sx in (-1, 1):
        # Поручень вдоль подлокотника.
        p.tube([(sx * 0.40, -0.36, 1.52), (sx * 0.40, 0.06, 1.56),
                (sx * 0.38, 0.22, 1.62)],
               [(0.035, 0.035)] * 3, M["metal"], "Torso", n=6)
    # Кольцо на голени: опознавательная деталь.
    for side in ("L", "R"):
        knee = R["LowerLeg." + side][0]
        ankle = R["LowerLeg." + side][1]
        middle = ((knee.y + ankle.y) * 0.5, (knee.z + ankle.z) * 0.5)
        p.disc(((knee.x + ankle.x) * 0.5, middle[0], middle[1]), 0.17,
               0.09, accent, "LowerLeg." + side, axis="Y", n=8)


DESIGNS = {
    "FrogWalker": dict(
        kit=_frog, pods=2, bulk=1.34, boot=1.16,
        colors={"base": "#2F4A42", "panel": "#456B5C", "dark": "#101A18"}),
    "BeeWalker": dict(
        kit=_bee, pods=1, bulk=1.24, boot=1.06,
        colors={"base": "#4A3D22", "panel": "#7A6431", "dark": "#181510"}),
    "PandaWalker": dict(
        kit=_panda, pods=2, bulk=1.36, boot=1.20, stacks=True,
        colors={"base": "#3B2A2E", "panel": "#5E4046", "dark": "#140F10"}),
    "FlamingoWalker": dict(
        kit=_flamingo, pods=1, bulk=1.18, boot=1.02, brace=True,
        colors={"base": "#4A3340", "panel": "#6E4C5A", "dark": "#171114"}),
}

#: Порядок сборки — как в `MECHS`.
UNITS = ("FrogWalker", "BeeWalker", "PandaWalker", "FlamingoWalker")

#: Координаты, под которые настроена кабина: голова кости Torso у рига `mech`.
COCKPIT_ANCHOR = (-0.123, 1.357)


def cockpit_offset(R, roles) -> tuple:
    """Насколько кабина сдвинута относительно базовых чисел.

    Только по высоте: кресло стоит на тазовом блоке, ровно на уровне кости
    `Torso`. По глубине кабину за костью не ведём: спинка тогда наезжает на
    наплечный блок, и объём кабины перестаёт быть свободным.
    """
    target = R[roles["torso"]][0].z
    return (0.0, 0.0, target - COCKPIT["seat"][2])


def build(unit: str, arm, meta):
    """Собирает корпус ходуна на подготовленном риге.

    → (name, obj, mats, cockpit): cockpit — якоря посадки и объём кабины в
    метрах модели. По этим числам живут префабы и `check_mech_clips.py`.
    """
    design = DESIGNS[unit]
    name = "ME_" + unit
    # Акцент юнита — ключ палитры (`patina`, `yellow`, `accent`, `rose`,
    # `orange`): цвет роли берётся из `robot_lib.PALETTE`, под местный окрас
    # переопределяются только корпусные роли.
    accent_key = meta["cfg"]["accent"]
    keys = ["base", "panel", "dark", "metal", accent_key, "glow", "warm",
            "rope", "rust", "white"]
    mats, index = make_materials(name, keys, colors=dict(design["colors"]))
    M = dict(index)
    M["highlight"] = index[accent_key]

    roles = meta["roles"]
    R = rig_points(arm)
    p = PartMesh()

    chest_bone = roles["chest"]
    seat_bone = roles["torso"]

    _hips(p, M, seat_bone, R["UpperLeg.L"][0], bulk=design["bulk"])
    _legs(p, M, R, bulk=design["bulk"], boot=design["boot"],
          brace=design.get("brace", False))
    offset = cockpit_offset(R, roles)
    _cockpit(_Shift(p, offset), M, seat_bone, bulk=design["bulk"])
    dz = offset[2]
    cockpit = {
        "seat": (0.0, COCKPIT["seat"][1], COCKPIT["seat"][2] + dz),
        "seat_point": (0.0, COCKPIT["seat_point"][1],
                       COCKPIT["seat_point"][2] + dz),
        "grips": COCKPIT["grips"],
        "grip": (-0.50, 1.69 + dz),
        "foot": (-0.44, 0.96 + dz),
        # Полуширина постановки ступней: подножка под креслом идёт от
        # x = ±0.19, а не от полуширины рукояток. В Unity из этих чисел
        # строятся якоря наездника, поэтому они едут в манифест.
        "foot_half": 0.19,
        "cavity": cavity(offset),
    }
    _shoulders(p, M, chest_bone, R[chest_bone][0], pods=design["pods"],
               bulk=design["bulk"], stacks=design.get("stacks", False))

    design["kit"](p, M, R, roles, meta)

    obj = p.build(name, mats)
    return name, obj, mats, cockpit


def cavity(offset=(0.0, 0.0, 0.0)) -> tuple:
    """Объём кабины для проверки: (x0, x1, y0, y1, z0, z1)."""
    x0, x1, y0, y1, z0, z1 = CAVITY
    return (x0, x1, y0 + offset[1], y1 + offset[1],
            z0 + offset[2], z1 + offset[2])
