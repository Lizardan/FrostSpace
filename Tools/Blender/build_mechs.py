"""Собирает ездовых ходунов FrostSpace на ригах Ultimate Space Kit.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_mechs.py -- FrogWalker
    blender --background --factory-startup --python Tools/Blender/build_mechs.py

Без аргументов собираются все юниты из `mech_lib.MECHS`. Выход —
`Assets/_Project/Content/Models/Mechs/ME_<Unit>.fbx` плюс регистр
`Tools/Blender/mech_manifest.json`: длины клипов, скорости Walk/Run, габарит,
точки посадки и объём кабины. По манифесту живут постпроцессор Unity, префабы
и EditMode-тест, поэтому числа не разъезжаются.

Корпус строит `mech_designs`, риг и клипы готовит `mech_lib`; пак Quaternius
остаётся в `AssetSource/` и в Unity не импортируется (ADR 0024).
"""

from __future__ import annotations

import json
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import animal_lib  # noqa: E402
import mech_designs  # noqa: E402
import mech_lib  # noqa: E402
import robot_lib  # noqa: E402

MANIFEST = os.path.join(HERE, "mech_manifest.json")


def build(unit: str) -> dict:
    arm, meta = mech_lib.load(unit)
    # Стойка выпрямляется в rest до клипов: дальше всё меряется по фактическим
    # длинам ноги, а idle пака остаётся валидным — клип это вращения костей.
    mech_lib.straighten_stance(arm)
    mech_lib.prepare_clips(arm, meta)
    # Походка печётся заново и до замера скорости: скорость земли в клипе
    # считается по фактическому ходу стопы в стойке.
    mech_lib.author_gait(arm, meta)
    mech_lib.flatten_root(arm, meta)
    mech_lib.measure_speed(arm, meta)

    name, obj, _mats, cockpit = mech_designs.build(unit, arm, meta)
    robot_lib.bind(obj, arm)
    # Тело ходуна строится в системе арматуры, поэтому трансформ обнуляется до
    # экспорта: иначе bind-матрицы в Unity считаются от разных систем (ADR 0022).
    animal_lib.bake_mesh_transform(obj)

    stats = robot_lib.stats(obj, arm)

    path = os.path.join(mech_lib.OUT, name + ".fbx")
    robot_lib.export_fbx(arm, obj, path)

    bbox = stats["bbox"]
    return {
        "unit": unit,
        "name": name,
        "path": path,
        "verts": stats["verts"],
        "tris": stats["tris"],
        # Рост — по факту меша, а не целевой: голова у ходуна убрана (ADR 0024),
        # и корпус стал ниже цели. По этому числу строится капсула в префабе.
        "height": round(bbox[2][1] - bbox[2][0], 3),
        "rig": meta["rig"],
        "source": meta["cfg"]["source"],
        "source_size": meta["source_size"],
        "size": [round(bbox[axis][1] - bbox[axis][0], 3) for axis in range(3)],
        "clips": {key: value["frames"] for key, value in meta["clips"].items()},
        "clip_seconds": {key: value["seconds"]
                         for key, value in meta["clips"].items()},
        "clip_speed": meta["clip_speed"],
        "root_drift": meta["root_drift"],
        "root": meta["roles"]["root"],
        "stance": meta["cfg"].get("stance"),
        "cockpit": {
            "seat": [round(v, 3) for v in cockpit["seat"]],
            "seat_point": [round(v, 3) for v in cockpit["seat_point"]],
            "grips": round(cockpit["grips"], 3),
            "grip": [round(v, 3) for v in cockpit["grip"]],
            "foot": [round(v, 3) for v in cockpit["foot"]],
            "foot_half": round(cockpit["foot_half"], 3),
            "cavity": [round(v, 3) for v in cockpit["cavity"]],
        },
        "layout": meta["layout"],
    }


def main() -> int:
    robot_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    selected = {a.lower() for a in args if not a.startswith("--")}
    units = [u for u in mech_designs.UNITS
             if not selected or u.lower() in selected]
    if not units:
        raise RuntimeError("ни один ходун не выбран: %s" % args)

    results = []
    for unit in units:
        try:
            results.append(build(unit))
        except Exception:  # noqa: BLE001
            print("FAILED", unit)
            traceback.print_exc()

    print("\nSUMMARY")
    for item in results:
        print("%-15s verts=%-5d tris=%-5d height=%.2f clips=%s speed=%s" % (
            item["unit"], item["verts"], item["tris"], item["height"],
            item["clips"], item["clip_speed"]))

    if results:
        previous = {}
        if os.path.exists(MANIFEST):
            with open(MANIFEST, encoding="utf-8") as handle:
                previous = {entry["unit"]: entry for entry in json.load(handle)}
        for item in results:
            previous[item["unit"]] = item
        ordered = [previous[unit] for unit in mech_designs.UNITS
                   if unit in previous]
        with open(MANIFEST, "w", encoding="utf-8") as handle:
            json.dump(ordered, handle, ensure_ascii=False, indent=2)
        print("MANIFEST", MANIFEST)

    if len(results) != len(units):
        return 1
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
