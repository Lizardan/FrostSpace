"""Рендерит превью края мира у депо: кадр прибытия, линия на юг, тупик, план.

Запуск (штатно — из `build_all.py --asset frontier --preview`):

    blender --background --factory-startup --python Tools/Blender/render_frontier_previews.py -- \
        --out AssetSource/Previews/Frontier

Снимается **сборка**, а не каждый меш по отдельности: решать надо вопрос
«что видно с платформы», а он не сводится к четырёх видов на террейн. Поэтому
шаг в `PIPELINE` один и он импортирует все `Frontier_*.fbx`, которые есть.

Депо в кадре заменено боксами-дублёрами (платформа, кузов, ворота) по
замеренным bounds из `Frontier.unity`: силуэт и перекрытия должны совпадать,
а сам вагон превью не проверяет — он принят раньше.

Тумана в этом превью нет: Workbench не считает объёмный туман, а EEVEE/Cycles
в headless — это минуты на кадр и GPU-контекст. Кадр без тумана показывает
**больше**, чем покажет игра, то есть оценка консервативна: если здесь край
читается как край, в тумане он тем более не обрыв. Сам туман проверяется
скриншотом в Play Mode на `Frontier`, где он настоящий.
"""

from __future__ import annotations

import glob
import math
import os
import sys
import traceback

import bpy
import bmesh
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import frontier_layout as L  # noqa: E402

MODELS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                      "Content", "Models", "Level"))

#: Замеры `Frontier.unity` (см. журнал плана): дублёры депо по фактическим
#: bounds, а не «примерно вагон».
STAND_INS = (
    ("Platform", (-4.0, 15.0, 0.0, 0.60, -7.0, 7.0)),
    ("Wagon", (-12.09, -3.20, -0.05, 4.76, -14.64, 14.30)),
    ("Gate", (6.07, 12.71, 0.0, 6.97, 5.43, 14.74)),
    ("Mast", (13.2, 14.8, 0.0, 6.0, -1.7, -0.3)),
)

FOG_COLOR = (0.52, 0.63, 0.72)


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def reset() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights):
        for item in list(block):
            block.remove(item)


def unity(x: float, y: float, z: float) -> Vector:
    """Unity (x, y, z) -> Blender: ось Y плана отрицательна, высота — Z."""
    return Vector((x, -z, y))


