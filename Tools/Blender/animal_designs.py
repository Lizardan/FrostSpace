"""Робо-животные FrostSpace: исходная оболочка + крупные робо-панели.

Органический меш Quaternius не выбрасывается: он уже сшит на суставах и
закрывает стыки тела, шеи и лап. Поверх него кладутся крупные фасетные
панели, суставные оболочки и визор — в палитре и масштабе роботов.

Мелких деталей (болтов, решёток, тонких антенн) нет: правило стиля —
крупные массы и толстые элементы.
"""

from __future__ import annotations

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import animal_lib
import robot_lib
from robot_lib import PartMesh, make_materials, rig_points

UP = Vector((0.0, 0.0, 1.0))
# Срез головы стоит на 20% длины кости головы впереди её основания: у
# длинношеих это сечение черепа, а не касательная к шее.
HEAD_CUT = 0.20
MAT_KEYS = ["base", "panel", "dark", "metal", "glow", "accent", "white",
            "rust", "brass", "patina", "warm", "cyan"]

QUADRUPEDS = {
    "Wolf": dict(
        body_width=0.95, body_height=0.42, leg=0.95, head_w=0.20,
        head_l=0.30, head_h=0.23, ears="point", tail="long", back="none",
        mane=False, stripes=False, horns=False, wool=False,
        colors={"base": "#2B3444", "panel": "#46566C", "accent": "#C9553A",
                "glow": "#6FD6E8"}),
    "Cat": dict(
        body_width=0.82, body_height=0.45, leg=0.82, head_w=0.23,
        head_l=0.25, head_h=0.23, ears="point", tail="long", back="none",
        mane=False, stripes=False, horns=False, wool=False,
        colors={"base": "#39404A", "panel": "#59636F", "accent": "#E8B84B",
                "glow": "#9BE8FF"}),
    "Dog": dict(
        body_width=1.05, body_height=0.44, leg=0.95, head_w=0.21,
        head_l=0.28, head_h=0.23, ears="flop", tail="short", back="pack",
        mane=False, stripes=False, horns=False, wool=False,
        colors={"base": "#3B3A2B", "panel": "#6B6545", "accent": "#F08A2E",
                "glow": "#FFC978"}),
    "Cow": dict(
        body_width=1.15, body_height=0.48, leg=1.00, head_w=0.23,
        head_l=0.30, head_h=0.25, ears="horn", tail="long", back="tank",
        mane=False, stripes=False, horns=True, wool=False,
        colors={"base": "#DDE4E8", "panel": "#B9C4C9", "accent": "#C9553A",
                "glow": "#6FD6E8"}),
    "Horse": dict(
        body_width=0.95, body_height=0.44, leg=1.15, head_w=0.18,
        head_l=0.32, head_h=0.23, ears="point", tail="long", back="saddle",
        mane=True, stripes=False, horns=False, wool=False,
        colors={"base": "#2A3B52", "panel": "#435A75", "accent": "#B08A4A",
                "glow": "#FFC978"}),
    "Zebra": dict(
        body_width=0.95, body_height=0.44, leg=1.10, head_w=0.18,
        head_l=0.32, head_h=0.23, ears="point", tail="long", back="saddle",
        mane=True, stripes=True, horns=False, wool=False,
        colors={"base": "#DDE4E8", "panel": "#1A1D22", "accent": "#6FD6E8",
                "glow": "#6FD6E8"}),
    "Llama": dict(
        body_width=0.82, body_height=0.44, leg=1.10, head_w=0.17,
        head_l=0.30, head_h=0.21, ears="tall", tail="short", back="pack",
        mane=True, stripes=False, horns=False, wool=False,
        colors={"base": "#35445A", "panel": "#59636F", "accent": "#C9553A",
                "glow": "#FFC978"}),
    "Pig": dict(
        body_width=1.10, body_height=0.54, leg=0.70, head_w=0.25,
        head_l=0.26, head_h=0.26, ears="flop", tail="curl", back="pack",
        mane=False, stripes=False, horns=False, wool=False,
        colors={"base": "#8C4A32", "panel": "#A9673F", "accent": "#E8B84B",
                "glow": "#FF7A1A"}),
    "Sheep": dict(
        body_width=1.10, body_height=0.58, leg=0.78, head_w=0.20,
        head_l=0.24, head_h=0.22, ears="flop", tail="short", back="wool",
        mane=False, stripes=False, horns=False, wool=True,
        colors={"base": "#E9EFF5", "panel": "#C9D2DA", "accent": "#6E9C86",
                "glow": "#9BE8FF"}),
    "Pug": dict(
        body_width=1.05, body_height=0.50, leg=0.68, head_w=0.28,
        head_l=0.25, head_h=0.27, ears="flop", tail="curl", back="pack",
        mane=False, stripes=False, horns=False, wool=False,
        colors={"base": "#3A332C", "panel": "#5A4E40", "accent": "#E8B84B",
                "glow": "#FFC978"}),
}


