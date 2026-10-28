"""Ядро сборки ездовых ходунов FrostSpace на ригах Ultimate Space Kit.

Пак Quaternius даёт **скелет и клипы** (`Idle`/`Walk`/`Run`); корпус строится нашим
скриптом в стиле FrostSpace — как у роботов (ADR 0001, 0017). Исходный меш пака в
Unity не едет: он органический «мех-костюм» с атласной текстурой, а нам нужен
фасетный корпус из палитры `robot_lib`.

Что делает модуль:
- вынимает FBX из zip-пака в `AssetSource/_staging/space_kit` (zip в git не идёт);
- импортирует, убирает исходный меш, приводит размер к целевому росту и пол на z=0;
- сужает стойку ног (`narrow_stance`): у пака бёдра на ±1.2 при росте 2.75 — в мире
  FrostSpace это «раскоряка», не проходящая между постройками;
- берёт из пака три клипа и переименовывает их в `idle_loop` / `walk_loop` / `run_loop`
  (имена ролей, как у роботов и животных);
- проверяет и при необходимости снимает горизонтальный ход корня: клипы обязаны быть
  in-place (ADR 0022/0023), актёра двигает контроллер;
- меряет скорость стопы в контакте для Walk и Run — по ней `Mech.cs` подгоняет
  `Animator.speed`, чтобы стопа не скользила.

Оси после подготовки: вперёд −Y, вверх +Z, вбок +X (как у роботов); в Unity это
+Z forward, +Y up. Рост и центр уже в метрах, масштаб объекта — 1.
"""

from __future__ import annotations

import math
import os
import statistics
import zipfile

import bpy
from mathutils import Matrix, Quaternion, Vector

import animal_lib
import robot_lib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING = os.path.join(ROOT, "AssetSource", "_staging", "space_kit")
OUT = os.path.join(ROOT, "Assets", "_Project", "Content", "Models", "Mechs")

#: Пак и шаблон члена архива. Путь к zip — внутри `AssetSource/`, как у животных.
PACK = os.path.join(
    ROOT, "AssetSource",
    "Ultimate Space Kit - March 2023-20261024T221803Z-1-001.zip")
MEMBER = "Ultimate Space Kit - March 2023/Characters/FBX/{source}.fbx"

#: Реестр ездовых ходунов.
#:
#: Скелет у всех один — `mech`: `Body → Torso → Chest → Neck → Head` плюс ноги,
#: рук нет. Гуманоидного пятого (человеческий риг) в партии нет: его корпус
#: собирался отдельной веткой дизайна и на общий силуэт не работал.
#: target — рост в метрах: ходун заметно выше игрового робота, но не гигант.
#: stance — целевой полуразнос бёдер, м (см. `narrow_stance`).
#: accent — роль палитры, которой юнит опознаётся (сама геометрия важнее цвета).
#: gait — профиль шага из `GAIT_STYLES`. Профиль **один на всю партию**
#: (`waddle`): владелец выбрал шаг PandaWalker и попросил сделать его общим, а
#: остальных ходунов убрать. Скорость контроллера в Unity не задаётся здесь: она
#: **меряется** по клипу (`measure_speed`) и уходит в манифест, иначе
#: `Animator.speed` пришлось бы подкручивать вслепую и стопа поехала бы по снегу.
MECHS = {
    "PandaWalker": dict(
        source="Mech_RaeTheRedPanda", rig="mech", target=2.60, stance=0.62, leg_lift=0.26,
        idle="Idle", walk="Walk", run="Run", accent="accent",
        gait="waddle"),
}

#: Клипы, которые едут в Unity. Порядок = порядок NLA-полос, он же порядок клипов
#: в FBX; постпроцессор переименует их в `Idle` / `Walk` / `Run`.
CLIP_KEYS = ("idle", "walk", "run")
CLIP_NAMES = {"idle": "idle_loop", "walk": "walk_loop", "run": "run_loop"}

#: Допустимый ход корня в клипе, м: больше — клип не in-place, и это надо чинить.
#: Совпадает с `IN_PLACE_TOLERANCE` чекера: чинить то, что проверка считает нормой,
#: значит менять позу ради числа.
ROOT_DRIFT_TOLERANCE = 0.02

#: Частота кадров пака (проверено по GLTF: Walk 25 кадров = 0.833 с).
FPS = 30


# ------------------------------------------------------------------ источник

def extract(name: str) -> str:
    """Достаёт FBX из zip-пака в staging и возвращает путь."""
    cfg = MECHS[name]
    out = os.path.join(STAGING, cfg["source"] + ".fbx")
    if not os.path.exists(out):
        os.makedirs(os.path.dirname(out), exist_ok=True)
        member = MEMBER.format(source=cfg["source"])
        with zipfile.ZipFile(PACK) as archive:
            with open(out, "wb") as handle:
                handle.write(archive.read(member))
        print("EXTRACT %s <- %s" % (out, member))
    return out


def _mesh_bbox(meshes):
    """Габарит мешей по вершинам, в метрах мира.

    `obj.bound_box` берётся не всегда: после повторного импорта в том же
    процессе Blender отдаёт границы, посчитанные до обновления depsgraph, и
    масштаб уезжает в 2–3 раза (так и случилось на человеческом риге). Вершины
    честнее и стоят недорого — их сотни.
    """
    bpy.context.view_layer.update()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in meshes:
        matrix = obj.matrix_world
        for vertex in obj.data.vertices:
            point = matrix @ vertex.co
            for i in range(3):
                lo[i] = min(lo[i], point[i])
                hi[i] = max(hi[i], point[i])
    return lo, hi


# ------------------------------------------------------------------ загрузка

def narrow_stance(arm, target_half_width: float) -> float:
    """Сужает стойку ног до целевого полуразноса бёдер, не трогая клипы.

    Правятся только rest-точки: клипы — это вращения костей, поэтому походка
    остаётся той же, а ноги просто стоят ближе. Стопа пака — отдельная кость
    (IK-цель, без родителя), её тоже надо сузить, иначе ступни разъедутся.

    **Голова кости `use_connect` не масштабируется.** У привязанной кости она
    вычисляется из хвоста родителя: если умножить её ещё раз, сустав уедет
    дважды (так и было — фактор 0.433 давал 0.19 вместо 0.44), бедро вставало
    наискось, а стопа оказывалась в 25 см от голени. Поэтому двигаем хвост
    каждой кости и голову только у непривязанных.
    """
    names = []
    for bone in arm.data.bones:
        base = bone.name
        if base.startswith(("UpperLeg", "LowerLeg", "Foot", "PoleTarget",
                            "Knee", "Ankle")):
            names.append(base)
    if not names:
        return 1.0

    current = max(abs((arm.matrix_world @ arm.data.bones[n].head_local).x)
                  for n in names if n.startswith("UpperLeg"))
    factor = target_half_width / max(current, 1e-4)
    if abs(factor - 1.0) < 1e-3:
        return 1.0

    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name in names:
        bone = arm.data.edit_bones.get(name)
        if bone is None:
            continue
        bone.tail.x *= factor
        if not bone.use_connect:
            bone.head.x *= factor
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    print("STANCE %s x%.3f (half-width %.3f -> %.3f)" % (
        arm.name, factor, current, target_half_width))
    return factor


