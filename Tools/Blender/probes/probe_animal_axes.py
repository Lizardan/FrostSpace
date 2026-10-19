"""Оси ригов животных: куда смотрит корпус и кость головы.

Нужно, чтобы срез головы и посадка черепа не зависели от того, как автор
рига повернул модель: у рыб и птиц «вперёд» может быть не -Y.

Печатает для каждого вида: габарит меша, кости body/neck/head (голова и
хвост), направление головы, горизонтальную проекцию и угол к горизонту.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_axes.py -- Eagle Piranha Wolf
"""

from __future__ import annotations

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, ".."))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import animal_lib  # noqa: E402
import robot_lib  # noqa: E402


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def main() -> int:
    configure_stdout()
    robot_lib.configure_stdout()
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    species = args or list(animal_lib.SPECIES)
    for item in species:
        arm, mesh, meta = animal_lib.load(item)
        roles = meta["roles"]
        R = robot_lib.rig_points(arm)
        mw = mesh.matrix_world
        points = [mw @ v.co for v in mesh.data.vertices]
        lo = Vector((min(p.x for p in points), min(p.y for p in points),
                     min(p.z for p in points)))
        hi = Vector((max(p.x for p in points), max(p.y for p in points),
                     max(p.z for p in points)))
        print("SPECIES %s mesh_size=%s" % (
            item, tuple(round(v, 3) for v in (hi - lo))))
        for role in ("body", "neck", "head"):
            name = roles.get(role)
            if name and name in R:
                a, b = R[name]
                d = (b - a).normalized()
                print("  %-5s %-12s head=%s tail=%s dir=%s len=%.3f" % (
                    role, name,
                    tuple(round(v, 2) for v in a),
                    tuple(round(v, 2) for v in b),
                    tuple(round(v, 2) for v in d),
                    (b - a).length))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