def _mats(name, colors):
    return make_materials(name, MAT_KEYS, colors=colors)


def _head_frame(R, roles):
    h0, h1 = R[roles["head"]]
    return h0, h1, (h1 - h0).normalized(), (h1 - h0).length


def _body_frame(R, roles, cfg):
    pelvis = R[roles["pelvis"]][0]
    neck = R[roles["neck"]][0]
    length = max((neck - pelvis).length, 0.2)
    width = cfg["body_width"] * length * 0.38
    height = cfg["body_height"] * length
    center = (pelvis + neck) * 0.5 + UP * height * 0.10
    return pelvis, neck, center, width, height, length


def _remove_head(mesh, roles, R):
    """Убирает органическую голову вертикальным срезом и закрывает его.

    Срез по весам уносит всю шею (вес головы тянется до торса), а наклонная
    плоскость у длинношеих режет шею по касательной. Поэтому плоскость
    ставится поперёк корпуса на 20% длины головы вперёд от её основания:
    шея остаётся целой, а контур среза — это сечение черепа.

    Отверстие закрывается `holes_fill`; если контур не плоский —
    `triangle_fill`. Без заглушки в Unity с backface culling видна изнанка
    оболочки, это и есть «прозрачные» пятна на шее.

    Возвращает точки контура среза в мировых координатах.
    """
    group = mesh.vertex_groups.get(roles["head"])
    if group is None:
        return []
    h0, _h1, hd, hl = _head_frame(R, roles)
    forward = Vector((hd.x, hd.y, 0.0))
    if forward.length < 1e-6:
        forward = Vector((0.0, -1.0, 0.0))
    forward.normalize()
    plane = h0 + forward * hl * HEAD_CUT
    mw = mesh.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bm.verts.ensure_lookup_table()
    layer = bm.verts.layers.deform.active
    bmesh.ops.bisect_plane(
        bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
        dist=1e-6, plane_co=inv @ plane, plane_no=(inv.to_3x3() @ forward).normalized(),
        clear_outer=False, clear_inner=False)
    doomed = []
    for vert in bm.verts:
        if (mw @ vert.co - plane).dot(forward) <= 1e-6:
            continue
        if layer is not None and vert[layer].get(group.index, 0.0) > 0.25:
            doomed.append(vert)
    if not doomed:
        bm.free()
        return []
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    boundary = [e for e in bm.edges if len(e.link_faces) == 1]
    points = [mw @ v.co for e in boundary for v in e.verts]
    result = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    filled = len(result.get("faces", []))
    left = [e for e in bm.edges if len(e.link_faces) == 1]
    if left:
        extra = bmesh.ops.triangle_fill(
            bm, use_beauty=True, use_dissolve=False, edges=left)
        filled += len(extra.get("geom", []))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    remaining = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.to_mesh(mesh.data)
    bm.free()
    mesh.data.update()
    print("HEAD_CUT %s removed=%d filled=%d boundary=%d" %
          (mesh.name, len(doomed), filled, remaining))
    return points


def _weighted_bbox(mesh, names):
    """Габарит вершин, у которых одна из групп ``names`` доминирует."""
    indices = {g.index for g in mesh.vertex_groups if g.name in names}
    points = []
    if indices:
        for vert in mesh.data.vertices:
            if not vert.groups:
                continue
            best = max(vert.groups, key=lambda item: item.weight)
            if best.group in indices:
                points.append(mesh.matrix_world @ vert.co)
    if not points:
        return None
    lo = Vector((min(p.x for p in points), min(p.y for p in points),
                 min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points),
                 max(p.z for p in points)))
    return lo, hi


def _shell_metrics(mesh, roles):
    """Фактические габариты торса, шеи и головы исходной оболочки.

    Эвристика `body_width * length * 0.38` занижала корпус в 1.5–2 раза:
    череп, построенный от неё, выглядел мелким. Размеры берём с оболочки,
    которую реально видно в кадре.
    """
    result = {}
    for key, names in (
            ("torso", {roles.get("body"), roles.get("pelvis"),
                       roles.get("chest")} - {None}),
            ("neck", {roles.get("neck")} - {None}),
            ("head", {roles.get("head")} - {None})):
        box = _weighted_bbox(mesh, names)
        result[key] = None if box is None else box[1] - box[0]
    return result


def _frame_rot(forward):
    """Углы Эйлера коробки: локальный Y — вперёд, Z — вверх."""
    right = forward.cross(UP)
    if right.length < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(forward).normalized()
    matrix = Matrix((right, forward, up)).transposed()
    return tuple(math.degrees(value) for value in matrix.to_euler("XYZ"))


