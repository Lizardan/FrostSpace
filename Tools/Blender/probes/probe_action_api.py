"""Проба API действий в этой версии Blender: как добраться до F-кривых.

Зачем: в Blender 5.x у действия больше нет `action.fcurves`. Кривые лежат в
`action.layers[].strips[].channelbags[].fcurves`. Этот скрипт печатает
фактическую структуру, чтобы не полагаться на память и не гадать по чужому
коду.

Запуск:
    blender --background --factory-startup --python probe_action_api.py

Ожидаемый признак 5.x: `HAS fcurves: False`, непустые `layers` и `channelbags`.
"""

from __future__ import annotations

import sys

import bpy


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def main() -> int:
    configure_stdout()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.armature_add()
    arm = bpy.context.active_object
    arm.animation_data_create()
    action = bpy.data.actions.new("Probe")
    arm.animation_data.action = action
    arm.pose.bones[0].keyframe_insert("rotation_euler", frame=1)

    print("VERSION", bpy.app.version_string)
    print("ACTION_ATTRS", [a for a in dir(action) if not a.startswith("_")])
    print("HAS fcurves:", hasattr(action, "fcurves"))
    print("slots:", [s.name_display for s in getattr(action, "slots", [])])
    print("layers:", len(getattr(action, "layers", [])))
    for layer in getattr(action, "layers", []):
        print("  layer", layer.name, "strips:", len(layer.strips))
        for strip in layer.strips:
            print("    strip", strip.type,
                  [a for a in dir(strip) if "channel" in a.lower()])
            for cb in getattr(strip, "channelbags", []):
                print("      channelbag slot:", cb.slot_handle,
                      "fcurves:", len(cb.fcurves))

    slot = getattr(arm.animation_data, "action_slot", None)
    print("action_slot:", slot, getattr(slot, "name_display", None))
    print("action_slot attr present:", hasattr(arm.animation_data, "action_slot"))
    return 0


if __name__ == "__main__":
    if bpy.app.background:
        sys.exit(main())
    else:
        main()
