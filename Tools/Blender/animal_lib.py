"""Ядро сборки робо-животных на ригах Quaternius.

Берём из `AssetSource` готовый скелет и клипы Idle/движения, удаляем
органический меш, приводим размер и ориентацию к метрам и +Z-forward Unity,
готовим два take (`idle_loop`, `move_loop`) и отдаём скелет дизайнеру.

Оси после подготовки: вперёд −Y, вверх +Z, вбок +X — как у роботов.
"""

from __future__ import annotations

import math
import os
import statistics
import zipfile

import bpy
from mathutils import Matrix, Vector

import robot_lib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING = os.path.join(ROOT, "AssetSource", "_staging")
OUT = os.path.join(ROOT, "Assets", "_Project", "Content", "Models",
                   "RobotAnimals")

PACKS = {
    "animals_farm": (
        os.path.join(ROOT, "AssetSource", "quaternius_animals_farm.zip"),
        "Farm Animals by @Quaternius/FBX/{name}.fbx"),
    "animals_vol2": (
        os.path.join(ROOT, "AssetSource", "quaternius_animals_vol2.zip"),
        "Animal Pack Vol.2 by @Quaternius/FBX/{name}.fbx"),
}

#: forward — куда смотрит животное в исходном риге (до поворота).
#: metric — по какому габариту нормируем размер.
#: mode — walk (есть шаг), hop (Jump), fly (Flying), swim (Swimming).
SPECIES = {
    "Wolf": dict(pack="animals_vol2", source="Wolf", forward="+X",
                 metric="height", target=1.15, idle="Idle", move="Walking",
                 mode="walk"),
    "Cat": dict(pack="animals_vol2", source="Cat", forward="+X",
                metric="height", target=0.62, idle="Idle", move="Walking",
                mode="walk"),
    "Dog": dict(pack="animals_vol2", source="Dog", forward="+X",
                metric="height", target=0.90, idle="Idle", move="Walking",
                mode="walk"),
    "Cow": dict(pack="animals_farm", source="Cow", forward="-Y",
                metric="height", target=1.50, idle="Idle", move="Walk",
                mode="walk"),
    "Horse": dict(pack="animals_farm", source="Horse", forward="-Y",
                  metric="height", target=2.10, idle="Idle", move="Walk",
                  mode="walk"),
    "Zebra": dict(pack="animals_farm", source="Zebra", forward="-Y",
                  metric="height", target=1.60, idle="Idle", move="Walk",
                  mode="walk"),
    "Llama": dict(pack="animals_farm", source="Llama", forward="-Y",
                  metric="height", target=1.80, idle="Idle", move="Jump",
                  mode="hop"),
    "Pig": dict(pack="animals_farm", source="Pig", forward="-Y",
                metric="height", target=0.85, idle="Idle", move="Jump",
                mode="hop"),
    "Sheep": dict(pack="animals_farm", source="Sheep", forward="-Y",
                  metric="height", target=0.95, idle="Idle", move="Jump",
                  mode="hop"),
    "Pug": dict(pack="animals_farm", source="Pug", forward="-Y",
                metric="height", target=0.45, idle="Idle", move="Jump",
                mode="hop"),
    "Eagle": dict(pack="animals_vol2", source="Eagle", forward="+Y",
                  metric="width", target=1.80, idle="Idle", move="Flying",
                  mode="fly"),
    "Piranha": dict(pack="animals_vol2", source="Piranha", forward="-Y",
                    metric="length", target=0.70, idle="Swimming",
                    move="Swimming", mode="swim"),
}

ANGLES = {"-Y": 0.0, "+X": -90.0, "+Y": 180.0, "-X": 90.0}

