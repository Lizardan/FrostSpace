"""Пять дизайнов северных роботов на общем скелете ActionForge.

Каждый робот — отдельный меш, жёстко привязанный к костям (робот не гнётся,
он вращается в суставах). Это даёт чистый faceted-силуэт и исключает
артефакты скиннинга.

Общие правила:
- вперёд −Y, вверх +Z, вбок +X;
- руки в T-позе вдоль ±X, как в скелете ActionForge;
- каждая деталь весит ровно в одну кость;
- снег и мех — отдельные светлые детали поверх тёмного корпуса.
"""

from __future__ import annotations

import math

from robot_lib import PartMesh, make_materials, rig_points


def _bolts(p: PartMesh, points, mat, bone, size=0.018):
    for point in points:
        p.box(point, (size, size, size), mat, bone, bevel=0.0)


def _snow_cap(p: PartMesh, center, size, mat, bone):
    """Снеговые шапки на роботах убраны: корпус должен читаться как техника."""
    return


def _fur_mass(p: PartMesh, center, sx, mat, bone, scale=1.0):
    """Пушистый наплечник: несколько гранёных комков вместо одной плиты."""
    cx, cy, cz = center
    p.box((cx, cy, cz), (0.27 * scale, 0.31 * scale, 0.15 * scale),
          mat, bone, bevel=0.055, rot=(0, sx * 8, sx * 5))
    for i, (dx, dy, dz, radius) in enumerate((
        (0.00, -0.10, 0.06, 0.072),
        (0.05, 0.03, 0.08, 0.078),
        (-0.04, 0.10, 0.04, 0.062),
        (0.09, -0.02, 0.02, 0.058),
    )):
        p.sphere((cx + sx * dx, cy + dy, cz + dz), radius * scale, mat, bone,
                 subdivisions=1)


def build_robot_01(arm):
    """Полярный механик: меховой воротник, оранжевый визор и перчатки."""
    name = "R1_PolarMechanic"
    keys = ["base", "panel", "dark", "snow", "fur", "orange", "glow", "metal"]
    mats, M = make_materials(name, keys, colors={
        "base": "#2A3B52", "panel": "#435A75", "dark": "#101722",
        "snow": "#DDE5EE", "fur": "#F2F5F8", "orange": "#F08A2E",
        "glow": "#FF8A2A", "metal": "#94A1B2",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, fur, orange = M["snow"], M["fur"], M["orange"]
    glow, metal = M["glow"], M["metal"]

    shoulder_l, elbow_l, wrist_l = R["upperarm_l"][0], R["lowerarm_l"][0], R["hand_l"][0]
    hand_l = R["hand_l"][1]
    hip_l, knee_l, ankle_l = R["thigh_l"][0], R["calf_l"][0], R["foot_l"][0]
    ball_l = R["ball_l"][0]

    # --- таз и пояс
    p.box((0, 0.015, 0.955), (0.40, 0.30, 0.23), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.43, 0.33, 0.08), dark, "pelvis", bevel=0.02)
    p.box((0, -0.175, 1.03), (0.11, 0.05, 0.07), orange, "pelvis", bevel=0.01)
    p.box((0, 0.175, 1.035), (0.26, 0.055, 0.085), panel, "pelvis", bevel=0.015)

    # --- торс: шире к груди, уже к шее
    p.tube(
        [(0, 0.02, 1.00), (0, 0.0, 1.16), (0, 0.01, 1.34), (0, 0.02, 1.47)],
        [(0.215, 0.165), (0.255, 0.205), (0.245, 0.195), (0.175, 0.145)],
        base, "spine_02", n=8,
    )
    p.box((0, -0.195, 1.25), (0.36, 0.07, 0.30), panel, "spine_02", bevel=0.03)
    p.box((0, -0.235, 1.27), (0.09, 0.04, 0.22), glow, "spine_02", bevel=0.012)
    p.box((0, -0.23, 1.39), (0.24, 0.04, 0.06), orange, "spine_02", bevel=0.012)
    p.box((0, -0.20, 1.10), (0.26, 0.055, 0.10), base, "spine_02", bevel=0.02)
    # ремни
    for sx in (-1, 1):
        p.beam((sx * 0.16, -0.19, 1.43), (sx * 0.10, -0.18, 1.05),
               0.052, 0.024, 0.046, 0.024, dark, "spine_02")
        p.box((sx * 0.19, -0.215, 1.19), (0.07, 0.05, 0.10), orange,
              "spine_02", bevel=0.012)
    # вентиляция
    for i in range(3):
        p.box((0.115, -0.235, 1.20 + i * 0.05), (0.09, 0.02, 0.018),
              dark, "spine_02", bevel=0.0)
    _bolts(p, [(-0.14, -0.235, 1.11), (0.14, -0.235, 1.11),
               (-0.14, -0.235, 1.38), (0.14, -0.235, 1.38)], metal, "spine_02")

    # --- меховой воротник
    p.tube([(0, 0.005, 1.43), (0, 0.005, 1.56)],
           [(0.235, 0.205), (0.275, 0.235)], fur, "spine_03", n=10)
    for i in range(10):
        a = math.tau * i / 10.0
        p.sphere((math.cos(a) * 0.235, math.sin(a) * 0.20 + 0.01, 1.535),
                 0.068, fur, "spine_03", subdivisions=1)

    # --- плечи
    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.30, 0.0, 1.40), (0.24, 0.29, 0.075), orange, clav,
              bevel=0.02, rot=(0, sx * 7, 0))
        _fur_mass(p, (sx * 0.335, 0.0, 1.475), sx, fur, clav, scale=1.15)
        _snow_cap(p, (sx * 0.34, -0.015, 1.575), (0.26, 0.28, 0.035), snow, clav)

    # --- руки
    for sx, side in ((1, "l"), (-1, "r")):
        upper, lower = "upperarm_" + side, "lowerarm_" + side
        hand = "hand_" + side
        p.sphere((sx * shoulder_l.x, shoulder_l.y, shoulder_l.z), 0.105,
                 panel, upper, subdivisions=1)
        p.beam((sx * shoulder_l.x, shoulder_l.y, shoulder_l.z),
               (sx * elbow_l.x, elbow_l.y, elbow_l.z),
               0.17, 0.175, 0.135, 0.14, base, upper)
        p.box((sx * (shoulder_l.x + 0.085), shoulder_l.y, shoulder_l.z),
              (0.055, 0.185, 0.185), orange, upper, bevel=0.015)
        p.sphere((sx * elbow_l.x, elbow_l.y, elbow_l.z), 0.098,
                 panel, lower, subdivisions=1)
        p.beam((sx * elbow_l.x, elbow_l.y, elbow_l.z),
               (sx * wrist_l.x, wrist_l.y, wrist_l.z),
               0.185, 0.19, 0.14, 0.145, panel, lower)
        p.box((sx * wrist_l.x, wrist_l.y, wrist_l.z),
              (0.05, 0.16, 0.16), dark, lower, bevel=0.014)
        p.box((sx * (wrist_l.x + 0.055), wrist_l.y, wrist_l.z),
              (0.15, 0.19, 0.20), orange, hand, bevel=0.04)
        p.box((sx * (wrist_l.x + 0.045), wrist_l.y - 0.095, wrist_l.z - 0.045),
              (0.085, 0.07, 0.08), orange, hand, bevel=0.022)
        _snow_cap(p, (sx * (shoulder_l.x + 0.12), shoulder_l.y, shoulder_l.z + 0.075),
                  (0.12, 0.17, 0.025), snow, upper)

    # --- ноги
    for sx, side in ((1, "l"), (-1, "r")):
        thigh, calf = "thigh_" + side, "calf_" + side
        foot, ball = "foot_" + side, "ball_" + side
        p.sphere((sx * hip_l.x, hip_l.y, hip_l.z), 0.11, dark, thigh,
                 subdivisions=1)
        p.beam((sx * hip_l.x, hip_l.y, hip_l.z),
               (sx * knee_l.x, knee_l.y, knee_l.z),
               0.21, 0.22, 0.18, 0.19, base, thigh)
        p.box((sx * hip_l.x, hip_l.y - 0.115, (hip_l.z + knee_l.z) * 0.5),
              (0.17, 0.06, 0.22), panel, thigh, bevel=0.02)
        p.sphere((sx * knee_l.x, knee_l.y, knee_l.z), 0.10, panel, calf,
                 subdivisions=1)
        p.box((sx * knee_l.x, knee_l.y - 0.115, knee_l.z),
              (0.19, 0.085, 0.17), snow, calf, bevel=0.03, rot=(-8, 0, 0))
        p.beam((sx * knee_l.x, knee_l.y, knee_l.z),
               (sx * ankle_l.x, ankle_l.y, ankle_l.z),
               0.20, 0.21, 0.155, 0.165, panel, calf)
        p.box((sx * knee_l.x, knee_l.y - 0.105, knee_l.z - 0.17),
              (0.155, 0.06, 0.20), base, calf, bevel=0.018)
        _bolts(p, [(sx * (knee_l.x + 0.08), knee_l.y - 0.12, knee_l.z - 0.17),
                   (sx * (knee_l.x + 0.08), knee_l.y - 0.12, knee_l.z - 0.02)],
               metal, calf, 0.016)
        p.sphere((sx * ankle_l.x, ankle_l.y, ankle_l.z), 0.085, dark, foot,
                 subdivisions=1)
        p.box((sx * ankle_l.x, ankle_l.y - 0.015, 0.085),
              (0.21, 0.28, 0.15), dark, foot, bevel=0.03)
        p.box((sx * ankle_l.x, ankle_l.y - 0.06, 0.028),
              (0.22, 0.36, 0.045), dark, foot, bevel=0.012)
        p.box((sx * ball_l.x, ball_l.y, 0.07), (0.20, 0.24, 0.14), dark, ball,
              bevel=0.03)
        _snow_cap(p, (sx * ball_l.x, ball_l.y, 0.142), (0.17, 0.21, 0.025),
                  snow, ball)

    # --- рюкзак
    p.box((0, 0.205, 1.25), (0.34, 0.21, 0.33), panel, "spine_02", bevel=0.035)
    p.box((0, 0.315, 1.26), (0.24, 0.045, 0.24), dark, "spine_02", bevel=0.014)
    p.tube([(0.14, 0.29, 1.12), (0.14, 0.29, 1.42)],
           [(0.06, 0.06), (0.05, 0.05)], metal, "spine_02", n=8)
    p.box((0.14, 0.29, 1.43), (0.075, 0.075, 0.045), orange, "spine_02",
          bevel=0.012)
    p.tube([(0.12, 0.21, 1.41), (0.07, 0.13, 1.52), (0.0, 0.07, 1.55)],
           [(0.03, 0.03), (0.03, 0.03), (0.026, 0.026)], metal, "spine_02",
           n=6)
    _snow_cap(p, (0, 0.205, 1.425), (0.30, 0.19, 0.04), snow, "spine_02")

    # --- голова
    p.tube([(0, 0.02, 1.47), (0, 0.02, 1.58)],
           [(0.08, 0.08), (0.07, 0.07)], metal, "neck_01", n=8)
    p.tube(
        [(0, 0.0, 1.55), (0, -0.005, 1.68), (0, 0.0, 1.81), (0, 0.015, 1.885)],
        [(0.185, 0.18), (0.19, 0.19), (0.16, 0.17), (0.10, 0.125)],
        base, "Head", n=8,
    )
    p.box((0, -0.17, 1.71), (0.27, 0.09, 0.23), dark, "Head", bevel=0.035)
    p.box((0, -0.22, 1.72), (0.24, 0.025, 0.065), glow, "Head", bevel=0.008)
    p.box((0, -0.19, 1.805), (0.255, 0.075, 0.07), panel, "Head", bevel=0.02)
    p.box((0, -0.20, 1.62), (0.19, 0.065, 0.06), panel, "Head", bevel=0.016)
    for sx in (-1, 1):
        p.disc((sx * 0.185, 0.0, 1.70), 0.06, 0.05, metal, "Head",
               axis="X", n=8)
        p.box((sx * 0.165, -0.165, 1.855), (0.05, 0.065, 0.04), orange, "Head",
              bevel=0.008)
    p.tube([(0.12, 0.02, 1.865), (0.165, 0.02, 2.005)],
           [(0.017, 0.017), (0.012, 0.012)], metal, "Head", n=6)
    p.sphere((0.168, 0.02, 2.015), 0.03, glow, "Head", subdivisions=1)
    _snow_cap(p, (0, 0.01, 1.902), (0.20, 0.22, 0.03), snow, "Head")

    obj = p.build(name, mats)
    return name, obj, mats


def _legs(p, R, sx, side, mats, thigh=(0.21, 0.22, 0.18, 0.19),
          calf=(0.20, 0.21, 0.155, 0.165), boot=(0.21, 0.28, 0.15),
          sole=(0.22, 0.36, 0.045), toe=(0.20, 0.24, 0.14),
          knee_pad=(0.19, 0.085, 0.17), thigh_plate=True, shin_plate=True):
    """Стандартная нога; размеры и цвета переопределяются вариантом."""
    hip_l, knee_l, ankle_l = R["thigh_l"][0], R["calf_l"][0], R["foot_l"][0]
    ball_l = R["ball_l"][0]
    thigh_b, calf_b = "thigh_" + side, "calf_" + side
    foot_b, ball_b = "foot_" + side, "ball_" + side
    base, panel = mats["base"], mats["panel"]
    dark, snow, metal = mats["dark"], mats["snow"], mats["metal"]

    p.sphere((sx * hip_l.x, hip_l.y, hip_l.z), 0.11, dark, thigh_b, subdivisions=1)
    p.beam((sx * hip_l.x, hip_l.y, hip_l.z),
           (sx * knee_l.x, knee_l.y, knee_l.z),
           thigh[0], thigh[1], thigh[2], thigh[3], base, thigh_b)
    if thigh_plate:
        p.box((sx * hip_l.x, hip_l.y - 0.115, (hip_l.z + knee_l.z) * 0.5),
              (0.17, 0.06, 0.22), panel, thigh_b, bevel=0.02)
    p.sphere((sx * knee_l.x, knee_l.y, knee_l.z), 0.10, panel, calf_b, subdivisions=1)
    if knee_pad:
        p.box((sx * knee_l.x, knee_l.y - 0.115, knee_l.z), knee_pad, snow, calf_b,
              bevel=0.03, rot=(-8, 0, 0))
    p.beam((sx * knee_l.x, knee_l.y, knee_l.z),
           (sx * ankle_l.x, ankle_l.y, ankle_l.z),
           calf[0], calf[1], calf[2], calf[3], panel, calf_b)
    if shin_plate:
        p.box((sx * knee_l.x, knee_l.y - 0.105, knee_l.z - 0.17),
              (0.155, 0.06, 0.20), base, calf_b, bevel=0.018)
    p.sphere((sx * ankle_l.x, ankle_l.y, ankle_l.z), 0.085, dark, foot_b, subdivisions=1)
    p.box((sx * ankle_l.x, ankle_l.y - 0.015, 0.085),
          (boot[0], boot[1], boot[2]), dark, foot_b, bevel=0.03)
    p.box((sx * ankle_l.x, ankle_l.y - 0.06, 0.028),
          (sole[0], sole[1], sole[2]), dark, foot_b, bevel=0.012)
    p.box((sx * ball_l.x, ball_l.y, 0.07), toe, dark, ball_b, bevel=0.03)
    _snow_cap(p, (sx * ball_l.x, ball_l.y, 0.07 + toe[2] * 0.5 + 0.012),
              (toe[0] * 0.85, toe[1] * 0.85, 0.025), snow, ball_b)
    _bolts(p, [(sx * (knee_l.x + 0.08), knee_l.y - 0.12, knee_l.z - 0.17)],
           metal, calf_b, 0.016)


