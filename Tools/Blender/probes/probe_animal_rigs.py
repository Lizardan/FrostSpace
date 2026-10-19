"""Разведка ригов животных Quaternius.

Извлекает исходные FBX из `AssetSource/*.zip` во временную папку вне Assets,
импортирует каждый, удаляет меш и печатает всё, что нужно для дизайна:
иерархию костей, rest-позиции, клипы, длины, root motion, fps и габарит.

Запуск:
    blender --background --factory-startup --python Tools/Blender/probes/probe_animal_rigs.py
"""

from __future__ import annotations

import os
import sys
import traceback
import zipfile

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
STAGING = os.path.join(ROOT, "AssetSource", "_staging")

PACKS = {
    "animals_farm": (os.path.join(ROOT, "AssetSource", "quaternius_animals_farm.zip"),
                     "Farm Animals by @Quaternius/FBX/{name}.fbx"),
    "animals_vol2": (os.path.join(ROOT, "AssetSource", "quaternius_animals_vol2.zip"),
                     "Animal Pack Vol.2 by @Quaternius/FBX/{name}.fbx"),
}

ANIMALS = (
    ("Wolf", "animals_vol2"),
    ("Cat", "animals_vol2"),
    ("Dog", "animals_vol2"),
    ("Eagle", "animals_vol2"),
    ("Piranha", "animals_vol2"),
    ("Cow", "animals_farm"),
    ("Horse", "animals_farm"),
    ("Zebra", "animals_farm"),
    ("Llama", "animals_farm"),
    ("Pig", "animals_farm"),
    ("Sheep", "animals_farm"),
    ("Pug", "animals_farm"),
)


def iter_fcurves(action):
    if hasattr(action, "layers"):
        for layer in action.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    yield from bag.fcurves
    else:
        yield from action.fcurves


def extract(species: str, pack: str) -> str:
    archive, pattern = PACKS[pack]
    member = pattern.format(name=species)
    out = os.path.join(STAGING, pack)
    target = os.path.join(out, species + ".fbx")
    if not os.path.exists(target):
        os.makedirs(out, exist_ok=True)
        with zipfile.ZipFile(archive) as z:
            data = z.read(member)
        with open(target, "wb") as f:
            f.write(data)
    return target


def clear() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def bbox(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            lo.x = min(lo.x, point.x)
            lo.y = min(lo.y, point.y)
            lo.z = min(lo.z, point.z)
            hi.x = max(hi.x, point.x)
            hi.y = max(hi.y, point.y)
            hi.z = max(hi.z, point.z)
    return lo, hi


def probe(species: str, pack: str) -> None:
    path = extract(species, pack)
    clear()
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.fbx(filepath=path)
    print("\n===== %s (%s)" % (species, path))
    mesh_objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    print("OBJECTS", [(o.name, o.type) for o in bpy.context.scene.objects])
    if mesh_objs:
        lo, hi = bbox(mesh_objs)
        print("MESH_BBOX local_min=%s local_max=%s size=%s" %
              (tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi),
               tuple(round(hi[i] - lo[i], 3) for i in range(3))))
        print("MESH_SCALE", [tuple(round(v, 4) for v in o.scale) for o in mesh_objs])

    for arm in arms:
        print("ARM %s scale=%s loc=%s rot=%s" % (
            arm.name, tuple(round(v, 4) for v in arm.scale),
            tuple(round(v, 4) for v in arm.location),
            tuple(round(v, 4) for v in arm.rotation_euler)))
        bones = arm.data.bones
        print("BONES", len(bones))
        roots = [b for b in bones if b.parent is None]
        for bone in bones:
            head = arm.matrix_world @ bone.head_local
            tail = arm.matrix_world @ bone.tail_local
            print("  %-12s parent=%-12s head=(%.3f, %.3f, %.3f) tail=(%.3f, %.3f, %.3f)" % (
                bone.name, bone.parent.name if bone.parent else "-",
                head.x, head.y, head.z, tail.x, tail.y, tail.z))
        for root in roots:
            print("ROOT_BONE", root.name)
            for action in bpy.data.actions:
                for fc in iter_fcurves(action):
                    if fc.data_path == 'pose.bones["%s"].location' % root.name:
                        values = [kp.co[1] for kp in fc.keyframe_points]
                        if values:
                            print("  ROOT_LOC %s axis=%d min=%.4f max=%.4f first=%.4f last=%.4f" % (
                                action.name, fc.array_index, min(values), max(values),
                                values[0], values[-1]))

    print("ACTIONS", [(a.name, tuple(round(v, 2) for v in a.frame_range))
                      for a in bpy.data.actions])
    if arms and arms[0].animation_data:
        print("NLA", [(t.name, [(s.name, s.frame_start, s.frame_end) for s in t.strips])
                      for t in arms[0].animation_data.nla_tracks])
        print("ACTIVE_ACTION", arms[0].animation_data.action.name
              if arms[0].animation_data.action else None)


def main() -> int:
    for species, pack in ANIMALS:
        try:
            probe(species, pack)
        except Exception:  # noqa: BLE001
            print("FAILED", species)
            traceback.print_exc()
    return 0


if __name__ == "__main__":
    sys.exit(main())
