"""Диагностика дорог: где полотно уходит под землю.

Проверяет каждую дорожную линию раскладки:
- на сколько земля выше полотна дороги (дорога закопана);
- на каком расстоянии дорога идёт от оси пути;
- кто владеет выемкой в этой точке (путь или дорога).

Запуск:
    python Tools/Blender/diag_roads.py
"""

from __future__ import annotations

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import world_layout as W  # noqa: E402


def main() -> int:
    # Именно `W.TRANSPORT`, а не свежий `_transport_lines()`: привязка столов к
    # дворам (`anchor_tables`) правит таблицы на месте, и новый список линий
    # вернул бы непривязанные профили — диагностика тогда видит дорогу на
    # метрах ниже, чем она есть на самом деле.
    lines = W.TRANSPORT
    rail = lines[0]
    rpts = rail["pts"]
    print("RAIL_CUT: %s  ROAD_CUT: %s  CUT_DEPTH: %s"
          % (W.RAIL_CUT, W.ROAD_CUT, W.CUT_DEPTH))
    print()
    for ln in lines[1:]:
        pts = ln["pts"]
        table = ln["table"]
        s = 0.0
        total = 0
        buried = 0
        worst = 0.0
        worst_at = None
        near_rail = 0
        owner_rail = 0
        for k in range(len(pts) - 1):
            ax, az = pts[k][0], pts[k][1]
            s0 = s
            s += math.dist((ax, az), pts[k + 1][:2])
            lvl = W.level_at(table, s0)
            ground = W.world_height(ax, az)
            d = min(math.dist((ax, az), (rpts[q][0], rpts[q][1]))
                    for q in range(len(rpts)))
            total += 1
            if d < 30.0:
                near_rail += 1
            f = W.transport_floor(ax, az)
            if f is not None and f["kind"] == "rail":
                owner_rail += 1
            diff = ground - lvl
            if diff > 0.02:
                buried += 1
                if diff > worst:
                    worst = diff
                    worst_at = (round(ax, 1), round(az, 1), round(lvl, 2),
                                round(ground, 2), round(d, 1))
        print("%s: точек %d" % (ln["k"], total))
        print("   в путевом коридоре (<30 м от оси пути): %d" % near_rail)
        print("   выемку режет путь, а не дорога: %d" % owner_rail)
        print("   полотно дороги ниже земли: %d точек, хуже всего на %.2f м"
              % (buried, worst))
        if worst_at:
            print("   хуже всего в (%s, %s): полотно %.2f, земля %.2f, до оси пути %.1f м"
                  % worst_at)
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