def _arms(p, R, sx, side, mats, upper=(0.17, 0.175, 0.135, 0.14),
          lower=(0.185, 0.19, 0.14, 0.145), hand=(0.15, 0.19, 0.20),
          glove="orange", band=True, snow_cap=True):
    """Стандартная рука в T-позе; кисть и наплечник добавляет вариант."""
    shoulder, elbow, wrist = R["upperarm_l"][0], R["lowerarm_l"][0], R["hand_l"][0]
    upper_b, lower_b, hand_b = ("upperarm_" + side, "lowerarm_" + side,
                                "hand_" + side)
    base, panel, dark = mats["base"], mats["panel"], mats["dark"]
    m_glove, snow = mats[glove], mats["snow"]

    p.sphere((sx * shoulder.x, shoulder.y, shoulder.z), 0.105, panel, upper_b,
             subdivisions=1)
    p.beam((sx * shoulder.x, shoulder.y, shoulder.z),
           (sx * elbow.x, elbow.y, elbow.z),
           upper[0], upper[1], upper[2], upper[3], base, upper_b)
    if band:
        p.box((sx * (shoulder.x + 0.085), shoulder.y, shoulder.z),
              (0.055, upper[1] * 1.05, upper[1] * 1.05), m_glove, upper_b,
              bevel=0.015)
    p.sphere((sx * elbow.x, elbow.y, elbow.z), 0.098, panel, lower_b, subdivisions=1)
    p.beam((sx * elbow.x, elbow.y, elbow.z),
           (sx * wrist.x, wrist.y, wrist.z),
           lower[0], lower[1], lower[2], lower[3], panel, lower_b)
    p.box((sx * wrist.x, wrist.y, wrist.z), (0.05, 0.16, 0.16), dark, lower_b,
          bevel=0.014)
    if hand is not None:
        p.box((sx * (wrist.x + 0.055), wrist.y, wrist.z), hand, m_glove, hand_b,
              bevel=0.04)
        p.box((sx * (wrist.x + 0.045), wrist.y - hand[1] * 0.5, wrist.z - hand[2] * 0.22),
              (0.085, 0.07, 0.08), m_glove, hand_b, bevel=0.022)
    if snow_cap:
        _snow_cap(p, (sx * (shoulder.x + 0.12), shoulder.y, shoulder.z + 0.075),
                  (0.12, 0.17, 0.025), snow, upper_b)