def _cut_extents(points, h0, hd):
    """Размер и центр контура среза в осях «вбок / вверх / вперёд»."""
    right = hd.cross(UP)
    if right.length < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(hd).normalized()
    local = [((p - h0).dot(right), (p - h0).dot(up), (p - h0).dot(hd))
             for p in points]
    lo = [min(v[i] for v in local) for i in range(3)]
    hi = [max(v[i] for v in local) for i in range(3)]
    return {
        "size": tuple(hi[i] - lo[i] for i in range(3)),
        "center": h0 + right * ((lo[0] + hi[0]) * 0.5)
        + up * ((lo[1] + hi[1]) * 0.5) + hd * ((lo[2] + hi[2]) * 0.5),
        "right": right,
        "up": up,
    }


def paint_shell(mesh, mats, cfg, roles, R):
    """Красит исходную оболочку и убирает органическую голову.

    Возвращает контур среза: по нему строятся воротник и череп, поэтому
    размер головы берётся из фактической оболочки, а не из эвристики.
    """
    cut = _remove_head(mesh, roles, R)
    while len(mesh.data.materials):
        mesh.data.materials.pop()
    for mat in mats:
        mesh.data.materials.append(mat)
    mw = mesh.matrix_world
    h0, _h1, _hd, hl = _head_frame(R, roles)
    body_center = _body_frame(R, roles, cfg)[2]
    panel, dark, accent = 1, 2, 5
    for poly in mesh.data.polygons:
        center = mw @ poly.center
        normal = (mw.to_3x3() @ poly.normal).normalized()
        index = 0
        if normal.z < -0.30:
            index = dark
        elif (center - h0).length < hl * 0.9:
            index = panel
        elif center.z > body_center.z and abs(center.x) < cfg["body_width"]:
            index = panel
        if cfg.get("stripes") and int((center.y + 100.0) * 2.0) % 3 == 0:
            index = dark
        poly.material_index = index
        poly.use_smooth = False
    print("PAINT %s faces=%d" % (mesh.name, len(mesh.data.polygons)))
    return cut


def _body_armor(p, R, roles, cfg, M):
    pelvis, neck, center, width, height, length = _body_frame(
        R, roles, cfg)
    bone = roles["body"]
    # Наспинная броня и бортовые юбки — крупные массы, перекрывают оболочку.
    p.box(center + UP * height * 0.52,
          (width * 1.02, length * 0.82, height * 0.30),
          M["panel"], bone, bevel=0.035)
    p.box(center + UP * height * 0.72,
          (width * 0.62, length * 0.48, height * 0.14),
          M["accent"], bone, bevel=0.02)
    p.box(center + UP * height * 0.84,
          (width * 0.16, length * 0.46, height * 0.06),
          M["accent"], bone, bevel=0.0)
    for sx in (-1, 1):
        p.box(center + Vector((sx * width * 0.52, 0.0, -height * 0.05)),
              (width * 0.20, length * 0.66, height * 0.58),
              M["base"], bone, bevel=0.03)
        p.box(center + Vector((sx * width * 0.62, -length * 0.08,
                               height * 0.10)),
              (width * 0.06, length * 0.34, height * 0.22),
              M["accent"], bone, bevel=0.01)
    # Грудная и тазовая плиты закрывают переходы к шее и ногам.
    p.box(neck + UP * height * 0.10,
          (width * 1.05, length * 0.18, height * 0.62),
          M["panel"], roles["chest"] if "chest" in roles else bone,
          bevel=0.03)
    p.box(pelvis + UP * height * 0.05,
          (width * 0.95, length * 0.20, height * 0.55),
          M["panel"], roles["pelvis"], bevel=0.03)
    # Кольцо шеи — визуально сшивает торс и голову.
    a = R[roles["neck"]][0]
    b = R[roles["head"]][0]
    p.tube([a + (b - a) * 0.05, a + (b - a) * 0.22],
           [(width * 0.52, height * 0.40), (width * 0.46, height * 0.36)],
           M["accent"], roles["neck"], n=8)
    # Крупные оболочки плеч и бёдер — квадратные, в духе роботов.
    for side, chain in roles["legs"].items():
        root = R[chain[0]][0]
        p.box(root + UP * height * 0.02,
              (width * 0.52, width * 0.46, height * 0.48),
              M["base"], bone, bevel=0.03)
    # Светящееся ядро на груди.
    p.disc(neck + UP * height * 0.10, width * 0.16, width * 0.05,
           M["glow"], bone, axis="Y", n=10)
    return center, width, height, length


