"""Что внутри героев Ultimate Space Kit: кости, клипы, оси и масштаб.

Запуск:
    blender --background --factory-startup --python probe_space_kit.py -- Mech_FinnTheFrog Enemy_Large

Печатает по каждому персонажу: трансформ арматуры, имена костей с rest-точками,
список действий с диапазоном кадров, габарит меша до/после нормализации и ход
корневой кости по кадрам. Этого достаточно, чтобы выбрать `forward` для реестра
`mech_lib.MECHS` и понять, где в клипах спрятано перемещение.

Источник — zip пака, поэтому FBX вытаскивается во временный каталог
(`AssetSource/_staging/space_kit`), как это делает `animal_lib.extract`.
"""

from __future__ import annotations

import math
import os
import sys
import zipfile

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PACK = os.path.join(ROOT, "AssetSource",
                    "Ultimate Space Kit - March 2023-20261024T221803Z-1-001.zip")
MEMBER = "Ultimate Space Kit - March 2023/Characters/FBX/%s.fbx"
STAGING = os.path.join(ROOT, "AssetSource", "_staging", "space_kit")


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def extract(name: str) -> str:
    os.makedirs(STAGING, exist_ok=True)
    out = os.path.join(STAGING, name + ".fbx")
    if not os.path.exists(out):
        with zipfile.ZipFile(PACK) as z:
            with open(out, "wb") as f:
                f.write(z.read(MEMBER % name))
    return out


def bbox(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], point[i])
                hi[i] = max(hi[i], point[i])
    return lo, hi


def probe(name: str) -> None:
    reset()
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.fbx(filepath=extract(name))

    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    print("\n=== %s" % name)
    print("objects arm=%s meshes=%d" % ([o.name for o in arms], len(meshes)))
    if not arms:
        return
    arm = arms[0]
    print("arm scale=%s rot=%s loc=%s" % (
        tuple(round(v, 4) for v in arm.scale),
        tuple(round(math.degrees(v), 2) for v in arm.rotation_euler),
        tuple(round(v, 4) for v in arm.location)))
    lo, hi = bbox(meshes)
    print("mesh size=%s min=%s" % (
        tuple(round(v, 3) for v in (hi - lo)), tuple(round(v, 3) for v in lo)))

    print("bones=%d" % len(arm.data.bones))
    for bone in arm.data.bones:
        head = arm.matrix_world @ bone.head_local
        print("  %-16s head=(%6.3f %6.3f %6.3f) parent=%s" % (
            bone.name, head.x, head.y, head.z,
            bone.parent.name if bone.parent else "-"))

    print("actions:")
    for action in bpy.data.actions:
        start, end = action.frame_range
        print("  %-18s %d..%d" % (action.name, int(round(start)), int(round(end))))

    # Ход корневой кости и вертикальный боб по клипам. Смотрим и формальный
    # корень, и `Body`: у мехов это одна кость, у Enemy_Large движение живёт
    # на `Body`, а `Root` стоит.
    watched = [arm.data.bones[0].name]
    for name in ("Body", "Hips"):
        if name in arm.data.bones and name not in watched:
            watched.append(name)
    if arm.animation_data is None:
        arm.animation_data_create()
    for action in bpy.data.actions:
        arm.animation_data.action = action
        start, end = (int(round(v)) for v in action.frame_range)
        for bone_name in watched:
            xs, ys, zs = [], [], []
            for frame in range(start, end + 1):
                bpy.context.scene.frame_set(frame)
                bpy.context.view_layer.update()
                head = arm.matrix_world @ arm.pose.bones[bone_name].head
                xs.append(head.x)
                ys.append(head.y)
                zs.append(head.z)
            print("  drift %-10s %-16s dx=%.3f dy=%.3f dz=%.3f" % (
                bone_name, action.name.split("|")[-1],
                max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
    arm.animation_data.action = None


def main() -> int:
    configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = args or ["Mech_FinnTheFrog", "Enemy_Large"]
    for name in names:
        probe(name)
    return 0


if __name__ == "__main__":
    sys.exit(main())