def build_robot_02(arm):
    """Топка: грузный корпус, топка в груди, труба и один глаз."""
    name = "R2_Furnace"
    keys = ["base", "panel", "dark", "snow", "orange", "glow", "metal", "rust", "yellow"]
    mats, M = make_materials(name, keys, colors={
        "base": "#3A2B24", "panel": "#6A4734", "dark": "#16100D",
        "snow": "#D8C3A8", "orange": "#E07A2C", "glow": "#FFAE4D",
        "metal": "#A98363", "rust": "#8C4A32", "yellow": "#E8B84B",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, orange, glow = M["snow"], M["orange"], M["glow"]
    metal, rust, yellow = M["metal"], M["rust"], M["yellow"]

    p.box((0, 0.02, 0.955), (0.44, 0.32, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.035), (0.47, 0.35, 0.08), panel, "pelvis", bevel=0.02)
    p.box((0, -0.19, 1.035), (0.12, 0.05, 0.075), orange, "pelvis", bevel=0.01)
    p.tube([(0, 0.03, 1.00), (0, 0.0, 1.16), (0, -0.01, 1.34), (0, 0.00, 1.48)],
           [(0.24, 0.19), (0.30, 0.235), (0.29, 0.225), (0.20, 0.165)],
           base, "spine_02", n=8)
    # топка
    p.box((0, -0.235, 1.24), (0.36, 0.09, 0.27), dark, "spine_02", bevel=0.025)
    p.box((0, -0.285, 1.24), (0.30, 0.035, 0.21), glow, "spine_02", bevel=0.01)
    for i in range(3):
        p.box((0, -0.306, 1.16 + i * 0.075), (0.28, 0.02, 0.026), dark, "spine_02", bevel=0.0)
    for sx in (-1, 1):
        p.box((sx * 0.16, -0.28, 1.24), (0.035, 0.04, 0.24), metal, "spine_02", bevel=0.008)
    p.box((0, -0.29, 1.40), (0.34, 0.045, 0.06), rust, "spine_02", bevel=0.01)
    # труба
    p.tube([(0.19, 0.20, 1.38), (0.21, 0.20, 1.72), (0.25, 0.18, 2.02)],
           [(0.065, 0.065), (0.055, 0.055), (0.075, 0.075)], metal, "spine_03", n=8)
    p.box((0.25, 0.18, 2.04), (0.14, 0.14, 0.05), dark, "spine_03", bevel=0.012)
    _snow_cap(p, (0.25, 0.18, 2.075), (0.13, 0.13, 0.03), snow, "spine_03")
    # воротник-металл
    p.tube([(0, 0.01, 1.44), (0, 0.01, 1.53)], [(0.22, 0.19), (0.25, 0.22)],
           rust, "spine_03", n=8)
    # наплечники: слева большой, справа малый
    p.box((0.36, 0.0, 1.47), (0.34, 0.38, 0.18), metal, "clavicle_l", bevel=0.05, rot=(0, 10, 6))
    p.box((0.36, -0.02, 1.51), (0.30, 0.30, 0.055), orange, "clavicle_l", bevel=0.015, rot=(0, 10, 6))
    p.box((-0.32, 0.0, 1.45), (0.24, 0.28, 0.14), rust, "clavicle_r", bevel=0.04, rot=(0, -8, -4))
    _snow_cap(p, (-0.32, -0.01, 1.525), (0.22, 0.24, 0.03), snow, "clavicle_r")
    _snow_cap(p, (0.36, -0.02, 1.555), (0.26, 0.28, 0.03), snow, "clavicle_l")
    _arms(p, R, 1, "l", M, upper=(0.20, 0.21, 0.15, 0.155), lower=(0.21, 0.22, 0.16, 0.165), hand=(0.20, 0.24, 0.24), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.20, 0.21, 0.15, 0.155), lower=(0.21, 0.22, 0.16, 0.165), hand=(0.20, 0.24, 0.24), glove="dark")
    # поршень на левом предплечье
    shoulder, elbow, wrist = R["upperarm_l"][0], R["lowerarm_l"][0], R["hand_l"][0]
    p.tube([(elbow.x + 0.08, elbow.y - 0.06, elbow.z + 0.08), (wrist.x - 0.06, wrist.y - 0.06, wrist.z + 0.08)],
           [(0.035, 0.035), (0.03, 0.03)], metal, "lowerarm_l", n=6)
    p.tube([(elbow.x + 0.08, elbow.y + 0.06, elbow.z - 0.06), (wrist.x - 0.06, wrist.y + 0.06, wrist.z - 0.06)],
           [(0.035, 0.035), (0.03, 0.03)], rust, "lowerarm_l", n=6)
    # ноги
    _legs(p, R, 1, "l", M, thigh=(0.24, 0.25, 0.20, 0.21), calf=(0.23, 0.24, 0.18, 0.19),
          boot=(0.24, 0.30, 0.16), sole=(0.25, 0.40, 0.05), toe=(0.23, 0.28, 0.15),
          knee_pad=(0.21, 0.09, 0.18))
    _legs(p, R, -1, "r", M, thigh=(0.24, 0.25, 0.20, 0.21), calf=(0.23, 0.24, 0.18, 0.19),
          boot=(0.24, 0.30, 0.16), sole=(0.25, 0.40, 0.05), toe=(0.23, 0.28, 0.15),
          knee_pad=(0.21, 0.09, 0.18))
    # предупреждающие полосы
    for i in range(3):
        p.box((0.20, -0.10, 0.34 - i * 0.06), (0.10, 0.03, 0.03), yellow, "calf_l", bevel=0.0)
        p.box((-0.20, -0.10, 0.34 - i * 0.06), (0.10, 0.03, 0.03), yellow, "calf_r", bevel=0.0)
    # котёл за спиной
    p.tube([(0, 0.30, 1.10), (0, 0.30, 1.52)], [(0.17, 0.17), (0.17, 0.17)], panel, "spine_02", n=10)
    p.sphere((0, 0.30, 1.53), 0.17, panel, "spine_02", subdivisions=1)
    p.tube([(0.12, 0.30, 1.36), (0.30, 0.24, 1.46), (0.36, 0.10, 1.48)],
           [(0.035, 0.035), (0.035, 0.035), (0.03, 0.03)], metal, "spine_02", n=6)
    p.disc((0, 0.47, 1.36), 0.055, 0.03, glow, "spine_02", axis="Y", n=8)
    _snow_cap(p, (0, 0.30, 1.61), (0.26, 0.26, 0.035), snow, "spine_02")
    # голова
    p.tube([(0, 0.02, 1.47), (0, 0.02, 1.57)], [(0.09, 0.09), (0.08, 0.08)], metal, "neck_01", n=8)
    p.box((0, -0.05, 1.68), (0.26, 0.24, 0.30), base, "Head", bevel=0.04)
    p.box((0, -0.18, 1.68), (0.25, 0.06, 0.28), dark, "Head", bevel=0.025)
    p.disc((0, -0.215, 1.70), 0.070, 0.035, glow, "Head", axis="Y", n=10)
    p.disc((0, -0.23, 1.70), 0.095, 0.02, metal, "Head", axis="Y", n=10)
    p.box((0, -0.16, 1.82), (0.27, 0.10, 0.055), rust, "Head", bevel=0.015)
    p.box((0, -0.16, 1.57), (0.20, 0.08, 0.06), metal, "Head", bevel=0.012)
    for sx in (-1, 1):
        p.box((sx * 0.16, -0.05, 1.68), (0.05, 0.10, 0.16), orange, "Head", bevel=0.01)
    return name, p.build(name, mats), mats


def build_robot_03(arm):
    """Вьюга: разведчик в капюшоне, длинный шарф и снегоступы."""
    name = "R3_Scout"
    keys = ["base", "panel", "dark", "snow", "fur", "orange", "cyan", "metal"]
    mats, M = make_materials(name, keys, colors={
        "base": "#1D3944", "panel": "#2D5A68", "dark": "#0C1C22",
        "snow": "#CFE7EC", "fur": "#EDF7FA", "orange": "#F2A33C",
        "cyan": "#7FE3EF", "metal": "#93A9B4",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, fur, orange = M["snow"], M["fur"], M["orange"]
    cyan, metal = M["cyan"], M["metal"]

    p.box((0, 0.01, 0.955), (0.34, 0.27, 0.21), dark, "pelvis", bevel=0.035)
    p.box((0, 0.0, 1.025), (0.37, 0.29, 0.07), dark, "pelvis", bevel=0.02)
    p.box((0, -0.155, 1.025), (0.09, 0.04, 0.06), cyan, "pelvis", bevel=0.01)
    p.tube([(0, 0.02, 1.00), (0, 0.0, 1.16), (0, 0.01, 1.34), (0, 0.02, 1.47)],
           [(0.19, 0.15), (0.21, 0.17), (0.20, 0.165), (0.15, 0.13)],
           base, "spine_02", n=8)
    p.box((0, -0.175, 1.26), (0.29, 0.06, 0.25), panel, "spine_02", bevel=0.025)
    p.box((0, -0.21, 1.28), (0.06, 0.03, 0.16), cyan, "spine_02", bevel=0.01)
    p.box((0, -0.20, 1.13), (0.22, 0.05, 0.07), orange, "spine_02", bevel=0.012)
    # шарф: кольцо и хвост
    p.tube([(0, 0.01, 1.43), (0, 0.01, 1.50)], [(0.20, 0.18), (0.23, 0.21)],
           orange, "spine_03", n=10)
    p.tube([(0.05, 0.18, 1.45), (0.10, 0.30, 1.25), (0.06, 0.36, 1.05)],
           [(0.055, 0.04), (0.05, 0.035), (0.035, 0.025)], orange, "spine_03", n=6)
    # капюшон
    p.tube([(0, 0.0, 1.45), (0, -0.01, 1.56)], [(0.235, 0.21), (0.25, 0.23)],
           fur, "spine_03", n=10)
    for i in range(8):
        a = math.tau * i / 8.0
        p.sphere((math.cos(a) * 0.22, math.sin(a) * 0.19 + 0.01, 1.53), 0.06, fur, "spine_03", subdivisions=1)
    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.27, 0.0, 1.44), (0.22, 0.28, 0.10), fur, clav, bevel=0.03, rot=(0, sx * 7, 0))
        _snow_cap(p, (sx * 0.28, -0.01, 1.50), (0.19, 0.22, 0.03), snow, clav)
    _arms(p, R, 1, "l", M, upper=(0.14, 0.145, 0.11, 0.115), lower=(0.145, 0.15, 0.115, 0.12), hand=(0.13, 0.17, 0.18), glove="orange")
    _arms(p, R, -1, "r", M, upper=(0.14, 0.145, 0.11, 0.115), lower=(0.145, 0.15, 0.115, 0.12), hand=(0.13, 0.17, 0.18), glove="orange")
    # снегоступы: тёмная рама с оранжевыми креплениями, без снега
    for sx, side in ((1, "l"), (-1, "r")):
        foot, ball = R["foot_l"][0], R["ball_l"][0]
        p.box((sx * foot.x, foot.y - 0.08, 0.035), (0.24, 0.42, 0.05), panel, "foot_" + side, bevel=0.015)
        p.box((sx * foot.x, foot.y - 0.08, 0.062), (0.20, 0.30, 0.02), orange, "foot_" + side, bevel=0.005)
        p.box((sx * ball.x, ball.y, 0.075), (0.19, 0.25, 0.12), dark, "ball_" + side, bevel=0.025)
        for i in range(3):
            p.box((sx * (ball.x - 0.08 + i * 0.08), ball.y - 0.10, 0.015), (0.025, 0.06, 0.03), metal, "foot_" + side, bevel=0.0)
    _legs(p, R, 1, "l", M, thigh=(0.16, 0.17, 0.135, 0.145), calf=(0.16, 0.17, 0.12, 0.13),
          boot=(0.18, 0.24, 0.12), sole=(0.19, 0.30, 0.04), toe=(0.17, 0.21, 0.12),
          knee_pad=(0.15, 0.07, 0.14), shin_plate=False)
    _legs(p, R, -1, "r", M, thigh=(0.16, 0.17, 0.135, 0.145), calf=(0.16, 0.17, 0.12, 0.13),
          boot=(0.18, 0.24, 0.12), sole=(0.19, 0.30, 0.04), toe=(0.17, 0.21, 0.12),
          knee_pad=(0.15, 0.07, 0.14), shin_plate=False)
    # радио
    p.box((0, 0.19, 1.26), (0.22, 0.16, 0.26), panel, "spine_02", bevel=0.025)
    p.tube([(0.08, 0.24, 1.38), (0.12, 0.24, 1.72)], [(0.018, 0.018), (0.012, 0.012)], metal, "spine_02", n=6)
    p.sphere((0.12, 0.24, 1.73), 0.025, cyan, "spine_02", subdivisions=1)
    _snow_cap(p, (0, 0.19, 1.395), (0.20, 0.15, 0.03), snow, "spine_02")
    # голова в капюшоне
    p.tube([(0, 0.02, 1.47), (0, 0.02, 1.57)], [(0.07, 0.07), (0.06, 0.06)], metal, "neck_01", n=8)
    p.tube([(0, -0.01, 1.55), (0, -0.02, 1.71), (0, 0.0, 1.88)],
           [(0.16, 0.15), (0.16, 0.15), (0.10, 0.13)], base, "Head", n=8)
    p.box((0, -0.15, 1.72), (0.23, 0.08, 0.22), dark, "Head", bevel=0.025)
    for sx in (-1, 1):
        p.disc((sx * 0.065, -0.195, 1.74), 0.045, 0.03, cyan, "Head", axis="Y", n=10)
        p.disc((sx * 0.065, -0.18, 1.74), 0.060, 0.02, metal, "Head", axis="Y", n=10)
        p.box((sx * 0.13, -0.10, 1.86), (0.035, 0.06, 0.07), orange, "Head", bevel=0.008, rot=(0, 0, sx * 18))
    p.box((0, -0.17, 1.61), (0.15, 0.05, 0.045), orange, "Head", bevel=0.01)
    p.tube([(0.09, 0.02, 1.88), (0.12, 0.02, 2.06)], [(0.012, 0.012), (0.008, 0.008)], metal, "Head", n=6)
    p.sphere((0.122, 0.02, 2.07), 0.024, cyan, "Head", subdivisions=1)
    return name, p.build(name, mats), mats


def build_robot_04(arm):
    """Кочегар: сгорбленный корпус, котёл выше плеч, обе кисти пустые."""
    name = "R4_Hauler"
    keys = ["base", "panel", "dark", "snow", "rust", "orange", "metal", "yellow"]
    mats, M = make_materials(name, keys, colors={
        "base": "#3B3A2B", "panel": "#6B6545", "dark": "#17160F",
        "snow": "#D9D2B8", "rust": "#8C5A34", "orange": "#E0A33A",
        "metal": "#9A9878", "yellow": "#E6B94A",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, rust, orange = M["snow"], M["rust"], M["orange"]
    metal, yellow = M["metal"], M["yellow"]

    p.box((0, 0.02, 0.95), (0.44, 0.32, 0.22), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.02), (0.47, 0.35, 0.075), panel, "pelvis", bevel=0.02)
    p.box((0, -0.18, 1.02), (0.12, 0.05, 0.07), yellow, "pelvis", bevel=0.01)
    p.tube([(0, 0.03, 0.99), (0, -0.015, 1.15), (0, -0.055, 1.33), (0, -0.04, 1.45)],
           [(0.24, 0.20), (0.30, 0.24), (0.28, 0.23), (0.20, 0.17)],
           base, "spine_02", n=8)
    p.box((0, -0.23, 1.20), (0.40, 0.07, 0.28), rust, "spine_02", bevel=0.03)
    for i in range(3):
        p.box((0, -0.27, 1.12 + i * 0.07), (0.30, 0.025, 0.025), dark, "spine_02", bevel=0.0)
    p.box((0, -0.245, 1.38), (0.22, 0.04, 0.055), yellow, "spine_02", bevel=0.01)
    # горб-котёл
    p.tube([(0, 0.28, 1.06), (0, 0.30, 1.58)], [(0.20, 0.18), (0.18, 0.16)], panel, "spine_02", n=10)
    p.sphere((0, 0.30, 1.60), 0.18, panel, "spine_02", subdivisions=1)
    p.tube([(0, 0.30, 1.62), (0, 0.30, 1.80)], [(0.07, 0.07), (0.055, 0.055)], metal, "spine_02", n=8)
    p.box((0, 0.30, 1.82), (0.12, 0.12, 0.045), dark, "spine_02", bevel=0.012)
    _snow_cap(p, (0, 0.30, 1.845), (0.11, 0.11, 0.03), snow, "spine_02")
    for sx in (-1, 1):
        p.tube([(sx * 0.12, 0.30, 1.30), (sx * 0.33, 0.16, 1.24), (sx * 0.42, 0.02, 1.20)],
               [(0.04, 0.04), (0.035, 0.035), (0.03, 0.03)], metal, "spine_02", n=6)
        p.box((sx * 0.37, 0.02, 1.47), (0.20, 0.26, 0.13), rust, "clavicle_" + ("l" if sx > 0 else "r"), bevel=0.035, rot=(0, sx * 8, 0))
        _snow_cap(p, (sx * 0.37, 0.01, 1.545), (0.18, 0.22, 0.03), snow, "clavicle_" + ("l" if sx > 0 else "r"))
    # Обе руки одинаковы: кисть правая (в проекте руки не держат предметов).
    _arms(p, R, 1, "l", M, upper=(0.22, 0.24, 0.18, 0.19), lower=(0.24, 0.25, 0.19, 0.20), hand=(0.22, 0.26, 0.26), glove="orange")
    _arms(p, R, -1, "r", M, upper=(0.22, 0.24, 0.18, 0.19), lower=(0.24, 0.25, 0.19, 0.20), hand=(0.22, 0.26, 0.26), glove="orange")
    _legs(p, R, 1, "l", M, thigh=(0.25, 0.26, 0.21, 0.22), calf=(0.24, 0.25, 0.20, 0.21),
          boot=(0.25, 0.32, 0.17), sole=(0.27, 0.42, 0.055), toe=(0.24, 0.30, 0.16),
          knee_pad=(0.22, 0.10, 0.19))
    _legs(p, R, -1, "r", M, thigh=(0.25, 0.26, 0.21, 0.22), calf=(0.24, 0.25, 0.20, 0.21),
          boot=(0.25, 0.32, 0.17), sole=(0.27, 0.42, 0.055), toe=(0.24, 0.30, 0.16),
          knee_pad=(0.22, 0.10, 0.19))
    for sx, side in ((1, "l"), (-1, "r")):
        for i in range(4):
            p.box((sx * 0.24, -0.14, 0.42 - i * 0.06), (0.08, 0.025, 0.03), yellow, "calf_" + side, bevel=0.0)
    # голова
    p.tube([(0, 0.0, 1.45), (0, -0.02, 1.55)], [(0.08, 0.08), (0.07, 0.07)], metal, "neck_01", n=8)
    p.box((0, -0.08, 1.64), (0.30, 0.26, 0.21), base, "Head", bevel=0.05)
    p.box((0, -0.20, 1.66), (0.30, 0.09, 0.13), rust, "Head", bevel=0.025)
    for sx in (-1, 1):
        p.disc((sx * 0.075, -0.245, 1.66), 0.042, 0.025, orange, "Head", axis="Y", n=8)
    p.box((0, -0.19, 1.76), (0.32, 0.08, 0.055), dark, "Head", bevel=0.012)
    _snow_cap(p, (0, -0.08, 1.75), (0.24, 0.22, 0.03), snow, "Head")
    return name, p.build(name, mats), mats


def build_robot_05(arm):
    """Смотритель: длинное пальто, меховая накидка и прожектор на груди."""
    name = "R5_Ranger"
    keys = ["base", "panel", "dark", "snow", "fur", "orange", "cyan", "metal"]
    mats, M = make_materials(name, keys, colors={
        "base": "#26382F", "panel": "#3E5849", "dark": "#0F1A14",
        "snow": "#E7E1D1", "fur": "#EDE6D6", "orange": "#D9A441",
        "cyan": "#CFEFE2", "metal": "#9AA89C",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, fur, orange = M["snow"], M["fur"], M["orange"]
    cyan, metal = M["cyan"], M["metal"]

    p.box((0, 0.01, 0.96), (0.36, 0.28, 0.22), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.39, 0.31, 0.07), panel, "pelvis", bevel=0.02)
    # пальто: спинка и полы
    p.box((0, 0.15, 0.82), (0.34, 0.06, 0.40), panel, "pelvis", bevel=0.02, rot=(-4, 0, 0))
    for sx in (-1, 1):
        p.box((sx * 0.185, 0.04, 0.83), (0.06, 0.24, 0.38), base, "pelvis", bevel=0.015, rot=(0, sx * 3, 0))
        p.box((sx * 0.12, -0.135, 0.84), (0.14, 0.05, 0.36), base, "pelvis", bevel=0.012)
        p.box((sx * 0.19, -0.06, 0.65), (0.055, 0.10, 0.30), panel, "pelvis", bevel=0.01)
    p.box((0, 0.0, 1.03), (0.08, 0.34, 0.06), orange, "pelvis", bevel=0.01)
    p.tube([(0, 0.02, 1.00), (0, 0.0, 1.16), (0, 0.005, 1.34), (0, 0.02, 1.49)],
           [(0.20, 0.16), (0.235, 0.19), (0.225, 0.185), (0.16, 0.14)],
           base, "spine_02", n=8)
    p.box((0, -0.19, 1.26), (0.32, 0.06, 0.31), panel, "spine_02", bevel=0.03)
    for i in range(3):
        p.box((0, -0.225, 1.36 - i * 0.085), (0.24, 0.025, 0.05), base, "spine_02", bevel=0.0)
    p.box((0, -0.225, 1.38), (0.07, 0.025, 0.07), orange, "spine_02", bevel=0.0)
    # меховая накидка
    p.tube([(0, 0.02, 1.43), (0, 0.0, 1.54)], [(0.24, 0.21), (0.28, 0.25)], fur, "spine_03", n=10)
    for sx in (-1, 1):
        for i in range(4):
            p.sphere((sx * (0.16 + i * 0.07), -0.02 + (i % 2) * 0.10, 1.47 - i * 0.015),
                     0.075, fur, "clavicle_" + ("l" if sx > 0 else "r"), subdivisions=1)
    _arms(p, R, 1, "l", M, upper=(0.15, 0.16, 0.12, 0.13), lower=(0.16, 0.165, 0.125, 0.135), hand=(0.14, 0.18, 0.19), glove="orange")
    _arms(p, R, -1, "r", M, upper=(0.15, 0.16, 0.12, 0.13), lower=(0.16, 0.165, 0.125, 0.135), hand=(0.14, 0.18, 0.19), glove="orange")
    _legs(p, R, 1, "l", M, thigh=(0.17, 0.18, 0.14, 0.15), calf=(0.17, 0.18, 0.13, 0.14),
          boot=(0.19, 0.26, 0.14), sole=(0.20, 0.34, 0.045), toe=(0.18, 0.23, 0.14),
          knee_pad=(0.16, 0.075, 0.15))
    _legs(p, R, -1, "r", M, thigh=(0.17, 0.18, 0.14, 0.15), calf=(0.17, 0.18, 0.13, 0.14),
          boot=(0.19, 0.26, 0.14), sole=(0.20, 0.34, 0.045), toe=(0.18, 0.23, 0.14),
          knee_pad=(0.16, 0.075, 0.15))
    # рюкзак и скатка
    p.box((0, 0.20, 1.24), (0.26, 0.14, 0.34), panel, "spine_02", bevel=0.03)
    p.tube([(-0.20, 0.28, 1.39), (0.20, 0.28, 1.39)], [(0.075, 0.075), (0.075, 0.075)], orange, "spine_02", n=8)
    _snow_cap(p, (0, 0.20, 1.42), (0.22, 0.13, 0.03), snow, "spine_02")
    # голова с прожектором
    p.tube([(0, 0.02, 1.48), (0, 0.02, 1.58)], [(0.075, 0.075), (0.065, 0.065)], metal, "neck_01", n=8)
    p.box((0, -0.03, 1.70), (0.24, 0.24, 0.30), base, "Head", bevel=0.04)
    p.box((0, -0.15, 1.71), (0.23, 0.08, 0.26), dark, "Head", bevel=0.03)
    p.disc((0, -0.20, 1.72), 0.075, 0.045, cyan, "Head", axis="Y", n=12)
    p.disc((0, -0.175, 1.72), 0.105, 0.02, metal, "Head", axis="Y", n=12)
    p.box((0, -0.16, 1.84), (0.25, 0.08, 0.05), orange, "Head", bevel=0.01)
    for sx in (-1, 1):
        p.box((sx * 0.13, -0.09, 1.64), (0.04, 0.08, 0.12), metal, "Head", bevel=0.008)
    # рога-антенны
    for sx in (-1, 1):
        p.tube([(sx * 0.07, 0.02, 1.84), (sx * 0.12, 0.02, 1.96), (sx * 0.20, 0.0, 2.06)],
               [(0.02, 0.02), (0.015, 0.015), (0.008, 0.008)], metal, "Head", n=6)
        p.tube([(sx * 0.12, 0.02, 1.96), (sx * 0.09, 0.02, 2.10)],
               [(0.014, 0.014), (0.006, 0.006)], metal, "Head", n=6)
    return name, p.build(name, mats), mats


# ------------------------------------------------------------------ помощники
# Вторая партия (R6–R20) собрана из тех же примитивов, что и первая: коробки с
# фаской, балки, лофты и диски. Помощники ниже не меняют R1–R5.


def _ribs(p, center, size, mat, bone, count=3, gap=0.06, rot=None):
    """Горизонтальные рёбра на передней панели (жёсткость, маркировка)."""
    cx, cy, cz = center
    for i in range(count):
        z = cz + (i - (count - 1) * 0.5) * gap
        p.box((cx, cy, z), size, mat, bone, bevel=0.0, rot=rot)


def _rivet_ring(p, center, radius, mat, bone, axis="Y", count=8, size=0.022):
    """Заклёпки по кругу — вокруг иллюминатора, горловины, ступицы."""
    cx, cy, cz = center
    for i in range(count):
        a = math.tau * i / count
        if axis == "Y":
            p.box((cx + math.cos(a) * radius, cy, cz + math.sin(a) * radius),
                  (size, size, size), mat, bone, bevel=0.0)
        elif axis == "X":
            p.box((cx, cy + math.cos(a) * radius, cz + math.sin(a) * radius),
                  (size, size, size), mat, bone, bevel=0.0)
        else:
            p.box((cx + math.cos(a) * radius, cy + math.sin(a) * radius, cz),
                  (size, size, size), mat, bone, bevel=0.0)


def _gear(p, center, radius, depth, mat, bone, axis="Y", teeth=8):
    """Шестерня: диск плюс зубья по ободу."""
    p.disc(center, radius, depth, mat, bone, axis=axis, n=12)
    cx, cy, cz = center
    for i in range(teeth):
        a = math.tau * i / teeth
        tooth = (radius * 0.42, radius * 0.42)
        if axis == "Y":
            pos = (cx + math.cos(a) * radius, cy, cz + math.sin(a) * radius)
            rot = (0, -math.degrees(a), 0)
            size = (tooth[0], depth * 0.85, tooth[1])
        elif axis == "X":
            pos = (cx, cy + math.cos(a) * radius, cz + math.sin(a) * radius)
            rot = (math.degrees(a), 0, 0)
            size = (depth * 0.85, tooth[0], tooth[1])
        else:
            pos = (cx + math.cos(a) * radius, cy + math.sin(a) * radius, cz)
            rot = (0, 0, math.degrees(a))
            size = (tooth[0], tooth[1], depth * 0.85)
        p.box(pos, size, mat, bone, bevel=0.0, rot=rot)


def _valve(p, center, radius, mat, bone, axis="Y"):
    """Вентиль: обод, ступица и четыре спицы в плоскости лица."""
    p.disc(center, radius, 0.04, mat, bone, axis=axis, n=12)
    p.disc(center, radius * 0.30, 0.09, mat, bone, axis=axis, n=8)
    cx, cy, cz = center
    for i in range(4):
        a = math.tau * i / 8.0 + math.pi / 8.0
        p.beam(center,
               (cx + math.cos(a) * radius * 0.88, cy,
                cz + math.sin(a) * radius * 0.88),
               0.035, 0.03, 0.028, 0.028, mat, bone)


def _tank(p, center, radius, length, mat, bone, axis="Z", n=10):
    """Цилиндр-бак вдоль оси; торцы закрыты."""
    cx, cy, cz = center
    half = length * 0.5
    if axis == "Z":
        points = [(cx, cy, cz - half), (cx, cy, cz + half)]
    elif axis == "X":
        points = [(cx - half, cy, cz), (cx + half, cy, cz)]
    else:
        points = [(cx, cy - half, cz), (cx, cy + half, cz)]
    p.tube(points, [(radius, radius), (radius, radius)], mat, bone, n=n)


def _hazard(p, center, size, mat, bone, count=3, gap=0.055, rot=None):
    """Полосатая маркировка: плашки, поставленные друг над другом."""
    _ribs(p, center, size, mat, bone, count=count, gap=gap, rot=rot)


# ------------------------------------------------------------------ R6–R10


def build_robot_06(arm):
    """Литейщик: бочкообразный корпус, горн в груди, два выхлопа, рукавицы."""
    name = "R6_Smelter"
    keys = ["base", "panel", "dark", "snow", "metal", "glow", "rust", "yellow", "accent"]
    mats, M = make_materials(name, keys, colors={
        "base": "#33373D", "panel": "#4E545C", "dark": "#14171A",
        "snow": "#D9D4C8", "metal": "#8E959C", "glow": "#FF6E1E",
        "rust": "#8C4A32", "yellow": "#E8B84B", "accent": "#C9553A",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, glow = M["snow"], M["metal"], M["glow"]
    rust, yellow, accent = M["rust"], M["yellow"], M["accent"]

    # таз, пояс и кожаный фартук
    p.box((0, 0.02, 0.95), (0.46, 0.34, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.035), (0.50, 0.37, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.195, 1.035), (0.14, 0.05, 0.08), accent, "pelvis", bevel=0.01)
    p.box((0, -0.205, 0.97), (0.40, 0.05, 0.34), rust, "pelvis", bevel=0.02)
    _bolts(p, [(-0.15, -0.235, 1.10), (0.15, -0.235, 1.10),
               (-0.15, -0.235, 0.86), (0.15, -0.235, 0.86)], metal, "pelvis")

    # корпус-бочка
    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.25, 0.20), (0.31, 0.25), (0.30, 0.245), (0.21, 0.18)],
           base, "spine_02", n=8)
    # горн в груди
    p.box((0, -0.245, 1.25), (0.34, 0.09, 0.28), dark, "spine_02", bevel=0.02)
    p.box((0, -0.30, 1.25), (0.26, 0.03, 0.20), glow, "spine_02", bevel=0.008)
    _ribs(p, (0, -0.315, 1.25), (0.28, 0.02, 0.024), dark, "spine_02", count=3, gap=0.08)
    for sx in (-1, 1):
        p.box((sx * 0.18, -0.285, 1.25), (0.045, 0.05, 0.30), metal, "spine_02",
              bevel=0.008)
    p.box((0, -0.245, 1.41), (0.34, 0.05, 0.10), rust, "spine_02", bevel=0.012)

    # наплечники и выхлопные трубы
    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.34, 0.0, 1.47), (0.34, 0.36, 0.16), panel, clav, bevel=0.04,
              rot=(0, sx * 8, 0))
        p.box((sx * 0.34, -0.01, 1.55), (0.30, 0.32, 0.05), metal, clav, bevel=0.012,
              rot=(0, sx * 8, 0))
        _ribs(p, (sx * 0.34, -0.195, 1.46), (0.24, 0.02, 0.02), yellow, clav,
              count=3, gap=0.055, rot=(0, sx * 8, 0))
    for sx in (-1, 1):
        p.tube([(sx * 0.26, 0.20, 1.36), (sx * 0.28, 0.24, 1.68),
                (sx * 0.28, 0.25, 1.88)],
               [(0.07, 0.07), (0.065, 0.065), (0.08, 0.08)], metal, "spine_03", n=8)
        p.disc((sx * 0.28, 0.25, 1.905), 0.085, 0.03, dark, "spine_03", axis="Z",
               n=8)
        p.disc((sx * 0.28, 0.25, 1.925), 0.05, 0.02, glow, "spine_03", axis="Z",
               n=8)

    # руки: обе — литейные рукавицы одного размера, кисти пустые
    _arms(p, R, 1, "l", M, upper=(0.22, 0.23, 0.17, 0.18),
          lower=(0.23, 0.24, 0.18, 0.19), hand=(0.22, 0.26, 0.26), glove="accent")
    _arms(p, R, -1, "r", M, upper=(0.22, 0.23, 0.17, 0.18),
          lower=(0.23, 0.24, 0.18, 0.19), hand=(0.22, 0.26, 0.26), glove="accent")

    # ноги с жёсткими щитками
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.26, 0.27, 0.22, 0.23),
              calf=(0.25, 0.26, 0.21, 0.22), boot=(0.24, 0.30, 0.16),
              sole=(0.25, 0.40, 0.05), toe=(0.23, 0.28, 0.15),
              knee_pad=(0.22, 0.09, 0.18))
        p.box((sx * 0.102, -0.115, 0.30), (0.20, 0.05, 0.28), metal, "calf_" + side,
              bevel=0.012)
        _ribs(p, (sx * 0.102, -0.145, 0.26), (0.16, 0.02, 0.025), yellow,
              "calf_" + side, count=3, gap=0.055)

    # голова: маленькая, с широкой смотровой щелью
    p.tube([(0, 0.02, 1.44), (0, 0.02, 1.58)], [(0.10, 0.10), (0.09, 0.09)],
           metal, "neck_01", n=8)
    p.box((0, -0.03, 1.70), (0.28, 0.26, 0.26), base, "Head", bevel=0.04)
    p.box((0, -0.17, 1.78), (0.30, 0.09, 0.06), metal, "Head", bevel=0.012)
    p.box((0, -0.165, 1.70), (0.24, 0.05, 0.10), dark, "Head", bevel=0.01)
    p.box((0, -0.19, 1.70), (0.20, 0.02, 0.035), glow, "Head", bevel=0.0)
    p.box((0, -0.145, 1.59), (0.22, 0.10, 0.08), panel, "Head", bevel=0.02)
    return name, p.build(name, mats), mats