FARM_LEGS = {
    "front_l": ("FrontLeg.L", "FrontUpLeg.L", "FrontLowLeg.L"),
    "front_r": ("FrontLeg.R", "FrontUpLeg.R", "FrontLowLeg.R"),
    "back_l": ("BackLeg.L", "BackUpLeg.L", "BackLowLeg.L"),
    "back_r": ("BackLeg.R", "BackUpLeg.R", "BackLowLeg.R"),
}
FARM_FEET = ("FrontFoot.L", "FrontFoot.R", "BackFoot.L", "BackFoot.R")
#: Стопа каждой ноги по стороне: у фермерских ригов стопа — отдельная кость,
#: а не конец цепочки ноги, и привязывать броню стопы нужно именно к ней.
FARM_FEET_BY_SIDE = {
    "front_l": "FrontFoot.L", "front_r": "FrontFoot.R",
    "back_l": "BackFoot.L", "back_r": "BackFoot.R",
}


def roles(arm) -> dict:
    """Семантические роли костей для конкретного рига."""
    names = {b.name for b in arm.data.bones}
    if "Bone.001" in names:
        if "IKFrontRight" in names:
            # Волк / кот / собака: имена обезличены, роли по индексам.
            return {
                "body": "Bone.001", "pelvis": "Bone", "neck": "Bone.002",
                "head": "Bone.003",
                "tail": ["Bone.004", "Bone.005", "Bone.006", "Bone.007"],
                "legs": {
                    "front_l": ["Bone.017", "Bone.018", "Bone.019"],
                    "front_r": ["Bone.014", "Bone.015", "Bone.016"],
                    "back_l": ["Bone.008", "Bone.009", "Bone.010"],
                    "back_r": ["Bone.011", "Bone.012", "Bone.013"],
                },
                "feet": ["Bone.010", "Bone.013", "Bone.016", "Bone.019"],
                "feet_by_side": {
                    "front_l": "Bone.019", "front_r": "Bone.016",
                    "back_l": "Bone.010", "back_r": "Bone.013",
                },
            }
        if "Bone.020" in names:
            # Орёл: крылья 007–011 и 012–015, лапы 016–023.
            return {
                "body": "Bone.001", "pelvis": "Bone", "neck": "Bone.002",
                "head": "Bone.003",
                "tail": ["Bone.004", "Bone.005", "Bone.006"],
                "wings": {
                    "right": ["Bone.007", "Bone.008", "Bone.009",
                              "Bone.010", "Bone.011"],
                    "left": ["Bone.012", "Bone.013", "Bone.014", "Bone.015"],
                },
                "legs": {
                    "front_r": ["Bone.016", "Bone.017", "Bone.018",
                                "Bone.019"],
                    "front_l": ["Bone.020", "Bone.021", "Bone.022",
                                "Bone.023"],
                },
                "feet": ["Bone.019", "Bone.023"],
                "feet_by_side": {"front_r": "Bone.019", "front_l": "Bone.023"},
            }
        # Пиранья: хвост 005–006, боковые плавники 007–008.
        return {
            "body": "Bone.001", "pelvis": "Bone", "neck": "Bone.002",
            "head": "Bone.003",
            "tail": ["Bone.005", "Bone.006"],
            "fins": ["Bone.007", "Bone.008"],
            "lower_fin": "Bone.004",
            "feet": [],
            "feet_by_side": {},
        }

    tail = [name for name in ("Tail1", "Tail2", "Tail3", "Tail4")
            if name in names]
    return {
        "body": "Body", "pelvis": "Hips", "chest": "Shoulders",
        "neck": "Neck", "head": "Head", "tail": tail,
        "legs": {side: list(bones) for side, bones in FARM_LEGS.items()},
        "feet": [name for name in FARM_FEET if name in names],
        "feet_by_side": {side: name for side, name in FARM_FEET_BY_SIDE.items()
                         if name in names},
    }


def extract(species: str) -> str:
    cfg = SPECIES[species]
    archive, pattern = PACKS[cfg["pack"]]
    member = pattern.format(name=cfg["source"])
    out = os.path.join(STAGING, cfg["pack"], species + ".fbx")
    if not os.path.exists(out):
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with zipfile.ZipFile(archive) as z:
            with open(out, "wb") as f:
                f.write(z.read(member))
    return out


