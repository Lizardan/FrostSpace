"""Где у меша пака подошва и с какой скоростью она едет по земле.

Запуск:
    blender --background --factory-startup --python probe_mech_sole.py -- Mech_FinnTheFrog walk

Считает по кадрам ОЦЕНЁННЫЙ меш: вершины в пределах 6 см от пола, отдельно для
левой (x>0) и правой половины, и печатает их средний Y и минимальный Z. Это
единственный надёжный ответ на вопрос «какая кость двигает стопу»: кость `Foot`
у пака — IK-цель со своими ключами, и по её позиции скорость клипа не считается.
"""

from __future__ import annotations

import os
import sys
import zipfile

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PACK = os.path.join(
    ROOT, "AssetSource",
    "Ultimate Space Kit - March 2023-20261024T221803Z-1-001.zip")
MEMBER = "Ultimate Space Kit - March 2023/Characters/FBX/%s.fbx"
STAGING = os.path.join(ROOT, "AssetSource", "_staging", "space_kit")


def extract(name: str) -> str:
    os.makedirs(STAGING, exist_ok=True)
    out = os.path.join(STAGING, name + ".fbx")
    if not os.path.exists(out):
        with zipfile.ZipFile(PACK) as archive:
            with open(out, "wb") as handle:
                handle.write(archive.read(MEMBER % name))
    return out


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass

    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    name = args[0] if args else "Mech_FinnTheFrog"
    clip = (args[1] if len(args) > 1 else "walk").lower()

    scene = bpy.context.scene
    scene.render.fps = 30
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=extract(name))
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    arm.animation_data_create()
    action = next(a for a in bpy.data.actions
                  if a.name.split("|")[-1].lower() == clip)
    arm.animation_data.action = action
    start, end = (int(round(v)) for v in action.frame_range)
    print("%s %s frames %d..%d" % (name, clip, start, end))

    depsgraph = bpy.context.evaluated_depsgraph_get()
    print("  f |  L sole y / min z / n |  R sole y / min z / n | dx L/R")
    previous = {}
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        depsgraph.update()
        data = mesh.evaluated_get(depsgraph).to_mesh()
        matrix = mesh.evaluated_get(depsgraph).matrix_world
        points = [matrix @ v.co for v in data.vertices]
        mesh.evaluated_get(depsgraph).to_mesh_clear()
        floor = min(p.z for p in points)
        sides = {}
        for label, sign in (("L", 1), ("R", -1)):
            sole = [p for p in points
                    if p.x * sign > 0 and p.z - floor < 0.06]
            sides[label] = (sum(p.y for p in sole) / len(sole) if sole else 0.0,
                            len(sole))
        # шаг стопы за кадр по Y (м/с)
        speeds = []
        for label in ("L", "R"):
            key = label
            if key in previous:
                speeds.append((sides[label][0] - previous[key]) * 30.0)
            previous[key] = sides[label][0]
        print("  %2d | %7.3f %6.3f %4d | %7.3f %6.3f %4d | %+6.2f %+6.2f" % (
            frame, sides["L"][0], floor, sides["L"][1],
            sides["R"][0], min(p.z for p in points if p.x < 0), sides["R"][1],
            speeds[0] if speeds else 0.0, speeds[1] if len(speeds) > 1 else 0.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