def _head_box(R, roles, cut, torso):
    """Габарит и посадка черепа по фактическому контуру среза.

    Череп не может быть меньше среза, иначе из-под брони торчит обрезанная
    шея; и не должен быть мельче 0.6 торса — это и была «мелкая голова».
    """
    h0, _h1, hd, hl = _head_frame(R, roles)
    if torso is None:
        torso = Vector((hl, hl, hl))
    fwd = Vector((hd.x, hd.y, 0.0))
    if fwd.length < 1e-6:
        fwd = Vector((0.0, -1.0, 0.0))
    fwd.normalize()
    right = UP.cross(fwd).normalized()
    if cut:
        us = [(q - h0).dot(right) for q in cut]
        vs = [(q - h0).dot(UP) for q in cut]
        ws = [(q - h0).dot(fwd) for q in cut]
        cut_w = max(us) - min(us)
        cut_h = max(vs) - min(vs)
        base = (h0 + right * ((max(us) + min(us)) * 0.5)
                + UP * ((max(vs) + min(vs)) * 0.5)
                + fwd * ((max(ws) + min(ws)) * 0.5))
    else:
        cut_w, cut_h = torso.x * 0.55, torso.z * 0.55
        base = h0
    width = max(cut_w * 1.08, torso.x * 0.62)
    height = min(max(cut_h * 1.06, torso.z * 0.58), width * 1.35)
    length = hl * 1.12
    center = base + fwd * (length * 0.5 - hl * 0.16)
    return dict(h0=h0, hd=hd, hl=hl, fwd=fwd, right=right, base=base,
                center=center, width=width, height=height, length=length,
                cut_w=cut_w, cut_h=cut_h)


def _head_armor(p, R, roles, cfg, M, cut, metrics):
    head = roles["head"]
    box = _head_box(R, roles, cut, metrics.get("torso") or Vector(
        (_head_frame(R, roles)[3],) * 3))
    width, height, length = box["width"], box["height"], box["length"]
    center, base, fwd, right = box["center"], box["base"], box["fwd"], box["right"]
    hd, hl = box["hd"], box["hl"]
    cut_w, cut_h = box["cut_w"], box["cut_h"]
    p.box(center, (width, length, height), M["panel"], head, bevel=0.045)
    # Воротник на стыке: перекрывает заглушку среза и зазор под черепом.
    p.box(base + fwd * hl * 0.02, (cut_w * 1.16, hl * 0.22, cut_h * 1.16),
          M["accent"], roles["neck"], bevel=0.03)
    # Толстая шея от торса до воротника — вместо тонкой трубы.
    neck_a = R[roles["neck"]][0]
    p.tube([neck_a, (neck_a + base) * 0.5, base],
           [(cut_w * 0.38, cut_h * 0.38), (cut_w * 0.44, cut_h * 0.44),
            (cut_w * 0.50, cut_h * 0.50)],
           M["base"], roles["neck"], n=8)
    p.box(center + UP * height * 0.48,
          (width * 0.84, length * 0.62, height * 0.14),
          M["accent"], head, bevel=0.02)
    front = center + fwd * length * 0.50
    p.box(front, (width * 0.80, length * 0.10, height * 0.34),
          M["dark"], head, bevel=0.015)
    p.box(front + fwd * length * 0.03,
          (width * 0.54, length * 0.05, height * 0.12),
          M["glow"], head, bevel=0.008)
    for sx in (-1, 1):
        p.box(center + right * (sx * width * 0.52),
              (width * 0.10, length * 0.56, height * 0.62),
              M["base"], head, bevel=0.02)
        p.disc(center + right * (sx * width * 0.58),
               height * 0.14, width * 0.05, M["glow"], head,
               axis="X", n=8)
    kind = cfg.get("ears")
    for sx in (-1, 1):
        base_ear = (center + right * (sx * width * 0.28)
                    + UP * (height * 0.46) - fwd * (length * 0.05))
        if kind in ("point", "tall"):
            tip = (base_ear + right * (sx * width * 0.12)
                   + UP * (height * (0.42 if kind == "point" else 0.66)))
            p.beam(base_ear, tip, width * 0.22, length * 0.18,
                   width * 0.10, length * 0.10, M["base"], head)
        elif kind == "flop":
            tip = (base_ear + right * (sx * width * 0.26)
                   - UP * (height * 0.30))
            p.beam(base_ear, tip, width * 0.18, length * 0.12,
                   width * 0.12, length * 0.08, M["base"], head)
    if cfg.get("horns"):
        for sx in (-1, 1):
            horn = center + right * (sx * width * 0.40) + UP * (height * 0.36)
            p.tube([horn,
                    horn + right * (sx * width * 0.28) + UP * (height * 0.22),
                    horn + right * (sx * width * 0.52) + UP * (height * 0.52)],
                   [(width * 0.10, width * 0.10),
                    (width * 0.07, width * 0.07),
                    (width * 0.03, width * 0.03)],
                   M["brass"], head, n=6)
    if cfg.get("mane"):
        for i in range(3):
            p.box(center - fwd * length * (0.08 + i * 0.17)
                  + UP * height * 0.42,
                  (width * 0.70, length * 0.14, height * 0.18),
                  M["accent"], head, bevel=0.02)
    return width, height, length



