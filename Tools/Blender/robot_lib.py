"""Сборка low-poly роботов на скелете ActionForge.

Модуль содержит только переносимое ядро: палитру, материалы, геометрические
примитивы, загрузку скелета с тремя клипами, жёсткую сkinning-привязку деталей
к костям и экспорт FBX. Конкретные дизайны — в `robot_designs.py`.

Оси Blender: вперёд −Y, вверх +Z, вбок +X. Скелет ActionForge — T-поза,
руки вдоль ±X. Все размеры в метрах.
"""

from __future__ import annotations

import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

import lowpoly

SAMPLES = os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..", "AssetSource", "ActionForge"))

#: Скелет ActionForge узкий для грузного северного робота. Раздвигаем руки и
#: бёдра в rest-позе; анимации — это вращения, поэтому клипы остаются валидными,
#: а стопы не меняют высоту. Меши строятся по фактическим точкам костей.
ARM_WIDTH = 1.45
LEG_WIDTH = 1.15

PALETTE = {
    # key: (hex, metallic, roughness, emission_strength)
    "base": ("#232C3C", 0.05, 0.62, 0.0),
    "panel": ("#35445A", 0.12, 0.52, 0.0),
    "dark": ("#111722", 0.0, 0.78, 0.0),
    "snow": ("#E9EFF5", 0.0, 0.86, 0.0),
    "fur": ("#F6F8FA", 0.0, 0.94, 0.0),
    "orange": ("#F08A2E", 0.0, 0.48, 0.0),
    "glow": ("#FF7A1A", 0.0, 0.35, 4.0),
    "metal": ("#8B96A5", 0.9, 0.32, 0.0),
    "rust": ("#8C4A32", 0.25, 0.72, 0.0),
    "cyan": ("#6FD6E8", 0.0, 0.35, 3.0),
    "yellow": ("#E8B84B", 0.0, 0.5, 0.0),
}

#: Скорости клипов в м/с, вычисленные из метаданных ActionForge:
#: walk 1.3 м / 1.333 с, sprint 5.5 м / 0.667 с. Нужны для настройки
#: playback speed в Unity, чтобы стопы не скользили.
CLIP_SPEEDS = {"walk": 1.3 / (40.0 / 30.0), "sprint": 5.5 / (20.0 / 30.0)}

#: Читаемость палитры в игровом свете (2026-10-18). В игре видимая сторона
#: робота получает примерно половину альбедо (часть граней освещена только
#: ambient), а снег занимает верх диапазона кадра — корпус читается почти
#: чёрным. Поэтому светлота альбедо поднимается с затуханием к белому:
#: снег, мех и акценты сохраняют свою роль, подъём достаётся корпусу и тёмной
#: подложке. Оттенок не меняется — опознавательный цвет робота остаётся прежним.
#: Правило и таблица пересчёта: `Tools/Blender/robot_palette_lift.py`.
GAME_LIFT_GAIN = 0.85
GAME_SAT_POWER = 0.80


