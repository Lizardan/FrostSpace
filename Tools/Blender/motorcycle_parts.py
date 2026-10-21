"""Дизайн снежного мотоцикла FrostSpace: статическая геометрия уровня.

Байк собран из трёх мешей, потому что колёса крутятся независимо от корпуса:

    MotorcycleBody   — рама, двигатель, бак, сиденье, вилка, руль, свет;
    MotorcycleWheelF — переднее колесо, начало координат в оси;
    MotorcycleWheelR — заднее колесо, начало координат в оси.

Оси и палитра — как в `level_lib.py`: вверх +Z, нос смотрит в Blender −Y. После
`export_static` это Unity +Z, то есть `transform.forward`; низ колёс лежит на
z = 0, поэтому корень модели ставится прямо на снег и ничего не висит в воздухе.

Колёса строятся вокруг своего нуля, а origin объекта ставится в ось: в Unity у них
должен быть собственный pivot, иначе вращение уводит колесо по кругу. Меш при этом
не сдвигается — геометрия колеса совпадает с его pivot.

Все массы крупные (от 0.03 м), фаска 0.008–0.03: силуэт читается с игрового
кадра, где мотоцикл занимает сотню пикселей. Тёплый свет — только Level_Window
(фара и стоп-сигнал), акцент — Level_Orange (полоса на баке).
"""

from __future__ import annotations

import math

from mathutils import Vector

import level_lib

#: Радиус колеса, м: низ колеса на земле, ось на этой высоте.
WHEEL_RADIUS = 0.38

#: Полуширина покрышки, м.
TIRE_WIDTH = 0.11

#: Внутренний радиус покрышки (обод), м.
TIRE_INNER = 0.25

#: Ось переднего и заднего колеса в координатах модели (y, z).
FRONT_WHEEL = (0.0, -0.78, WHEEL_RADIUS)
REAR_WHEEL = (0.0, 0.74, WHEEL_RADIUS)


def _ring(builder, center, outer, inner, half_width, mat, segments=14):
    """Покрышка: замкнутое кольцо в плоскости YZ с осью по X.

    Четыре ленты — наружная, внутренняя и два борта — дают замкнутый меш без
    n-гонов: при экспорте не появляется веерных треугольников и «стеклянных»
    нормалей.
    """
    cx, cy, cz = center
    base = len(builder.mb.verts)
    for i in range(segments):
        a = math.tau * i / segments
        y, z = math.cos(a), math.sin(a)
        builder.mb.verts.extend([
            Vector((cx - half_width, cy + y * outer, cz + z * outer)),
            Vector((cx + half_width, cy + y * outer, cz + z * outer)),
            Vector((cx - half_width, cy + y * inner, cz + z * inner)),
            Vector((cx + half_width, cy + y * inner, cz + z * inner)),
        ])

    mat_index = builder._index(mat)
    for i in range(segments):
        j = (i + 1) % segments
        ol, orr = base + i * 4, base + i * 4 + 1
        il, ir = base + i * 4 + 2, base + i * 4 + 3
        ol2, or2 = base + j * 4, base + j * 4 + 1
        il2, ir2 = base + j * 4 + 2, base + j * 4 + 3
        builder.mb.add_face((ol, ol2, or2, orr), mat_index)      # протектор
        builder.mb.add_face((il2, il, ir, ir2), mat_index)       # внутренняя
        builder.mb.add_face((il, il2, ol2, ol), mat_index)       # борт −X
        builder.mb.add_face((orr, or2, ir2, ir), mat_index)      # борт +X


