"""Пересчёт палитр роботов под игровой свет.

Правило читаемости (2026-10-18): в игре робот рендерится примерно вдвое темнее
своего альбедо (видимая сторона освещена частично, снег занимает верх диапазона),
поэтому альбедо поднимается по светлоте без изменения оттенка:

    множитель = 1 + 0.85 * (1 - V)   # тёмное +55-75 %, светлое почти не трогается
    V' = V * множитель
    S' = S ** 0.80                   # компенсация потери насыщенности при подъёме V

Множитель затухает к 1 по мере приближения к белому: снег, мех и акценты
сохраняют роль, подъём достаётся корпусу и тёмной подложке. Оттенок H не
меняется, поэтому опознавательный цвет робота сохраняется.

Запуск: python Tools/Blender/robot_palette_lift.py
"""

from __future__ import annotations

import colorsys
import os
import sys

LIFT_GAIN = 0.85
S_POWER = 0.80

PALETTES = {
    "общие ключи": {
        "base": "#232C3C", "panel": "#35445A", "dark": "#111722",
        "snow": "#E9EFF5", "fur": "#F6F8FA", "orange": "#F08A2E",
        "glow": "#FF7A1A", "metal": "#8B96A5", "rust": "#8C4A32",
        "cyan": "#6FD6E8", "yellow": "#E8B84B",
    },
    "R1_PolarMechanic": {
        "base": "#2A3B52", "panel": "#435A75", "dark": "#101722",
        "snow": "#DDE5EE", "fur": "#F2F5F8", "orange": "#F08A2E",
        "glow": "#FF8A2A", "metal": "#94A1B2",
    },
    "R2_Furnace": {
        "base": "#3A2B24", "panel": "#6A4734", "dark": "#16100D",
        "snow": "#D8C3A8", "orange": "#E07A2C", "glow": "#FFAE4D",
        "metal": "#A98363", "rust": "#8C4A32", "yellow": "#E8B84B",
    },
    "R3_Scout": {
        "base": "#1D3944", "panel": "#2D5A68", "dark": "#0C1C22",
        "snow": "#CFE7EC", "fur": "#EDF7FA", "orange": "#F2A33C",
        "cyan": "#7FE3EF", "metal": "#93A9B4",
    },
    "R4_Hauler": {
        "base": "#3B3A2B", "panel": "#6B6545", "dark": "#17160F",
        "snow": "#D9D2B8", "rust": "#8C5A34", "orange": "#E0A33A",
        "metal": "#9A9878", "yellow": "#E6B94A",
    },
    "R5_Ranger": {
        "base": "#26382F", "panel": "#3E5849", "dark": "#0F1A14",
        "snow": "#E7E1D1", "fur": "#EDE6D6", "orange": "#D9A441",
        "cyan": "#CFEFE2", "metal": "#9AA89C",
    },
}


def lift(hex_value: str) -> str:
    value = hex_value.lstrip("#")
    rgb = [int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    h, s, v = colorsys.rgb_to_hsv(*rgb)
    v = min(1.0, v * (1.0 + LIFT_GAIN * (1.0 - v)))
    s = s ** S_POWER
    out = colorsys.hsv_to_rgb(h, s, v)
    return "#%02X%02X%02X" % tuple(int(round(c * 255)) for c in out)


def main() -> int:
    print("правило: V' = V*(1+%.2f*(1-V)), S' = S^%.2f\n" % (LIFT_GAIN, S_POWER))
    for robot, colors in PALETTES.items():
        print("== %s" % robot)
        for key, hex_value in colors.items():
            new = lift(hex_value)
            if new == hex_value:
                continue
            print("   %-7s %s -> %s" % (key, hex_value, new))
    return 0


if __name__ == "__main__":
    sys.exit(main())
