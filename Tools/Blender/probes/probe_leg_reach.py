"""Запас вылета ноги: какой шаг вообще решается IK для данного рига и профиля.

Зачем: `travel * stance` — длина шага, и она упирается не в тайминг, а в
геометрию. Бедро стоит позади стопы, поэтому максимум выноса считается как

    span_max = 2 * (sqrt(reach^2 - cz^2) - dy0)

где `reach = leg_reach * (UpperLeg + LowerLeg)`, `cz` — высота бедра над
головой стопы в момент касания (то есть минус `bob`, который в этот момент
максимален), `dy0` — насколько бедро стоит позади стопы в rest. Если шаг
больше `span_max`, IK зажимает цель в стойке, и подошва едет по снегу —
ровно то, что показывает счётчик зажимов в `mech_lib.author_gait`.

Запуск (после `--` можно перечислить юнитов):

    blender --background --factory-startup --python probe_leg_reach.py -- FlamingoWalker

Печатает по юниту: длины костей ноги, `cz`, `dy0` и сравнение текущего
`travel * stance` с пределом. Запас 8 % — рабочий, потому что в клипе к `cz`
добавляются наклон и крен корпуса.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import bpy  # noqa: E402

import mech_lib  # noqa: E402

#: Доля предела, которую разрешено занимать шагом.
MARGIN = 0.92


def probe(unit: str) -> None:
    arm, meta = mech_lib.load(unit)
    style = mech_lib.gait_style(meta)
    reach_scale = float(style.get("leg_reach", mech_lib.LEG_REACH))
    mech_lib.straighten_stance(arm, style["knee_bulge"])
    print("\n=== %s style=%s bulge=%.2f reach=%.3f" % (
        unit, meta["cfg"].get("gait", mech_lib.DEFAULT_GAIT),
        style["knee_bulge"], reach_scale))
    for side in ("L", "R"):
        upper = arm.data.bones.get("UpperLeg." + side)
        lower = arm.data.bones.get("LowerLeg." + side)
        foot = arm.data.bones.get("Foot." + side)
        if upper is None or lower is None or foot is None:
            continue
        first = (upper.tail_local - upper.head_local).length
        second = (lower.tail_local - lower.head_local).length
        hip = upper.head_local
        # Цель IK — голова кости стопы, а не хвост голени: стопа в этом риге
        # отдельная кость без родителя, и её rest не совпадает с концом голени.
        ankle = foot.head_local
        reach = reach_scale * (first + second)
        dy0 = hip.y - ankle.y
        print("%s: бедро %.3f голень %.3f сумма %.3f вылет %.3f dy0 %.3f"
              % (side, first, second, first + second, reach, dy0))
        for key in ("idle", "walk", "run"):
            cfg = style[key]
            span = cfg["travel"] * cfg["stance"]
            cz = hip.z - ankle.z - cfg["bob"]
            if reach <= cz:
                print("   %-4s: недостижимо (нога короче высоты бедра)" % key)
                continue
            limit = 2.0 * (math.sqrt(reach * reach - cz * cz) - dy0)
            verdict = "OK" if span <= limit * MARGIN else "ЗАЖИМ"
            print("   %-4s: travel %.2f stance %.2f шаг %.3f, предел %.3f, "
                  "с запасом %.3f → %s"
                  % (key, cfg["travel"], cfg["stance"], span, limit,
                     limit * MARGIN, verdict))


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    units = args or list(mech_lib.MECHS)
    for unit in units:
        try:
            probe(unit)
        except Exception:  # noqa: BLE001
            print("FAILED", unit)
            traceback.print_exc()
    return 0


if __name__ == "__main__":
    sys.exit(main())
