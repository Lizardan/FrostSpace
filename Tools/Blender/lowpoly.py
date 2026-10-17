"""Низкоуровневое ядро процедурной low-poly геометрии.

Только bpy / bmesh / mathutils — никаких внешних зависимостей, поэтому модуль
работает и в headless-скрипте, и внутри аддона.

Идея: объект собирается не булевыми операциями и не ремешем, а из лофтов —
труб, протянутых по точкам, с квадовой топологией. Плюс детали в виде
скруглённых коробок с явно заданной топологией.

Оси: вперёд −Y, вверх +Z, вбок +X (соглашение Blender). При экспорте FBX в
Unity это даёт +Z forward, +Y up; проверяйте габаритом после импорта.

Модуль не знает ничего о конкретной игре или персонаже: это строительные блоки
для процедурных ассетов.
"""

from __future__ import annotations

import math

import bmesh
import bpy
from mathutils import Vector

TAU = 2.0 * math.pi


# ---------------------------------------------------------------- профили


def poly_profile(n: int, offset: float | None = None) -> list[tuple[float, float]]:
    """Единичный правильный n-угольник в плоскости кольца.

    Координаты: (x — вбок, y — вперёд). ``offset = pi/n`` разворачивает
    фигуру так, что на «перёд» смотрит РЕБРО, а не вершина: силуэт спереди
    и сзади читается ровнее, а боковые рёбра дают грани под flat shading.
    """
    if offset is None:
        offset = math.pi / n
    return [
        (math.cos(TAU * i / n + offset), math.sin(TAU * i / n + offset))
        for i in range(n)
    ]


def frame(direction, ref=(0.0, -1.0, 0.0)):
    """Локальный базис кольца: (right, up) для оси ``direction``.

    ``ref`` — «вперёд» объекта; если ось трубы почти совпала с ним,
    подставляем другую опору, чтобы базис не выродился.
    """
    d = Vector(direction)
    if d.length < 1e-9:
        d = Vector((0.0, 0.0, 1.0))
    d.normalize()
    r0 = Vector(ref)
    if abs(d.dot(r0)) > 0.98:
        r0 = Vector((1.0, 0.0, 0.0))
    right = r0.cross(d)
    if right.length < 1e-9:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = d.cross(right)
    up.normalize()
    return right, up


def ring_points(center, direction, rx, ry, profile, ref=(0.0, -1.0, 0.0)):
    """Кольцо вершин вокруг ``center`` в плоскости, перпендикулярной оси."""
    right, up = frame(direction, ref)
    c = Vector(center)
    return [c + right * (px * rx) + up * (py * ry) for px, py in profile]


# ------------------------------------------------------------ скруглённый бокс

#: Роли граней скруглённого бокса В ПОРЯДКЕ их создания в `beveled_box`:
#: 8 угловых треугольников, 12 рёберных квадов, 6 граневых квадов.
#:
#: Зачем это нужно как данные. Правило «нет растянутых граней» проверяемо
#: только если отличать ПОВЕРХНОСТЬ от фаски: у фаски вытянутость — это и есть
#: фаска, и требование 1:6 к ней делает невозможным скругление любого не-
#: кубического бокса. Роль грани — факт конструкции, а не догадка по числам.
BOX_FACE_TAGS = (("corner",) * 8 + ("bevel",) * 12 + ("face",) * 6)

#: Роли, которые НЕ считаются поверхностью при проверке растянутости.
CHAMFER_TAGS = ("corner", "bevel", "cap")