def _wheel(materials, center, name, radius=WHEEL_RADIUS, half_width=TIRE_WIDTH,
           spokes=5):
    """Колесо с pivot в оси: покрышка, обод, ступица, спицы, тормозной диск."""
    b = level_lib.LevelBuilder(materials)
    inner = radius * (TIRE_INNER / WHEEL_RADIUS)

    _ring(b, (0.0, 0.0, 0.0), radius, inner, half_width, "Level_Stone",
          segments=14)

    # Обод и ступица — две трубы по оси X, между ними спицы.
    b.tube([(-half_width * 0.72, 0.0, 0.0), (half_width * 0.72, 0.0, 0.0)],
           [(inner * 1.02, inner * 1.02)] * 2, "Level_Metal", n=14,
           caps=(True, True))
    b.tube([(-half_width * 0.95, 0.0, 0.0), (half_width * 0.95, 0.0, 0.0)],
           [(0.075, 0.075)] * 2, "Level_Metal", n=8, caps=(True, True))

    spoke_center = (inner + 0.07) * 0.5 + 0.035
    for i in range(spokes):
        angle = 360.0 * i / spokes + 12.0
        rad = math.radians(angle)
        b.box_rot((0.0, math.cos(rad) * spoke_center,
                   math.sin(rad) * spoke_center),
                  (0.038, inner - 0.05, 0.05), (angle, 0.0, 0.0),
                  "Level_Metal", bevel=0.008)

    # Тормозной диск только с одной стороны — как на настоящем колесе.
    b.tube([(-half_width - 0.025, 0.0, 0.0), (-half_width - 0.005, 0.0, 0.0)],
           [(inner * 0.82, inner * 0.82)] * 2, "Level_Rust", n=14,
           caps=(True, True))

    obj = b.build(name)

    # Меш уже собран вокруг нуля (`_ring`/`tube`/`box_rot` выше получают (0, 0, 0)),
    # а origin объекта ставится в ось колеса. Сдвигать вершины на −center нельзя:
    # объект и меш уехали бы в противоположные стороны, и колесо оказалось бы
    # в начале модели — именно так оба колеса и рисовались в её центре.
    obj.location = Vector(center)
    return obj


def _fender(builder, center, radius, angles, mat, width, thickness=0.045):
    """Крыло по дуге колеса: короткие пластины, поставленные по касательной.

    Отдельные сегменты, а не гладкая лента: фасетный силуэт совпадает с
    остальным уровнем, а на стыках не нужна общая топология.
    """
    cx, cy, cz = center
    for angle in angles:
        rad = math.radians(angle)
        y = cy + math.cos(rad) * radius
        z = cz + math.sin(rad) * radius
        builder.box_rot((cx, y, z), (width, 0.30, thickness),
                        (angle - 90.0, 0.0, 0.0), mat, bevel=0.01)