def _mesh_bbox(meshes):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in meshes:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], point[i])
                hi[i] = max(hi[i], point[i])
    return lo, hi


def load(species: str):
    """Импортирует риг, убирает органический меш, нормирует размер/оси."""
    cfg = SPECIES[species]
    robot_lib.clear_scene()
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.fbx(filepath=extract(species))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not meshes or not arms:
        raise RuntimeError("%s: в FBX нет меша или арматуры" % species)
    lo, hi = _mesh_bbox(meshes)
    size = hi - lo
    arm = arms[0]
    # Исходная органическая оболочка остаётся: она уже сшита с суставами и
    # закрывает стыки, поверх неё строятся крупные робо-панели.
    source_mesh = max(meshes, key=lambda o: len(o.data.vertices))

    metric = {"height": size.z, "width": size.x, "length": size.y}[
        cfg["metric"]]
    scale = cfg["target"] / max(metric, 1e-6)

    # Масштаб не трогаем на объекте: он применяется к FBX целиком при
    # экспорте (`global_scale`), иначе импортёр Unity добавляет компенсацию
    # корня и геометрия приезжает крупнее задуманного.
    arm.rotation_mode = "XYZ"
    arm.rotation_euler.z += math.radians(ANGLES[cfg["forward"]])
    bpy.context.view_layer.update()

    points = []
    for bone in arm.data.bones:
        points.append(arm.matrix_world @ bone.head_local)
        points.append(arm.matrix_world @ bone.tail_local)
    xs = [p.x for p in points]
    ys = [p.y for p in points]
    min_z = min(p.z for p in points)
    arm.location.x -= (min(xs) + max(xs)) * 0.5
    arm.location.y -= (min(ys) + max(ys)) * 0.5
    arm.location.z -= min_z
    bpy.context.view_layer.update()

    lo, hi = _mesh_bbox([source_mesh])
    meta = {
        "species": species,
        "cfg": cfg,
        "scale": scale,
        "source_size": tuple(round(v, 3) for v in size),
        "unity_size": tuple(round(v, 3) for v in (hi - lo)),
        "source_mesh": source_mesh,
        "roles": roles(arm),
    }
    print("LOAD %s source=%s unity_scale_hint=%.4f" %
          (species, tuple(round(v, 2) for v in size), scale))
    return arm, source_mesh, meta


# ------------------------------------------------------------------ клипы

def _find_action(token: str):
    for action in bpy.data.actions:
        if token in action.name:
            return action
    return None


def _pose_equal(arm, action, start: int, end: int, tol=2e-3) -> bool:
    arm.animation_data.action = action
    cache = []
    for frame in (start, end):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        cache.append({pb.name: pb.matrix.copy() for pb in arm.pose.bones})
    if set(cache[0]) != set(cache[1]):
        return False
    worst = 0.0
    for name in cache[0]:
        a, b = cache[0][name], cache[1][name]
        for row in range(4):
            for col in range(4):
                worst = max(worst, abs(a[row][col] - b[row][col]))
    return worst < tol


def _pose_deltas(arm, action, start: int, end: int):
    """Позы кадров [start, end] и изменение позы между соседними кадрами."""
    arm.animation_data.action = action
    poses = []
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        poses.append([pb.matrix.copy() for pb in arm.pose.bones])
    deltas = []
    for i in range(1, len(poses)):
        worst = 0.0
        for current, previous in zip(poses[i], poses[i - 1]):
            for row in range(4):
                for col in range(4):
                    worst = max(worst, abs(current[row][col]
                                          - previous[row][col]))
        deltas.append(worst)
    return poses, deltas


