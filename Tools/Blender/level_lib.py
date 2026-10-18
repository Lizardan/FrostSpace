"""Ядро сборки статических low-poly ассетов уровня FrostSpace.

Палитра — те же цвета, что у существующих Unity-материалов
`Assets/_Project/Content/Materials/Level/Level_*.mat` (сняты из ассетов и
переведены из линейного пространства в sRGB-hex). Имена меш-материалов
совпадают с именами ассетов один-в-один: импортный постпроцессор уровня
подставляет существующий материал вместо созданного при импорте FBX.

Оси — как в `lowpoly.py`: вверх +Z, вбок +X. При выгрузке `export_static`
зеркалит геометрию по X: сам `bake_space_transform` в Unity даёт
преобразование (x, y, z) = (−X, Z, −Y), то есть разворот на 180° со сменой
знака по горизонтали (проверено по импортированному мешу: камни фундамента
и занос рельефа оказывались в противоположных четвертях). После зеркала
работает ожидаемая смена осей: Blender +X → Unity +X, Blender +Y → Unity −Z,
Blender +Z → Unity +Y.

Поэтому дом `SnowCabin` рисуется входом в Blender +Y — в Unity это −Z (юг,
к точке спавна), префаб ставится без поворота, — а раскладка
`level_layout` задаётся прямо в координатах Unity.
"""

from __future__ import annotations

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Vector

import lowpoly

# ---------------------------------------------------------------- палитра

#: Имя = имя ассета Unity `Level_<Name>.mat`.
#: (sRGB hex, roughness, metallic, emission_strength)
MATERIALS = {
    "Level_Wood": ("#7A5334", 0.72, 0.00, 0.0),
    "Level_WoodDark": ("#432E20", 0.68, 0.00, 0.0),
    "Level_Roof": ("#5A4034", 0.70, 0.00, 0.0),
    "Level_Snow": ("#E4EDF6", 0.82, 0.00, 0.0),
    "Level_SnowDeep": ("#C9D8E6", 0.78, 0.00, 0.0),
    "Level_Pine": ("#244534", 0.65, 0.00, 0.0),
    "Level_PineSnow": ("#DCE7F1", 0.78, 0.00, 0.0),
    "Level_Stone": ("#4C5158", 0.40, 0.00, 0.0),
    "Level_Metal": ("#98A3AF", 0.55, 0.85, 0.0),
    "Level_Rust": ("#8C4A32", 0.35, 0.20, 0.0),
    "Level_Orange": ("#E08A2E", 0.60, 0.00, 0.0),
    "Level_Window": ("#FFC978", 0.65, 0.00, 2.2),
}


def _principled(mat):
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node
    return None


def make_level_materials() -> dict:
    """Создать все материалы палитры уровня: имя → материал."""
    made = {}
    for name, (hex_color, roughness, metallic, emission) in MATERIALS.items():
        mat = lowpoly.make_material(name, hex_color, roughness=roughness,
                                    metallic=metallic)
        if emission > 0.0:
            bsdf = _principled(mat)
            if bsdf is not None:
                color = bsdf.inputs.get("Emission Color")
                strength = bsdf.inputs.get("Emission Strength")
                if color is not None:
                    color.default_value = lowpoly.hex_to_linear(hex_color)
                if strength is not None:
                    strength.default_value = emission
        made[name] = mat
    return made


# ---------------------------------------------------------------- служебное


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights, bpy.data.armatures, bpy.data.actions):
        for item in list(block):
            block.remove(item)