def build_robot_07(arm):
    """Бродяга: тонкий сгорбленный корпус, рваный плащ и один глаз."""
    name = "R7_Drifter"
    keys = ["base", "panel", "dark", "snow", "metal", "fur", "accent", "warm",
            "rust"]
    mats, M = make_materials(name, keys, colors={
        "base": "#4C5054", "panel": "#6B7075", "dark": "#1D2023",
        "snow": "#E3E6E8", "metal": "#8F9498", "fur": "#CFC9BC",
        "accent": "#7E9086", "warm": "#FFC27A", "rust": "#7A4A34",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, fur = M["snow"], M["metal"], M["fur"]
    accent, warm, rust = M["accent"], M["warm"], M["rust"]

    p.box((0, 0.01, 0.95), (0.34, 0.27, 0.21), dark, "pelvis", bevel=0.035)
    p.box((0, 0.0, 1.02), (0.37, 0.29, 0.07), rust, "pelvis", bevel=0.015)
    # сгорбленный узкий корпус
    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.14), (0, -0.04, 1.30), (0, -0.06, 1.44)],
           [(0.20, 0.16), (0.22, 0.18), (0.20, 0.17), (0.15, 0.14)],
           base, "spine_02", n=8)
    p.box((0, -0.175, 1.24), (0.26, 0.05, 0.24), panel, "spine_02", bevel=0.02)
    p.box((0.06, -0.205, 1.31), (0.13, 0.03, 0.09), rust, "spine_02", bevel=0.0)
    p.box((-0.05, -0.205, 1.17), (0.11, 0.03, 0.07), metal, "spine_02", bevel=0.0)
    for sx in (-1, 1):
        p.beam((sx * 0.12, -0.17, 1.41), (sx * 0.10, -0.16, 1.04),
               0.05, 0.025, 0.045, 0.025, dark, "spine_02")

    # плащ: спинка, воротник и рваные полосы
    p.box((0, 0.175, 1.26), (0.32, 0.05, 0.40), fur, "spine_02", bevel=0.015,
          rot=(4, 0, 0))
    p.tube([(0, 0.005, 1.42), (0, 0.0, 1.50)], [(0.19, 0.17), (0.22, 0.20)],
           fur, "spine_03", n=10)
    for i, x in enumerate((-0.15, -0.05, 0.05, 0.15)):
        length = (0.34, 0.46, 0.30, 0.40)[i]
        p.box((x, 0.19, 1.02 - length * 0.5), (0.09, 0.04, length), fur, "pelvis",
              bevel=0.008, rot=(0, 0, (i - 1.5) * 3))
    p.box((0.16, -0.10, 0.86), (0.10, 0.04, 0.30), accent, "pelvis", bevel=0.008,
          rot=(0, 0, -4))

    # одно плечо — асимметрия
    p.box((0.30, 0.0, 1.46), (0.26, 0.30, 0.09), rust, "clavicle_l", bevel=0.025,
          rot=(0, 10, 0))
    _bolts(p, [(0.24, -0.13, 1.47), (0.36, -0.13, 1.47)], metal, "clavicle_l")

    _arms(p, R, 1, "l", M, upper=(0.15, 0.155, 0.115, 0.12),
          lower=(0.15, 0.16, 0.12, 0.125), hand=(0.13, 0.16, 0.17), glove="fur")
    _arms(p, R, -1, "r", M, upper=(0.15, 0.155, 0.115, 0.12),
          lower=(0.15, 0.16, 0.12, 0.125), hand=(0.13, 0.16, 0.17), glove="fur")

    # посох убран по просьбе владельца

    # голова: капюшон, один глаз, решётка вместо рта
    p.tube([(0, 0.02, 1.45), (0, 0.02, 1.56)], [(0.08, 0.08), (0.07, 0.07)],
           metal, "neck_01", n=8)
    p.box((0, -0.05, 1.68), (0.24, 0.24, 0.24), base, "Head", bevel=0.035)
    p.box((0, -0.02, 1.80), (0.28, 0.26, 0.06), fur, "Head", bevel=0.02,
          rot=(-8, 0, 0))
    p.box((0, -0.165, 1.69), (0.20, 0.04, 0.18), dark, "Head", bevel=0.012)
    p.disc((0.05, -0.195, 1.71), 0.05, 0.03, warm, "Head", axis="Y", n=10)
    p.disc((0.05, -0.18, 1.71), 0.072, 0.025, metal, "Head", axis="Y", n=10)
    for i in range(2):
        p.box((-0.06 + i * 0.05, -0.19, 1.62), (0.03, 0.02, 0.07), metal, "Head",
              bevel=0.0)
    p.tube([(0.09, 0.06, 1.80), (0.12, 0.09, 1.98), (0.11, 0.10, 2.06)],
           [(0.016, 0.016), (0.012, 0.012), (0.01, 0.01)], metal, "Head", n=6)
    p.sphere((0.11, 0.10, 2.07), 0.03, warm, "Head", subdivisions=1)

    # ноги: тонкие, с тряпичными обмотками
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.16, 0.17, 0.13, 0.14),
              calf=(0.16, 0.17, 0.125, 0.135), boot=(0.18, 0.24, 0.13),
              sole=(0.19, 0.32, 0.04), toe=(0.17, 0.22, 0.12),
              knee_pad=(0.15, 0.07, 0.14), shin_plate=False)
        for i in range(3):
            p.box((sx * 0.102, 0.0, 0.18 + i * 0.10), (0.19, 0.20, 0.035), fur,
                  "calf_" + side, bevel=0.0)
    return name, p.build(name, mats), mats


def build_robot_08(arm):
    """Щитоносец: башенный щит на предплечье, приземистый корпус, крошечная голова."""
    name = "R8_Bulwark"
    keys = ["base", "panel", "dark", "snow", "metal", "yellow", "accent", "cyan"]
    mats, M = make_materials(name, keys, colors={
        "base": "#33475C", "panel": "#4E6883", "dark": "#121A24",
        "snow": "#E9EFF5", "metal": "#A6AFB8", "yellow": "#E8B84B",
        "accent": "#C9553A", "cyan": "#8FD8F0",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, yellow = M["snow"], M["metal"], M["yellow"]
    accent, cyan = M["accent"], M["cyan"]

    p.box((0, 0.02, 0.95), (0.48, 0.34, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.52, 0.37, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.20, 1.03), (0.15, 0.05, 0.08), yellow, "pelvis", bevel=0.01)

    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.13), (0, -0.01, 1.30), (0, 0.0, 1.46)],
           [(0.27, 0.21), (0.33, 0.25), (0.32, 0.245), (0.23, 0.19)],
           base, "spine_02", n=8)
    p.box((0, -0.235, 1.25), (0.42, 0.06, 0.32), panel, "spine_02", bevel=0.03)
    p.box((0, -0.275, 1.25), (0.10, 0.04, 0.30), metal, "spine_02", bevel=0.012)
    _ribs(p, (0, -0.27, 1.25), (0.30, 0.02, 0.03), dark, "spine_02", count=4, gap=0.07)
    # радиатор на спине
    for i in range(4):
        p.box((-0.12 + i * 0.08, 0.28, 1.26), (0.05, 0.20, 0.30), metal,
              "spine_02", bevel=0.008)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.35, 0.0, 1.48), (0.32, 0.36, 0.20), panel, clav, bevel=0.035,
              rot=(0, sx * 6, 0))
        p.box((sx * 0.35, -0.01, 1.585), (0.28, 0.32, 0.05), metal, clav,
              bevel=0.012, rot=(0, sx * 6, 0))
        _bolts(p, [(sx * 0.30, -0.17, 1.42), (sx * 0.40, -0.17, 1.42),
                   (sx * 0.30, -0.17, 1.55), (sx * 0.40, -0.17, 1.55)],
               metal, clav, 0.02)

    # Обе руки одинаковы и без предметов; щит остаётся экипировкой предплечья.
    _arms(p, R, 1, "l", M, upper=(0.23, 0.24, 0.18, 0.19),
          lower=(0.24, 0.25, 0.19, 0.20), hand=(0.22, 0.26, 0.26), glove="accent")
    _arms(p, R, -1, "r", M, upper=(0.23, 0.24, 0.18, 0.19),
          lower=(0.24, 0.25, 0.19, 0.20), hand=(0.22, 0.26, 0.26), glove="accent")

    # круглый щит на внешней стороне предплечья: плоскость вдоль руки,
    # лицом наружу (в T-позе лежит сверху, в idle встаёт сбоку)
    p.box((0.88, 0.0, 1.50), (0.20, 0.20, 0.14), metal, "lowerarm_l", bevel=0.02)
    p.disc((0.88, 0.0, 1.57), 0.24, 0.05, metal, "lowerarm_l", axis="Z", n=14)
    p.disc((0.88, 0.0, 1.605), 0.20, 0.03, base, "lowerarm_l", axis="Z", n=14)
    p.disc((0.88, 0.0, 1.625), 0.08, 0.03, panel, "lowerarm_l", axis="Z", n=10)
    p.disc((0.88, 0.0, 1.645), 0.045, 0.02, cyan, "lowerarm_l", axis="Z", n=8)
    _rivet_ring(p, (0.88, 0.0, 1.615), 0.16, yellow, "lowerarm_l", axis="Z",
                count=6, size=0.026)
    p.box((0.78, 0.0, 1.44), (0.18, 0.24, 0.24), panel, "lowerarm_l", bevel=0.03)

    # ноги: приземистые, с наружными щитками
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.28, 0.29, 0.24, 0.25),
              calf=(0.27, 0.28, 0.23, 0.24), boot=(0.24, 0.32, 0.17),
              sole=(0.26, 0.44, 0.06), toe=(0.24, 0.30, 0.16),
              knee_pad=(0.24, 0.10, 0.20))
        p.box((sx * 0.21, 0.0, 0.72), (0.10, 0.26, 0.28), panel, "thigh_" + side,
              bevel=0.02, rot=(0, sx * 5, 0))
        _hazard(p, (sx * 0.102, -0.145, 0.30), (0.17, 0.02, 0.03), yellow,
                "calf_" + side, count=3, gap=0.055)

    # голова посажена низко между наплечниками
    p.box((0, -0.04, 1.66), (0.24, 0.24, 0.20), dark, "Head", bevel=0.03)
    p.box((0, -0.16, 1.67), (0.22, 0.05, 0.06), cyan, "Head", bevel=0.0)
    p.box((0, -0.14, 1.74), (0.28, 0.10, 0.05), panel, "Head", bevel=0.012)
    return name, p.build(name, mats), mats