def _pose_distance(left, right) -> float:
    worst = 0.0
    for current, other in zip(left, right):
        for row in range(4):
            for col in range(4):
                worst = max(worst, abs(current[row][col] - other[row][col]))
    return worst


def _static_tail(arm, action, start: int, end: int,
                 min_frames: int = 8, share: float = 0.25):
    """Кадр, на котором замыкается цикл перед хвостовым «стоянием», или None.

    Фермерская пачка Quaternius отдаёт `Walk` вдвое длиннее цикла: после
    прохода цикла идут реальные ключи, животное «стоит» до конца take
    (в игре это читается как две пачки шагов с паузой). Такой хвост
    срезается по кадру, поза которого совпадает с первым кадром: петля
    остаётся бесшовной. Между последним кадром цикла и концом take обычно
    лежит медленный дрейф (~0.0002 за кадр), поэтому точка среза ищется
    по совпадению позы рядом с началом хвоста.
    """
    if end - start < min_frames * 2:
        return None
    poses, deltas = _pose_deltas(arm, action, start, end)
    if not deltas:
        return None
    amplitude = max(deltas)
    threshold = max(amplitude * 1e-3, 1e-7)
    tail = 0
    for delta in reversed(deltas):
        if delta > threshold:
            break
        tail += 1
    if tail < min_frames or tail < (end - start + 1) * share:
        return None

    first_static = len(poses) - tail
    residuals = []
    for index in range(max(1, first_static - 3), first_static + 1):
        residuals.append((_pose_distance(poses[index], poses[0]),
                          start + index))
    best, best_frame = min(residuals)
    if best > amplitude * 0.05:
        print("HOLD %s: хвост %d..%d не срезан — петля не замыкается (%.4f)"
              % (action.name, start + first_static, end, best))
        return None
    # Из кадров, где петля замыкается одинаково хорошо, берём самый ранний:
    # между последним кадром цикла и концом take лежит дрейф, и лишний кадр
    # в клипе — это лишняя доля секунды «стояния» на каждом повторе.
    limit = max(best * 1.25, amplitude * 1e-3)
    for residual, frame in residuals:
        if residual <= limit:
            best_frame = frame
            break
    print("HOLD %s: хвост %d..%d, цикл замыкается на %d (%.4f)"
          % (action.name, start + first_static, end, best_frame, best))
    return best_frame


def prepare_clips(arm, meta) -> dict:
    """Два take: idle_loop и move_loop, NLA, обрезка дубля последнего кадра."""
    cfg = meta["cfg"]
    arm.animation_data_create()
    for track in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(track)

    idle = _find_action(cfg["idle"])
    move = _find_action(cfg["move"])
    if idle is None or move is None:
        raise RuntimeError("%s: не найден клип %s/%s" %
                           (meta["species"], cfg["idle"], cfg["move"]))
    if idle == move:
        idle = move.copy()
        idle.name = "idle_loop_tmp"

    clips = {}
    for key, action in (("idle", idle), ("move", move)):
        action.name = key + "_loop"
        start, end = (int(round(v)) for v in action.frame_range)
        stop = _static_tail(arm, action, start, end)
        if stop is not None:
            print("TRIM %s %s: хвост %d..%d (стояние) срезан" %
                  (meta["species"], key, stop, end))
            end = stop
        if _pose_equal(arm, action, start, end):
            end = max(start + 1, end - 1)
        if hasattr(action, "use_frame_range"):
            action.use_frame_range = True
            action.frame_start = start
            action.frame_end = end
        clips[key] = {"action": action, "start": start, "end": end,
                      "frames": end - start}
        print("CLIP %s %s frames=%d..%d" %
              (meta["species"], key, start, end))

    for key in ("idle", "move"):
        info = clips[key]
        track = arm.animation_data.nla_tracks.new()
        track.name = key + "_loop"
        strip = track.strips.new(key + "_loop", info["start"], info["action"])
        strip.name = key + "_loop"
        strip.frame_start = info["start"]
        strip.frame_end = info["end"]
        strip.action_frame_start = info["start"]
        strip.action_frame_end = info["end"]
        track.mute = False

    arm.animation_data.action = None
    meta["clips"] = clips
    flatten_feet(arm, meta)
    return clips