def _leg_armor(p, R, roles, cfg, M, width, height, length):
    thick = length * 0.070 * cfg["leg"]
    for side, chain in roles["legs"].items():
        points = [R[name][0] for name in chain]
        if len(points) < 2:
            continue
        # Верхняя броня бедра/плеча — квадратные сегменты.
        p.beam(points[0], points[1], thick * 2.6, thick * 2.5,
               thick * 1.8, thick * 1.8, M["panel"], chain[0])
        p.box(points[0], (thick * 2.6, thick * 2.4, thick * 2.4),
              M["base"], chain[0], bevel=0.03)
        if len(points) > 2:
            p.beam(points[1], points[2], thick * 1.9, thick * 1.9,
                   thick * 1.2, thick * 1.2, M["base"], chain[1])
            p.box(points[1], (thick * 2.2, thick * 2.0, thick * 2.0),
                  M["metal"], chain[1], bevel=0.025)
        # Стопа — своя кость на каждую ногу: у фермерских ригов она отдельная
        # (FrontFoot.L и т.д.) и в цепочку ноги не входит, у волка/кота/собаки
        # стопой служит конец цепочки. Броня стопы должна ехать за стопой,
        # иначе шаг читается как «пятка болтается».
        foot_bone = roles.get("feet_by_side", {}).get(side)
        if not foot_bone or foot_bone not in R:
            foot_bone = chain[-1]
        foot = R[foot_bone][1]
        p.box(foot + UP * thick * 0.35,
              (thick * 1.9, thick * 2.6, thick * 0.85),
              M["dark"], foot_bone, bevel=0.02)
        p.box(foot + UP * thick * 0.35 + Vector((0.0, -thick * 0.75, 0.0)),
              (thick * 1.5, thick * 0.45, thick * 0.45),
              M["accent"], foot_bone, bevel=0.01)


def _tail_armor(p, R, roles, cfg, M, width):
    chain = roles.get("tail", [])
    if chain:
        for i in range(len(chain) - 1):
            a, b = R[chain[i]][0], R[chain[i + 1]][0]
            thick = max((b - a).length * 0.18, width * 0.08)
            p.beam(a, b, thick * 1.3, thick * 1.3, thick * 0.7,
                   thick * 0.7, M["panel"], chain[i])
            p.sphere(b, thick * 0.75, M["accent"], chain[i + 1],
                     subdivisions=0)
        return
    if cfg.get("tail") == "curl":
        pelvis = R[roles["pelvis"]][0]
        p.tube([pelvis, pelvis + Vector((0.0, 0.0, width * 0.55)),
                pelvis + Vector((width * 0.25, -width * 0.12,
                                 width * 0.75))],
               [(width * 0.12, width * 0.12)] * 3,
               M["panel"], roles["pelvis"], n=6)
    elif cfg.get("tail") == "short":
        pelvis = R[roles["pelvis"]][0]
        p.beam(pelvis, pelvis + Vector((0.0, 0.0, width * 0.55)),
               width * 0.16, width * 0.16, width * 0.08, width * 0.08,
               M["panel"], roles["pelvis"])


def _accessories(p, R, roles, cfg, M, center, width, height, length):
    back = cfg.get("back")
    bone = roles["body"]
    top = center + UP * height * 0.78
    if back == "tank":
        p.tube([top + Vector((0.0, -length * 0.16, 0.0)),
                top + Vector((0.0, length * 0.18, 0.0))],
               [(width * 0.30, width * 0.30)] * 2, M["metal"], bone, n=8)
        for y in (-0.14, 0.14):
            p.disc(top + Vector((0.0, y * length, 0.0)),
                   width * 0.31, width * 0.06, M["brass"], bone,
                   axis="Y", n=8)
        p.box(top + Vector((0.0, 0.0, width * 0.24)),
              (width * 0.18, width * 0.18, width * 0.08),
              M["glow"], bone, bevel=0.0)
    elif back == "saddle":
        p.box(top, (width * 0.86, width * 0.66, height * 0.22),
              M["rust"], bone, bevel=0.025)
        p.box(top + UP * height * 0.10,
              (width * 0.42, width * 0.34, height * 0.10),
              M["accent"], bone, bevel=0.015)
    elif back == "pack":
        p.box(top, (width * 0.78, width * 0.60, height * 0.32),
              M["panel"], bone, bevel=0.025)
        p.box(top + Vector((0.0, -width * 0.18, height * 0.18)),
              (width * 0.46, width * 0.22, height * 0.10),
              M["accent"], bone, bevel=0.01)
    elif back == "wool":
        for i in range(6):
            x = (i - 2.5) * width * 0.22
            p.sphere(top + Vector((x, (i % 2) * width * 0.16 - width * 0.08,
                                   (i % 3) * height * 0.04)),
                     width * 0.28, M["white"], bone, subdivisions=1)