def beveled_box(center, size, bevel):
    """Коробка с фаской по рёбрам — 24 вершины, 26 граней, замкнутый меш.

    Своя реализация вместо ``bmesh.ops.bevel``: результат детерминирован и
    топология ровно такая, какая нужна для low-poly — 6 квадов на гранях,
    12 квадов на рёбрах, 8 треугольников на углах.

    Ключ к правильной топологии: у каждого исходного угла куба появляются
    ТРИ вершины — по одной на каждую прилегающую грань, а не «сдвинутые по
    оси». Именно они и связывают грани с рёберными квадами и угловыми
    треугольниками без разрывов.
    """
    cx, cy, cz = center
    a, b, c = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    e = min(bevel, a * 0.49, b * 0.49, c * 0.49)

    verts: list[Vector] = []
    faces: list[tuple[int, ...]] = []

    def v(x, y, z):
        verts.append(Vector((cx + x, cy + y, cz + z)))
        return len(verts) - 1

    # V[('x', sx, sy, sz)] — вершина, принадлежащая грани ±X в этом углу,
    # т.е. угол грани после её отступа внутрь на e.
    V: dict[tuple, int] = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                V[("x", sx, sy, sz)] = v(sx * a, sy * (b - e), sz * (c - e))
                V[("y", sx, sy, sz)] = v(sx * (a - e), sy * b, sz * (c - e))
                V[("z", sx, sy, sz)] = v(sx * (a - e), sy * (b - e), sz * c)

    # 8 угловых треугольников
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                faces.append(
                    (V[("x", sx, sy, sz)], V[("y", sx, sy, sz)], V[("z", sx, sy, sz)])
                )

    # 12 рёберных квадов: ребро = пересечение двух граней, поэтому его фаску
    # образуют вершины именно этих двух граней на обоих концах ребра.
    for sy in (-1, 1):
        for sz in (-1, 1):  # ребро вдоль X: грани ±Y и ±Z
            faces.append(
                (
                    V[("y", -1, sy, sz)], V[("y", 1, sy, sz)],
                    V[("z", 1, sy, sz)], V[("z", -1, sy, sz)],
                )
            )
    for sx in (-1, 1):
        for sz in (-1, 1):  # ребро вдоль Y: грани ±X и ±Z
            faces.append(
                (
                    V[("x", sx, -1, sz)], V[("x", sx, 1, sz)],
                    V[("z", sx, 1, sz)], V[("z", sx, -1, sz)],
                )
            )
    for sx in (-1, 1):
        for sy in (-1, 1):  # ребро вдоль Z: грани ±X и ±Y
            faces.append(
                (
                    V[("x", sx, sy, -1)], V[("x", sx, sy, 1)],
                    V[("y", sx, sy, 1)], V[("y", sx, sy, -1)],
                )
            )

    # 6 отступивших граневых квадов
    for sx in (-1, 1):
        faces.append(
            (
                V[("x", sx, -1, -1)], V[("x", sx, 1, -1)],
                V[("x", sx, 1, 1)], V[("x", sx, -1, 1)],
            )
        )
    for sy in (-1, 1):
        faces.append(
            (
                V[("y", -1, sy, -1)], V[("y", 1, sy, -1)],
                V[("y", 1, sy, 1)], V[("y", -1, sy, 1)],
            )
        )
    for sz in (-1, 1):
        faces.append(
            (
                V[("z", -1, -1, sz)], V[("z", 1, -1, sz)],
                V[("z", 1, 1, sz)], V[("z", -1, 1, sz)],
            )
        )

    return verts, faces


# --------------------------------------------------------------- сборщик


class MeshBuilder:
    """Копилка вершин/граней с материалом на грань и зеркалированием."""

    def __init__(self) -> None:
        self.verts: list[Vector] = []
        self.faces: list[tuple[int, ...]] = []
        self.mats: list[int] = []
        #: Роль грани ("surface"/"cap"/"face"/"bevel"/"corner"). Нужна
        #: проверкам топологии: растянутость меряется по поверхности, а фаска
        #: исключена явно (см. `BOX_FACE_TAGS`).
        self.tags: list[str] = []

    # -- низкий уровень

    def add_face(self, indices, mat: int, tag: str = "surface") -> None:
        if len(set(indices)) != len(indices):
            return  # вырожденная грань — молча пропускаем
        self.faces.append(tuple(indices))
        self.mats.append(mat)
        self.tags.append(tag)

    def add_geometry(self, verts, faces, mat: int, tags=None) -> None:
        base = len(self.verts)
        self.verts.extend(Vector(v) for v in verts)
        for index, f in enumerate(faces):
            tag = tags[index] if tags is not None and index < len(tags) else "surface"
            self.add_face([base + i for i in f], mat, tag)

    # -- труба (лофт)

    def tube(
        self,
        points,
        radii,
        mat: int,
        n: int = 8,
        caps=(True, True),
        profile=None,
        ref=(0.0, -1.0, 0.0),
        offsets=None,
        fan_caps: bool = False,
    ):
        """Протянуть кольцо по точкам пути.

        ``radii[i]`` — (rx, ry) в плоскости кольца; ``offsets[i]`` — сдвиг
        центра кольца в его же локальных осях (нужно, чтобы поджать низ
        торса вперёд, а не двигать всю трубу).

        ``fan_caps`` — закрывать торец веером треугольников вместо одного
        n-угольника. Нужно там, где действует правило «без n-гонов»: n-гон
        на торце всё равно триангулируется при экспорте, но делает это
        экспортёр, и результат не виден в отчёте по мешу.
        """
        prof = profile if profile is not None else poly_profile(n)
        pts = [Vector(p) for p in points]
        rings = []
        for i, p in enumerate(pts):
            if i == 0:
                d = pts[1] - pts[0]
            elif i == len(pts) - 1:
                d = pts[-1] - pts[-2]
            else:
                d = pts[i + 1] - pts[i - 1]
            right, up = frame(d, ref)
            rx, ry = radii[i]
            shift = Vector((0.0, 0.0, 0.0))
            if offsets is not None:
                ox, oy = offsets[i]
                shift = right * ox + up * oy
            center = p + shift
            rings.append(
                [center + right * (px * rx) + up * (py * ry) for px, py in prof]
            )

        base = len(self.verts)
        for r in rings:
            self.verts.extend(r)
        count = len(prof)
        for a in range(len(rings) - 1):
            for i in range(count):
                j = (i + 1) % count
                self.add_face(
                    (
                        base + a * count + i,
                        base + a * count + j,
                        base + (a + 1) * count + j,
                        base + (a + 1) * count + i,
                    ),
                    mat,
                    "surface",
                )
        if caps[0]:
            if fan_caps:
                for i in range(1, count - 1):
                    self.add_face((base, base + i, base + i + 1), mat, "cap")
            else:
                self.add_face([base + i for i in range(count)], mat, "cap")
        if caps[1]:
            s = base + (len(rings) - 1) * count
            if fan_caps:
                for i in range(1, count - 1):
                    self.add_face((s, s + i, s + i + 1), mat, "cap")
            else:
                self.add_face([s + i for i in range(count)], mat, "cap")
        return rings

    # -- коробка

    def box(self, center, size, mat: int, bevel: float = 0.02):
        verts, faces = beveled_box(center, size, bevel)
        self.add_geometry(verts, faces, mat, tags=BOX_FACE_TAGS)

    # -- зеркалирование

    def mark(self):
        return len(self.verts), len(self.faces)

    def mirror_from(self, mark) -> None:
        """Отразить в X всё, что добавлено после ``mark``.

        Симметрия гарантируется конструкцией: левая половина строится
        параметрически, правая — её точное отражение.
        """
        v0 = mark[0]
        offset = len(self.verts) - v0
        for vert in self.verts[v0:]:
            self.verts.append(Vector((-vert.x, vert.y, vert.z)))
        for face, mat, tag in zip(self.faces[mark[1]:], self.mats[mark[1]:],
                                  self.tags[mark[1]:]):
            self.add_face([offset + i for i in reversed(face)], mat, tag)

    # -- сборка объекта

    def build(self, name: str, materials, collection=None):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        mesh.validate(verbose=False)
        mesh.update()

        obj = bpy.data.objects.new(name, mesh)
        (collection or bpy.context.scene.collection).objects.link(obj)
        for mat in materials:
            mesh.materials.append(mat)
        for i, poly in enumerate(mesh.polygons):
            if i < len(self.mats):
                poly.material_index = self.mats[i]
            poly.use_smooth = False

        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        return obj