def normalize_size(arm, obj, target: float, metric: str) -> float:
    """Приводит меш и скелет к целевому габариту и обнуляет их масштабы.

    Масштаб применяется к данным, а не к объектам: иначе Unity-импортёр
    добавляет компенсацию корня и габарит «плывёт». Клипы животных построены
    на поворотах костей; location-кривые IK-целей не масштабируются, но эти
    кости не деформируют меш (проверено: constraints отсутствуют).
    """
    axis = {"height": 2, "width": 0, "length": 1}[metric]
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    actual = max((hi - lo)[axis], 1e-4)
    k = target / actual

    print("SCALE_BEFORE obj=%s arm=%s" % (
        tuple(round(v, 4) for v in obj.scale),
        tuple(round(v, 4) for v in arm.scale)))
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.scale = obj.scale * k
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.scale = arm.scale * k
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.context.view_layer.update()
    print("SCALE_AFTER obj=%s arm=%s" % (
        tuple(round(v, 4) for v in obj.scale),
        tuple(round(v, 4) for v in arm.scale)))

    points = []
    for bone in arm.data.bones:
        points.append(arm.matrix_world @ bone.head_local)
        points.append(arm.matrix_world @ bone.tail_local)
    xs = [p.x for p in points]
    ys = [p.y for p in points]
    min_z = min(p.z for p in points)
    shift = Vector((-(min(xs) + max(xs)) * 0.5,
                    -(min(ys) + max(ys)) * 0.5, -min_z))
    arm.location += shift
    obj.location += shift
    bpy.context.view_layer.update()
    print("NORMALIZE %s actual=%.3f k=%.4f" % (metric, actual, k))
    return k


def measure_speed(arm, meta) -> float:
    """Скорость стопы в контакте, м/с, после масштабирования.

    Клипы пока in-place: корень не едет. Поэтому скорость земли — это медианная
    горизонтальная скорость стопы, пока она стоит на полу. Кадры переводятся в
    секунды по текущему fps сцены: сколько кадров в секунду лежит в файле, таким
    и будет время клипа в Unity.
    """
    cfg = meta["cfg"]
    if cfg["mode"] in ("hop", "fly", "swim"):
        meta["clip_speed"] = 1.0
        print("SPEED %s nominal=1.0 (%s)" % (meta["species"], cfg["mode"]))
        return 1.0

    feet = meta["roles"].get("feet", [])
    if not feet:
        meta["clip_speed"] = 1.0
        return 1.0
    move = meta["clips"]["move"]["action"]
    start = meta["clips"]["move"]["start"]
    end = meta["clips"]["move"]["end"]
    arm.animation_data.action = move
    if arm.animation_data:
        for track in arm.animation_data.nla_tracks:
            track.mute = True

    positions = {name: [] for name in feet}
    fps = float(bpy.context.scene.render.fps or 30.0)
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in feet:
            pb = arm.pose.bones.get(name)
            if pb is None:
                continue
            positions[name].append(arm.matrix_world @ ((pb.head + pb.tail) * 0.5))

    speeds = []
    ground = max(0.03, cfg["target"] * 0.02 / meta["scale"])
    for name, points in positions.items():
        if len(points) < 3:
            continue
        min_z = min(p.z for p in points)
        for i in range(1, len(points)):
            a, b = points[i - 1], points[i]
            if a.z - min_z > ground or b.z - min_z > ground:
                continue
            # Стойка — стопа едет назад (+Y после нормализации осей).
            if b.y - a.y <= 0.0:
                continue
            speeds.append(math.hypot(b.x - a.x, b.y - a.y) * fps)

    speed = statistics.median(speeds) if speeds else 0.0
    if speed < 0.05:
        speed = 1.0
    meta["clip_speed"] = speed
    print("SPEED %s foot=%.3f m/s samples=%d (%.0f fps)" %
          (meta["species"], speed, len(speeds), fps))
    arm.animation_data.action = None
    for track in arm.animation_data.nla_tracks:
        track.mute = False
    return speed


