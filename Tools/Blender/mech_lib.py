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
#: gait — профиль шага из `GAIT_STYLES`: у каждого юнита своя каденция и
#: пластика. walk_speed/run_speed — скорость контроллера в Unity: она
#: согласована со скоростью земли в клипе, чтобы `Animator.speed` был около 1.
MECHS = {
    "FrogWalker": dict(
        source="Mech_FinnTheFrog", rig="mech", target=2.60, stance=0.62,
        idle="Idle", walk="Walk", run="Run", accent="patina",
        gait="column", walk_speed=1.9, run_speed=5.0),
    "BeeWalker": dict(
        source="Mech_BarbaraTheBee", rig="mech", target=2.60, stance=0.62,
        idle="Idle", walk="Walk", run="Run", accent="yellow",
        gait="strut", walk_speed=2.2, run_speed=5.0),
    "PandaWalker": dict(
        source="Mech_RaeTheRedPanda", rig="mech", target=2.60, stance=0.62,
        idle="Idle", walk="Walk", run="Run", accent="accent",
        gait="waddle", walk_speed=2.0, run_speed=5.0),
    "FlamingoWalker": dict(
        source="Mech_FernandoTheFlamingo", rig="mech", target=2.66, stance=0.60,
        idle="Idle", walk="Walk", run="Run", accent="rose",
        gait="stilt", walk_speed=1.75, run_speed=4.2),
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

#: Куда выгибается колено: у пака оно смотрит вперёд (обратноколенная стойка).
KNEE_POLE = Vector((0.0, -1.0, 0.0))

#: Насколько колено вынесено вперёд от линии «бедро—голеностоп» в rest-позе, м.
#:
#: У пака колено уходит вперёд на 0.93 м при базе 1.36 м: нога складывается
#: лягушачьим зигзагом, и на шаге это читается как присед с подскоком. 0.40 —
#: нога становится колонной с живым суставом, но сохраняет запас вылета на шаг
#: (бедро и голень по 0.79 м против базы 1.36 м).
KNEE_BULGE = 0.40

#: Какая доля вылета ноги разрешена стопе. 0.98 — колено почти выпрямляется на
#: краю шага; ровно на прямой ноге ходун выглядит как ходули, поэтому не 1.0.
LEG_REACH = 0.98


#: Профили походки: у каждого юнита своя пластика, а не общий шаг на всех.
#:
#: `knee_bulge` — форма ноги в rest: колено вынесено вперёд от линии
#: «бедро—голеностоп». Это единственный рычаг формы ноги: `straighten_stance`
#: правит rest до печки клипов, а меш, кабина и якоря едут за костями. Больше
#: вынос — глубже присед и больше запас хода; меньше — нога читается почти
#: прямой, но запас вылета падает, поэтому шаг у такой ноги короче.
#:
#: `frames` — длина цикла, и это главное отличие: скорость земли =
#: `travel / (frames / FPS)`. При `FPS = 30` шаг в 38 кадров даёт 1.74 м/с,
#: а в 26 кадров — 2.08 м/с. `travel * stance` — длина шага, и она упирается
#: в геометрию: бедро стоит позади стопы, а IK режет вынос за `leg_reach`
#: длины ноги. За этим следит счётчик зажимов в `author_gait`.
#:
#: `bob` — вертикальная качка корпуса, `sway` — боковой сдвиг, `roll` — крен
#: вокруг оси движения, `lean` — постоянный наклон вперёд, `yaw` — доворот,
#: `chest` — контр-доворот груди (верх гасит качку корпуса),
#: `toe` — подъём носка на замахе (в стойке подошва плоская всегда).
#:
#: `sway` не может быть большим: сдвиг корня по горизонтали ловит `flatten_root`
#: и замер in-place (порог 0.02 м на весь размах, то есть `sway` ≤ 0.008).
#: Поэтому перенос веса читается не сдвигом, а креном `roll` — он вращает корпус
#: вокруг оси движения и уводит бёдра вбок без трансляции корня.
_GAIT_IDLE = dict(frames=100, travel=0.0, lift=0.0, stance=1.0,
                  bob=0.015, yaw=0.8, sway=0.004, roll=0.5, lean=1.5,
                  toe=0.0, chest=0.4)
_GAIT_RUN = dict(frames=20, travel=3.3, lift=0.30, stance=0.345,
                 bob=0.040, yaw=2.5, sway=0.008, roll=1.2, lean=3.0,
                 toe=8.0, chest=1.0)

#: Бег для почти прямой ноги: запас вылета меньше, поэтому шаг короче —
#: на общем 3.3 м IK зажимал цель в стойке и подошва ехала по снегу.
_GAIT_RUN_STILT = dict(frames=20, travel=2.7, lift=0.32, stance=0.36,
                       bob=0.036, yaw=2.2, sway=0.006, roll=0.8, lean=1.0,
                       toe=12.0, chest=0.8)

GAIT_STYLES = {
    "column": dict(
        note="колонный шаг: длинный цикл, тяжёлый боб, плоская стопа",
        knee_bulge=0.40, leg_reach=0.98,
        idle=dict(_GAIT_IDLE),
        walk=dict(frames=38, travel=2.2, lift=0.22, stance=0.50,
                  bob=0.045, yaw=2.5, sway=0.008, roll=2.0, lean=3.0,
                  toe=8.0, chest=1.3),
        run=dict(_GAIT_RUN)),
    "strut": dict(
        note="частый шаг с высоким коленом и приседом",
        knee_bulge=0.50, leg_reach=0.96,
        idle=dict(_GAIT_IDLE),
        walk=dict(frames=26, travel=1.8, lift=0.32, stance=0.50,
                  bob=0.022, yaw=2.0, sway=0.005, roll=1.0, lean=1.5,
                  toe=14.0, chest=1.0),
        run=dict(_GAIT_RUN)),
    "waddle": dict(
        note="валкий шаг: широкий боковой перенос веса",
        knee_bulge=0.46, leg_reach=0.98,
        idle=dict(_GAIT_IDLE),
        walk=dict(frames=34, travel=2.1, lift=0.20, stance=0.52,
                  bob=0.032, yaw=3.0, sway=0.008, roll=4.0, lean=2.5,
                  toe=6.0, chest=1.6),
        run=dict(_GAIT_RUN)),
    "stilt": dict(
        note="журавлиный шаг: почти прямая нога, высокий носок",
        # 0.985 вместо 0.97: у прямой ноги запас вылета меньше всех, а шаг в
        # 0.97 упирался в предел и IK зажимал цель в стойке (ловится счётчиком).
        knee_bulge=0.32, leg_reach=0.985,
        idle=dict(_GAIT_IDLE),
        walk=dict(frames=30, travel=1.6, lift=0.40, stance=0.50,
                  bob=0.016, yaw=1.6, sway=0.004, roll=0.5, lean=0.5,
                  toe=18.0, chest=0.6),
        run=dict(_GAIT_RUN_STILT)),
}

#: Стиль по умолчанию для юнита без поля `gait` в `MECHS`.
DEFAULT_GAIT = "column"

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
    """
    while len(curve.keyframe_points) > 0:
        curve.keyframe_points.remove(curve.keyframe_points[0])
    for frame in range(start, end + 1):
        curve.keyframe_points.insert(frame, value(frame))
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


def _ankle_target(base, cfg: dict, phase: float) -> Vector:
    """Голеностоп на фазе цикла: стойка едет назад, замах идёт дугой вперёд.

    Стойка и есть «земля»: стопа стоит на полу и уезжает назад ровно со
    скоростью корпуса, поэтому проскальзывания нет и `Animator.speed` в Unity не
    приходится подкручивать вслепую. Вперёд у ходуна −Y, поэтому назад — плюс Y.
    """
    span = cfg["travel"] * cfg["stance"]
    if span <= 1e-6:
        return Vector(base)
    if phase < cfg["stance"]:
        u = phase / cfg["stance"]
        return Vector((base.x, base.y + span * (u - 0.5), base.z))
    u = (phase - cfg["stance"]) / max(1.0 - cfg["stance"], 1e-6)
    return Vector((base.x, base.y + span * (0.5 - u),
                   base.z + cfg["lift"] * math.sin(math.pi * u)))


def author_gait(arm, meta) -> dict:
    """Печёт все три клипа заново: механический шаг вместо походки пака.

    Пак анимирует ногу целиком из бедра: корпус на шаге ходит по высоте на
    0.68 м, а стопа выносится на метры — в игре это читается прыжком лягушки.
    Здесь наоборот, **задаётся траектория стопы, а нога решается под неё**:

    - стойка: стопа стоит на полу и едет назад со скоростью корпуса, поэтому
      проскальзывания нет;
    - замах: стопа поднимается дугой и возвращается вперёд;
    - подошва параллельна полу всё время: `Foot` — отдельная кость без родителя,
      её нулевое вращение гасит наклон голени;
    - корпус качается на 1.5–4 см и доворачивается на 2.5°: вес есть, подскока нет;
    - бедро и голень считаются двухзвенной IK с коленом вперёд и ограничением
      вылета `LEG_REACH`, чтобы нога не распрямлялась в палку.

    `idle` печётся тоже: `straighten_stance` меняет rest-ориентацию голени, и
    клип пака ставит свою стопу уже не туда, где оказалась нога (замер — 0.17 м
    отрыва). Всё, что эта функция не печёт (`Torso`, `Chest`, `Neck`, `Head`),
    из клипа снимается — иначе остаточный присед пака спорит с новой походкой
    (`_prune`).
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
    body_rest = arm.data.bones[body_name].matrix_local.to_3x3()
    body_inv = body_rest.inverted()
    chest_name = meta["roles"]["chest"]
    chest_rest = arm.data.bones[chest_name].matrix_local.to_3x3()
    chest_inv = chest_rest.inverted()

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

        # 1. Корпус: боб, боковой перенос веса, крен, наклон и доворот; грудь
        # гасит доворот контр-доворотом. Всё считается в мировых осях и
        # переводится в оси кости: `Body` смотрит вверх, поэтому её локальные
        # X/Y — это «вперёд» и «вверх», а не «вперёд» и «вбок».
        body_loc = _curves(bags, 'pose.bones["%s"].location' % body_name, 3)
        body_rot = _curves(
            bags, 'pose.bones["%s"].rotation_quaternion' % body_name, 4)
        chest_rot = _curves(
            bags, 'pose.bones["%s"].rotation_quaternion' % chest_name, 4)
        bob, turn, chest_turn = {}, {}, {}
        for frame in range(start, end + 1):
            t = (frame - start) / float(frames)
            phase = 2.0 * math.pi * t
            # Два приседа на цикл: каждый шаг даёт свою просадку корпуса.
            drop = cfg["bob"] * (0.5 + 0.5 * math.cos(2.0 * phase))
            shift = cfg["sway"] * math.sin(phase)
            bob[frame] = body_inv @ Vector((shift, 0.0, -drop))
            yaw = Quaternion(Vector((0.0, 0.0, 1.0)),
                             math.radians(cfg["yaw"]) * math.sin(phase))
            roll = Quaternion(Vector((0.0, 1.0, 0.0)),
                              math.radians(cfg["roll"]) * math.sin(phase))
            lean = Quaternion(Vector((1.0, 0.0, 0.0)),
                              math.radians(cfg["lean"]))
            world = lean @ yaw @ roll
            turn[frame] = (body_inv.to_quaternion() @ world
                           @ body_rest.to_quaternion())
            counter = Quaternion(
                Vector((0.0, 0.0, 1.0)),
                -math.radians(cfg["chest"]) * math.sin(phase))
            chest_turn[frame] = (chest_inv.to_quaternion() @ counter
                                 @ chest_rest.to_quaternion())
        for index, curve in enumerate(body_loc):
            _bake(curve, start, end, lambda f, i=index: bob[f][i])
        for index, curve in enumerate(body_rot):
            _bake(curve, start, end, lambda f, i=index: turn[f][i])
        for index, curve in enumerate(chest_rot):
            _bake(curve, start, end, lambda f, i=index: chest_turn[f][i])

        # 2. Стопы и IK ног по кадрам. Боб уже в кривых, поэтому бедро на каждом
        # кадре стоит там, куда его опустил корпус, и IK считает ногу по факту.
        thigh, shin, foot_loc = {"L": {}, "R": {}}, {"L": {}, "R": {}}, {
            "L": {}, "R": {}}
        foot_rot = {"L": {}, "R": {}}
        clamps = {"stance": 0, "swing": 0}
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            t = (frame - start) / float(frames)
            for order, side in enumerate(("L", "R")):
                upper = arm.pose.bones.get("UpperLeg." + side)
                lower = arm.pose.bones.get("LowerLeg." + side)
                foot = arm.data.bones.get("Foot." + side)
                if upper is None or lower is None or foot is None:
                    continue
                phase = (t + 0.5 * order) % 1.0
                ankle = _ankle_target(foot.head_local, cfg, phase)
                hip = upper.head.copy()
                upper_length = _bone_length(
                    arm.data.bones["UpperLeg." + side])
                lower_length = _bone_length(
                    arm.data.bones["LowerLeg." + side])
                # Стопа не выносится дальше доли вылета ноги: иначе на замахе
                # колено распрямляется в палку, и ходун «шагает» прямой ногой.
                # Зажим в стойке — это уже не поза, а съехавшая подошва.
                reach = reach_scale * (upper_length + lower_length)
                offset = ankle - hip
                if offset.length > reach:
                    ankle = hip + offset.normalized() * reach
                    clamps["stance" if phase < cfg["stance"] else "swing"] += 1
                    if clamps["stance"] + clamps["swing"] <= 4:
                        print("     CLAMP %s %s f=%d фаза=%.2f нужно %.3f предел "
                              "%.3f бедро z=%.3f y=%.3f стопа y=%.3f z=%.3f"
                              % (meta["name"], key, frame, phase,
                                 offset.length, reach, hip.z, hip.y,
                                 ankle.y, ankle.z))
                knee = _solve_knee(
                    hip, ankle, upper_length, lower_length, KNEE_POLE)
                if knee is None:
                    continue
                _aim(upper, knee)
                _aim(lower, ankle)
                thigh[side][frame] = upper.rotation_quaternion.copy()
                shin[side][frame] = lower.rotation_quaternion.copy()
                rest = foot.matrix_local.to_3x3()
                foot_loc[side][frame] = rest.inverted() @ (
                    ankle - foot.head_local)
                # Носок поднимается только на замахе: в стойке подошва плоская,
                # иначе перекат втыкает носок в снег.
                pitch = 0.0
                if phase >= cfg["stance"]:
                    u = (phase - cfg["stance"]) / max(1.0 - cfg["stance"], 1e-6)
                    pitch = math.radians(cfg["toe"]) * math.sin(math.pi * u)
                toe_up = Quaternion(Vector((1.0, 0.0, 0.0)), -pitch)
                foot_rot[side][frame] = (rest.to_quaternion().inverted()
                                         @ toe_up @ rest.to_quaternion())

        # 3. Печём кривые ног; пропущенные кадры тянем с предыдущего.
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

        # 4. Границы клипа и полос NLA: длина цикла изменилась, а экспортёр
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
        speed = cfg["travel"] / max((end - start) / FPS, 1e-6)
        print("GAIT %s %s frames=%d..%d (%.3f c) шаг %.2f м, земля %.2f м/с, "
              "кривых пака снято %d, зажимов IK стойка/замах %d/%d"
              % (meta["name"], key, start, end, info["seconds"],
                 cfg["travel"], speed, removed,
                 clamps["stance"], clamps["swing"]))
        if clamps["stance"] > STANCE_CLAMP_LIMIT:
            raise RuntimeError(
                "%s %s: IK зажал цель в стойке %d кадров — подошва съедет "
                "относительно clip_speed; уменьши travel*stance или увеличь "
                "knee_bulge" % (meta["name"], key, clamps["stance"]))

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