def lift_for_game(hex_color: str) -> str:
    """Поднять светлоту альбедо под игровой свет, сохранив оттенок."""
    import colorsys
    value = hex_color.lstrip("#")
    rgb = [int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    hue, sat, val = colorsys.rgb_to_hsv(*rgb)
    val = min(1.0, val * (1.0 + GAME_LIFT_GAIN * (1.0 - val)))
    sat = sat ** GAME_SAT_POWER
    out = colorsys.hsv_to_rgb(hue, sat, val)
    return "#%02X%02X%02X" % tuple(int(round(c * 255)) for c in out)


def configure_stdout() -> None:
    import sys
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                  bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def make_materials(prefix: str, keys: list[str], colors=None):
    """Создаёт материалы палитры и возвращает (materials, key→index).

    `colors` переопределяет только hex-цвет роли, сохраняя металличность и
    шероховатость из общей палитры. Так у каждого робота свой окрас, но
    материал-роль остаётся предсказуемой для Unity.
    """
    colors = colors or {}
    mats, index = [], {}
    for key in keys:
        hex_color, metallic, roughness, emission = PALETTE[key]
        hex_color = lift_for_game(colors.get(key, hex_color))
        mat = lowpoly.make_material("%s_%s" % (prefix, key), hex_color,
                                    roughness=roughness, metallic=metallic)
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if emission > 0.0 and bsdf is not None:
            color = bsdf.inputs.get("Emission Color")
            strength = bsdf.inputs.get("Emission Strength")
            if color is not None:
                color.default_value = lowpoly.hex_to_linear(hex_color)
            if strength is not None:
                strength.default_value = emission
        index[key] = len(mats)
        mats.append(mat)
    return mats, index


# ---------------------------------------------------------------- геометрия


def _rotate(verts, center, rot):
    if rot is None:
        return verts
    matrix = Euler(tuple(math.radians(a) for a in rot), "XYZ").to_matrix()
    c = Vector(center)
    return [c + matrix @ (Vector(v) - c) for v in verts]


def box_verts(center, size, rot=None):
    verts, faces = lowpoly.beveled_box((0.0, 0.0, 0.0), size, 0.0)
    verts = _rotate(verts, (0.0, 0.0, 0.0), rot)
    return [Vector(center) + v for v in verts], faces


def icosphere(center, radius, subdivisions=1):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=radius)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    verts = [Vector(center) + v.co.copy() for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return verts, faces


class PartMesh:
    """Копилка деталей с привязкой каждой детали к одной кости."""

    def __init__(self):
        self.builder = lowpoly.MeshBuilder()
        self.groups: list[tuple[int, int, str]] = []

    def _add(self, verts, faces, mat, bone):
        start = len(self.builder.verts)
        self.builder.add_geometry(verts, faces, mat)
        self.groups.append((start, len(self.builder.verts), bone))

    def box(self, center, size, mat, bone, bevel=0.02, rot=None):
        verts, faces = lowpoly.beveled_box(center, size, bevel)
        verts = _rotate(verts, center, rot)
        self._add(verts, faces, mat, bone)

    def box_raw(self, center, size, mat, bone, rot=None):
        """Коробка без фаски — для тонких панелей и нашивок."""
        self.box(center, size, mat, bone, bevel=0.0, rot=rot)

    def beam(self, a, b, w0, d0, w1, d1, mat, bone, roll=0.0):
        """Сужающаяся прямоугольная балка от a до b."""
        a, b = Vector(a), Vector(b)
        axis = b - a
        length = axis.length
        if length < 1e-6:
            return
        quat = axis.to_track_quat("Z", "Y")
        if roll:
            quat = quat @ Matrix.Rotation(math.radians(roll), 4, "Z").to_quaternion()
        rings = [(0.0, w0, d0), (1.0, w1, d1)]
        verts = []
        for t, w, d in rings:
            center = a + axis * t
            for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                local = Vector((sx * w * 0.5, sy * d * 0.5, 0.0))
                verts.append(center + quat @ local)
        faces = [
            (0, 1, 2, 3),          # ближний торец
            (7, 6, 5, 4),          # дальний торец
            (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0),
        ]
        self._add(verts, faces, mat, bone)

    def tube(self, points, radii, mat, bone, n=8, caps=(True, True),
             profile=None, fan_caps=False):
        before = len(self.builder.verts)
        self.builder.tube(points, radii, mat, n=n, caps=caps,
                          profile=profile, fan_caps=fan_caps)
        self.groups.append((before, len(self.builder.verts), bone))

    def sphere(self, center, radius, mat, bone, subdivisions=1):
        verts, faces = icosphere(center, radius, subdivisions)
        self._add(verts, faces, mat, bone)

    def disc(self, center, radius, depth, mat, bone, axis="Y", n=8):
        """Короткий цилиндр вдоль оси; для глаз, заклёпок, фляг."""
        points = [Vector(center) - Vector((0, 0, depth * 0.5)),
                  Vector(center) + Vector((0, 0, depth * 0.5))]
        if axis == "Y":
            points = [Vector(center) + Vector((0, -depth * 0.5, 0)),
                      Vector(center) + Vector((0, depth * 0.5, 0))]
        elif axis == "X":
            points = [Vector(center) + Vector((-depth * 0.5, 0, 0)),
                      Vector(center) + Vector((depth * 0.5, 0, 0))]
        self.tube(points, [(radius, radius), (radius, radius)], mat, bone, n=n)

    def build(self, name, materials):
        obj = self.builder.build(name, materials)
        for start, end, bone in self.groups:
            vg = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            vg.add(list(range(start, end)), 1.0, "REPLACE")
        return obj


# ---------------------------------------------------------------- скелет


def widen_rig(arm, arm_factor=ARM_WIDTH, leg_factor=LEG_WIDTH):
    """Раздвигает руки и ноги в rest-позе, не трогая rotations в клипах."""
    arm_bones = {
        "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
        "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
        "index_01_l", "index_02_l", "index_03_l", "index_04_leaf_l",
        "middle_01_l", "middle_02_l", "middle_03_l", "middle_04_leaf_l",
        "pinky_01_l", "pinky_02_l", "pinky_03_l", "pinky_04_leaf_l",
        "ring_01_l", "ring_02_l", "ring_03_l", "ring_04_leaf_l",
        "thumb_01_l", "thumb_02_l", "thumb_03_l", "thumb_04_leaf_l",
        "index_01_r", "index_02_r", "index_03_r", "index_04_leaf_r",
        "middle_01_r", "middle_02_r", "middle_03_r", "middle_04_leaf_r",
        "pinky_01_r", "pinky_02_r", "pinky_03_r", "pinky_04_leaf_r",
        "ring_01_r", "ring_02_r", "ring_03_r", "ring_04_leaf_r",
        "thumb_01_r", "thumb_02_r", "thumb_03_r", "thumb_04_leaf_r",
    }
    leg_bones = {
        "thigh_l", "calf_l", "foot_l", "ball_l", "ball_leaf_l",
        "thigh_r", "calf_r", "foot_r", "ball_r", "ball_leaf_r",
    }
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = arm.data.edit_bones
    for name in arm_bones:
        bone = edit.get(name)
        if bone:
            bone.head.x *= arm_factor
            bone.tail.x *= arm_factor
    for name in leg_bones:
        bone = edit.get(name)
        if bone:
            bone.head.x *= leg_factor
            bone.tail.x *= leg_factor
    bpy.ops.object.mode_set(mode="OBJECT")


def rig_points(arm):
    """Словарь rest-точек костей в мировых координатах."""
    points = {}
    for bone in arm.data.bones:
        points[bone.name] = (arm.matrix_world @ bone.head_local,
                             arm.matrix_world @ bone.tail_local)
    return points


def load_rig():
    """Импортирует скелет ActionForge и три клипа, возвращает арматуру.

    Все клипы переведены в in-place: смещение root по Y (вперёд) обнулено.
    Оригинальные FBX/GLB не меняются — правка идёт только в памяти Blender.
    """
    clear_scene()
    bpy.context.scene.render.fps = 30

    base = None
    actions = {}
    for clip in ("walk_loop", "idle_loop", "sprint_loop"):
        path = os.path.join(SAMPLES, clip + ".glb")
        if not os.path.exists(path):
            raise FileNotFoundError("нет клипа ActionForge: %s" % path)
        bpy.ops.import_scene.gltf(filepath=path)
        armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
        arm = armatures[-1]
        action = arm.animation_data.action if arm.animation_data else None
        if action is None:
            raise RuntimeError("клип %s импортировался без action" % clip)
        actions[clip] = action
        if base is None:
            base = arm
        else:
            for obj in list(bpy.context.scene.objects):
                if obj.type == "MESH":
                    bpy.data.objects.remove(obj, do_unlink=True)
            if arm is not base:
                bpy.data.objects.remove(arm, do_unlink=True)

    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    base.animation_data_create()
    base.animation_data.action = None
    for track in list(base.animation_data.nla_tracks):
        base.animation_data.nla_tracks.remove(track)

    for clip, action in actions.items():
        # in-place: root.location[1] — это перенос вперёд
        for layer in getattr(action, "layers", []):
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    for fc in bag.fcurves:
                        if (fc.data_path == 'pose.bones["root"].location'
                                and fc.array_index == 1):
                            for kp in fc.keyframe_points:
                                kp.co[1] = 0.0
                                kp.handle_left[1] = 0.0
                                kp.handle_right[1] = 0.0
                            fc.update()
        track = base.animation_data.nla_tracks.new()
        track.name = clip
        strip = track.strips.new(clip, int(action.frame_range[0]), action)
        strip.name = clip
        track.mute = False

    base.name = "Armature"
    base.show_in_front = True
    widen_rig(base)
    print("RIG bones=%d actions=%s" % (len(base.data.bones),
                                       sorted(a.name for a in bpy.data.actions)))
    return base, actions


def bind(obj, arm):
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_vertex_groups = True
    return obj


def export_fbx(arm, mesh, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = None
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
    )
    if not os.path.exists(path):
        raise RuntimeError("FBX не создан: %s" % path)
    print("EXPORT %s (%d bytes)" % (path, os.path.getsize(path)))


def stats(obj):
    data = lowpoly.mesh_stats(obj)
    unweighted = sum(1 for v in obj.data.vertices if not v.groups)
    print("STATS %s verts=%d tris=%d shells=%d unweighted=%d"
          % (obj.name, data["verts"], data["tris"], data["shells"], unweighted))
    if unweighted:
        raise AssertionError("есть вершины без веса: %d" % unweighted)
    return data