def raise_body(arm, delta: float) -> float:
    """Поднимает таз и весь корпус на `delta`, оставляя ступни на полу.

    Зачем: у пака таз стоит на 1.448 при ноге 1.36 от стопы — запаса вылета
    почти нет, и на шаге корпус приходилось сажать на 10–15 см ниже rest, иначе
    IK выпрямлял ногу в палку. Ходун в таком приседе читается крадущимся, а не
    шагающим. Поднимаем таз: `straighten_stance` после этого строит колено по
    новому расстоянию «бедро—голеностоп», и нога становится длиннее на ту же
    величину. Корпус едет вверх вместе с тазом, поэтому кабина, кресло и броня
    остаются на своих местах относительно бёдер — меняется только высота роста.
    """
    if delta <= 0.0:
        return 0.0
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = arm.data.edit_bones
    for side in ("L", "R"):
        bone = edit.get("UpperLeg." + side)
        if bone is not None:
            bone.head.z += delta
    # Позвоночник — вместе с тазом: иначе кабина осталась бы на прежней высоте
    # и повисла между ног. Двигается только `Torso`: его хвост совпадает с
    # головой `Chest`, и цепочка `Chest → Neck → Head` едет за ним сама.
    # (Если сдвинуть каждую кость отдельно, они уезжают дважды — проверено.)
    torso = edit.get("Torso")
    if torso is not None:
        torso.head.z += delta
        torso.tail.z += delta
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    print("LIFT %s таз и корпус +%.3f м" % (arm.name, delta))
    return delta