# ------------------------------------------------ стопы и root motion

#: Допустимое отклонение подошвы от плоскости пола в контакте, градусы.
FLAT_TOLERANCE_DEG = 5.0

#: Кость лежит вдоль подошвы, если её горизонтальная проекция длиннее этого
#: отношения полной длины. Иначе «выпрямлять» нечего — кость смотрит вниз.
HORIZONTAL_SHARE = 0.5

#: Зона стойки: насколько высота стопы может уйти от уровня стойки, чтобы кадр
#: считался контактом. Доля габарита тела.
STANCE_BAND_SHARE = 0.005

#: То же, но не уже этой доли размаха высоты стопы за клип: у высокого шага
#: абсолютная зона слишком узкая.
STANCE_BAND_OF_SPREAD = 0.05

#: Стопа, гуляющая по высоте меньше этой доли габарита, стоит весь клип:
#: у неё каждый кадр — контакт (покой, стойка на месте).
STANCE_SPREAD_SHARE = 0.02


def _use_action(arm, action) -> None:
    """Включает клип для посева и глушит NLA, чтобы он не мешал."""
    arm.data.pose_position = "POSE"
    arm.animation_data_create()
    arm.animation_data.action = action
    for track in arm.animation_data.nla_tracks:
        track.mute = True


def _release_action(arm) -> None:
    arm.animation_data.action = None
    for track in arm.animation_data.nla_tracks:
        track.mute = False


def _action_fcurves(action):
    """Кривые экшена: в Blender 5.x они лежат в слоях, а не в `action.fcurves`."""
    try:
        curves = list(action.fcurves)
    except (AttributeError, RuntimeError):
        curves = []
    if curves:
        return curves
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def foot_samples(arm, feet, start, end):
    """Точки стопы (голень/носок) по кадрам текущего клипа."""
    samples = {name: [] for name in feet}
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in feet:
            bone = arm.pose.bones.get(name)
            if bone is None:
                continue
            samples[name].append((arm.matrix_world @ bone.head,
                                  arm.matrix_world @ bone.tail))
    return samples


def lie_flat_along_floor(samples) -> bool:
    """Стопа лежит вдоль подошвы: её можно выпрямить, а не только повернуть вниз."""
    for head, tail in samples:
        direction = tail - head
        if direction.length < 1e-6:
            continue
        if math.hypot(direction.x, direction.y) >= (HORIZONTAL_SHARE
                                                    * direction.length):
            return True
    return False