def build_robot_09(arm):
    """Ремонтник: короткий, большеголовый, с ящиками на спине и пустыми кистями."""
    name = "R9_Tinker"
    keys = ["base", "panel", "dark", "snow", "metal", "accent", "glow", "rust"]
    mats, M = make_materials(name, keys, colors={
        "base": "#6E5B33", "panel": "#8E7A45", "dark": "#241E14",
        "snow": "#DCD3BC", "metal": "#A8A08E", "accent": "#C9553A",
        "glow": "#FFC061", "rust": "#8C5A34",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, accent = M["snow"], M["metal"], M["accent"]
    glow, rust = M["glow"], M["rust"]

    p.box((0, 0.01, 0.95), (0.44, 0.32, 0.23), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.50, 0.38, 0.10), dark, "pelvis", bevel=0.02)
    # пояс с карманами и подвешенным инструментом
    for sx in (-1, 1):
        p.box((sx * 0.20, -0.17, 1.00), (0.15, 0.10, 0.15), rust, "pelvis",
              bevel=0.02)
        p.box((sx * 0.20, -0.225, 1.045), (0.10, 0.02, 0.03), metal, "pelvis",
              bevel=0.0)
    p.beam((0.24, -0.14, 0.99), (0.26, -0.14, 0.82), 0.06, 0.05, 0.05, 0.05,
           metal, "pelvis")
    p.box((0.26, -0.14, 0.79), (0.10, 0.08, 0.07), metal, "pelvis", bevel=0.012)

    # короткий бочкообразный корпус
    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.12), (0, -0.01, 1.28), (0, 0.0, 1.42)],
           [(0.26, 0.21), (0.30, 0.25), (0.29, 0.24), (0.20, 0.17)],
           base, "spine_02", n=8)
    p.box((0, -0.225, 1.20), (0.34, 0.06, 0.26), panel, "spine_02", bevel=0.025)
    p.box((0, -0.26, 1.20), (0.16, 0.03, 0.10), dark, "spine_02", bevel=0.0)
    p.box((0, -0.265, 1.20), (0.12, 0.02, 0.06), glow, "spine_02", bevel=0.0)
    _bolts(p, [(-0.13, -0.26, 1.30), (0.13, -0.26, 1.30),
               (-0.13, -0.26, 1.10), (0.13, -0.26, 1.10)], metal, "spine_02")

    # ящики на спине
    p.box((0, 0.24, 1.22), (0.34, 0.16, 0.34), panel, "spine_02", bevel=0.025)
    for i in range(3):
        p.box((0, 0.325, 1.11 + i * 0.10), (0.26, 0.03, 0.08), dark, "spine_02",
              bevel=0.0)
        p.box((0, 0.345, 1.11 + i * 0.10), (0.08, 0.02, 0.02), metal, "spine_02",
              bevel=0.0)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.28, 0.0, 1.44), (0.22, 0.26, 0.10), accent, clav, bevel=0.025,
              rot=(0, sx * 8, 0))

    _arms(p, R, 1, "l", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="dark")

    # большая голова с лупой и вторым объективом
    p.tube([(0, 0.02, 1.42), (0, 0.02, 1.54)], [(0.09, 0.09), (0.08, 0.08)],
           metal, "neck_01", n=8)
    p.box((0, -0.04, 1.70), (0.36, 0.32, 0.30), base, "Head", bevel=0.05)
    p.box((0, -0.19, 1.70), (0.32, 0.06, 0.26), dark, "Head", bevel=0.02)
    p.disc((0.07, -0.225, 1.73), 0.075, 0.04, glow, "Head", axis="Y", n=12)
    p.disc((0.07, -0.205, 1.73), 0.105, 0.03, metal, "Head", axis="Y", n=12)
    p.disc((-0.10, -0.22, 1.72), 0.045, 0.035, glow, "Head", axis="Y", n=10)
    p.disc((-0.10, -0.205, 1.72), 0.065, 0.025, metal, "Head", axis="Y", n=10)
    p.box((0, -0.02, 1.86), (0.34, 0.30, 0.06), panel, "Head", bevel=0.015)
    p.box((0.16, -0.02, 1.90), (0.06, 0.10, 0.05), accent, "Head", bevel=0.008)
    _bolts(p, [(-0.14, -0.22, 1.60), (0.14, -0.22, 1.60)], metal, "Head", 0.02)

    # короткие толстые ноги
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.24, 0.25, 0.20, 0.21),
              calf=(0.23, 0.24, 0.19, 0.20), boot=(0.22, 0.30, 0.15),
              sole=(0.24, 0.38, 0.05), toe=(0.21, 0.26, 0.14),
              knee_pad=(0.20, 0.09, 0.16))
    return name, p.build(name, mats), mats


def build_robot_10(arm):
    """Трубоукладчик: вентиль на груди, трубы через плечи, бак; кисти пустые."""
    name = "R10_Pipewright"
    keys = ["base", "panel", "dark", "snow", "metal", "copper", "brass", "glow",
            "patina"]
    mats, M = make_materials(name, keys, colors={
        "base": "#3F5B52", "panel": "#5C7A6E", "dark": "#16201C",
        "snow": "#DDE4DA", "metal": "#9AA39B", "copper": "#8C5A38",
        "brass": "#B08A4A", "glow": "#FFB05A", "patina": "#6E9C86",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, copper = M["snow"], M["metal"], M["copper"]
    brass, glow, patina = M["brass"], M["glow"], M["patina"]

    p.box((0, 0.02, 0.95), (0.42, 0.31, 0.23), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.46, 0.35, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.185, 1.03), (0.13, 0.05, 0.08), brass, "pelvis", bevel=0.01)

    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.13), (0, -0.01, 1.30), (0, 0.0, 1.46)],
           [(0.23, 0.19), (0.28, 0.23), (0.27, 0.22), (0.19, 0.17)],
           base, "spine_02", n=8)
    # вентиль на груди
    p.box((0, -0.21, 1.25), (0.30, 0.05, 0.26), panel, "spine_02", bevel=0.02)
    _valve(p, (0, -0.26, 1.25), 0.14, brass, "spine_02")
    # трубы через плечи к баку
    for sx in (-1, 1):
        p.tube([(sx * 0.15, -0.19, 1.03), (sx * 0.24, -0.21, 1.20),
                (sx * 0.28, -0.16, 1.40), (sx * 0.26, 0.02, 1.49),
                (sx * 0.20, 0.22, 1.38)],
               [(0.045, 0.045)] * 5, copper, "spine_02", n=6)
        p.sphere((sx * 0.28, -0.16, 1.40), 0.055, copper, "spine_02", subdivisions=1)
    # горизонтальный бак на спине
    _tank(p, (0, 0.30, 1.27), 0.13, 0.56, panel, "spine_02", axis="X", n=10)
    for sx in (-1, 1):
        p.disc((sx * 0.28, 0.30, 1.27), 0.10, 0.03, metal, "spine_02", axis="X", n=10)
        p.box((sx * 0.14, 0.30, 1.40), (0.06, 0.20, 0.04), dark, "spine_02",
              bevel=0.0)
    p.disc((0.14, 0.30, 1.40), 0.04, 0.05, brass, "spine_02", axis="Z", n=8)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.30, 0.0, 1.46), (0.26, 0.30, 0.12), copper, clav, bevel=0.03,
              rot=(0, sx * 7, 0))
        _bolts(p, [(sx * 0.26, -0.14, 1.47), (sx * 0.34, -0.14, 1.47)], brass, clav)

    _arms(p, R, 1, "l", M, upper=(0.19, 0.20, 0.15, 0.16),
          lower=(0.20, 0.21, 0.16, 0.17), hand=(0.17, 0.21, 0.21), glove="brass")
    _arms(p, R, -1, "r", M, upper=(0.19, 0.20, 0.15, 0.16),
          lower=(0.20, 0.21, 0.16, 0.17), hand=(0.17, 0.21, 0.21), glove="brass")

    # ноги с трубами-усилителями
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.22, 0.23, 0.18, 0.19),
              calf=(0.21, 0.22, 0.17, 0.18), boot=(0.21, 0.28, 0.15),
              sole=(0.23, 0.36, 0.05), toe=(0.20, 0.25, 0.14),
              knee_pad=(0.19, 0.08, 0.16))
        p.tube([(sx * 0.20, 0.0, 0.50), (sx * 0.20, 0.0, 0.16)],
               [(0.035, 0.035), (0.03, 0.03)], copper, "calf_" + side, n=6)
        p.disc((sx * 0.20, 0.0, 0.46), 0.055, 0.04, brass, "calf_" + side,
               axis="Z", n=8)

    # голова-цилиндр с манометром
    p.tube([(0, 0.02, 1.44), (0, 0.02, 1.56)], [(0.09, 0.09), (0.08, 0.08)],
           metal, "neck_01", n=8)
    p.tube([(0, -0.01, 1.57), (0, -0.01, 1.78)], [(0.17, 0.16), (0.16, 0.15)],
           base, "Head", n=10)
    p.box((0, -0.01, 1.80), (0.30, 0.28, 0.05), metal, "Head", bevel=0.012)
    p.disc((0, -0.185, 1.68), 0.08, 0.03, snow, "Head", axis="Y", n=12)
    p.disc((0, -0.20, 1.68), 0.095, 0.02, brass, "Head", axis="Y", n=12)
    p.box((0.02, -0.205, 1.70), (0.02, 0.02, 0.07), dark, "Head", bevel=0.0,
          rot=(0, 0, 22))
    p.tube([(0.12, 0.0, 1.78), (0.16, 0.10, 1.86), (0.10, 0.20, 1.82)],
           [(0.03, 0.03), (0.03, 0.03), (0.025, 0.025)], copper, "Head", n=6)
    for sx in (-1, 1):
        p.box((sx * 0.16, -0.02, 1.66), (0.04, 0.12, 0.14), patina, "Head",
              bevel=0.008)
    return name, p.build(name, mats), mats


# ------------------------------------------------------------------ R11–R15


def build_robot_11(arm):
    """Снегоуборщик: приземистый, с отвалом на тазу и маячком на плече."""
    name = "R11_Snowplow"
    keys = ["base", "panel", "dark", "snow", "metal", "orange", "glow"]
    mats, M = make_materials(name, keys, colors={
        "base": "#3A3F45", "panel": "#565C63", "dark": "#15181C",
        "snow": "#E9EFF5", "metal": "#9AA3AC", "orange": "#E8752A",
        "glow": "#FF9A3C",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, orange = M["snow"], M["metal"], M["orange"]
    glow = M["glow"]

    p.box((0, 0.02, 0.95), (0.46, 0.33, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.50, 0.36, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.19, 1.03), (0.14, 0.05, 0.08), orange, "pelvis", bevel=0.01)

    # отвал убран: по просьбе владельца балки и передние плиты удалены

    # корпус, наклонённый вперёд
    p.tube([(0, 0.03, 0.99), (0, -0.02, 1.13), (0, -0.05, 1.30), (0, -0.04, 1.44)],
           [(0.25, 0.21), (0.31, 0.26), (0.29, 0.24), (0.21, 0.18)],
           base, "spine_02", n=8)
    p.box((0, -0.235, 1.22), (0.36, 0.06, 0.26), panel, "spine_02", bevel=0.025)
    _ribs(p, (0, -0.27, 1.22), (0.28, 0.02, 0.03), dark, "spine_02", count=3, gap=0.07)
    p.box((0, -0.24, 1.38), (0.30, 0.05, 0.07), orange, "spine_02", bevel=0.01)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.33, 0.0, 1.47), (0.30, 0.34, 0.16), panel, clav, bevel=0.04,
              rot=(0, sx * 6, 0))
        p.box((sx * 0.33, -0.01, 1.56), (0.26, 0.30, 0.05), orange, clav,
              bevel=0.012, rot=(0, sx * 6, 0))
    # маячок и выхлоп
    p.tube([(0.33, 0.02, 1.59), (0.33, 0.02, 1.64)], [(0.06, 0.06), (0.055, 0.055)],
           metal, "clavicle_l", n=8)
    p.sphere((0.33, 0.02, 1.68), 0.055, glow, "clavicle_l", subdivisions=1)
    p.tube([(-0.24, 0.20, 1.36), (-0.26, 0.22, 1.66), (-0.26, 0.22, 1.80)],
           [(0.055, 0.055), (0.05, 0.05), (0.06, 0.06)], metal, "spine_03", n=8)
    p.disc((-0.26, 0.22, 1.82), 0.065, 0.03, dark, "spine_03", axis="Z", n=8)

    _arms(p, R, 1, "l", M, upper=(0.21, 0.22, 0.16, 0.17),
          lower=(0.22, 0.23, 0.17, 0.18), hand=(0.19, 0.23, 0.23), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.21, 0.22, 0.16, 0.17),
          lower=(0.22, 0.23, 0.17, 0.18), hand=(0.19, 0.23, 0.23), glove="dark")

    # ноги в широких башмаках и с крыльями над голенью
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.26, 0.27, 0.22, 0.23),
              calf=(0.25, 0.26, 0.21, 0.22), boot=(0.24, 0.30, 0.16),
              sole=(0.26, 0.42, 0.06), toe=(0.23, 0.28, 0.15),
              knee_pad=(0.22, 0.09, 0.18))
        p.box((sx * 0.16, -0.02, 0.30), (0.18, 0.34, 0.22), panel, "calf_" + side,
              bevel=0.02, rot=(0, sx * 4, 0))
        _hazard(p, (sx * 0.102, -0.145, 0.30), (0.17, 0.02, 0.03), orange,
                "calf_" + side, count=3, gap=0.055)

    # низкая голова с козырьком
    p.box((0, -0.08, 1.66), (0.30, 0.28, 0.22), base, "Head", bevel=0.035)
    p.box((0, -0.22, 1.67), (0.28, 0.05, 0.09), dark, "Head", bevel=0.008)
    p.box((0, -0.235, 1.67), (0.24, 0.02, 0.035), glow, "Head", bevel=0.0)
    p.box((0, -0.17, 1.76), (0.32, 0.10, 0.05), orange, "Head", bevel=0.01)
    for sx in (-1, 1):
        p.box((sx * 0.15, -0.10, 1.63), (0.04, 0.10, 0.10), metal, "Head",
              bevel=0.008)
    return name, p.build(name, mats), mats