def load(name: str):
    """Импортирует риг пака, убирает его меш и приводит модель к метрам/+Z.

    Возвращает `(armature, meta)`. Исходный меш удаляется сразу после замера
    габарита: корпус строит дизайн, а не пак.
    """
    cfg = MECHS[name]
    robot_lib.clear_scene()
    bpy.context.scene.render.fps = FPS
    bpy.ops.import_scene.fbx(filepath=extract(name))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not meshes or not arms:
        raise RuntimeError("%s: в FBX нет меша или арматуры" % name)
    arm = arms[0]
    lo, hi = _mesh_bbox(meshes)
    size = hi - lo
    source_size = (size.x, size.y, size.z)
    scale = cfg["target"] / max(size.z, 1e-6)

    for obj in meshes:
        bpy.data.objects.remove(obj, do_unlink=True)

    # Пивот — середина опоры (между стопами), а не центр габарита: у ходуна
    # подошвы уезжают назад относительно кабины, и центр габарита ставил бы
    # капсулу `CharacterController` мимо ног.
    foot_points = [arm.matrix_world @ arm.data.bones[n].head_local
                   for n in ("Foot.L", "Foot.R") if n in arm.data.bones]
    if not foot_points:
        foot_points = [Vector(((lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5, 0.0))]
    anchor = sum(foot_points, Vector((0.0, 0.0, 0.0))) / len(foot_points)

    # Масштаб и центр — в данные арматуры, а не в объект: иначе Unity добавит
    # компенсацию корня и геометрия приедет крупнее задуманного (ASSETS.md).
    arm.scale = arm.scale * scale
    arm.location.x -= anchor.x * scale
    arm.location.y -= anchor.y * scale
    arm.location.z -= lo.z * scale
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()

    # Масштаб вкатан в данные арматуры — значит, и ключи локации клипов надо
    # привести к тем же метрам. Иначе непривязанная стопа (IK-цель) остаётся
    # в старом масштабе и уезжает от голени.
    scale_clip_locations(scale)

    stance_factor = (narrow_stance(arm, cfg["stance"])
                     if cfg.get("stance") else 1.0)
    # Подошва короче паковой лапы: кость стопы укорачивается до носка до того,
    # как по её точкам построятся броня и траектория переката.
    shape_feet(arm)
    # Таз поднимается до печки клипов: длина ноги считается уже по новой высоте.
    raise_body(arm, cfg.get("leg_lift", 0.0))

    role_map = roles(arm)
    meta = {
        "name": name,
        "cfg": cfg,
        "scale": scale,
        "stance_factor": stance_factor,
        "source_size": tuple(round(v, 3) for v in source_size),
        "height": round(cfg["target"], 3),
        "rig": cfg["rig"],
        "roles": role_map,
        "layout": layout(arm, role_map),
    }
    print("LOAD %s source=%s scale=%.4f rig=%s" % (
        name, meta["source_size"], scale, cfg["rig"]))
    return arm, meta


# ------------------------------------------------------------------ роли

def roles(arm) -> dict:
    """Семантические роли костей: ими пользуются и дизайн, и проверки."""
    return {
        "root": "Body",
        "body": "Body",
        "hips": "Body",
        "abdomen": "Torso",
        "torso": "Torso",
        "chest": "Chest",
        "neck": "Neck",
        "head": "Head",
        "legs": {
            "l": ("UpperLeg.L", "LowerLeg.L", "Foot.L"),
            "r": ("UpperLeg.R", "LowerLeg.R", "Foot.R"),
        },
        "arms": {},
        "feet": ["Foot.L", "Foot.R"],
    }


def layout(arm, role_map: dict) -> dict:
    """Опорные точки корпуса в метрах: по ним дизайн ставит кабину и броню."""
    points = robot_lib.rig_points(arm)

    def head(bone):
        return tuple(round(v, 3) for v in points[bone][0])

    def tail(bone):
        return tuple(round(v, 3) for v in points[bone][1])

    return {
        "hip": head("UpperLeg.L"),
        "knee": head("LowerLeg.L"),
        "ankle": head("Foot.L"),
        "toe": tail("Foot.L"),
        "body": head(role_map["body"]),
        "seat_frame": head(role_map["torso"]),
        "chest": head(role_map["chest"]),
        "chest_tail": tail(role_map["chest"]),
        "neck": head(role_map["neck"]),
        "head": head(role_map["head"]),
        "head_tail": tail(role_map["head"]),
    }


# ------------------------------------------------------------------ клипы

def _action_fcurves(action):
    """Все кривые действия: в Blender 5.x они лежат в слоях, не в `action.fcurves`."""
    curves = list(getattr(action, "fcurves", []))
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def scale_clip_locations(scale: float) -> int:
    """Масштабирует `location`-кривые всех клипов на `scale`.

    `load()` вкатывает масштаб модели в данные арматуры (`transform_apply`), и
    локальные единицы костей становятся метрами. Ключи локации при этом остаются
    в прежних единицах: смещение стопы 1.889 превращалось в 1.889 м вместо 1.6,
    и ступня уезжала от голени на 30 см. Стопа пака — **отдельная кость без
    родителя** (IK-цель), её позиция задаётся локацией в собственных осях, поэтому
    без этой правки она единственная из всего скелета жила в старом масштабе.
    """
    if abs(scale - 1.0) < 1e-4:
        return 0
    touched = 0
    for action in bpy.data.actions:
        for curve in _action_fcurves(action):
            if not curve.data_path.endswith(".location"):
                continue
            for key in curve.keyframe_points:
                key.co[1] *= scale
                key.handle_left[1] *= scale
                key.handle_right[1] *= scale
            touched += 1
    print("SCALE_LOCATIONS x%.4f кривых=%d" % (scale, touched))
    return touched


def _action_by_role(role: str):
    """Действие пака по роли клипа: `Idle`, `Walk`, `Run`.

    Имя в FBX — `Armature|Armature|Walk`, поэтому сравниваем хвост после `|`.
    """
    wanted = role.lower()
    for action in bpy.data.actions:
        tail = action.name.split("|")[-1].strip().lower()
        if tail == wanted:
            return action
    for action in bpy.data.actions:
        if action.name.lower().split("|")[-1].startswith(wanted):
            return action
    return None


def prepare_clips(arm, meta) -> dict:
    """Три take (`idle_loop`/`walk_loop`/`run_loop`), NLA и обрезка стоячего хвоста."""
    cfg = meta["cfg"]
    arm.animation_data_create()
    for track in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(track)

    clips = {}
    for key in CLIP_KEYS:
        action = _action_by_role(cfg[key])
        if action is None:
            raise RuntimeError("%s: нет клипа %s (есть %s)" % (
                meta["name"], cfg[key],
                sorted(a.name.split("|")[-1] for a in bpy.data.actions)))
        action.name = CLIP_NAMES[key]
        start, end = (int(round(v)) for v in action.frame_range)
        stop = animal_lib._static_tail(arm, action, start, end)
        if stop is not None:
            print("TRIM %s %s: стоячий хвост %d..%d срезан" % (
                meta["name"], key, stop, end))
            end = stop
        if animal_lib._pose_equal(arm, action, start, end):
            end = max(start + 1, end - 1)
        if hasattr(action, "use_frame_range"):
            action.use_frame_range = True
            action.frame_start = start
            action.frame_end = end
        clips[key] = {"action": action, "start": start, "end": end,
                      "frames": end - start,
                      "seconds": round((end - start) / FPS, 3)}
        print("CLIP %s %s frames=%d..%d (%.3f c)" % (
            meta["name"], key, start, end, clips[key]["seconds"]))

    for key in CLIP_KEYS:
        info = clips[key]
        track = arm.animation_data.nla_tracks.new()
        track.name = CLIP_NAMES[key]
        strip = track.strips.new(CLIP_NAMES[key], info["start"], info["action"])
        strip.name = CLIP_NAMES[key]
        strip.frame_start = info["start"]
        strip.frame_end = info["end"]
        strip.action_frame_start = info["start"]
        strip.action_frame_end = info["end"]
        track.mute = False

    arm.animation_data.action = None
    meta["clips"] = clips
    return clips


# --------------------------------------------------------------- походка

#: Куда выгибается колено: назад — птичья (обратноколенная) стойка шагохода.
KNEE_POLE = Vector((0.0, 1.0, 0.0))

#: Насколько колено вынесено вперёд от линии «бедро—голеностоп» в rest-позе, м.
#:
#: У пака колено уходит вперёд на 0.93 м при базе 1.36 м: нога складывается
#: лягушачьим зигзагом, и на шаге это читается как присед с подскоком. Меньше
#: вынос — прямее нога и меньше запас вылета, поэтому шаг короче: это главный
#: размен формы и длины шага, и он у каждого профиля свой.
KNEE_BULGE = 0.36

#: Какая доля вылета ноги разрешена стопе. Ровно 1.0 — колено выпрямляется в
#: палку и нога «щёлкает» на краю шага, поэтому не 1.0. 0.99 при этом штатно
#: разгибает ногу на касании и отрыве: так и ходят — прямая опора под весом.
LEG_REACH = 0.99

#: Подошва ступни от голеностопа, м: пятка назад, носок вперёд. Кость `Foot`
#: укорачивается до носка (`shape_feet`), поэтому кость, броня и точки опоры в
#: перекате — одни и те же числа. 0.56 м на ногу 1.54: ступня шагохода, а не лыжа
#: (0.775 м читались именно лыжей — подошва была длиной с голень).
FOOT_HEEL_BACK = 0.14
FOOT_TOE_AHEAD = 0.42

#: Запас под колено, м: таз не поднимается выше, чем нога может достать с
#: согнутым коленом. Без него IK выпрямляет ногу на краю шага в палку, а
#: зажатая цель — это уже съехавшая подошва, а не поза.
KNEE_SLACK = 0.02


def _sole_points(arm, side: str):
    """Голеностоп, пятка и носок подошвы в rest-позе, мировые координаты.

    Стопа в риге пака — кость без родителя: её голова это голеностоп, хвост —
    носок. Подошва лежит на `z = 0`, поэтому точки опоры берутся на земле, а не
    на высоте голеностопа: перекат вращает ступню **вокруг края подошвы**, иначе
    пятка или носок уходят под пол.
    """
    ankle = arm.data.bones["Foot." + side].head_local.copy()
    return (ankle,
            Vector((ankle.x, ankle.y + FOOT_HEEL_BACK, 0.0)),
            Vector((ankle.x, ankle.y - FOOT_TOE_AHEAD, 0.0)))


def shape_feet(arm) -> None:
    """Укорачивает кость `Foot` до носка подошвы.

    У пака кость стопы длиной 0.535 м — это «лапа» под его собственный меш. Наша
    подошва короче, и если оставить кость как есть, броня, точки опоры переката и
    хвост кости разъезжаются тремя разными числами. Теперь хвост кости и есть
    носок, а `FOOT_HEEL_BACK`/`FOOT_TOE_AHEAD` — единственный источник размеров.
    """
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for side in ("L", "R"):
        bone = arm.data.edit_bones.get("Foot." + side)
        if bone is None:
            continue
        bone.tail = Vector((bone.head.x, bone.head.y - FOOT_TOE_AHEAD,
                            bone.head.z))
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()


def _tilt_matrix(tilt_deg: float) -> Matrix:
    """Поворот подошвы вокруг оси X: `tilt > 0` поднимает носок."""
    return Matrix.Rotation(math.radians(-tilt_deg), 3, "X")


#: Профили походки: у каждого юнита своя пластика, а не общий шаг на всех.
#:
#: `knee_bulge` — форма ноги в rest: колено вынесено вперёд от линии
#: «бедро—голеностоп». Это единственный рычаг формы ноги: `straighten_stance`
#: правит rest до печки клипов, а меш, кабина и якоря едут за костями. Больше
#: вынос — глубже присед и больше запас хода; меньше — нога читается почти
#: прямой, но запас вылета падает, поэтому шаг у такой ноги короче.
#:
#: `frames` — длина цикла, `duty` — доля цикла, которую нога стоит на земле,
#: `stride` — путь подошвы по земле за стойку, м. Скорость земли =
#: `stride / (duty * frames / FPS)`, и она упирается в геометрию: вынос стопы
#: не может быть больше вылета ноги. Зажимы считает `author_gait` и падает,
#: если они попали в стойку.
#:
#: `heel_strike` — насколько носок поднят в момент касания, `heel_off` — насколько
#: пятка поднята в конце стойки, `toe_clear` — добавочный подъём носка на замахе.
#: Перекат и есть то, что отличает шагоход от скользящей коробки: подошва
#: катится с пятки через всю плиту на носок, а не едет плоско.
#:
#: `heel_roll`/`toe_roll` — доли стойки на перекат через пятку и через носок.
#: `lift` — клиренс стопы на замахе, `track` — насколько стопа заносится внутрь
#: по дуге, `crouch` — насколько таз опущен против rest.
#:
#: `bob` — амплитуда высоты таза, `sway` — боковой сдвиг (≤0.008: размах ловит
#: `flatten_root`), `roll` — крен корпуса, `lean` — наклон вперёд, `twist` —
#: доворот таза, `shoulder` — контр-доворот груди (плечи против таза).
_GAIT_IDLE = dict(frames=100, stride=0.0, duty=1.0, lift=0.0,
                  heel_strike=0.0, heel_off=0.0, toe_clear=0.0,
                  heel_roll=0.0, toe_roll=0.0, track=0.0, breath=2.0,
                  crouch=0.012, bob=0.010, sway=0.003, roll=0.5, lean=1.5,
                  twist=0.6, shoulder=0.4)

#: Профиль походки. **Один на всю партию.** Владелец выбрал шаг PandaWalker
#: (`waddle`) и попросил сделать его общим для шагоходов, а остальных ходунов
#: убрать: общий шаг — это и есть «как у PandaWalker», и он не разъезжается при
#: добавлении нового корпуса. Ключ в `MECHS` остался, чтобы второй профиль можно
#: было завести данными, а не веткой кода.
GAIT_STYLES = {
    "waddle": dict(
        note="валкий шаг: широкий перенос веса креном, длинная двойная опора",
        knee_bulge=0.40, leg_reach=0.98,
        idle=dict(_GAIT_IDLE),
        walk=dict(frames=36, stride=1.25, duty=0.68, lift=0.15,
                  heel_strike=3.0, heel_off=12.0, toe_clear=9.0,
                  heel_roll=0.20, toe_roll=0.24, track=0.05,
                  crouch=0.070, bob=0.062, sway=0.008, roll=6.5, lean=3.5,
                  twist=2.0, shoulder=1.4),
        run=dict(frames=22, stride=1.40, duty=0.46, lift=0.26,
                 heel_strike=4.0, heel_off=16.0, toe_clear=12.0,
                 heel_roll=0.14, toe_roll=0.26, track=0.04,
                 crouch=0.085, bob=0.070, sway=0.008, roll=3.5, lean=5.0,
                 twist=2.6, shoulder=2.0)),
}

#: Стиль по умолчанию для юнита без поля `gait` в `MECHS`.
DEFAULT_GAIT = "waddle"

#: Сколько кадров стойки IK вправе зажать цель. Зажатая цель — подошва едет
#: по снегу быстрее или медленнее `clip_speed`, поэтому это ошибка сборки,
#: а не предупреждение.
STANCE_CLAMP_LIMIT = 2


def gait_style(meta) -> dict:
    """Профиль походки юнита: ключ `cfg["gait"]` в `GAIT_STYLES`."""
    name = meta["cfg"].get("gait", DEFAULT_GAIT)
    style = GAIT_STYLES.get(name)
    if style is None:
        raise RuntimeError("%s: неизвестный стиль походки %r (есть %s)" % (
            meta["name"], name, sorted(GAIT_STYLES)))
    return style


# ------------------------------------------------------- траектория подошвы

def _stance_foot(cfg: dict, u: float, ankle0, heel0, toe0):
    """Голеностоп и наклон подошвы в стойке; `u ∈ [0, 1]` — доля стойки.

    Подошва катится, а не едет плоско: сначала опускается с пятки на всю плиту,
    потом стоит плоско, потом поднимается через носок. Вращение идёт **вокруг
    края подошвы на земле**, поэтому опорная кромка остаётся на `z = 0` — ступня
    не проваливается и не висит.

    Точка опоры при этом едет назад линейно: клип in-place, земля уходит под
    ходуном с постоянной скоростью, и опора обязана двигаться с ней синхронно —
    иначе стопа скользит и `Animator.speed` в Unity подкручивать нечем.
    """
    slide = cfg["stride"] * (u - 0.5)
    ankle_flat = Vector((ankle0.x, ankle0.y + slide, ankle0.z))
    heel_roll = cfg.get("heel_roll", 0.0)
    toe_roll = cfg.get("toe_roll", 0.0)
    if heel_roll > 1e-6 and u < heel_roll:
        tilt = cfg["heel_strike"] * (1.0 - u / heel_roll)
        pivot = Vector((heel0.x, heel0.y + slide, 0.0))
    elif toe_roll > 1e-6 and u > 1.0 - toe_roll:
        tilt = -cfg["heel_off"] * ((u - (1.0 - toe_roll)) / toe_roll)
        pivot = Vector((toe0.x, toe0.y + slide, 0.0))
    else:
        return ankle_flat, 0.0
    return pivot + _tilt_matrix(tilt) @ (ankle_flat - pivot), tilt


def _foot_pose(cfg: dict, phase: float, ankle0, heel0, toe0):
    """Голеностоп и наклон подошвы на фазе цикла `phase ∈ [0, 1)`.

    Замах — одна плавная дуга от отрыва до следующего касания: горизонталь по
    `smoothstep` (стопа разгоняется и тормозит), вертикаль — колокол клиренса,
    наклон плавно переходит от «пятка поднята» к «носок поднят». Стопа заносится
    по дуге внутрь (`track`): иначе ноги идут параллельными рельсами, и силуэт
    читается тележкой.
    """
    if cfg["stride"] <= 1e-6:
        return Vector(ankle0), 0.0
    duty = cfg["duty"]
    if phase < duty:
        return _stance_foot(cfg, phase / duty, ankle0, heel0, toe0)
    s = (phase - duty) / max(1.0 - duty, 1e-6)
    start, tilt_start = _stance_foot(cfg, 1.0, ankle0, heel0, toe0)
    end, tilt_end = _stance_foot(cfg, 0.0, ankle0, heel0, toe0)
    ease = s * s * (3.0 - 2.0 * s)
    hump = math.sin(math.pi * s)
    ankle = start.lerp(end, ease)
    ankle.z += cfg["lift"] * hump ** 1.3
    ankle.x -= math.copysign(1.0, ankle0.x) * cfg.get("track", 0.0) * hump
    tilt = (tilt_start + (tilt_end - tilt_start) * ease
            + cfg["toe_clear"] * hump ** 1.2)
    return ankle, tilt


def _height_wave(cfg: dict, t: float) -> float:
    """Профиль высоты таза за цикл: 1 — верх, 0 — низ.

    Шаг: максимум на середине стойки — корпус проходит над выпрямленной опорой,
    минимум в двойной опоре, когда обе ноги разведены. Бег наоборот: просадка
    на середине стойки (амортизация) и подъём в полёте, поэтому волна считается
    от фазы стойки, а не от `sin`.
    """
    if cfg["stride"] <= 1e-6:
        # Покой: ровное дыхание, два вдоха на цикл.
        return 0.5 - 0.5 * math.cos(2.0 * math.pi * cfg.get("breath", 2.0) * t)
    duty = cfg["duty"]
    if duty < 0.5:
        return 0.5 - 0.5 * math.cos(4.0 * math.pi * (t - duty * 0.5))
    best = 0.0
    for offset in (0.0, 0.5):
        phase = (t + offset) % 1.0
        if phase < duty:
            best = max(best, math.sin(math.pi * phase / duty))
    return best


def _wave_bounds(cfg: dict) -> tuple:
    """Минимум и максимум волны за цикл: шаг с длинной двойной опорой никогда
    не доходит до нуля, и без нормировки боб оказался бы втрое меньше заданного."""
    values = [_height_wave(cfg, index / 96.0) for index in range(96)]
    low, high = min(values), max(values)
    if high - low < 1e-6:
        return 0.0, 1.0
    return low, high


def straighten_stance(arm, bulge: float = KNEE_BULGE) -> float:
    """Выпрямляет ноги в rest-позе: колено подтянуто к линии бедро—голеностоп.

    У пака стойка обратноколенная: колено вынесено вперёд на 0.93 м, голень
    возвращается назад — нога читается лягушачьим зигзагом, а на шаге корпус
    приседает и подпрыгивает. Здесь бедро и голеностоп остаются на месте
    (ступня стоит на полу, кабина и якоря посадки не съезжают), а колено
    подтягивается к линии между ними: кости укорачиваются, нога становится
    колонной с живым суставом. Поэтому контрольная база `LEG_REACH` считается
    уже по новым длинам — их возвращает эта же функция в лог.

    Клипы — это вращения костей, поэтому они остаются валидными; походка всё
    равно печётся заново в `author_gait()`.
    """
    changed = 0
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = arm.data.edit_bones
    for side in ("L", "R"):
        upper = edit.get("UpperLeg." + side)
        lower = edit.get("LowerLeg." + side)
        if upper is None or lower is None:
            continue
        hip = upper.head.copy()
        ankle = lower.tail.copy()
        chord = ankle - hip
        distance = chord.length
        if distance < 1e-5:
            continue
        direction = chord / distance
        pole = KNEE_POLE - direction * KNEE_POLE.dot(direction)
        if pole.length < 1e-6:
            continue
        pole.normalize()
        knee = hip + direction * (distance * 0.5) + pole * bulge
        upper.tail = knee
        lower.head = knee
        lower.tail = ankle
        changed += 1
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    lengths = [_bone_length(arm.data.bones["UpperLeg." + side])
               + _bone_length(arm.data.bones["LowerLeg." + side])
               for side in ("L", "R")
               if "UpperLeg." + side in arm.data.bones]
    print("STANCE %s: ноги выпрямлены (колено %.2f м), длина ноги %s, изменено %d"
          % (arm.name, bulge,
             "/".join("%.3f" % value for value in lengths), changed))
    return bulge


def _bone_length(bone) -> float:
    return (bone.tail_local - bone.head_local).length


def _curve_bags(action) -> list:
    """Контейнеры кривых действия: в Blender 5.x это channelbags слоёв."""
    bags = [bag for layer in getattr(action, "layers", [])
            for strip in getattr(layer, "strips", [])
            for bag in getattr(strip, "channelbags", [])]
    if bags:
        return bags
    return [action] if hasattr(action, "fcurves") else []


def _curves(bags, path: str, count: int) -> list:
    """Кривые свойства по индексам; недостающие создаются на месте.

    После `_prune` у действия остаются только наши свойства, а стопе и корпусу
    их может не быть вовсе — печь было бы некуда, поэтому каналы заводятся сами.
    """
    found = {}
    for bag in bags:
        for curve in bag.fcurves:
            if curve.data_path == path and curve.array_index not in found:
                found[curve.array_index] = curve
    for index in range(count):
        if index not in found:
            found[index] = bags[0].fcurves.new(data_path=path, index=index)
    return [found[index] for index in range(count)]


def _prune(action, keep: set) -> int:
    """Убирает из действия кривые, которые печёт `author_gait`.

    Клипы пака описывают всю его фигуру: присед, раскачку корпуса и лягушачий
    боб. Если их оставить, авторская походка ног будет спорить с ними, поэтому в
    действии остаются только наши свойства, а всё прочее встаёт в rest-позу.
    """
    removed = 0
    for bag in _curve_bags(action):
        for curve in list(getattr(bag, "fcurves", [])):
            if curve.data_path in keep:
                continue
            bag.fcurves.remove(curve)
            removed += 1
    return removed


def _bake(curve, start: int, end: int, value) -> None:
    """Переписывает кривую ключами на каждом кадре диапазона.

    Точки удаляются по одной с перечитыванием ссылки: после первого `remove`
    остальные ссылки из `list()` становятся невалидными, и Blender отвечает
    «Keyframe not in F-Curve».

    Интерполяция — **линейная**: ключ стоит на каждом кадре, то есть кривая уже
    содержит ровно те значения, что мы посчитали. Безье между такими ключами
    добавляет перелёт на смене направления, и на быстром клипе это читается как
    дрожь накладки на суставе.
    """
    while len(curve.keyframe_points) > 0:
        curve.keyframe_points.remove(curve.keyframe_points[0])
    for frame in range(start, end + 1):
        curve.keyframe_points.insert(frame, value(frame))
    for point in curve.keyframe_points:
        point.interpolation = "LINEAR"
    curve.update()


def _solve_knee(hip, ankle, upper: float, lower: float, pole):
    """Позиция колена для двухзвенной ноги (та же формула, что в `RiderPose`)."""
    to_target = ankle - hip
    distance = to_target.length
    if distance < 1e-5:
        return None
    reach = min(max(distance, abs(upper - lower) + 1e-3), upper + lower - 1e-3)
    direction = to_target / distance
    along = (upper * upper - lower * lower + reach * reach) / (2.0 * reach)
    height = math.sqrt(max(0.0, upper * upper - along * along))
    plane = pole - direction * pole.dot(direction)
    if plane.length < 1e-6:
        return hip + direction * along
    plane.normalize()
    return hip + direction * along + plane * height


def _aim(pb, point) -> None:
    """Поворот кости вокруг её головы так, чтобы хвост смотрел в точку."""
    head = pb.head.copy()
    current = pb.tail - head
    desired = point - head
    if current.length < 1e-6 or desired.length < 1e-6:
        return
    delta = current.rotation_difference(desired).to_matrix().to_4x4()
    pb.matrix = (Matrix.Translation(head) @ delta
                 @ Matrix.Translation(-head) @ pb.matrix)
    bpy.context.view_layer.update()


def author_gait(arm, meta) -> dict:
    """Печёт все три клипа заново: механический шаг вместо походки пака.

    Пак анимирует ногу целиком из бедра: корпус на шаге ходит по высоте на
    0.68 м, а стопа выносится на метры — в игре это читается прыжком лягушки.
    Здесь наоборот, **задаётся траектория подошвы, а нога решается под неё**:

    - подошва катится с пятки на носок (`_foot_pose`), а не едет плоско: это
      главное, что отличает шагоход от коробки на полозьях;
    - точка опоры в стойке уходит назад ровно со скоростью земли, поэтому
      проскальзывания нет и `Animator.speed` в Unity не подкручивается вслепую;
    - высота таза считается от геометрии: таз опускается настолько, насколько
      его держат опорные ноги (`hip_max`), плюс заданный профилем боб. Поэтому
      колено всегда согнуто, а IK не зажимает цель;
    - бедро и голень — двухзвенная IK с коленом вперёд, стопа ставится ровно в
      конец голени (она в этом риге отдельная кость без родителя).

    **Корпус вращается вокруг середины бёдер, а не вокруг головы кости `Body`.**
    У рига пака `Body` начинается на 0.88 м впереди бёдер, и наклон в 5°
    просаживал бы таз на 7.7 см, а доворот — сносил бы его вбок на 6 см. Поэтому
    поворот компенсируется переносом, и высотой таза управляет только боб.

    `idle` печётся тоже: `straighten_stance` меняет rest-ориентацию голени, и
    клип пака ставит свою стопу уже не туда, где оказалась нога (замер — 0.17 м
    отрыва). Всё, что эта функция не печёт, из клипа снимается (`_prune`).
    """
    if arm.animation_data is None:
        arm.animation_data_create()
    tracks = arm.animation_data.nla_tracks
    for track in tracks:
        track.mute = True

    style = gait_style(meta)
    meta["gait_style"] = meta["cfg"].get("gait", DEFAULT_GAIT)
    reach_scale = float(style.get("leg_reach", LEG_REACH))

    body_name = meta["roles"]["root"]
    body_bone = arm.data.bones[body_name]
    body_rest_rot = body_bone.matrix_local.to_3x3()
    body_rest_q = body_rest_rot.to_quaternion()
    body_inv_rot = body_rest_rot.inverted()
    body_inv_q = body_rest_q.inverted()

    torso_name = meta["roles"]["torso"]
    torso_bone = arm.data.bones[torso_name]
    torso_rest_q = torso_bone.matrix_local.to_3x3().to_quaternion()
    torso_inv_q = torso_rest_q.inverted()

    chest_name = meta["roles"]["chest"]
    chest_bone = arm.data.bones[chest_name]
    chest_rest_q = chest_bone.matrix_local.to_3x3().to_quaternion()
    chest_inv_q = chest_rest_q.inverted()

    legs = {}
    for side in ("L", "R"):
        upper_bone = arm.data.bones.get("UpperLeg." + side)
        lower_bone = arm.data.bones.get("LowerLeg." + side)
        foot_bone = arm.data.bones.get("Foot." + side)
        if upper_bone is None or lower_bone is None or foot_bone is None:
            raise RuntimeError("%s: в риге нет ноги %s" % (meta["name"], side))
        ankle, heel, toe = _sole_points(arm, side)
        foot_rot = foot_bone.matrix_local.to_3x3()
        legs[side] = {
            "upper": "UpperLeg." + side,
            "lower": "LowerLeg." + side,
            "foot": "Foot." + side,
            "upper_len": _bone_length(upper_bone),
            "lower_len": _bone_length(lower_bone),
            "hip": upper_bone.head_local.copy(),
            "ankle": ankle, "heel": heel, "toe": toe,
            "foot_head": foot_bone.head_local.copy(),
            "foot_rot": foot_rot,
            "foot_inv": foot_rot.inverted(),
            "foot_rest_q": foot_rot.to_quaternion(),
        }
    leg_length = (sum(legs[s]["upper_len"] + legs[s]["lower_len"]
                      for s in legs) / 2.0)
    reach = reach_scale * leg_length
    # Точка вращения корпуса — середина бёдер в rest.
    hip_center = (legs["L"]["hip"] + legs["R"]["hip"]) * 0.5
    pivot_arm = hip_center - body_bone.head_local
    rest_hip_z = hip_center.z

    for key in CLIP_KEYS:
        info = meta["clips"][key]
        action = info["action"]
        cfg = style[key]
        frames = cfg["frames"]
        start, end = 0, frames

        arm.animation_data.action = action
        if getattr(arm.animation_data, "action_slot", None) is None:
            slots = list(getattr(action, "slots", []) or [])
            if slots:
                try:
                    arm.animation_data.action_slot = slots[0]
                except (AttributeError, TypeError):
                    pass

        keep = {'pose.bones["%s"].location' % body_name,
                'pose.bones["%s"].rotation_quaternion' % body_name,
                'pose.bones["%s"].rotation_quaternion' % torso_name,
                'pose.bones["%s"].rotation_quaternion' % chest_name}
        for side in ("L", "R"):
            for bone in ("UpperLeg." + side, "LowerLeg." + side):
                keep.add('pose.bones["%s"].rotation_quaternion' % bone)
            for prop in ("location", "rotation_quaternion"):
                keep.add('pose.bones["Foot.%s"].%s' % (side, prop))
        removed = _prune(action, keep)

        if hasattr(action, "use_frame_range"):
            action.use_frame_range = True
            action.frame_start = start
            action.frame_end = end
        bags = _curve_bags(action)
        if not bags:
            raise RuntimeError("%s %s: у клипа нет кривых" % (meta["name"], key))

        body_loc = _curves(bags, 'pose.bones["%s"].location' % body_name, 3)
        body_rot = _curves(
            bags, 'pose.bones["%s"].rotation_quaternion' % body_name, 4)
        torso_rot = _curves(
            bags, 'pose.bones["%s"].rotation_quaternion' % torso_name, 4)
        chest_rot = _curves(
            bags, 'pose.bones["%s"].rotation_quaternion' % chest_name, 4)

        # --- 1. Повороты корпуса и горизонтальный перенос. Высота пока нулевая:
        # её нельзя посчитать раньше, чем известна поза бёдер.
        lean = Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(cfg["lean"]))
        body_turn, torso_turn, chest_turn, shift = {}, {}, {}, {}
        for frame in range(start, end + 1):
            t = (frame - start) / float(frames)
            phase = 2.0 * math.pi * t
            roll = Quaternion(Vector((0.0, 1.0, 0.0)),
                              math.radians(cfg["roll"]) * math.sin(phase))
            world = lean @ roll
            # Компенсация поворота: точка вращения — середина бёдер.
            turned = world @ pivot_arm
            offset = pivot_arm - turned
            offset.x += cfg["sway"] * math.sin(phase)
            body_turn[frame] = (body_inv_q @ world @ body_rest_q)
            shift[frame] = body_inv_rot @ offset
            # Таз доворачивается в сторону вынесенной ноги, плечи — против.
            torso_world = Quaternion(
                Vector((0.0, 0.0, 1.0)),
                -math.radians(cfg["twist"]) * math.cos(phase))
            torso_turn[frame] = torso_inv_q @ torso_world @ torso_rest_q
            shoulder = Quaternion(
                Vector((0.0, 0.0, 1.0)),
                math.radians(cfg["shoulder"]) * math.cos(phase))
            chest_turn[frame] = chest_inv_q @ shoulder @ chest_rest_q

        for index, curve in enumerate(body_rot):
            _bake(curve, start, end, lambda f, i=index: body_turn[f][i])
        for index, curve in enumerate(torso_rot):
            _bake(curve, start, end, lambda f, i=index: torso_turn[f][i])
        for index, curve in enumerate(chest_rot):
            _bake(curve, start, end, lambda f, i=index: chest_turn[f][i])
        for index, curve in enumerate(body_loc):
            _bake(curve, start, end, lambda f, i=index: shift[f][i])

        # --- 2. Траектории подошв на весь цикл (не зависят от высоты таза).
        paths = {side: {} for side in legs}
        for frame in range(start, end + 1):
            t = (frame - start) / float(frames)
            for order, side in enumerate(("L", "R")):
                leg = legs[side]
                phase = (t + 0.5 * order) % 1.0
                paths[side][frame] = _foot_pose(
                    cfg, phase, leg["ankle"], leg["heel"], leg["toe"])

        # --- 3. Высота таза. Сначала предел, который держат опорные ноги, потом
        # боб поверх него. Предел считается по фактическим бёдрам после поворота
        # корпуса: крен поднимает одно бедро и опускает другое, и высота таза
        # задаётся общей — значит, ограничивает та нога, которой тянуться дальше.
        hip_limit, hip_now, stance_legs = {}, {}, {}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            drop_limit, current, count = 1e9, -1e9, 0
            for side in ("L", "R"):
                hip = arm.pose.bones[legs[side]["upper"]].head
                ankle, _tilt = paths[side][frame]
                current = max(current, hip.z)
                horizontal = abs(hip.y - ankle.y)
                allowed = ankle.z + math.sqrt(
                    max(reach * reach - horizontal * horizontal, 0.0))
                drop_limit = min(drop_limit, allowed - hip.z)
                if cfg["stride"] > 1e-6:
                    phase = ((frame - start) / float(frames)
                             + 0.5 * (0 if side == "L" else 1)) % 1.0
                    if phase < cfg["duty"]:
                        count += 1
            hip_limit[frame] = drop_limit
            hip_now[frame] = current
            stance_legs[frame] = count

        low, high = _wave_bounds(cfg)
        span = max(high - low, 1e-6)
        wave = {frame: ((_height_wave(
            cfg, (frame - start) / float(frames)) - low) / span)
            for frame in range(start, end + 1)}
        # База: ниже rest на `crouch`. Боб сдвигает её целиком, если в самой
        # жёсткой фазе он выводит таз за предел ноги: сдвиг сохраняет форму
        # волны, а обрезка по кадрам её ломала бы.
        frames_range = range(start, end + 1)
        base = min(rest_hip_z - cfg["crouch"],
                   min(hip_now[f] + hip_limit[f] - KNEE_SLACK
                       for f in frames_range))
        over = max(0.0, max(base + cfg["bob"] * wave[f]
                            - (hip_now[f] + hip_limit[f] - KNEE_SLACK)
                            for f in frames_range))
        base -= over
        clamps = {"stance": 0, "swing": 0}
        drop = {}
        for frame in frames_range:
            drop[frame] = base + cfg["bob"] * wave[frame] - hip_now[frame]
        for index, curve in enumerate(body_loc):
            _bake(curve, start, end,
                  lambda f, i=index: shift[f][i] + (body_inv_rot
                                                    @ Vector((0.0, 0.0, drop[f])))[i])

        # --- 4. Стопы и IK ног по кадрам. Высота уже в кривых, поэтому бедро на
        # каждом кадре стоит там, куда его опустил корпус, и IK считает по факту.
        thigh = {"L": {}, "R": {}}
        shin = {"L": {}, "R": {}}
        foot_loc = {"L": {}, "R": {}}
        foot_rot = {"L": {}, "R": {}}
        extension = {side: [] for side in legs}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for side in ("L", "R"):
                leg = legs[side]
                upper = arm.pose.bones[leg["upper"]]
                lower = arm.pose.bones[leg["lower"]]
                ankle, tilt = paths[side][frame]
                hip = upper.head.copy()
                offset = ankle - hip
                distance = offset.length
                extension[side].append(distance / leg_length)
                if distance > reach:
                    ankle = hip + offset.normalized() * reach
                    clamps["stance" if stance_legs[frame] else "swing"] += 1
                    if clamps["stance"] + clamps["swing"] <= 4:
                        print("     CLAMP %s %s f=%d нужно %.3f предел %.3f "
                              "бедро z=%.3f y=%.3f стопа y=%.3f z=%.3f"
                              % (meta["name"], key, frame, distance, reach,
                                 hip.z, hip.y, ankle.y, ankle.z))
                knee = _solve_knee(hip, ankle, leg["upper_len"],
                                   leg["lower_len"], KNEE_POLE)
                if knee is None:
                    continue
                # Поза ноги обнуляется перед наведением: `_aim` крутит кость
                # **дельтой** от текущего направления, и скрутка вокруг её оси
                # наследуется от того, что стояло в кости до этого. До печки
                # кривых там ещё лежит поворот пака, и он меняется от кадра к
                # кадру — нога подрагивала, а в беге на первом переходе
                # телепортировалась на полметра. С identity результат зависит
                # только от цели.
                upper.rotation_quaternion = Quaternion()
                lower.rotation_quaternion = Quaternion()
                bpy.context.view_layer.update()
                _aim(upper, knee)
                _aim(lower, ankle)
                thigh[side][frame] = upper.rotation_quaternion.copy()
                shin[side][frame] = lower.rotation_quaternion.copy()
                foot_loc[side][frame] = leg["foot_inv"] @ (
                    ankle - leg["foot_head"])
                # Подошва поворачивается вокруг оси X: `tilt` уже посчитан от
                # края подошвы на земле, поэтому перекат не режет пол.
                foot_rot[side][frame] = (
                    leg["foot_rest_q"].inverted() @ _tilt_matrix(tilt).to_quaternion()
                    @ leg["foot_rest_q"])

        # --- 5. Печём кривые ног; пропущенные кадры тянем с предыдущего.
        for side in ("L", "R"):
            for store in (thigh, shin, foot_loc, foot_rot):
                _fill_gaps(store[side], start, end)
            for bone, values in (("UpperLeg." + side, thigh),
                                 ("LowerLeg." + side, shin)):
                path = 'pose.bones["%s"].rotation_quaternion' % bone
                for index, curve in enumerate(_curves(bags, path, 4)):
                    _bake(curve, start, end,
                          lambda f, i=index, s=side, v=values: v[s][f][i])
            path = 'pose.bones["Foot.%s"].location' % side
            for index, curve in enumerate(_curves(bags, path, 3)):
                _bake(curve, start, end,
                      lambda f, i=index, s=side: foot_loc[s][f][i])
            path = 'pose.bones["Foot.%s"].rotation_quaternion' % side
            for index, curve in enumerate(_curves(bags, path, 4)):
                _bake(curve, start, end,
                      lambda f, i=index, s=side: foot_rot[s][f][i])

        # --- 6. Границы клипа и полос NLA: длина цикла изменилась, а экспортёр
        # берёт диапазон анимации из полосы.
        for track in tracks:
            for strip in track.strips:
                if strip.action is not action:
                    continue
                strip.action_frame_start = start
                strip.action_frame_end = end
                strip.frame_start = start
                strip.frame_end = end
        info.update({"start": start, "end": end, "frames": end - start,
                     "seconds": round((end - start) / FPS, 3)})
        seconds = max((end - start) / FPS, 1e-6)
        speed = cfg["stride"] / max(cfg["duty"] * seconds, 1e-6)
        stretch = max(max(v) for v in extension.values())
        print("GAIT %s %-4s frames=%d..%d (%.3f c) шаг %.2f м, земля %.2f м/с, "
              "таз %.3f (rest %.3f), вылет ноги %.0f%%, кривых пака снято %d, "
              "зажимов стойка/замах %d/%d"
              % (meta["name"], key, start, end, info["seconds"],
                 cfg["stride"], speed, base, rest_hip_z, stretch * 100.0,
                 removed, clamps["stance"], clamps["swing"]))
        if clamps["stance"] > STANCE_CLAMP_LIMIT:
            raise RuntimeError(
                "%s %s: IK зажал цель в стойке %d кадров — подошва съедет "
                "относительно clip_speed; уменьши stride или увеличь crouch/"
                "knee_bulge" % (meta["name"], key, clamps["stance"]))
        info["ground_speed"] = round(speed, 3)

    arm.animation_data.action = None
    for track in tracks:
        track.mute = False
    meta["gait"] = style
    return style