def stance_weights(samples, names, body: float) -> dict:
    """Вес контакта стопы по кадрам: 1.0 в стойке, 0.5 на перекате, 0 — перенос.

    Уровень стойки — медиана высоты сустава стопы (её головы) за клип: в стойке
    стопа проводит большую часть цикла, поэтому медиана попадает на её уровень.
    Минимум высоты брать нельзя: в переносе стопа проносится ниже собственного
    уровня, и зона контакта уезжает на замах. Высота сустава, а не концов кости,
    потому что доворот стопы идёт вокруг сустава: иначе сборка и проверка мерили
    бы разные кадры, ведь после доворота минимум концов поднимается.

    Первый и последний кадр серии — перекат (пятка/носок): там доворот
    половинный, иначе стопа щёлкает на входе в стойку и на выходе из неё. Серии
    считаются по кругу: клип замкнут, кадр `end` переходит в `start`.
    """
    weights = {}
    for name in names:
        points = samples.get(name) or []
        if len(points) < 4:
            continue
        heights = [head.z for head, _tail in points]
        spread = max(heights) - min(heights)
        if spread <= STANCE_SPREAD_SHARE * body:
            # Стопа стоит весь клип — выпрямлять можно каждый кадр.
            weights[name] = [1.0] * len(heights)
            continue
        level = statistics.median(heights)
        band = max(STANCE_BAND_SHARE * body, STANCE_BAND_OF_SPREAD * spread)
        inside = [abs(height - level) <= band for height in heights]

        count = len(inside)
        first = next((i for i, flag in enumerate(inside) if not flag), None)
        if first is None:
            weights[name] = [1.0] * count
            continue
        order = [(first + i) % count for i in range(count)]
        weight = [0.0] * count
        index = 0
        while index < count:
            if not inside[order[index]]:
                index += 1
                continue
            last = index
            while last + 1 < count and inside[order[last + 1]]:
                last += 1
            if last > index:
                for step in range(index, last + 1):
                    weight[order[step]] = 1.0
                weight[order[index]] = 0.5
                weight[order[last]] = 0.5
            index = last + 1
        weights[name] = weight
    return weights


def flatten_feet(arm, meta) -> int:
    """Ставит стопу ровно на пол в кадрах контакта.

    Клипы Quaternius держат стопу под углом к земле: в контакте это читается
    как «ломаный» шаг. Здесь в кадрах контакта кость стопы доворачивается так,
    чтобы направление пятка→носок стало горизонтальным (рыскание сохраняется),
    а на входе/выходе из контакта доворот половинный — иначе стопа щёлкает.
    Ключ ставится на каждом кадре клипа, поэтому петля и шов не разъезжаются.
    """
    cfg = meta["cfg"]
    if cfg["mode"] in ("fly", "swim"):
        return 0
    feet = [name for name in meta["roles"].get("feet", [])
            if arm.pose.bones.get(name) is not None]
    if not feet:
        return 0
    body = cfg["target"] / max(meta["scale"], 1e-6)

    total = 0
    for key in ("idle", "move"):
        info = meta["clips"].get(key)
        if info is None:
            continue
        action = info.get("action") or _find_action(key + "_loop")
        if action is None:
            continue
        start = int(info.get("start", round(action.frame_range[0])))
        end = int(info.get("end", round(action.frame_range[1])))
        frames = list(range(start, end + 1))
        _use_action(arm, action)
        samples = foot_samples(arm, feet, start, end)
        straight = [name for name in feet
                    if samples[name] and lie_flat_along_floor(samples[name])]

        rest = {name: [] for name in feet}
        for frame in frames:
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for name in feet:
                rest[name].append(arm.pose.bones[name].rotation_quaternion.copy())

        weights = stance_weights(samples, straight, body)

        target = {name: [None] * len(frames) for name in straight}
        for index, frame in enumerate(frames):
            if not any(weights[name][index] > 0.0 for name in straight):
                continue
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            for name in straight:
                if weights[name][index] <= 0.0:
                    continue
                bone = arm.pose.bones[name]
                if bone.rotation_mode != "QUATERNION":
                    continue
                head = arm.matrix_world @ bone.head
                tail = arm.matrix_world @ bone.tail
                direction = tail - head
                planar = Vector((direction.x, direction.y, 0.0))
                if direction.length < 1e-6 or planar.length < 1e-6:
                    continue
                turn = direction.normalized().rotation_difference(
                    planar.normalized())
                turned = (Matrix.Translation(head) @ turn.to_matrix().to_4x4()
                          @ Matrix.Translation(-head)
                          @ (arm.matrix_world @ bone.matrix))
                bone.matrix = arm.matrix_world.inverted() @ turned
                target[name][index] = bone.rotation_quaternion.copy()

        moved = 0
        for name in straight:
            bone = arm.pose.bones[name]
            if bone.rotation_mode != "QUATERNION":
                print("FLAT %s: кость %s не в QUATERNION — пропуск"
                      % (meta["species"], name))
                continue
            for index, frame in enumerate(frames):
                quat = rest[name][index]
                weight = weights[name][index]
                if target[name][index] is not None and weight > 0.0:
                    quat = quat.slerp(target[name][index], weight)
                    moved += 1
                bone.rotation_quaternion = quat
                bone.keyframe_insert("rotation_quaternion", frame=frame)
            path = 'pose.bones["%s"].rotation_quaternion' % name
            for curve in _action_fcurves(action):
                if curve.data_path != path:
                    continue
                for point in curve.keyframe_points:
                    point.interpolation = "LINEAR"
                curve.update()
        total += moved
        print("FLAT %s %s: довёрнуто кадров %d из %d" %
              (meta["species"], key, moved, len(frames) * len(straight)))
    _release_action(arm)
    return total