def build_robot_12(arm):
    """Смотритель маяка: высокий, тонкий, с фонарём вместо головы и длинным пальто."""
    name = "R12_Beacon"
    keys = ["base", "panel", "dark", "snow", "metal", "brass", "warm", "glass",
            "patina"]
    mats, M = make_materials(name, keys, colors={
        "base": "#23303F", "panel": "#3A4C60", "dark": "#101720",
        "snow": "#E7E3D6", "metal": "#9AA6AE", "brass": "#B08A4A",
        "warm": "#FFC978", "glass": "#FFE0AE", "patina": "#6E9C86",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, brass = M["snow"], M["metal"], M["brass"]
    warm, glass, patina = M["warm"], M["glass"], M["patina"]

    p.box((0, 0.01, 0.96), (0.34, 0.27, 0.21), dark, "pelvis", bevel=0.035)
    p.box((0, 0.0, 1.03), (0.38, 0.30, 0.08), panel, "pelvis", bevel=0.02)
    # пальто: спинка, полы и борта
    p.box((0, 0.16, 0.80), (0.32, 0.05, 0.44), panel, "pelvis", bevel=0.015)
    for sx in (-1, 1):
        p.box((sx * 0.19, 0.0, 0.80), (0.05, 0.26, 0.44), base, "pelvis",
              bevel=0.012)
        p.box((sx * 0.09, -0.16, 0.80), (0.16, 0.05, 0.44), base, "pelvis",
              bevel=0.012)
        for i in range(3):
            p.disc((sx * 0.09, -0.195, 0.94 - i * 0.12), 0.028, 0.02, brass,
                   "pelvis", axis="Y", n=8)
    p.tube([(0, 0.005, 1.42), (0, 0.0, 1.50)], [(0.20, 0.18), (0.23, 0.21)],
           panel, "spine_03", n=10)

    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.19, 0.16), (0.21, 0.18), (0.20, 0.17), (0.15, 0.14)],
           base, "spine_02", n=8)
    p.box((0, -0.185, 1.26), (0.26, 0.05, 0.28), panel, "spine_02", bevel=0.02)
    p.box((0, -0.215, 1.26), (0.06, 0.03, 0.24), brass, "spine_02", bevel=0.008)
    # генератор на спине
    p.box((0, 0.21, 1.22), (0.26, 0.16, 0.28), panel, "spine_02", bevel=0.025)
    p.disc((0.0, 0.30, 1.22), 0.08, 0.03, patina, "spine_02", axis="Y", n=10)
    p.tube([(0.12, 0.22, 1.36), (0.20, 0.16, 1.46), (0.16, 0.06, 1.44)],
           [(0.025, 0.025)] * 3, metal, "spine_02", n=6)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.28, 0.0, 1.46), (0.22, 0.26, 0.06), brass, clav, bevel=0.015,
              rot=(0, sx * 7, 0))

    _arms(p, R, 1, "l", M, upper=(0.14, 0.15, 0.11, 0.12),
          lower=(0.15, 0.155, 0.12, 0.125), hand=(0.13, 0.17, 0.18), glove="brass")
    _arms(p, R, -1, "r", M, upper=(0.14, 0.15, 0.11, 0.12),
          lower=(0.15, 0.155, 0.12, 0.125), hand=(0.13, 0.17, 0.18), glove="brass")

    # ноги тонкие, с раструбами на голени
    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.17, 0.18, 0.14, 0.15),
              calf=(0.17, 0.18, 0.13, 0.14), boot=(0.19, 0.26, 0.14),
              sole=(0.20, 0.34, 0.045), toe=(0.18, 0.23, 0.13),
              knee_pad=(0.16, 0.07, 0.14))
        p.box((sx * 0.15, 0.0, 0.26), (0.10, 0.24, 0.28), panel, "calf_" + side,
              bevel=0.015, rot=(0, sx * 8, 0))

    # голова-фонарь: рама, стекло, крышка и шпиль
    p.tube([(0, 0.01, 1.46), (0, 0.01, 1.58)], [(0.08, 0.08), (0.075, 0.075)],
           metal, "neck_01", n=8)
    p.box((0, -0.02, 1.57), (0.28, 0.26, 0.06), brass, "Head", bevel=0.012)
    p.box((0, -0.02, 1.68), (0.22, 0.20, 0.18), glass, "Head", bevel=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * 0.10, -0.02 + sy * 0.09, 1.68), (0.04, 0.04, 0.20),
                  brass, "Head", bevel=0.0)
    for sy in (-1, 1):
        p.box((0, -0.02 + sy * 0.10, 1.68), (0.20, 0.03, 0.03), brass, "Head",
              bevel=0.0)
    p.box((0, -0.02, 1.79), (0.28, 0.26, 0.05), brass, "Head", bevel=0.012)
    p.tube([(0, -0.02, 1.81), (0, -0.02, 1.88)], [(0.10, 0.10), (0.04, 0.04)],
           brass, "Head", n=8)
    p.sphere((0, -0.02, 1.90), 0.035, warm, "Head", subdivisions=1)
    # узкая маска спереди, чтобы у фонаря было «лицо»
    p.box((0, -0.125, 1.66), (0.18, 0.03, 0.10), dark, "Head", bevel=0.008)
    for sx in (-1, 1):
        p.box((sx * 0.05, -0.145, 1.66), (0.04, 0.02, 0.03), warm, "Head",
              bevel=0.0)
    return name, p.build(name, mats), mats


def build_robot_13(arm):
    """Старатель: шахтёрский шлем с лампой, мешок за спиной, обе кисти пустые."""
    name = "R13_Prospector"
    keys = ["base", "panel", "dark", "snow", "metal", "yellow", "rust", "glow"]
    mats, M = make_materials(name, keys, colors={
        "base": "#5A4B36", "panel": "#7A6547", "dark": "#1E1811",
        "snow": "#D9D0BC", "metal": "#8E8A80", "yellow": "#D9A94A",
        "rust": "#8C4A32", "glow": "#FFD27A",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, yellow = M["snow"], M["metal"], M["yellow"]
    rust, glow = M["rust"], M["glow"]

    p.box((0, 0.02, 0.95), (0.44, 0.32, 0.23), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.48, 0.36, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.19, 1.03), (0.13, 0.05, 0.07), yellow, "pelvis", bevel=0.01)

    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.30), (0, 0.0, 1.46)],
           [(0.24, 0.20), (0.29, 0.24), (0.28, 0.23), (0.20, 0.17)],
           base, "spine_02", n=8)
    p.box((0, -0.225, 1.24), (0.34, 0.06, 0.28), panel, "spine_02", bevel=0.025)
    p.box((0, -0.26, 1.15), (0.30, 0.03, 0.10), rust, "spine_02", bevel=0.0)
    p.disc((0.10, -0.26, 1.31), 0.055, 0.03, snow, "spine_02", axis="Y", n=10)
    p.disc((0.10, -0.275, 1.31), 0.07, 0.02, metal, "spine_02", axis="Y", n=10)
    p.box((0.10, -0.28, 1.31), (0.015, 0.02, 0.05), dark, "spine_02", bevel=0.0,
          rot=(0, 0, 30))

    # мешок с рудой и скатка
    p.box((0, 0.29, 1.16), (0.34, 0.24, 0.34), panel, "spine_02", bevel=0.05)
    p.tube([(-0.20, 0.29, 1.40), (0.20, 0.29, 1.40)],
           [(0.085, 0.085), (0.085, 0.085)], rust, "spine_02", n=8)
    for sx in (-1, 1):
        p.beam((sx * 0.14, -0.16, 1.40), (sx * 0.16, 0.16, 1.20),
               0.05, 0.025, 0.045, 0.025, dark, "spine_02")

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.31, 0.0, 1.46), (0.28, 0.32, 0.13), panel, clav, bevel=0.035,
              rot=(0, sx * 7, 0))
        p.box((sx * 0.31, -0.01, 1.535), (0.24, 0.28, 0.04), rust, clav,
              bevel=0.01, rot=(0, sx * 7, 0))

    # Обе кисти одинаковы: правая раньше была буром, теперь это кисть левой руки.
    _arms(p, R, 1, "l", M, upper=(0.20, 0.21, 0.15, 0.16),
          lower=(0.21, 0.22, 0.16, 0.17), hand=(0.18, 0.22, 0.22), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.20, 0.21, 0.15, 0.16),
          lower=(0.21, 0.22, 0.16, 0.17), hand=(0.18, 0.22, 0.22), glove="dark")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.25, 0.26, 0.21, 0.22),
              calf=(0.24, 0.25, 0.20, 0.21), boot=(0.23, 0.31, 0.16),
              sole=(0.25, 0.40, 0.05), toe=(0.22, 0.28, 0.15),
              knee_pad=(0.21, 0.09, 0.17))
        p.box((sx * 0.102, -0.16, 0.10), (0.18, 0.10, 0.08), metal, "foot_" + side,
              bevel=0.01)

    # голова: шлем с лампой и два глаза
    p.tube([(0, 0.02, 1.43), (0, 0.02, 1.55)], [(0.09, 0.09), (0.08, 0.08)],
           metal, "neck_01", n=8)
    p.box((0, -0.04, 1.67), (0.28, 0.26, 0.24), dark, "Head", bevel=0.03)
    p.tube([(0, -0.03, 1.78), (0, -0.03, 1.88), (0, -0.03, 1.95)],
           [(0.20, 0.19), (0.17, 0.16), (0.09, 0.09)], panel, "Head", n=10)
    p.box((0, -0.10, 1.785), (0.34, 0.30, 0.04), panel, "Head", bevel=0.008)
    p.disc((0, -0.245, 1.82), 0.075, 0.04, glow, "Head", axis="Y", n=10)
    p.disc((0, -0.225, 1.82), 0.095, 0.025, metal, "Head", axis="Y", n=10)
    for sx in (-1, 1):
        p.disc((sx * 0.07, -0.185, 1.66), 0.04, 0.03, glow, "Head", axis="Y", n=8)
        p.disc((sx * 0.07, -0.17, 1.66), 0.058, 0.02, metal, "Head", axis="Y", n=8)
    p.box((0, -0.17, 1.58), (0.16, 0.06, 0.05), rust, "Head", bevel=0.008)
    return name, p.build(name, mats), mats


def build_robot_14(arm):
    """Фельдшер: светлый корпус, красный крест, сумка на бедре, кисти пустые."""
    name = "R14_Medic"
    keys = ["base", "panel", "dark", "snow", "metal", "red", "glass", "warm"]
    mats, M = make_materials(name, keys, colors={
        "base": "#6E7B82", "panel": "#98A5AA", "dark": "#22282C",
        "snow": "#EEF2F4", "metal": "#A8B0B4", "red": "#B23A2E",
        "glass": "#CFEAF2", "warm": "#FFE6B0",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, red = M["snow"], M["metal"], M["red"]
    glass, warm = M["glass"], M["warm"]

    p.box((0, 0.01, 0.95), (0.36, 0.28, 0.22), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.40, 0.31, 0.08), panel, "pelvis", bevel=0.02)
    # аптечная сумка на левом бедре
    p.box((-0.26, 0.02, 0.98), (0.16, 0.24, 0.22), snow, "pelvis", bevel=0.03)
    p.box((-0.26, -0.105, 0.99), (0.08, 0.02, 0.08), red, "pelvis", bevel=0.0)
    p.beam((-0.10, -0.14, 1.36), (-0.24, -0.10, 1.08), 0.05, 0.03, 0.045, 0.03,
           dark, "spine_02")

    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.20, 0.17), (0.24, 0.20), (0.23, 0.19), (0.17, 0.15)],
           base, "spine_02", n=8)
    # фартук с крестом
    p.box((0, -0.215, 1.18), (0.34, 0.05, 0.36), snow, "spine_02", bevel=0.02)
    p.box((0, -0.245, 1.22), (0.07, 0.02, 0.22), red, "spine_02", bevel=0.0)
    p.box((0, -0.245, 1.22), (0.18, 0.02, 0.07), red, "spine_02", bevel=0.0)
    p.box((0, -0.21, 1.42), (0.30, 0.05, 0.07), panel, "spine_02", bevel=0.01)
    # пеналы и фляга на спине
    p.box((0, 0.21, 1.24), (0.28, 0.14, 0.30), panel, "spine_02", bevel=0.025)
    for sx in (-1, 1):
        p.tube([(sx * 0.10, 0.30, 1.13), (sx * 0.10, 0.30, 1.36)],
               [(0.05, 0.05), (0.05, 0.05)], snow, "spine_02", n=8)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.28, 0.0, 1.46), (0.22, 0.26, 0.08), panel, clav, bevel=0.02,
              rot=(0, sx * 7, 0))
        p.box((sx * 0.28, -0.12, 1.47), (0.08, 0.03, 0.05), red, clav, bevel=0.0)

    _arms(p, R, 1, "l", M, upper=(0.17, 0.18, 0.13, 0.14),
          lower=(0.18, 0.19, 0.14, 0.15), hand=(0.15, 0.19, 0.19), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.17, 0.18, 0.13, 0.14),
          lower=(0.18, 0.19, 0.14, 0.15), hand=(0.15, 0.19, 0.19), glove="dark")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.20, 0.21, 0.17, 0.18),
              calf=(0.19, 0.20, 0.16, 0.17), boot=(0.20, 0.27, 0.14),
              sole=(0.21, 0.34, 0.045), toe=(0.19, 0.24, 0.13),
              knee_pad=(0.18, 0.08, 0.15))
        p.box((sx * 0.102, -0.125, 0.30), (0.17, 0.04, 0.24), snow,
              "calf_" + side, bevel=0.012)
        p.box((sx * 0.102, -0.145, 0.34), (0.09, 0.02, 0.04), red, "calf_" + side,
              bevel=0.0)
        p.box((sx * 0.102, -0.145, 0.34), (0.03, 0.02, 0.11), red, "calf_" + side,
              bevel=0.0)

    # голова: два объектива и лампа
    p.tube([(0, 0.02, 1.44), (0, 0.02, 1.56)], [(0.08, 0.08), (0.075, 0.075)],
           metal, "neck_01", n=8)
    p.box((0, -0.04, 1.70), (0.28, 0.26, 0.26), panel, "Head", bevel=0.04)
    p.box((0, -0.185, 1.70), (0.24, 0.05, 0.20), dark, "Head", bevel=0.015)
    for sx in (-1, 1):
        p.disc((sx * 0.065, -0.215, 1.72), 0.045, 0.03, glass, "Head", axis="Y", n=10)
        p.disc((sx * 0.065, -0.20, 1.72), 0.065, 0.02, metal, "Head", axis="Y", n=10)
    p.disc((0, -0.21, 1.62), 0.03, 0.02, warm, "Head", axis="Y", n=8)
    p.box((0, -0.03, 1.85), (0.30, 0.28, 0.05), snow, "Head", bevel=0.012)
    p.box((0.06, -0.185, 1.85), (0.06, 0.02, 0.06), red, "Head", bevel=0.0)
    p.box((0.06, -0.185, 1.85), (0.16, 0.02, 0.02), red, "Head", bevel=0.0)
    return name, p.build(name, mats), mats