def _fill_gaps(values: dict, start: int, end: int) -> None:
    """Добивает пропущенные кадры значением с ближайшего соседнего."""
    if not values:
        return
    last = None
    for frame in range(start, end + 1):
        if frame in values:
            last = values[frame]
        elif last is not None:
            values[frame] = last
    first = values.get(start) or next(iter(values.values()))
    for frame in range(start, end + 1):
        values.setdefault(frame, first)


def root_drift(arm, action, bone: str) -> tuple[float, float]:
    """Горизонтальный ход кости за клип, м: (dx, dy)."""
    if arm.animation_data is None:
        arm.animation_data_create()
    previous = arm.animation_data.action
    arm.animation_data.action = action
    start, end = (int(round(v)) for v in action.frame_range)
    xs, ys = [], []
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        point = arm.matrix_world @ arm.pose.bones[bone].head
        xs.append(point.x)
        ys.append(point.y)
    arm.animation_data.action = previous
    return max(xs) - min(xs), max(ys) - min(ys)


def flatten_root(arm, meta) -> dict:
    """Снимает горизонтальный ход корня, не трогая позу и вертикальный боб.

    Клип пака обязан быть in-place: актёра двигает `Mech`, иначе модель уезжает
    от капсулы и щёлкает на стыке цикла (ровно этот дефект разобран в ADR 0022).

    **Горизонталь считается в мировых осях.** У ходуна корневая кость `Body`
    направлена вверх, поэтому её локальные X/Y — это «вперёд» и «вверх», а не
    «вперёд» и «вбок». Прежняя версия обнуляла локальные X/Y и вместе с ходом
    съедала присед: корпус уезжал на 29 см, а стопы — отдельные кости без
    родителя — оставались на месте, и ноги вытягивались (замер: ступня отходила
    от голени на 0.28 м в `Idle`). Теперь берётся мировой сдвиг головы корня,
    из него вычитается только горизонтальная часть, и результат возвращается
    в локальные оси кости.

    Если горизонтальный ход и так в допуске (`IN_PLACE_TOLERANCE` чекера),
    кривые не трогаются вовсе: у пака это 0.00 у шага и бега и 0.017 у `Idle`.
    """
    bone_name = meta["roles"]["root"]
    bone = arm.data.bones[bone_name]
    rest_rot = bone.matrix_local.to_3x3()
    inv_rot = rest_rot.inverted()
    drift = {}
    for key in CLIP_KEYS:
        action = meta["clips"][key]["action"]
        start, end = (int(round(v)) for v in action.frame_range)

        curves = {}
        for layer in getattr(action, "layers", []):
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    for curve in bag.fcurves:
                        if curve.data_path == 'pose.bones["%s"].location' % bone_name:
                            curves[curve.array_index] = curve
        if len(curves) < 2:
            drift[key] = (0.0, 0.0)
            continue

        world = {}
        for frame in range(start, end + 1):
            local = Vector(tuple(curves[i].evaluate(frame) if i in curves else 0.0
                                 for i in range(3)))
            world[frame] = rest_rot @ local

        xs = [point.x for point in world.values()]
        ys = [point.y for point in world.values()]
        dx, dy = max(xs) - min(xs), max(ys) - min(ys)
        drift[key] = (round(dx, 4), round(dy, 4))
        if max(dx, dy) <= ROOT_DRIFT_TOLERANCE:
            continue

        base = world[start]
        # Горизонталь — на уровне первого кадра, вертикаль — как в клипе.
        baked = {frame: inv_rot @ Vector((base.x, base.y, world[frame].z))
                 for frame in range(start, end + 1)}
        for index in (0, 1):
            curve = curves.get(index)
            if curve is None:
                continue
            # Снимаем с конца и каждый раз заново берём последнюю точку:
            # список keyframe_points после удаления сдвигается, и старая ссылка
            # на следующую точку уже не принадлежит кривой (`Keyframe not in
            # F-Curve`).
            for _ in range(len(curve.keyframe_points)):
                curve.keyframe_points.remove(curve.keyframe_points[-1])
            for frame in range(start, end + 1):
                curve.keyframe_points.insert(frame, baked[frame][index])
            curve.update()
        print("INPLACE %s %s: снят ход dx=%.3f dy=%.3f" % (
            meta["name"], key, dx, dy))
    meta["root_drift"] = drift
    return drift


