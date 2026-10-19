"""Печатает трансформы импортированного FBX животного.

Запуск:
    blender --background --factory-startup --python \
        Tools/Blender/probes/probe_animal_import_transform.py -- <file.fbx>
"""

from __future__ import annotations

import os
import sys

import bpy
from mathutils import Vector


def main() -> int:
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    path = os.path.abspath(args[0])
    bpy.ops.import_scene.fbx(filepath=path)
    for obj in bpy.context.scene.objects:
        print("OBJ", obj.name, "type", obj.type,
              "loc", tuple(round(v, 3) for v in obj.location),
              "rot", tuple(round(v, 3) for v in obj.rotation_euler),
              "scale", tuple(round(v, 4) for v in obj.scale),
              "parent", obj.parent.name if obj.parent else None)
        if obj.type == "MESH":
            corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
            lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                         min(c.z for c in corners)))
            hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                         max(c.z for c in corners)))
            print("   world bbox size", tuple(round(v, 3) for v in (hi - lo)))
    for action in bpy.data.actions:
        print("ACTION", action.name, action.frame_range[:])
    return 0


if __name__ == "__main__":
    sys.exit(main())