def build_robot_15(arm):
    """Курьер: клиновидный корпус, широкий визор, сумка через плечо."""
    name = "R15_Courier"
    keys = ["base", "panel", "dark", "snow", "metal", "glow", "rubber"]
    mats, M = make_materials(name, keys, colors={
        "base": "#8C3B32", "panel": "#C25A44", "dark": "#1A1512",
        "snow": "#EDE7D8", "metal": "#A9A296", "glow": "#FFD27A",
        "rubber": "#1A1D22",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, glow = M["snow"], M["metal"], M["glow"]
    rubber = M["rubber"]

    p.box((0, 0.01, 0.95), (0.36, 0.28, 0.21), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.40, 0.31, 0.08), panel, "pelvis", bevel=0.02)
    # сумка на правом бедре и ремень через грудь
    p.box((0.26, 0.06, 0.97), (0.16, 0.26, 0.24), snow, "pelvis", bevel=0.03)
    p.box((0.26, -0.075, 0.99), (0.10, 0.02, 0.06), panel, "pelvis", bevel=0.0)
    p.beam((-0.14, -0.16, 1.40), (0.24, -0.12, 1.06), 0.06, 0.03, 0.05, 0.03,
           rubber, "spine_02")

    # клин: узкая талия, широкие плечи
    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.14), (0, -0.02, 1.31), (0, -0.01, 1.47)],
           [(0.17, 0.15), (0.24, 0.20), (0.27, 0.22), (0.21, 0.18)],
           base, "spine_02", n=8)
    p.box((0, -0.215, 1.28), (0.30, 0.05, 0.26), snow, "spine_02", bevel=0.02)
    for sx in (-1, 1):
        p.box((sx * 0.05, -0.245, 1.30), (0.16, 0.02, 0.05), panel, "spine_02",
              bevel=0.0, rot=(0, sx * 22, 0))
    p.box((0, -0.21, 1.10), (0.22, 0.05, 0.06), panel, "spine_02", bevel=0.01)
    # ранец-курьерский тубус
    p.tube([(-0.16, 0.24, 1.24), (0.16, 0.24, 1.24)],
           [(0.10, 0.10), (0.10, 0.10)], panel, "spine_02", n=8)
    p.disc((-0.18, 0.24, 1.24), 0.075, 0.03, snow, "spine_02", axis="X", n=8)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.28, 0.0, 1.46), (0.22, 0.26, 0.09), panel, clav, bevel=0.02,
              rot=(0, sx * 8, 0))

    _arms(p, R, 1, "l", M, upper=(0.15, 0.16, 0.12, 0.13),
          lower=(0.16, 0.17, 0.125, 0.135), hand=(0.14, 0.18, 0.18), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.15, 0.16, 0.12, 0.13),
          lower=(0.16, 0.17, 0.125, 0.135), hand=(0.14, 0.18, 0.18), glove="dark")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.18, 0.19, 0.15, 0.16),
              calf=(0.17, 0.18, 0.14, 0.15), boot=(0.19, 0.27, 0.14),
              sole=(0.20, 0.34, 0.045), toe=(0.18, 0.24, 0.13),
              knee_pad=(0.16, 0.07, 0.14))
        p.box((sx * 0.102, -0.125, 0.34), (0.15, 0.04, 0.26), snow, "calf_" + side,
              bevel=0.012, rot=(-6, 0, 0))

    # обтекаемый шлем с широким визором и гребнем
    p.box((0, -0.05, 1.70), (0.26, 0.28, 0.22), base, "Head", bevel=0.04)
    p.box((0, -0.20, 1.71), (0.28, 0.05, 0.08), dark, "Head", bevel=0.01)
    p.box((0, -0.225, 1.71), (0.24, 0.02, 0.045), glow, "Head", bevel=0.0)
    p.box((0, 0.0, 1.84), (0.06, 0.24, 0.10), panel, "Head", bevel=0.012)
    p.tube([(-0.10, 0.06, 1.80), (-0.14, 0.12, 1.98)],
           [(0.015, 0.015), (0.01, 0.01)], metal, "Head", n=6)
    p.sphere((-0.14, 0.12, 1.99), 0.028, glow, "Head", subdivisions=1)
    return name, p.build(name, mats), mats


# ------------------------------------------------------------------ R16–R20


def build_robot_16(arm):
    """Надзиратель: тяжёлые блоки плеч, решётка вместо лица, дубинка на спине."""
    name = "R16_Warden"
    keys = ["base", "panel", "dark", "snow", "metal", "yellow", "red", "warm"]
    mats, M = make_materials(name, keys, colors={
        "base": "#2E3338", "panel": "#464D54", "dark": "#12151A",
        "snow": "#DDE1E4", "metal": "#9BA3AB", "yellow": "#D89A3A",
        "red": "#B23A2E", "warm": "#FFC061",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, yellow = M["snow"], M["metal"], M["yellow"]
    red, warm = M["red"], M["warm"]

    p.box((0, 0.02, 0.95), (0.46, 0.33, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.50, 0.36, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.19, 1.03), (0.14, 0.05, 0.08), yellow, "pelvis", bevel=0.01)
    for sx in (-1, 1):
        p.box((sx * 0.22, -0.16, 0.97), (0.14, 0.10, 0.16), dark, "pelvis",
              bevel=0.02)

    p.tube([(0, 0.03, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.26, 0.21), (0.31, 0.25), (0.30, 0.24), (0.21, 0.18)],
           base, "spine_02", n=8)
    p.box((0, -0.235, 1.26), (0.40, 0.06, 0.30), panel, "spine_02", bevel=0.03)
    p.box((0.10, -0.275, 1.33), (0.10, 0.02, 0.08), yellow, "spine_02", bevel=0.0)
    _ribs(p, (0, -0.27, 1.18), (0.30, 0.02, 0.03), dark, "spine_02", count=3, gap=0.07)
    # дубинка на спине и катушка
    p.tube([(-0.10, 0.27, 1.10), (-0.10, 0.27, 1.44)],
           [(0.045, 0.045), (0.04, 0.04)], dark, "spine_02", n=8)
    p.box((-0.10, 0.27, 1.48), (0.09, 0.09, 0.10), metal, "spine_02", bevel=0.012)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.35, 0.0, 1.49), (0.34, 0.38, 0.22), panel, clav, bevel=0.035,
              rot=(0, sx * 4, 0))
        p.box((sx * 0.35, -0.01, 1.615), (0.30, 0.34, 0.05), metal, clav,
              bevel=0.012, rot=(0, sx * 4, 0))
        _rivet_ring(p, (sx * 0.35, -0.20, 1.49), 0.10, metal, clav, axis="Y",
                    count=4, size=0.024)

    _arms(p, R, 1, "l", M, upper=(0.21, 0.22, 0.16, 0.17),
          lower=(0.22, 0.23, 0.17, 0.18), hand=(0.19, 0.23, 0.23), glove="dark")
    _arms(p, R, -1, "r", M, upper=(0.21, 0.22, 0.16, 0.17),
          lower=(0.22, 0.23, 0.17, 0.18), hand=(0.19, 0.23, 0.23), glove="dark")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.24, 0.25, 0.20, 0.21),
              calf=(0.23, 0.24, 0.19, 0.20), boot=(0.23, 0.30, 0.16),
              sole=(0.25, 0.40, 0.05), toe=(0.22, 0.27, 0.15),
              knee_pad=(0.21, 0.09, 0.17))
        p.box((sx * 0.102, -0.14, 0.34), (0.17, 0.03, 0.03), red, "calf_" + side,
              bevel=0.0)

    # голова с вертикальной решёткой
    p.box((0, -0.04, 1.69), (0.30, 0.26, 0.24), base, "Head", bevel=0.03)
    p.box((0, -0.185, 1.69), (0.26, 0.04, 0.20), warm, "Head", bevel=0.0)
    for i in range(4):
        p.box((-0.09 + i * 0.06, -0.205, 1.69), (0.03, 0.03, 0.20), dark, "Head",
              bevel=0.0)
    p.box((0, -0.17, 1.82), (0.34, 0.10, 0.05), panel, "Head", bevel=0.012)
    p.box((0, -0.15, 1.57), (0.22, 0.10, 0.05), panel, "Head", bevel=0.01)
    return name, p.build(name, mats), mats


def build_robot_17(arm):
    """Отшельник: деревянные накладки поверх корпуса, мох и ветки на голове."""
    name = "R17_Hermit"
    keys = ["base", "panel", "dark", "snow", "metal", "wood", "moss", "glow",
            "fur"]
    mats, M = make_materials(name, keys, colors={
        "base": "#4A4636", "panel": "#6B5B3E", "dark": "#1A1710",
        "snow": "#CFC6AE", "metal": "#8A8578", "wood": "#5C4229",
        "moss": "#4A5E3C", "glow": "#C9E07A", "fur": "#B9AF95",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, wood = M["snow"], M["metal"], M["wood"]
    moss, glow, fur = M["moss"], M["glow"], M["fur"]

    p.box((0, 0.02, 0.95), (0.42, 0.31, 0.23), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.46, 0.35, 0.09), panel, "pelvis", bevel=0.02)
    # юбка из досок
    for i, x in enumerate((-0.16, -0.05, 0.06, 0.17)):
        p.box((x, -0.16, 0.86), (0.10, 0.05, 0.30), wood, "pelvis", bevel=0.008,
              rot=(0, 0, (i - 1.5) * 2))

    p.tube([(0, 0.03, 0.99), (0, -0.01, 1.13), (0, -0.05, 1.29), (0, -0.06, 1.43)],
           [(0.23, 0.19), (0.28, 0.23), (0.26, 0.22), (0.19, 0.17)],
           base, "spine_02", n=8)
    # дощатая нагрудная накладка
    p.box((0, -0.22, 1.26), (0.38, 0.06, 0.30), wood, "spine_02", bevel=0.015)
    for sx in (-1, 1):
        p.beam((sx * 0.15, -0.25, 1.10), (sx * 0.15, -0.25, 1.40),
               0.05, 0.03, 0.05, 0.03, metal, "spine_02")
    _bolts(p, [(-0.14, -0.255, 1.36), (0.14, -0.255, 1.36),
               (-0.14, -0.255, 1.16), (0.14, -0.255, 1.16)], metal, "spine_02")
    # мох
    for pos in ((0.20, 0.10, 1.45), (-0.18, 0.14, 1.41), (0.0, 0.27, 1.34),
                (0.24, -0.05, 1.52)):
        p.sphere(pos, 0.075, moss, "spine_02", subdivisions=1)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.31, 0.0, 1.47), (0.28, 0.32, 0.10), wood, clav, bevel=0.02,
              rot=(0, sx * 6, 0))
        _bolts(p, [(sx * 0.26, -0.15, 1.47), (sx * 0.36, -0.15, 1.47)], metal, clav)
        p.sphere((sx * 0.30, 0.02, 1.53), 0.07, moss, clav, subdivisions=1)

    _arms(p, R, 1, "l", M, upper=(0.17, 0.18, 0.13, 0.14),
          lower=(0.18, 0.19, 0.14, 0.15), hand=(0.15, 0.19, 0.19), glove="fur")
    _arms(p, R, -1, "r", M, upper=(0.17, 0.18, 0.13, 0.14),
          lower=(0.18, 0.19, 0.14, 0.15), hand=(0.15, 0.19, 0.19), glove="fur")

    # коряга убрана по просьбе владельца

    # вязанка хвороста за спиной
    for i in range(4):
        p.tube([(-0.18 + i * 0.02, 0.26 + (i % 2) * 0.04, 1.12),
                (0.16 + i * 0.02, 0.28 + (i % 2) * 0.04, 1.30)],
               [(0.045, 0.045), (0.04, 0.04)], wood, "spine_02", n=6)
    p.beam((-0.16, 0.24, 1.22), (0.18, 0.26, 1.22), 0.05, 0.04, 0.05, 0.04,
           fur, "spine_02")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.20, 0.21, 0.17, 0.18),
              calf=(0.19, 0.20, 0.16, 0.17), boot=(0.20, 0.27, 0.14),
              sole=(0.21, 0.35, 0.045), toe=(0.19, 0.24, 0.13),
              knee_pad=(0.17, 0.08, 0.15), shin_plate=False)
        for i in range(3):
            p.box((sx * 0.102, 0.0, 0.18 + i * 0.10), (0.19, 0.20, 0.035), fur,
                  "calf_" + side, bevel=0.0)

    # голова: кора вместо лица и ветки вместо рогов
    p.tube([(0, 0.02, 1.43), (0, 0.02, 1.55)], [(0.08, 0.08), (0.075, 0.075)],
           metal, "neck_01", n=8)
    p.box((0, -0.06, 1.69), (0.26, 0.26, 0.24), base, "Head", bevel=0.03)
    p.box((0, -0.17, 1.78), (0.30, 0.10, 0.06), wood, "Head", bevel=0.01)
    p.box((0, -0.17, 1.68), (0.22, 0.05, 0.16), wood, "Head", bevel=0.012)
    for sx in (-1, 1):
        p.disc((sx * 0.06, -0.20, 1.70), 0.035, 0.03, glow, "Head", axis="Y", n=8)
        p.disc((sx * 0.06, -0.19, 1.70), 0.052, 0.02, dark, "Head", axis="Y", n=8)
    p.tube([(0.06, 0.0, 1.80), (0.14, 0.02, 1.96), (0.20, 0.06, 2.04)],
           [(0.026, 0.026), (0.018, 0.018), (0.01, 0.01)], wood, "Head", n=6)
    p.tube([(-0.08, 0.0, 1.80), (-0.16, 0.04, 1.94), (-0.20, 0.10, 1.99)],
           [(0.022, 0.022), (0.014, 0.014), (0.008, 0.008)], wood, "Head", n=6)
    p.tube([(0.0, 0.04, 1.81), (0.02, 0.12, 1.97)],
           [(0.018, 0.018), (0.008, 0.008)], wood, "Head", n=6)
    return name, p.build(name, mats), mats