def build_quadruped(species, arm, source_mesh):
    cfg = dict(QUADRUPEDS[species])
    roles = animal_lib.roles(arm)
    R = rig_points(arm)
    name = "RA_" + species
    mats, M = _mats(name, cfg["colors"])
    metrics = _shell_metrics(source_mesh, roles)
    cut = paint_shell(source_mesh, mats, cfg, roles, R)
    p = PartMesh()
    center, width, height, length = _body_armor(p, R, roles, cfg, M)
    _head_armor(p, R, roles, cfg, M, cut, metrics)
    _leg_armor(p, R, roles, cfg, M, width, height, length)
    _tail_armor(p, R, roles, cfg, M, width)
    _accessories(p, R, roles, cfg, M, center, width, height, length)
    return name, p.build(name, mats), mats, M


# ------------------------------------------------------------------ орёл

def build_eagle(arm, source_mesh):
    name = "RA_Eagle"
    mats, M = _mats(name, {"base": "#3A3F4A", "panel": "#59636F",
                           "accent": "#E8B84B", "glow": "#FFC978",
                           "metal": "#8B96A5", "dark": "#111722"})
    roles = animal_lib.roles(arm)
    R = rig_points(arm)
    cfg = dict(body_width=1.0, body_height=0.4, body_color="#3A3F4A",
               stripes=False)
    metrics = _shell_metrics(source_mesh, roles)
    cut = paint_shell(source_mesh, mats, cfg, roles, R)
    p = PartMesh()
    bone = roles["body"]
    a = R["Bone"][0]
    b = R[roles["head"]][0]
    length = (b - a).length
    p.box((a + b) * 0.5 + UP * length * 0.08,
          (length * 0.26, length * 0.55, length * 0.10),
          M["panel"], bone, bevel=0.02)
    head = roles["head"]
    hb = _head_box(R, roles, cut, metrics.get("torso"))
    p.box(hb["center"], (hb["width"], hb["length"], hb["height"]),
          M["panel"], head, bevel=0.03)
    p.box(hb["base"] + hb["fwd"] * hb["hl"] * 0.02,
          (hb["cut_w"] * 1.14, hb["hl"] * 0.20, hb["cut_h"] * 1.14),
          M["accent"], roles["neck"], bevel=0.02)
    p.box(hb["center"] + hb["fwd"] * hb["length"] * 0.50,
          (hb["width"] * 0.72, hb["length"] * 0.10,
           hb["height"] * 0.26),
          M["dark"], head, bevel=0.01)
    p.box(hb["center"] + hb["fwd"] * hb["length"] * 0.55,
          (hb["width"] * 0.44, hb["length"] * 0.05,
           hb["height"] * 0.10),
          M["glow"], head, bevel=0.006)
    for sx in (-1, 1):
        p.disc(hb["center"] + hb["right"] * (sx * hb["width"] * 0.56),
               hb["height"] * 0.13, hb["width"] * 0.05,
               M["glow"], head, axis="X", n=8)
    for side, chain in roles["wings"].items():
        for i in range(len(chain) - 1):
            a0, b0 = R[chain[i]][0], R[chain[i + 1]][0]
            w = length * (0.30 - i * 0.045)
            p.beam(a0, b0, w, length * 0.05, w * 0.80, length * 0.035,
                   M["panel"], chain[i])
            p.box((a0 + b0) * 0.5 + UP * length * 0.03,
                  (w * 0.80, (b0 - a0).length * 0.70, length * 0.035),
                  M["accent"], chain[i], bevel=0.01)
            if i == 0:
                p.sphere(a0, w * 0.42, M["metal"], chain[i],
                         subdivisions=1)
        p.box(R[chain[-1]][1], (length * 0.16, length * 0.22,
                                length * 0.06),
              M["accent"], chain[-1], bevel=0.01)
    for i in range(len(roles["tail"]) - 1):
        a0, b0 = R[roles["tail"][i]][0], R[roles["tail"][i + 1]][0]
        p.beam(a0, b0, length * 0.18, length * 0.04, length * 0.10,
               length * 0.03, M["panel"], roles["tail"][i])
    for side, chain in roles["legs"].items():
        p.beam(R[chain[0]][0], R[chain[-1]][1], length * 0.06,
               length * 0.06, length * 0.04, length * 0.04,
               M["dark"], chain[0])
        p.box(R[chain[-1]][1], (length * 0.14, length * 0.18,
                                length * 0.05),
              M["accent"], chain[-1], bevel=0.008)
    return name, p.build(name, mats), mats, M


