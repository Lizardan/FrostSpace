"""Подробный разбор копланарных пар в одном FBX.

Проверка `check_frontier.py` отвечает на вопрос «есть ли мерцание» и называет
объекты, но не говорит, **какая именно грань** с чем совпадает. Для починки
этого мало: нужно знать высоту плоскости, материал, размер пятна и вершины.

Зонд повторяет отбор граней `check_frontier.cross_object_coplanar` с теми же
порогами, но печатает по каждой паре всё, что нужно для правки.

Запуск:
    blender --background --factory-startup --python probe_coplanar.py -- <file.fbx> [имя_объекта]
"""

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_frontier as CF  # noqa: E402


def _overlap(a_lo, a_hi, b_lo, b_hi) -> float:
    """Площадь перекрытия двух AABB по двум наибольшим измерениям, м²."""
    d = [min(a_hi[k], b_hi[k]) - max(a_lo[k], b_lo[k]) for k in range(3)]
    d = sorted((v for v in d if v > 0.0), reverse=True)
    if len(d) < 2:
        return 0.0
    return d[0] * d[1]


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи FBX после --")
        return 2
    path = argv[0]
    only = argv[1] if len(argv) > 1 else ""

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = {obj.name: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}

    rows = []
    for name, obj in meshes.items():
        if name.endswith("_Collider"):
            continue
        if only and only not in name:
            continue
        mw = obj.matrix_world
        scale = max(abs(s) for s in mw.to_scale()) or 1.0
        area_scale = scale * scale
        mats = [s.material.name if s.material else "?" for s in obj.material_slots]
        for poly in obj.data.polygons:
            if poly.area * area_scale < CF.COPANAR_MIN_AREA:
                continue
            nrm = (mw.to_3x3() @ poly.normal).normalized()
            if nrm.z < CF.COPANAR_UP:
                continue
            cen = mw @ poly.center
            corners = [mw @ obj.data.vertices[i].co for i in poly.vertices]
            lo = [min(c[a] for c in corners) for a in range(3)]
            hi = [max(c[a] for c in corners) for a in range(3)]
            # Настоящая нормаль грани — из самих вершин, а не из `poly.normal`,
            # переведённого матрицей: при зеркале по X (экспорт FBX его ставит)
            # переведённая нормаль уезжает, и плоскость читается неверно.
            geo = None
            if len(corners) >= 3:
                e1 = corners[1] - corners[0]
                e2 = corners[2] - corners[0]
                cr = e1.cross(e2)
                if cr.length > 1e-9:
                    geo = cr.normalized()
            rows.append({
                "obj": name,
                "mat": mats[poly.material_index] if poly.material_index < len(mats) else "?",
                "nrm": nrm,
                "geo": geo,
                "cen": cen,
                "area": poly.area * area_scale,
                "d": nrm.dot(cen),
                "lo": lo,
                "hi": hi,
                "corners": corners,
            })

    print("=== грани-кандидаты (вверх, площадь >= %.2f м²): %d ==="
          % (CF.COPANAR_MIN_AREA, len(rows)))
    for r in sorted(rows, key=lambda r: -r["area"])[:12]:
        print("  %-18s %-18s площадь=%8.3f  d=%9.4f  центр=(%.2f, %.2f, %.2f)"
              % (r["obj"], r["mat"], r["area"], r["d"],
                 r["cen"].x, r["cen"].y, r["cen"].z))

    pairs = []
    for i in range(len(rows)):
        a = rows[i]
        for j in range(i + 1, len(rows)):
            b = rows[j]
            if a["obj"] == b["obj"] or a["mat"] == b["mat"]:
                continue
            if abs(a["d"] - b["d"]) >= CF.COPANAR_GAP:
                continue
            if a["nrm"].dot(b["nrm"]) < CF.COPANAR_NORMAL_DOT:
                continue
            patch = _overlap(a["lo"], a["hi"], b["lo"], b["hi"])
            if patch < CF.COPANAR_MIN_PATCH:
                continue
            pairs.append((patch, a, b))
    pairs.sort(key=lambda p: -p[0])

    print("=== копланарных пар: %d ===" % len(pairs))
    for patch, a, b in pairs[:20]:
        print("  %-18s (%s)  <->  %-18s (%s)" % (a["obj"], a["mat"], b["obj"], b["mat"]))
        print("      пятно=%.3f м²  разница высот=%.4f м  площади граней=%.3f / %.3f"
              % (patch, abs(a["d"] - b["d"]), a["area"], b["area"]))
        print("      A: d=%.4f  lo=%s  hi=%s" % (a["d"],
              tuple(round(v, 3) for v in a["lo"]), tuple(round(v, 3) for v in a["hi"])))
        print("      B: d=%.4f  lo=%s  hi=%s" % (b["d"],
              tuple(round(v, 3) for v in b["lo"]), tuple(round(v, 3) for v in b["hi"])))

    # Все грани объектов, которые попали в пары: по ним видно, что именно
    # совпадает — длинная полоса профиля или отдельная деталь.
    involved = sorted({p[1]["obj"] for p in pairs} | {p[2]["obj"] for p in pairs})
    for name in involved:
        own = [r for r in rows if r["obj"] == name]
        if not own:
            continue
        print("=== грани объекта %s: %d ===" % (name, len(own)))
        for r in sorted(own, key=lambda r: (r["lo"][0], r["lo"][1]))[:40]:
            print("  %-14s площадь=%7.3f  d=%9.4f  lo=%s  hi=%s"
                  % (r["mat"], r["area"], r["d"],
                     tuple(round(v, 2) for v in r["lo"]), tuple(round(v, 2) for v in r["hi"])))

    # Вершины первой пары, где рельс встретился с землёй: по ним видно,
    # что именно лежит в одной плоскости.
    for patch, a, b in pairs:
        if "Rail" not in a["obj"] and "Rail" not in b["obj"]:
            continue
        if "WorldCell" not in a["obj"] and "WorldCell" not in b["obj"]:
            continue
        print("=== вершины конфликтной пары (пятно %.3f м²) ===" % patch)
        for tag, r in (("A", a), ("B", b)):
            print("  %s %s (%s) площадь=%.3f d=%.4f" % (tag, r["obj"], r["mat"], r["area"], r["d"]))
            for c in r["corners"]:
                print("      вершина (%.3f, %.3f, %.3f)" % (c.x, c.y, c.z))
        # Настоящая разница: вертикальный зазор и зазор вдоль геометрической
        # нормали, посчитанной из вершин. Именно она решает, мерцание это или
        # ложное срабатывание проверки.
        if a["geo"] is not None and b["geo"] is not None:
            geo_d = abs(a["geo"].dot(a["cen"]) - b["geo"].dot(b["cen"]))
            vert = abs(a["cen"].z - b["cen"].z)
            print("      геометрическая нормаль: d_A=%.4f d_B=%.4f -> зазор вдоль нормали %.4f м"
                  % (a["geo"].dot(a["cen"]), b["geo"].dot(b["cen"]), geo_d))
            print("      вертикальный зазор между центрами: %.4f м" % vert)
            print("      нормаль проверки: n_A=%s n_B=%s"
                  % (tuple(round(v, 4) for v in a["nrm"]), tuple(round(v, 4) for v in b["nrm"])))
            print("      геометрическая:  n_A=%s n_B=%s"
                  % (tuple(round(v, 4) for v in a["geo"]), tuple(round(v, 4) for v in b["geo"])))
        break
    return 0


if __name__ == "__main__":
    sys.exit(main())