def build_robot_18(arm):
    """Кузнец: сгорбленный, кожаный фартук, наковальня за спиной, кисти пустые."""
    name = "R18_Forge"
    keys = ["base", "panel", "dark", "snow", "metal", "rust", "glow", "wood",
            "yellow"]
    mats, M = make_materials(name, keys, colors={
        "base": "#3A3330", "panel": "#5A4C42", "dark": "#151210",
        "snow": "#D8CDBB", "metal": "#9A9086", "rust": "#8C4A32",
        "glow": "#FF7A1A", "wood": "#6B4B31", "yellow": "#E8B84B",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, rust = M["snow"], M["metal"], M["rust"]
    glow, wood, yellow = M["glow"], M["wood"], M["yellow"]

    p.box((0, 0.02, 0.95), (0.46, 0.33, 0.24), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.50, 0.36, 0.09), panel, "pelvis", bevel=0.02)
    p.box((0, -0.19, 1.03), (0.14, 0.05, 0.07), yellow, "pelvis", bevel=0.01)
    # кожаный фартук
    p.box((0, -0.21, 0.97), (0.42, 0.06, 0.38), wood, "pelvis", bevel=0.02)
    _bolts(p, [(-0.16, -0.245, 1.12), (0.16, -0.245, 1.12),
               (-0.16, -0.245, 0.84), (0.16, -0.245, 0.84)], metal, "pelvis")

    p.tube([(0, 0.03, 0.99), (0, -0.02, 1.14), (0, -0.06, 1.30), (0, -0.05, 1.44)],
           [(0.26, 0.22), (0.32, 0.26), (0.30, 0.25), (0.22, 0.19)],
           base, "spine_02", n=8)
    p.box((0, -0.245, 1.30), (0.38, 0.06, 0.24), panel, "spine_02", bevel=0.025)
    p.box((0, -0.285, 1.30), (0.22, 0.03, 0.10), dark, "spine_02", bevel=0.0)
    for i in range(2):
        p.box((0, -0.30, 1.27 + i * 0.06), (0.20, 0.02, 0.025), glow, "spine_02",
              bevel=0.0)
    # наковальня за спиной
    p.box((0, 0.30, 1.26), (0.38, 0.22, 0.18), metal, "spine_02", bevel=0.02)
    p.box((0, 0.30, 1.37), (0.24, 0.18, 0.06), metal, "spine_02", bevel=0.012)
    p.tube([(0.18, 0.30, 1.28), (0.34, 0.30, 1.24), (0.40, 0.30, 1.20)],
           [(0.06, 0.06), (0.05, 0.05), (0.02, 0.02)], metal, "spine_02", n=6)
    p.box((0, 0.30, 1.12), (0.26, 0.20, 0.10), rust, "spine_02", bevel=0.015)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        width = 0.30
        p.box((sx * 0.33, 0.0, 1.47), (width, 0.34, 0.15), panel, clav, bevel=0.035,
              rot=(0, sx * 7, 0))
        p.box((sx * 0.33, -0.01, 1.555), (width * 0.85, 0.30, 0.05), metal, clav,
              bevel=0.012, rot=(0, sx * 7, 0))
        _hazard(p, (sx * 0.33, -0.19, 1.46), (width * 0.6, 0.02, 0.03), yellow,
                clav, count=2, gap=0.06)

    # Обе руки одинаковы: правая раньше была молотом, теперь это кисть левой руки.
    _arms(p, R, 1, "l", M, upper=(0.22, 0.23, 0.17, 0.18),
          lower=(0.23, 0.24, 0.18, 0.19), hand=(0.18, 0.22, 0.22), glove="wood")
    _arms(p, R, -1, "r", M, upper=(0.22, 0.23, 0.17, 0.18),
          lower=(0.23, 0.24, 0.18, 0.19), hand=(0.18, 0.22, 0.22), glove="wood")

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.26, 0.27, 0.22, 0.23),
              calf=(0.25, 0.26, 0.21, 0.22), boot=(0.24, 0.31, 0.16),
              sole=(0.26, 0.42, 0.05), toe=(0.23, 0.28, 0.15),
              knee_pad=(0.22, 0.09, 0.18))
        p.box((sx * 0.102, -0.14, 0.28), (0.19, 0.04, 0.26), rust, "calf_" + side,
              bevel=0.012)

    # голова: низкая, с широкой смотровой щелью
    p.tube([(0, 0.02, 1.42), (0, 0.02, 1.54)], [(0.10, 0.10), (0.09, 0.09)],
           metal, "neck_01", n=8)
    p.box((0, -0.06, 1.66), (0.28, 0.26, 0.22), base, "Head", bevel=0.03)
    p.box((0, -0.19, 1.67), (0.26, 0.05, 0.08), dark, "Head", bevel=0.008)
    p.box((0, -0.21, 1.67), (0.22, 0.02, 0.035), glow, "Head", bevel=0.0)
    p.box((0, -0.15, 1.75), (0.30, 0.10, 0.06), metal, "Head", bevel=0.012)
    return name, p.build(name, mats), mats


def build_robot_19(arm):
    """Зимний рыбак: водолазный шлем с иллюминатором, плащ и ведро на бедре."""
    name = "R19_Tide"
    keys = ["base", "panel", "dark", "snow", "metal", "rope", "warm", "glass",
            "fur"]
    mats, M = make_materials(name, keys, colors={
        "base": "#2E4A52", "panel": "#476A73", "dark": "#142126",
        "snow": "#DCD3BE", "metal": "#93A0A6", "rope": "#A98B5F",
        "warm": "#FFD79A", "glass": "#CFEAF2", "fur": "#C9BFA8",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, rope = M["snow"], M["metal"], M["rope"]
    warm, glass, fur = M["warm"], M["glass"], M["fur"]

    p.box((0, 0.01, 0.95), (0.40, 0.30, 0.22), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.44, 0.33, 0.08), panel, "pelvis", bevel=0.02)
    # плащ-дождевик
    p.box((0, 0.15, 0.80), (0.34, 0.05, 0.42), panel, "pelvis", bevel=0.015)
    for sx in (-1, 1):
        p.box((sx * 0.18, 0.0, 0.80), (0.05, 0.24, 0.42), base, "pelvis",
              bevel=0.012)
        p.box((sx * 0.09, -0.15, 0.82), (0.15, 0.05, 0.40), base, "pelvis",
              bevel=0.012)
    p.tube([(0, 0.005, 1.42), (0, 0.0, 1.50)], [(0.21, 0.19), (0.24, 0.22)],
           fur, "spine_03", n=10)
    # спасательные круги на бёдрах
    for sx in (-1, 1):
        p.disc((sx * 0.24, 0.0, 0.97), 0.10, 0.10, rope, "pelvis", axis="X", n=10)
    # ведро на правом бедре
    p.box((-0.26, 0.04, 0.84), (0.20, 0.20, 0.24), metal, "pelvis", bevel=0.02)
    p.tube([(-0.36, 0.04, 0.96), (-0.16, 0.04, 0.96)],
           [(0.02, 0.02), (0.02, 0.02)], metal, "pelvis", n=6)

    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.22, 0.18), (0.27, 0.22), (0.26, 0.21), (0.19, 0.17)],
           base, "spine_02", n=8)
    p.box((0, -0.215, 1.24), (0.32, 0.06, 0.30), panel, "spine_02", bevel=0.025)
    for i in range(2):
        p.box((0, -0.25, 1.32 - i * 0.14), (0.24, 0.03, 0.04), dark, "spine_02",
              bevel=0.0)
    # сетка и поплавки за спиной
    p.box((0, 0.22, 1.22), (0.30, 0.16, 0.30), panel, "spine_02", bevel=0.03)
    for i in range(3):
        p.sphere((-0.10 + i * 0.10, 0.31, 1.34), 0.055, warm, "spine_02",
                 subdivisions=1)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.29, 0.0, 1.46), (0.24, 0.28, 0.10), panel, clav, bevel=0.025,
              rot=(0, sx * 7, 0))

    _arms(p, R, 1, "l", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="rope")
    _arms(p, R, -1, "r", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="rope")

    # удочка убрана по просьбе владельца

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.21, 0.22, 0.18, 0.19),
              calf=(0.20, 0.21, 0.17, 0.18), boot=(0.22, 0.30, 0.16),
              sole=(0.24, 0.40, 0.05), toe=(0.21, 0.27, 0.15),
              knee_pad=(0.19, 0.08, 0.16))
        for i in range(3):
            p.box((sx * 0.102, -0.10 + i * 0.09, 0.03), (0.20, 0.03, 0.04),
                  metal, "foot_" + side, bevel=0.0)

    # водолазный шлем с иллюминатором
    p.tube([(0, 0.02, 1.43), (0, 0.02, 1.57)], [(0.11, 0.11), (0.10, 0.10)],
           metal, "neck_01", n=8)
    p.tube([(0, -0.01, 1.57), (0, -0.01, 1.80)], [(0.20, 0.19), (0.19, 0.18)],
           panel, "Head", n=10)
    p.tube([(0, -0.01, 1.80), (0, -0.01, 1.88)],
           [(0.19, 0.18), (0.10, 0.10)], panel, "Head", n=10)
    p.disc((0, -0.20, 1.69), 0.075, 0.03, glass, "Head", axis="Y", n=12)
    p.disc((0, -0.19, 1.69), 0.11, 0.04, metal, "Head", axis="Y", n=12)
    _rivet_ring(p, (0, -0.20, 1.69), 0.145, metal, "Head", axis="Y", count=8,
                size=0.026)
    for sx in (-1, 1):
        p.box((sx * 0.19, -0.02, 1.66), (0.04, 0.10, 0.16), metal, "Head",
              bevel=0.008)
    p.tube([(0.10, 0.02, 1.86), (0.14, 0.02, 1.96)],
           [(0.03, 0.03), (0.02, 0.02)], metal, "Head", n=6)
    return name, p.build(name, mats), mats


def build_robot_20(arm):
    """Часовщик: латунь, открытые шестерни в груди, три объектива и заводная пружина."""
    name = "R20_Cogsmith"
    keys = ["base", "panel", "dark", "snow", "metal", "brass", "copper", "glow",
            "glass"]
    mats, M = make_materials(name, keys, colors={
        "base": "#5A4632", "panel": "#7A6248", "dark": "#1C1712",
        "snow": "#DCD3BE", "metal": "#9A9086", "brass": "#B08A4A",
        "copper": "#A9673F", "glow": "#FFC061", "glass": "#CFEAF2",
    })
    R = rig_points(arm)
    p = PartMesh()
    base, panel, dark = M["base"], M["panel"], M["dark"]
    snow, metal, brass = M["snow"], M["metal"], M["brass"]
    copper, glow, glass = M["copper"], M["glow"], M["glass"]

    p.box((0, 0.01, 0.95), (0.38, 0.29, 0.22), dark, "pelvis", bevel=0.04)
    p.box((0, 0.0, 1.03), (0.42, 0.32, 0.08), panel, "pelvis", bevel=0.02)
    for sx in (-1, 1):
        p.disc((sx * 0.14, -0.18, 1.03), 0.05, 0.03, brass, "pelvis", axis="Y",
               n=10)

    p.tube([(0, 0.02, 0.99), (0, 0.0, 1.14), (0, -0.01, 1.31), (0, 0.0, 1.47)],
           [(0.21, 0.18), (0.26, 0.21), (0.25, 0.20), (0.18, 0.16)],
           base, "spine_02", n=8)
    # окно с шестернями
    p.box((0, -0.215, 1.26), (0.28, 0.05, 0.26), dark, "spine_02", bevel=0.015)
    _gear(p, (0.07, -0.245, 1.26), 0.085, 0.05, brass, "spine_02", axis="Y",
          teeth=8)
    _gear(p, (-0.07, -0.245, 1.20), 0.055, 0.045, copper, "spine_02", axis="Y",
          teeth=6)
    p.box((0, -0.19, 1.42), (0.28, 0.05, 0.06), panel, "spine_02", bevel=0.01)
    # заводная пружина на спине
    for i in range(4):
        p.disc((0, 0.24, 1.14 + i * 0.07), 0.10 - i * 0.008, 0.03, brass,
               "spine_02", axis="Z", n=12)
    p.tube([(0, 0.24, 1.12), (0, 0.24, 1.44)], [(0.025, 0.025)] * 2, metal,
           "spine_02", n=6)
    p.sphere((0, 0.24, 1.46), 0.05, brass, "spine_02", subdivisions=1)

    for sx, clav in ((1, "clavicle_l"), (-1, "clavicle_r")):
        p.box((sx * 0.29, 0.0, 1.46), (0.24, 0.28, 0.10), brass, clav, bevel=0.02,
              rot=(0, sx * 7, 0))
    _gear(p, (0.30, -0.12, 1.47), 0.09, 0.04, copper, "clavicle_l", axis="Y",
          teeth=8)

    _arms(p, R, 1, "l", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="brass")
    _arms(p, R, -1, "r", M, upper=(0.18, 0.19, 0.14, 0.15),
          lower=(0.19, 0.20, 0.15, 0.16), hand=(0.16, 0.20, 0.20), glove="brass")

    # блок шестерён на левом предплечье
    _gear(p, (0.88, -0.10, 1.44), 0.10, 0.05, brass, "lowerarm_l", axis="Y",
          teeth=10)
    _gear(p, (1.00, -0.12, 1.38), 0.06, 0.04, copper, "lowerarm_l", axis="Y",
          teeth=6)

    for sx, side in ((1, "l"), (-1, "r")):
        _legs(p, R, sx, side, M, thigh=(0.21, 0.22, 0.18, 0.19),
              calf=(0.20, 0.21, 0.17, 0.18), boot=(0.21, 0.28, 0.15),
              sole=(0.22, 0.36, 0.05), toe=(0.20, 0.25, 0.14),
              knee_pad=(0.19, 0.08, 0.16))
        p.tube([(sx * 0.19, 0.02, 0.50), (sx * 0.19, 0.02, 0.88)],
               [(0.035, 0.035), (0.03, 0.03)], brass, "calf_" + side, n=6)
        p.disc((sx * 0.19, 0.02, 0.46), 0.05, 0.04, copper, "calf_" + side,
               axis="Z", n=8)

    # голова: три объектива и латунная корона
    p.tube([(0, 0.02, 1.44), (0, 0.02, 1.56)], [(0.08, 0.08), (0.075, 0.075)],
           metal, "neck_01", n=8)
    p.box((0, -0.04, 1.70), (0.28, 0.26, 0.24), base, "Head", bevel=0.035)
    p.box((0, -0.18, 1.70), (0.26, 0.05, 0.20), brass, "Head", bevel=0.012)
    p.disc((0, -0.215, 1.72), 0.06, 0.03, glass, "Head", axis="Y", n=12)
    p.disc((0, -0.20, 1.72), 0.085, 0.025, brass, "Head", axis="Y", n=12)
    for sx in (-1, 1):
        p.disc((sx * 0.09, -0.21, 1.68), 0.035, 0.025, glass, "Head", axis="Y",
               n=10)
        p.disc((sx * 0.09, -0.20, 1.68), 0.05, 0.02, brass, "Head", axis="Y", n=10)
    p.box((0, -0.02, 1.83), (0.30, 0.28, 0.05), brass, "Head", bevel=0.012)
    for i in range(5):
        a = math.tau * i / 5.0
        p.box((math.cos(a) * 0.12, -0.02 + math.sin(a) * 0.11, 1.86),
              (0.05, 0.05, 0.07), brass, "Head", bevel=0.0,
              rot=(0, 0, math.degrees(a)))
    p.sphere((0, -0.02, 1.90), 0.04, glow, "Head", subdivisions=1)
    return name, p.build(name, mats), mats


BUILDERS = [build_robot_01, build_robot_02, build_robot_03,
            build_robot_04, build_robot_05, build_robot_06,
            build_robot_07, build_robot_08, build_robot_09,
            build_robot_10, build_robot_11, build_robot_12,
            build_robot_13, build_robot_14, build_robot_15,
            build_robot_16, build_robot_17, build_robot_18,
            build_robot_19, build_robot_20]
