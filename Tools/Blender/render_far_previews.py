"""Превью дальнего слоя: читается ли силуэт с двора депо и за что цепляется глаз.

Запуск:
    blender --background --factory-startup --python Tools/Blender/render_far_previews.py
    ... -- --out AssetSource/Previews/Far --ids station kosoy

Сцена собирается теми же билдерами, что и FBX (`far_parts`), и ставится ровно
туда, куда её позовёт сцена: карточка на своих 380 м, глаз — в нуле двора на
высоте 1.7 м. Линза 25.7 мм — те же 50° по вертикали, что у камеры прибытия
(`ARRIVAL_FOV`), поэтому кадр превью это тот же кадр, что увидит игрок, а не
«красивый ракурс».

Чего здесь сознательно нет: тумана, неба `SkyNightStars` и гребня. Workbench не
умеет ни того, ни другого, а смысл слоя — как раз контраст силуэта цвета мглы
против потемневшего неба. Этот вопрос решает кадр из редактора Unity с настоящим
туманом и скайбоксом (`UNITY_MCP.md`), и он задаётся после расстановки. Здесь же
отвечают на два других: похожа ли карточка на тело с ярусами, а не на коробку, и
не съедает ли она половину горизонта.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import far_parts  # noqa: E402
import level_lib  # noqa: E402
import world_layout as W  # noqa: E402

#: Линза под вертикальный FOV камеры прибытия: 50° при сенсоре 36 мм и кадре
#: 3:2 — `lens = 0.5 * 36 / aspect / tan(fov/2)`.
LENS = 0.5 * 36.0 / 1.5 / math.tan(math.radians(W.CAM_FOV * 0.5))

#: Глаз наблюдателя во дворе депо: Unity (x, y, z).
EYE_UNITY = (0.0, 1.7, 0.0)

#: Куда смотреть: азимут и высота прицела. `throat` берёт оба силуэта
#: северо-востока (45° и 68°) в один 50-градусный кадр, `south` — остов
#: «Южный» за спиной игрока.
FIELDS = {
    "throat": (56.0, 45.0),
    "south": (225.0, 25.0),
}


def to_blender(unity_x: float, unity_y: float, unity_z: float) -> Vector:
    """Unity -> оси Blender для `export_static`: (x, y, z) = (-u.x, -u.z, u.y)."""
    return Vector((-unity_x, -unity_z, unity_y))


def reset() -> None:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                  bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def add_camera(name, eye, target, lens):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(eye)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam


def add_ground(materials) -> None:
    """Снежная плита под слоем: только чтобы было видно, где кончается земля."""
    cards = {c["k"]: c for c in W.far_layer() if c["draw"]}
    reach = max(math.hypot(c["x"], c["z"]) + c["w"] for c in cards.values())
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, -0.6), (2.0 * reach, 2.0 * reach, 1.0), "Level_SnowDeep",
          bevel=0.0)
    b.build("PreviewGround")


def setup_render() -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 800
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("FarSky")
    # Ровно цвет мглы: тогда превью хотя бы не врёт про яркость тела, а спорить
    # с небом будет уже кадр из редактора.
    scene.world.color = (0.52, 0.63, 0.72)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studio_light = "paint.sl"
    shading.color_type = "MATERIAL"
    shading.show_shadows = False
    scene.display.render_aa = "8"


def _place(card, materials):
    """Карточка на своём месте в осях превью (Blender), основание на нуле."""
    obj = far_parts.build_card(materials, card)
    pos = to_blender(card["x"], 0.0, card["z"])
    obj.location = (pos.x, pos.y, 0.0)
    return obj


def main() -> int:
    level_lib.configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(
        HERE, "..", "..", "AssetSource", "Previews", "Far")
    out = os.path.abspath(out)
    wanted = {a.lower() for a in argv if a.lower() in far_parts.FILENAMES}
    cards = [c for c in W.far_layer() if c["draw"]
             and (not wanted or c["k"] in wanted)]
    if not cards:
        print("нет читаемых карточек")
        return 1

    reset()
    materials = level_lib.make_level_materials()
    setup_render()
    os.makedirs(out, exist_ok=True)
    eye = to_blender(*EYE_UNITY)
    count = 0

    # Кадр с двора: все читаемые силуэты стоят вместе, как в сцене.
    for obj in list(bpy.data.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    add_ground(materials)
    for card in cards:
        _place(card, materials)
    for (name, (bearing, aim_y)) in FIELDS.items():
        (bx, bz) = W.polar(W.FAR_CARD_DISTANCE, bearing, base=(0.0, 0.0))
        add_camera(name, eye, to_blender(bx, aim_y, bz), LENS)
        path = os.path.join(out, "field_%s.png" % name)
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("RENDER", os.path.basename(path))
        count += 1

    # Форма каждой карточки в упор: ярусы, обходы, наклон.
    for card in cards:
        for obj in list(bpy.data.objects):
            if obj.type == "MESH" and obj.name != "PreviewGround":
                bpy.data.objects.remove(obj, do_unlink=True)
        obj = _place(card, materials)
        (ux, uz) = (card["x"], card["z"])
        # Глаз — на продолжении луча за депо, на высоте середины тела: кадр
        # показывает форму, а не перспективу двора.
        back = 3.4 * card["h"] / math.hypot(ux, uz)
        eye_u = (ux * (1.0 + back), 0.5 * card["h"], uz * (1.0 + back))
        add_camera("shape", to_blender(*eye_u), to_blender(ux, 0.5 * card["h"], uz),
                   LENS)
        path = os.path.join(out, "shape_%s.png" % card["k"])
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("RENDER", os.path.basename(path),
              "габарит %.2f x %.2f м" % (obj.dimensions.x, obj.dimensions.z))
        count += 1

    print("PREVIEWS %d" % count)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