# ------------------------------------------------------------------ скорость

def _stance_runs(points, zone: float):
    """Непрерывные отрезки контакта стопы: списки индексов кадров."""
    ground = min(p.z for p in points)
    runs, current = [], []
    for index, point in enumerate(points):
        if point.z - ground <= zone:
            current.append(index)
        elif current:
            runs.append(current)
            current = []
    if current:
        runs.append(current)
    return runs


def measure_speed(arm, meta) -> dict:
    """Скорость земли в клипе, м/с, — для Walk и Run отдельно.

    Клип in-place: земля «едет» назад ровно со скоростью стопы в стойке. Поэтому
    скорость клипа — это перемещение стопы за непрерывный отрезок контакта,
    делённое на его длительность (никаких медиан по кадрам: у пака шаг
    разгоняется внутри стойки, и медиана врёт вверх на 20–25 %).
    """
    feet = meta["roles"]["feet"]
    speeds = {}
    stance = {}
    for key in ("walk", "run"):
        action = meta["clips"][key]["action"]
        start = meta["clips"][key]["start"]
        end = meta["clips"][key]["end"]
        if arm.animation_data is None:
            arm.animation_data_create()
        arm.animation_data.action = action
        for track in arm.animation_data.nla_tracks:
            track.mute = True

        positions = {name: [] for name in feet}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for name in feet:
                pose_bone = arm.pose.bones.get(name)
                if pose_bone is None:
                    continue
                positions[name].append(
                    arm.matrix_world @ ((pose_bone.head + pose_bone.tail) * 0.5))

        samples = []
        for points in positions.values():
            if len(points) < 4:
                continue
            ground = min(p.z for p in points)
            span = max(p.z for p in points) - ground
            zone = max(0.02, min(0.08, span * 0.15))
            runs = _stance_runs(points, zone)
            stance[key] = max([len(run) for run in runs], default=0)
            for run in runs:
                if len(run) < 3:
                    continue
                first, last = points[run[0]], points[run[-1]]
                travel = last.y - first.y
                # Стойка: стопа едет назад (+Y после нормализации осей).
                if travel <= 0.0:
                    continue
                seconds = (len(run) - 1) / FPS
                samples.append(travel / seconds)

        speed = statistics.median(samples) if samples else 0.0
        if speed < 0.05:
            speed = 1.0
        speeds[key] = round(speed, 3)
        print("SPEED %s %s ground=%.3f m/s runs=%d stance=%d frames" % (
            meta["name"], key, speeds[key], len(samples),
            stance.get(key, 0)))
        arm.animation_data.action = None
        for track in arm.animation_data.nla_tracks:
            track.mute = False

    meta["clip_speed"] = speeds
    meta["stance_frames"] = stance
    return speeds