def build_body(materials):
    """Корпус без колёс: рама, двигатель, бак, сиденье, вилка, руль, свет."""
    b = level_lib.LevelBuilder(materials)

    # -- двигатель ------------------------------------------------------
    b.box((0.0, 0.04, 0.55), (0.44, 0.50, 0.36), "Level_Metal", bevel=0.03)
    b.box_rot((0.0, -0.10, 0.78), (0.38, 0.36, 0.16), (-8.0, 0.0, 0.0),
              "Level_Rust", bevel=0.02)
    # Рёбра охлаждения шире головки: тёмные полосы читаются как радиатор.
    for z in (0.72, 0.78, 0.84):
        b.box((0.0, -0.10, z), (0.42, 0.30, 0.018), "Level_Stone", bevel=0.005)
    b.box((0.0, 0.16, 0.36), (0.36, 0.34, 0.10), "Level_Stone", bevel=0.02)

    # -- рама и маятник --------------------------------------------------
    for side in (-1.0, 1.0):
        b.tube([(side * 0.155, -0.58, 0.90), (side * 0.155, -0.28, 0.84),
                (side * 0.155, 0.14, 0.78), (side * 0.155, 0.52, 0.72)],
               [(0.034, 0.034)] * 4, "Level_Metal", n=6)
        b.tube([(side * 0.16, 0.28, 0.50), (side * 0.16, 0.74, 0.42)],
               [(0.04, 0.04)] * 2, "Level_Metal", n=6)
        # Амортизаторы: от подседельной части рамы к оси маятника.
        b.tube([(side * 0.19, 0.42, 0.76), (side * 0.19, 0.68, 0.44)],
               [(0.032, 0.032)] * 2, "Level_Rust", n=6)

    b.tube([(0.0, -0.60, 0.88), (0.0, -0.48, 0.60), (0.0, -0.32, 0.44)],
           [(0.04, 0.04)] * 3, "Level_Rust", n=6)

    # -- бак, сиденье, корма --------------------------------------------
    b.box_rot((0.0, -0.28, 0.94), (0.40, 0.62, 0.24), (6.0, 0.0, 0.0),
              "Level_Metal", bevel=0.055)
    b.box((0.0, -0.26, 1.065), (0.28, 0.44, 0.02), "Level_Orange",
          bevel=0.006)
    b.box((0.0, 0.28, 0.98), (0.34, 0.70, 0.11), "Level_WoodDark",
          bevel=0.03)
    b.box((0.0, 0.70, 1.01), (0.28, 0.26, 0.07), "Level_Metal", bevel=0.02)
    b.box((0.0, 0.84, 1.01), (0.15, 0.04, 0.07), "Level_Window", bevel=0.008)
    _fender(b, REAR_WHEEL, 0.50, (18.0, 48.0, 78.0, 108.0), "Level_Rust",
            width=0.24, thickness=0.05)

    # -- вилка и руль ----------------------------------------------------
    for side in (-1.0, 1.0):
        b.tube([(side * 0.145, -0.64, 0.99), (side * 0.145, -0.72, 0.72),
                (side * 0.145, -0.78, WHEEL_RADIUS)],
               [(0.032, 0.032)] * 3, "Level_Metal", n=6)
    b.box((0.0, -0.63, 1.01), (0.32, 0.09, 0.09), "Level_Rust", bevel=0.02)
    b.tube([(-0.36, -0.62, 1.09), (-0.12, -0.60, 1.07),
            (0.12, -0.60, 1.07), (0.36, -0.62, 1.09)],
           [(0.024, 0.024)] * 4, "Level_Metal", n=6)
    for side in (-1.0, 1.0):
        b.box((side * 0.30, -0.61, 1.08), (0.10, 0.06, 0.06),
              "Level_WoodDark", bevel=0.012)

    # -- фара и крыло ----------------------------------------------------
    b.tube([(0.0, -0.70, 0.99), (0.0, -0.82, 0.99)],
           [(0.15, 0.15), (0.13, 0.13)], "Level_Metal", n=10)
    b.tube([(0.0, -0.83, 0.99), (0.0, -0.855, 0.99)],
           [(0.115, 0.115)] * 2, "Level_Window", n=10, caps=(False, True))
    _fender(b, FRONT_WHEEL, 0.50, (42.0, 66.0, 90.0, 114.0, 138.0),
            "Level_Rust", width=0.22)

    # -- выхлоп, цепь, подножка ------------------------------------------
    b.tube([(0.22, 0.08, 0.44), (0.25, 0.54, 0.50), (0.27, 0.90, 0.54)],
           [(0.05, 0.05)] * 3, "Level_Rust", n=8)
    b.tube([(0.27, 0.90, 0.54), (0.285, 1.04, 0.56)],
           [(0.065, 0.065), (0.07, 0.07)], "Level_Metal", n=8)
    b.box((0.19, 0.52, 0.42), (0.05, 0.52, 0.09), "Level_Metal", bevel=0.012)
    b.tube([(-0.20, 0.34, 0.42), (-0.34, 0.42, 0.02)],
           [(0.028, 0.028)] * 2, "Level_Metal", n=6)
    b.box((-0.35, 0.43, 0.02), (0.10, 0.16, 0.03), "Level_Stone", bevel=0.008)

    # -- снег -------------------------------------------------------------
    b.box((0.0, -0.24, 1.09), (0.26, 0.36, 0.03), "Level_Snow", bevel=0.01)
    b.box((0.0, 0.55, 1.05), (0.28, 0.30, 0.03), "Level_Snow", bevel=0.01)
    b.box((0.0, 0.60, 1.115), (0.20, 0.20, 0.02), "Level_Snow", bevel=0.008)

    return b.build("MotorcycleBody")


def build_motorcycle(materials):
    """Три меша байка: корпус и два колеса с pivot в осях."""
    body = build_body(materials)
    front = _wheel(materials, FRONT_WHEEL, "MotorcycleWheelF")
    rear = _wheel(materials, REAR_WHEEL, "MotorcycleWheelR")
    return [body, front, rear]
