"""Диагностика двора депо: где пол, где постель, где головка рельса.

Сравнивает по раскладке: отметку стола магистрали, пол двора (площадка,
прорезанная коридором пути) и уровни бокового пути (постель, шпалы, головка
рельса). Расхождение больше 6 см — это и есть зазор, который проверка
копланарности ловит.

Запуск:
    python Tools/Blender/diag_depot_yard.py
"""

from __future__ import annotations

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import depot_parts as DP  # noqa: E402
import frontier_layout as FL  # noqa: E402
import world_layout as W  # noqa: E402


def main() -> int:
    rail = None
    for ln in W.TRANSPORT:
        if ln["kind"] == "rail":
            rail = ln
    fine = DP.siding_line()
    print("стол магистрали, пол двора и уровни бокового пути")
    print("%-26s %8s %8s %8s %8s %8s %8s"
          % ("точка двора", "стол", "пол двора", "постель", "шпала", "головка",
             "постель−пол"))
    worst = 0.0
    for k in range(0, len(fine), max(1, len(fine) // 12)):
        (lx, lz) = fine[k]
        wx = W.DEPOT[0] + lx
        wz = W.DEPOT[1] + lz
        d, s, _dir = W.project_to_line(wx, wz, rail["pts"])
        table = W.level_at(rail["table"], s)
        ground = W.world_height(wx, wz)
        bed = table + FL.BED_TOP_Y
        sleeper = table + FL.SLEEPER_TOP_Y
        head = table + FL.RAIL_HEAD_Y
        gap = bed - ground
        if abs(gap) > abs(worst):
            worst = gap
        print("%-26s %8.3f %8.3f %8.3f %8.3f %8.3f %8.3f"
              % ("(%.0f, %.0f)" % (wx, wz), table, ground, bed, sleeper, head, gap))
    print()
    print("худшее расхождение постель↔пол двора: %.3f м" % worst)
    print("уровни бокового пути: постель %+.2f, шпала %+.2f, головка %+.2f (от стола)"
          % (FL.BED_TOP_Y, FL.SLEEPER_TOP_Y, FL.RAIL_HEAD_Y))
    return 0


if __name__ == "__main__":
    sys.exit(main())