def add_stand_in(name: str, box) -> None:
    x0, x1, y0, y1, z0, z1 = box
    # bmesh вместо primitive_cube_add: в background-контексте active_object после
    # ops ненадёжен, а куб через bmesh создаётся без него.
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    mesh = bpy.data.meshes.new("Stand_" + name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("Stand_" + name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = unity((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5)
    obj.scale = (x1 - x0, z1 - z0, y1 - y0)      # Blender (x, глубина, высота)
    mat = bpy.data.materials.get("StandConcrete")
    if mat is None:
        mat = bpy.data.materials.new("StandConcrete")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (0.42, 0.46, 0.52, 1)
            bsdf.inputs["Roughness"].default_value = 0.8
        mat.diffuse_color = (0.42, 0.46, 0.52, 1)
    mesh.materials.append(mat)


def add_camera(name: str, eye, target, fov_deg: float):
    cam_data = bpy.data.cameras.new(name)
    # Юнидовый вертикальный FOV -> фокусное Blender: sensor 36 мм по горизонтали,
    # при 16:9 вертикаль соответствует 20.25 мм.
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 20.25
    cam_data.lens = 10.125 / math.tan(math.radians(fov_deg * 0.5))
    cam_data.clip_end = 420.0
    cam_data.clip_start = 0.1
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = eye
    cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()
    return cam


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("FrontierPreviewWorld")
    scene.world.color = FOG_COLOR
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    # STUDIO, как в остальных превью: в 5.2 у Workbench нет варианта "SUN",
    # а направленность света здесь не предмет проверки — предмет формы,
    # силуэта и того, закрыт ли горизонт. Совпадение с `Sun` в сцене
    # проверяется скриншотом в Play Mode, где свет настоящий.
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    shading.curvature_ridge_factor = 1.4
    shading.curvature_valley_factor = 1.1
    scene.display.render_aa = "8"


def render_to(path: str) -> None:
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


def _at(bearing_deg: float, dist: float, y: float) -> Vector:
    """Точка на плане Unity по азимуту и дистанции -> Blender."""
    a = math.radians(bearing_deg)
    return unity(math.sin(a) * dist, y, math.cos(a) * dist)


def views(out: str) -> None:
    eye = unity(*L.ARRIVAL_EYE)
    aim = unity(*L.ARRIVAL_AIM)
    frames = {
        # Стартовая точка прибытия: P0 -> прицел в дверь проёма.
        "depot_from_platform": (eye, aim, L.ARRIVAL_FOV),
        # Куда уходит состав: игрок после передачи управления смотрит сюда.
        "off_depot": (unity(-2.6, 1.70, -1.0), unity(-14.0, 0.8, -34.0), 50.0),
        # Линия на юг: выемка, телеграф, обрыва не видно.
        "line_south": (unity(-15.0, 2.4, -16.0), unity(-8.0, 0.4, -120.0), 46.0),
        # Тупик: рельсы уходят из-под носа и упираются в стену заноса.
        # Снимается севернее состава, а не с платформы: кузов (z до 14.30)
        # перекрывает стрелку и упор целиком, и с платформы тупик не видно
        # вовсе — это проверено кадром, а не на глаз.
        "terminus": (unity(2.0, 1.80, 20.0), unity(-26.5, 1.0, 12.5), 46.0),
        # Стена терминуса из кадра игрока: читается ли край за крышей вагона.
        "wall_from_player": (unity(0.7, 1.70, 1.0), unity(-20.0, 6.0, 30.0),
                             50.0),
        # Дорога: прорыв гребня и коридор с телеграфом. Точка съёмки — за
        # восточным краем платформы (x = 15.00) и южнее ворот (z = 5.43):
        # внутри дублёра платформы камера упиралась бы в его стенку.
        "road_corridor": (unity(16.5, 2.0, 3.0), _at(L.ROAD_BEARING_DEG, 160.0, 6.0),
                          50.0),
        # План: раскладка целиком, проверяет, что ничего не наложено внахлёст.
        "top": (unity(0.0, 250.0, 0.0), unity(0.0, 0.0, 0.0), 60.0),
    }
    for name, (eye, target, fov) in frames.items():
        cam = add_camera("Cam_" + name, eye, target, fov)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "frontier_%s.png" % name))


def prop_views(out: str, name: str) -> None:
    """Четыре общих вида одиночного пропа (когда собран только он один)."""
    targets = [o for o in bpy.context.scene.objects if o.type == "MESH"
               and not o.name.startswith("Stand_")]
    if not targets:
        return
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in targets:
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    center = (lo + hi) * 0.5
    size = hi - lo
    print("BOUNDS %s %s" % (name, tuple(round(v, 3) for v in size)))
    # Дистанция от радиуса описанной сферы, а не от самой длинной стороны:
    # иначе 48-метровый пролёт телеграфа снимался бы с 74 м и занимал пиксели.
    radius = 0.5 * math.sqrt(size.x ** 2 + size.y ** 2 + size.z ** 2)
    distance = radius / math.sin(math.radians(26.0)) + 1.5
    frames = {
        "side": (center + Vector((distance, 0.0, size.z * 0.35)), 52.0),
        "three_quarter": (center + Vector((distance * 0.72, -distance * 0.78,
                                           size.z * 0.55)), 52.0),
        "front": (center + Vector((0.0, -distance * 1.15, size.z * 0.30)), 52.0),
        "top": (center + Vector((0.0, 0.0, distance * 1.25)), 60.0),
    }
    for key, (eye, fov) in frames.items():
        cam = add_camera("Cam_%s_%s" % (name, key), eye, center, fov)
        bpy.context.scene.camera = cam
        render_to(os.path.join(out, "%s_%s.png" % (name, key)))


def hide_collider() -> None:
    """Прокси-коллайдер в превью не нужен: это вторая копия той же земли."""
    for obj in bpy.data.objects:
        if obj.name.endswith("_Collider"):
            obj.hide_render = True
            obj.hide_set(True)


def import_unity(path: str):
    """Импорт FBX и приведение меша в координаты Unity.

    Импортёр Blender не снимает зеркало по X, которое `export_static` ставит
    навстречу `bake_space_transform`, поэтому без этого путь приходил бы на
    восток вместо запада и вся раскладка в превью была бы зеркальной.
    Возврат нормалей после зеркала — обязателен, иначе Workbench светит
    внутреннюю сторону граней.
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    bpy.context.view_layer.update()
    mirror = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
    for obj in new:
        if obj.type != "MESH":
            continue
        mw = obj.matrix_world.copy()
        obj.matrix_world = Matrix.Identity(4)
        obj.data.transform(mw @ mirror)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
    return [o for o in new if o.type == "MESH"]


def place_asset(path: str, instances) -> None:
    """Расставить копии ассета по таблице раскладки.

    Копии — `obj.copy()`: меш общий, поэтому десяток кластеров леса стоит
    десяток трансформов, а не десяток мешей. Это только для рендера: в сцену
    объекты ставит человек (ADR 0005, ADR 0035), и ровно те же координаты
    переносятся в журнал плана.

    Трансформ импорта запекается в меш намеренно: `bake_space_transform` на
    выгрузке оставляет на импортированном объекте поворот +90° по X, и любая
    запись в `rotation_euler` его затирает — сорокавосьмиметровый пролёт
    телеграфа вставал вертикально. После запекания у копии остаются только
    наши координаты.
    """
    template = import_unity(path)
    for (_name, x, z, yaw) in instances:
        for src in template:
            dup = src.copy()
            dup.name = "%s_%g_%g" % (src.name, x, z)
            bpy.context.scene.collection.objects.link(dup)
            dup.location = (x, -z, L.ground_height(x, z))
            # Unity yaw (0 = на +Z, по часовой) -> Blender-Z: план отражён по Y.
            dup.rotation_euler = (0.0, 0.0, math.radians(180.0 - yaw))
    for src in template:
        bpy.data.objects.remove(src, do_unlink=True)


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Frontier")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)

    files = sorted(glob.glob(os.path.join(MODELS, "Frontier_*.fbx")))
    if not files:
        print("нет файлов Frontier_*.fbx в", MODELS)
        return 2
    by_stem = {os.path.splitext(os.path.basename(f))[0]: f for f in files}

    reset()
    # Земля и путь набраны в абсолюте раскладки — ставятся один раз и как есть.
    for stem in ("Frontier_Ground", "Frontier_Track"):
        if stem in by_stem:
            import_unity(by_stem[stem])
    hide_collider()

    groups: dict[str, list] = {}
    for row in list(L.SCENE_INSTANCES) + L.telegraph_instances():
        groups.setdefault(row[0], []).append(row)
    for stem, insts in sorted(groups.items()):
        if stem in by_stem:
            place_asset(by_stem[stem], insts)
        else:
            print("ПРОПУСК: нет файла %s.fbx" % stem)

    for name, box in STAND_INS:
        add_stand_in(name, box)
    setup_render()
    views(out)

    for stem, path in sorted(by_stem.items()):
        if "Prop" not in stem:
            continue
        reset()
        import_unity(path)
        setup_render()
        prop_views(out, stem)
    print("PREVIEWS", len(os.listdir(out)), "files in", out)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
