"""Дымовой тест живой сессии: что за Blender и что в сцене.

Запуск через мост (нужен GUI-Blender, фоновый режим мост не обслуживает):

    python Tools/Blender/gui_session.py run Tools/Blender/probes/gui_ping.py --auto

`--auto` поднимает сессию и закрывает её после выполнения.
"""

from __future__ import annotations

import bpy

print("blender", bpy.app.version_string)
print("background", bpy.app.background)
print("file", bpy.data.filepath or "<пустая сцена>")
print("objects", len(bpy.data.objects))
print("actions", len(bpy.data.actions))
print("addons", sorted(bpy.context.preferences.addons.keys()))