def vertex_shells(mesh) -> list[list[int]]:
    """Разбить вершины меша на оболочки по связности рёбер.

    Нужно для весов: мелкая деталь (рюкзак, воротник, наколенник) — это
    отдельная оболочка, и решение «какой костью она управляется» принимается
    один раз на всю оболочку, а не для каждой вершины отдельно.
    """
    count = len(mesh.vertices)
    parent = list(range(count))

    def find(a: int) -> int:
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for edge in mesh.edges:
        a, b = edge.vertices
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    groups: dict[int, list[int]] = {}
    for index in range(count):
        groups.setdefault(find(index), []).append(index)
    return list(groups.values())


# ------------------------------------------------------------- статистика


def mesh_stats(obj) -> dict:
    """Сводка по мешу: бюджет граней, границы, компоненты, bbox."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    tris = sum(max(len(f.verts) - 2, 0) for f in bm.faces)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    non_manifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)

    seen = set()
    components = 0
    for f in bm.faces:
        if f.index in seen:
            continue
        components += 1
        stack = [f]
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index)
                        stack.append(nf)

    coords = [v.co for v in bm.verts]
    bbox = (
        (min(c.x for c in coords), max(c.x for c in coords)),
        (min(c.y for c in coords), max(c.y for c in coords)),
        (min(c.z for c in coords), max(c.z for c in coords)),
    ) if coords else ((0, 0), (0, 0), (0, 0))
    bm.free()
    return {
        "verts": len(me.vertices),
        "faces": len(me.polygons),
        "tris": tris,
        "boundary_edges": boundary,
        "non_manifold_edges": non_manifold,
        "shells": components,
        "bbox": bbox,
    }


# ---------------------------------------------------------------- материалы


def srgb_to_linear(c: float) -> float:
    """sRGB → linear. Палитра задаётся в sRGB (как в макете), а Base Color
    у Blender — линейный; без конвертации цвета выходят пересвеченными."""
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear(hex_str: str) -> tuple[float, float, float, float]:
    h = hex_str.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(srgb_to_linear(v) for v in rgb) + (1.0,)


def make_material(name: str, hex_color: str, roughness: float = 0.75,
                  metallic: float = 0.0):
    """Простой Principled-материал с цветом из sRGB-палитры."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is None:
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                bsdf = node
                break
    if bsdf is not None:
        base = bsdf.inputs.get("Base Color")
        rough = bsdf.inputs.get("Roughness")
        metal = bsdf.inputs.get("Metallic")
        if base is not None:
            base.default_value = hex_to_linear(hex_color)
        if rough is not None:
            rough.default_value = roughness
        if metal is not None:
            metal.default_value = metallic
    mat.diffuse_color = hex_to_linear(hex_color)
    return mat