# ---------------------------------------------------------------- пиранья

# ------------------------------------------------------------------ паук

SPIDER_LEGS = []


def make_spider():
    """Свой скелет паука: тело, голова и 8 трёхзвенных ног."""
    robot_lib.clear_scene()
    bpy.context.scene.render.fps = 30
    data = bpy.data.armatures.new("SpiderArmature")
    arm = bpy.data.objects.new("SpiderArmature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    body = data.edit_bones.new("body")
    body.head = (0.0, 0.05, 0.52)
    body.tail = (0.0, -0.25, 0.56)
    head = data.edit_bones.new("head")
    head.head = (0.0, -0.25, 0.56)
    head.tail = (0.0, -0.58, 0.54)
    head.parent = body

    legs = []
    for rank, y in enumerate((-0.30, -0.10, 0.10, 0.30)):
        for side, sx in (("l", 1.0), ("r", -1.0)):
            names = []
            parent = body
            hip = (sx * 0.24, y, 0.52)
            knee = (sx * 0.62, y + 0.06, 0.70)
            ankle = (sx * 0.86, y + 0.20, 0.82)
            foot = (sx * 0.94, y + 0.34, 0.02)
            for part, (a, b) in enumerate(((hip, knee), (knee, ankle),
                                          (ankle, foot))):
                bone = data.edit_bones.new("leg%d_%s_%d" % (rank, side, part))
                bone.head = a
                bone.tail = b
                bone.parent = parent
                parent = bone
                names.append(bone.name)
            legs.append(names)

    bpy.ops.object.mode_set(mode="OBJECT")
    arm.show_in_front = True
    arm.animation_data_create()

    def key_all(action, duration, gait):
        arm.animation_data.action = action
        for frame in range(1, duration + 1):
            t = (frame - 1) / max(duration - 1, 1)
            for pb in arm.pose.bones:
                pb.rotation_mode = "XYZ"
                pb.rotation_euler = (0.0, 0.0, 0.0)
                pb.location = (0.0, 0.0, 0.0)
            body_pb = arm.pose.bones["body"]
            body_pb.location.y = 0.015 * math.sin(math.tau * t * 2.0)
            for index, names in enumerate(legs):
                rank = index // 2
                sx = 1.0 if index % 2 == 0 else -1.0
                phase = ((rank + (0 if sx > 0 else 1)) % 2) * 0.5
                swing = gait * math.sin(math.tau * (t + phase))
                lift = max(0.0, math.sin(math.tau * (t + phase) + 1.2))
                hip_pb = arm.pose.bones[names[0]]
                femur_pb = arm.pose.bones[names[1]]
                tibia_pb = arm.pose.bones[names[2]]
                hip_pb.rotation_euler.z = swing * sx
                femur_pb.rotation_euler.x = lift * 0.22
                tibia_pb.rotation_euler.x = -lift * 0.16
            for pb in arm.pose.bones:
                pb.keyframe_insert("rotation_euler", frame=frame)
                pb.keyframe_insert("location", frame=frame)

    for name, duration, gait in (("idle_loop", 61, 0.04),
                                 ("move_loop", 31, 0.30)):
        action = bpy.data.actions.new(name)
        key_all(action, duration, gait)
        track = arm.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.name = name
        strip.frame_start = 1
        strip.frame_end = duration
        strip.action_frame_start = 1
        strip.action_frame_end = duration
        track.mute = False
    arm.animation_data.action = None

    meta = {
        "species": "Spider",
        "cfg": {"target": 0.55, "metric": "height", "mode": "walk"},
        "roles": {"body": "body", "head": "head", "legs": legs},
        "clips": {
            "idle": {"frames": 60},
            "move": {"frames": 30},
        },
        "clip_speed": 0.80,
        "unity_size": (1.9, 1.9, 0.9),
    }
    return arm, meta


def build_spider(arm, meta):
    name = "RA_Spider"
    mats, M = _mats(name, {"base": "#1A1D22", "panel": "#35445A",
                           "accent": "#C9553A", "glow": "#FF7A1A",
                           "metal": "#8B96A5", "dark": "#0B0E12"})
    R = rig_points(arm)
    p = PartMesh()
    body_bone = "body"
    body_a, body_b = R["body"]
    p.tube([body_a, (body_a + body_b) * 0.5, body_b],
           [(0.36, 0.28), (0.42, 0.32), (0.30, 0.24)],
           M["base"], body_bone, n=8)
    p.box((body_a + body_b) * 0.5 + UP * 0.26,
          (0.55, 0.46, 0.18), M["panel"], body_bone, bevel=0.03)
    p.box((body_a + body_b) * 0.5 + UP * 0.36,
          (0.30, 0.26, 0.08), M["accent"], body_bone, bevel=0.02)
    head_a, head_b = R["head"]
    p.box(head_a + (head_b - head_a) * 0.45,
          (0.34, 0.34, 0.22), M["panel"], "head", bevel=0.025)
    for sx in (-1, 1):
        p.disc(head_a + (head_b - head_a) * 0.55 +
               Vector((sx * 0.14, 0.0, 0.06)), 0.09, 0.04,
               M["glow"], "head", axis="X", n=8)
    p.box(head_b, (0.24, 0.10, 0.12), M["dark"], "head", bevel=0.01)
    for names in meta["roles"]["legs"]:
        points = [R[n][0] for n in names] + [R[names[-1]][1]]
        for i in range(3):
            a, b = points[i], points[i + 1]
            thick = 0.10 - i * 0.015
            p.beam(a, b, thick * 1.5, thick * 1.5, thick, thick,
                   M["panel"], names[i])
            p.sphere(a, thick * 1.25, M["metal"], names[i],
                     subdivisions=1)
        p.box(points[-1] + UP * 0.03,
              (0.12, 0.18, 0.06), M["dark"], names[-1], bevel=0.01)
    return name, p.build(name, mats), mats, M


def build_piranha(arm, source_mesh):
    name = "RA_Piranha"
    mats, M = _mats(name, {"base": "#2F4A4A", "panel": "#4A6B6B",
                           "accent": "#C9553A", "glow": "#6FD6E8",
                           "metal": "#8B96A5", "dark": "#111722"})
    roles = animal_lib.roles(arm)
    R = rig_points(arm)
    cfg = dict(body_width=1.0, body_height=0.4, stripes=False)
    metrics = _shell_metrics(source_mesh, roles)
    cut = paint_shell(source_mesh, mats, cfg, roles, R)
    p = PartMesh()
    bone = roles["body"]
    a = R["Bone"][0]
    b = R[roles["head"]][0]
    length = (b - a).length
    center = (a + b) * 0.5
    p.box(center + UP * length * 0.18,
          (length * 0.16, length * 0.55, length * 0.06),
          M["panel"], bone, bevel=0.02)
    p.box(center - UP * length * 0.14,
          (length * 0.12, length * 0.40, length * 0.05),
          M["dark"], bone, bevel=0.01)
    head = roles["head"]
    hb = _head_box(R, roles, cut, metrics.get("torso"))
    p.box(hb["center"], (hb["width"], hb["length"], hb["height"]),
          M["panel"], head, bevel=0.03)
    p.box(hb["base"] + hb["fwd"] * hb["hl"] * 0.02,
          (hb["cut_w"] * 1.14, hb["hl"] * 0.20, hb["cut_h"] * 1.14),
          M["accent"], roles["neck"], bevel=0.02)
    p.box(hb["center"] + hb["fwd"] * hb["length"] * 0.48,
          (hb["width"] * 0.76, hb["length"] * 0.10,
           hb["height"] * 0.22),
          M["dark"], head, bevel=0.01)
    p.box(hb["center"] + hb["fwd"] * hb["length"] * 0.52,
          (hb["width"] * 0.46, hb["length"] * 0.05,
           hb["height"] * 0.09),
          M["glow"], head, bevel=0.006)
    for sx in (-1, 1):
        p.disc(hb["center"] + hb["right"] * (sx * hb["width"] * 0.56),
               hb["height"] * 0.13, hb["width"] * 0.05,
               M["glow"], head, axis="X", n=8)
        # Крупные сопла-плавники.
        p.tube([center + Vector((sx * length * 0.16, length * 0.05, 0.0)),
                center + Vector((sx * length * 0.28, length * 0.30, 0.0))],
               [(length * 0.07, length * 0.07), (length * 0.04,
                                                 length * 0.04)],
               M["metal"], bone, n=8)
        p.disc(center + Vector((sx * length * 0.28, length * 0.30, 0.0)),
               length * 0.04, length * 0.02, M["glow"], bone,
               axis="Y", n=8)
    for i in range(len(roles["tail"]) - 1):
        a0, b0 = R[roles["tail"][i]][0], R[roles["tail"][i + 1]][0]
        p.beam(a0, b0, length * 0.16, length * 0.05, length * 0.10,
               length * 0.03, M["panel"], roles["tail"][i])
    for fin in roles["fins"]:
        a0, b0 = R[fin]
        p.beam(a0, b0, length * 0.08, length * 0.03, length * 0.22,
               length * 0.02, M["accent"], fin)
    return name, p.build(name, mats), mats, M
