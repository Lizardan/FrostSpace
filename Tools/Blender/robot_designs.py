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
    _arms(p, R, 1, "l", M, upper=(0.20, 0.21, 0.15, 0.155), lower=(0.21, 0.22, 0.16, 0.165), hand=(0.18, 0.22, 0.22), glove="dark")
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
    """Кочегар: сгорбленный корпус, котёл выше плеч и крюк вместо кисти."""
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
    _arms(p, R, 1, "l", M, upper=(0.20, 0.22, 0.16, 0.17), lower=(0.22, 0.23, 0.17, 0.18), hand=None, glove="metal")
    _arms(p, R, -1, "r", M, upper=(0.22, 0.24, 0.18, 0.19), lower=(0.24, 0.25, 0.19, 0.20), hand=(0.22, 0.26, 0.26), glove="orange")
    wrist_l = R["hand_l"][0]
    p.tube([(wrist_l.x + 0.02, wrist_l.y, wrist_l.z), (wrist_l.x + 0.14, wrist_l.y, wrist_l.z - 0.02),
            (wrist_l.x + 0.20, wrist_l.y, wrist_l.z - 0.16), (wrist_l.x + 0.16, wrist_l.y, wrist_l.z - 0.28)],
           [(0.055, 0.055), (0.05, 0.05), (0.045, 0.045), (0.035, 0.035)], metal, "hand_l", n=6)
    p.box((wrist_l.x + 0.20, wrist_l.y, wrist_l.z - 0.10), (0.10, 0.10, 0.10), yellow, "hand_l", bevel=0.02)
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
    """Смотритель: длинное пальто, меховая накидка, прожектор и ледоруб."""
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
    # ледоруб в правой руке
    wrist_r = R["hand_r"][0]
    p.box((wrist_r.x - 0.03, wrist_r.y, wrist_r.z - 0.25), (0.05, 0.06, 0.62), metal, "hand_r", bevel=0.015)
    p.box((wrist_r.x - 0.03, wrist_r.y, wrist_r.z + 0.075), (0.05, 0.10, 0.12), metal, "hand_r", bevel=0.012)
    p.box((wrist_r.x - 0.10, wrist_r.y, wrist_r.z + 0.10), (0.16, 0.035, 0.06), metal, "hand_r", bevel=0.008, rot=(0, 18, 0))
    p.box((wrist_r.x - 0.03, wrist_r.y, wrist_r.z - 0.58), (0.055, 0.07, 0.06), orange, "hand_r", bevel=0.008)
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


BUILDERS = [build_robot_01, build_robot_02, build_robot_03,
            build_robot_04, build_robot_05]