def rotate_verts(verts, center, rot_deg):
    """Повернуть точки вокруг центра (градусы, XYZ, как во вьюпорте)."""
    if rot_deg is None:
        return verts
    matrix = Euler(tuple(math.radians(a) for a in rot_deg), "XYZ").to_matrix()
    c = Vector(center)
    return [c + matrix @ (Vector(v) - c) for v in verts]


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    t = min(max((value - edge0) / (edge1 - edge0), 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def value_noise(x: float, y: float, seed: float = 0.0) -> float:
    """Детерминированный шум [0, 1) по узлу сетки — без внешних зависимостей."""
    v = math.sin(x * 127.1 + y * 311.7 + seed * 74.7) * 43758.5453
    return v - math.floor(v)


def noise2d(x: float, y: float, seed: float = 0.0) -> float:
    """Плавный интерполированный шум [-1, 1] из решётки 2×2."""
    xi, yi = math.floor(x), math.floor(y)
    xf, yf = x - xi, y - yi
    u = xf * xf * (3.0 - 2.0 * xf)
    v = yf * yf * (3.0 - 2.0 * yf)
    a = value_noise(xi, yi, seed)
    b = value_noise(xi + 1, yi, seed)
    c = value_noise(xi, yi + 1, seed)
    d = value_noise(xi + 1, yi + 1, seed)
    return 2.0 * ((a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v) - 1.0


# ------------------------------------------------------------- сборщик


class LevelBuilder:
    """MeshBuilder с палитрой: материалы задаются именем, индексы локальные.

    В меш объекта попадают только использованные материалы: у дома будет
    один слот на каждый применённый `Level_*`, а не все двенадцать.
    """

    def __init__(self, materials: dict):
        self.mb = lowpoly.MeshBuilder()
        self.materials = materials
        self.names: list[str] = []

    def _index(self, name: str) -> int:
        if name not in self.names:
            if name not in self.materials:
                raise KeyError("нет материала %r" % name)
            self.names.append(name)
        return self.names.index(name)

    # -- примитивы

    def box(self, center, size, mat, bevel=0.015):
        self.mb.box(center, size, self._index(mat), bevel)

    def box_rot(self, center, size, rot, mat, bevel=0.015):
        verts, faces = lowpoly.beveled_box((0.0, 0.0, 0.0), size, bevel)
        verts = rotate_verts(verts, (0.0, 0.0, 0.0), rot)
        verts = [Vector(center) + v for v in verts]
        self.mb.add_geometry(verts, faces, self._index(mat),
                             tags=lowpoly.BOX_FACE_TAGS)

    def tube(self, points, radii, mat, **kwargs):
        self.mb.tube(points, radii, self._index(mat), **kwargs)

    def ico(self, center, radius, mat, subdivisions=1, scale=(1.0, 1.0, 1.0),
            jitter=0.0, seeds=0.0):
        """Икосаэдр с необязательным «фасетным» искажением и масштабом."""
        verts, faces = lowpoly.icosphere((0.0, 0.0, 0.0), radius, subdivisions)
        if jitter > 0.0:
            shaken = []
            for i, v in enumerate(verts):
                n = value_noise(i * 3.7 + 1.3, i * 1.9 + seeds, seeds) - 0.5
                shaken.append(v * (1.0 + jitter * n * 2.0))
            verts = shaken
        verts = [Vector((v.x * scale[0], v.y * scale[1], v.z * scale[2]))
                 for v in verts]
        verts = [Vector(center) + v for v in verts]
        self.mb.add_geometry(verts, faces, self._index(mat))

    def band(self, center, radius, z0, z1, mat, n=12, squash=(1.0, 1.0),
             thickness=0.03):
        """Кольцевой пояс («обруч») с толщиной: бочки, трубы, обвязки."""
        cx, cy = center
        sx, sy = squash
        idx = self._index(mat)
        base = len(self.mb.verts)
        rings = []
        for r, z in ((radius, z0), (radius, z1),
                     (radius - thickness, z1), (radius - thickness, z0)):
            for i in range(n):
                a = lowpoly.TAU * i / n + math.pi / n
                rings.append(Vector((cx + math.cos(a) * r * sx,
                                     cy + math.sin(a) * r * sy, z)))
        self.mb.verts.extend(rings)
        for k in range(4):
            a0 = base + k * n
            a1 = base + ((k + 1) % 4) * n
            for i in range(n):
                j = (i + 1) % n
                self.mb.add_face((a0 + i, a0 + j, a1 + j, a1 + i), idx)

    def add(self, verts, faces, mat, tags=None):
        self.mb.add_geometry(verts, faces, self._index(mat), tags=tags)

    def mirror_from(self, mark) -> None:
        self.mb.mirror_from(mark)

    def mark(self):
        return self.mb.mark()

    def build(self, name: str, recalc: bool = True):
        return self.mb.build(name, [self.materials[n] for n in self.names],
                             recalc=recalc)


# ---------------------------------------------------------------- экспорт


def _mirror_x_inplace(obj) -> None:
    """Отразить меш по X и вывернуть грани: нормали остаются наружу."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for vert in bm.verts:
        vert.co.x = -vert.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def export_static(objs, path: str, mirror_x: bool = True) -> None:
    """FBX статики: выбранные меши, flat-shading, единицы 1:1.

    mirror_x компенсирует `bake_space_transform`, который иначе разворачивает
    модель в Unity на 180° (см. шапку модуля). Меши правятся на месте, поэтому
    после экспорта объекты уже зеркальны — сборка их сразу удаляет.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if mirror_x:
        for obj in objs:
            _mirror_x_inplace(obj)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
    )
    if not os.path.exists(path):
        raise RuntimeError("FBX не создан: %s" % path)
    print("EXPORT %s (%d bytes)" % (path, os.path.getsize(path)))


def report(objs) -> int:
    """Напечатать сводку по мешам и вернуть суммарное число треугольников."""
    total = 0
    for obj in objs:
        data = lowpoly.mesh_stats(obj)
        total += data["tris"]
        size = tuple(round(hi - lo, 3) for lo, hi in data["bbox"])
        print("STATS %-22s verts=%5d tris=%5d shells=%3d size=(%.3f, %.3f, %.3f)"
              % (obj.name, data["verts"], data["tris"], data["shells"],
                 size[0], size[1], size[2]))
        if data["non_manifold_edges"]:
            print("  ВНИМАНИЕ: non-manifold рёбер %d" % data["non_manifold_edges"])
    return total