def _ensure_root_bone(arm):
    """Кость Root над всем ригом: её ход Unity читает как root motion."""
    if "Root" in arm.data.bones:
        return arm.pose.bones["Root"]
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    root = arm.data.edit_bones.new("Root")
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, -0.25, 0.0)
    root.roll = 0.0
    root.use_deform = False
    for bone in list(arm.data.edit_bones):
        if bone.name == "Root":
            continue
        if bone.parent is None:
            bone.parent = root
            bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    print("ROOT %s: добавлена кость Root над верхними костями" % arm.name)
    return arm.pose.bones["Root"]


def bake_root_motion(arm, meta) -> float:
    """Печёт ход корня в `move_loop`: путь = clip_speed × длительность.

    После этого клип не in-place: стопа стоит на полу, а корень едет вперёд
    (−Y — та же ось, по которой меряется скорость стопы). Unity снимает его
    через root motion, поэтому шаг не проскальзывает. В `idle_loop` ход нулевой.
    """
    cfg = meta["cfg"]
    if cfg["mode"] in ("fly", "swim"):
        return 0.0
    clips = meta.get("clips") or {}
    if not clips:
        return 0.0
    speed = float(meta.get("clip_speed", 1.0))
    if speed < 0.05:
        speed = 1.0
    root = _ensure_root_bone(arm)
    # Базовый мировой матрикс корня: ход задаётся в мировых единицах, а не в
    # единицах кости — иначе масштаб арматуры (у импортированных FBX он не
    # единичный) растягивает путь и клип проскальзывает.
    root.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    base_world = arm.matrix_world @ root.matrix
    # fps сцены — это fps файла: в нём и мереем, иначе скорость разъедется
    # с временем клипа Unity на четверть (24 против 30).
    fps = float(bpy.context.scene.render.fps or 30.0)
    distances = {}
    for key in ("idle", "move"):
        info = clips.get(key)
        if info is None:
            continue
        action = info.get("action") or _find_action(key + "_loop")
        if action is None:
            continue
        start = int(info.get("start", round(action.frame_range[0])))
        end = int(info.get("end", round(action.frame_range[1])))
        _use_action(arm, action)
        distance = 0.0 if key == "idle" else speed * (end - start) / fps
        for frame, value in ((start, 0.0), (end, distance)):
            bpy.context.scene.frame_set(frame)
            target = Matrix.Translation(Vector((0.0, -value, 0.0))) @ base_world
            root.matrix = arm.matrix_world.inverted() @ target
            root.keyframe_insert("location", frame=frame)
        for curve in _action_fcurves(action):
            if curve.data_path != 'pose.bones["Root"].location':
                continue
            for point in curve.keyframe_points:
                point.interpolation = "LINEAR"
            curve.update()
        distances[key] = round(distance, 5)
        print("ROOT %s %s: ход %.4f за %.3f с (%.0f fps)" %
              (meta["species"], key, distance, (end - start) / fps, fps))
    _release_action(arm)
    meta["root_travel"] = distances
    return distances.get("move", 0.0)
