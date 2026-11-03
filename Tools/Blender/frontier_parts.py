"""Дизайны края открытого мира у депо: земля, путь, лес, пропы.

Размерности и оси — как в `level_parts.py`: метры, план в осях Blender,
высота по Blender-Z. Координаты раскладки берутся только из
`frontier_layout.py`: здесь нет чисел, которые можно подкрутить отдельно от
рельефа, пути и проверки.

Террейн повторяет приём `level_parts.build_terrain` дословно: одна сетка,
две октавы шума, гауссовы заносы, материал грани по вкладу заносов, никаких
UV и текстур, плоские нормали. Отличие одно и оно принципиально: сетка
неровномерная, а под путём прорезан коридор с плоским дном (`corridor_mask`).
"""

from __future__ import annotations

import math

from mathutils import Vector

import frontier_layout as L
import level_lib
import level_parts
import world_layout as W


# ---------------------------------------------------------------- земля


def world_h(ux: float, uz: float) -> float:
    """Высота снега в точке сцены — по **мировому** полю.

    До этого шага земля депо жила на `frontier_layout.ground_height`, а мир —
    на `world_layout.world_height`, и сходились они только внутри 300 м. За
    этой границей плита уходит на +/-31 м, а вал домачивается до полных 8,5 м,
    так что край авторского квадрата (600 м от депо) встал бы на 8-31 м выше
    или ниже соседней клетки: не шов, а обрыв. Один источник высот — это не
    аккуратность, а условие того, что клетки вообще пристыкуются к депо.

    Принятый кадр прибытия правка не задевает: внутри 300 м поля побито равны
    (инвариант №7), а все 37 расставленных объектов стоят ближе 300 м.
    """
    return W.world_height(*W.to_world(ux, uz))


def collider_h(ux: float, uz: float) -> float:
    """Поверхность для физики: мировое поле, но не глубже головки рельса."""
    h = world_h(ux, uz)
    if L.corridor_mask(ux, uz) >= 1.0:
        return max(h, L.RAIL_HEAD_Y)
    return h


def build_ground(materials):
    """Земля края мира: 1200×1200 м, гребень-стена, стена терминуса, выемка пути.

    Сетка сгущена к депо (`GROUND_FOCUS`): шаг ~0.94 м у площадки и ~11.5 м у
    края. Равномерная сетка того же качества в центре стоила бы 460 тыс. трис
    вместо 73 728 — на WebGL это не «чуть дороже», а другой порядок.

    Обход идёт по возрастающему Blender-y, а Unity-z = -Blender-y, поэтому
    порядок обхода граней совпадает с `build_terrain` и нормали смотрят вверх
    без `recalc_face_normals`.
    """
    b = level_lib.LevelBuilder(materials)
    n = L.GROUND_N
    axis = L.grid_axis(n, L.GROUND_SIZE * 0.5, L.GROUND_FOCUS)

    snow = b._index("Level_Snow")
    deep = b._index("Level_SnowDeep")

    # Вклад заносов считается один раз на узел и усредняется по грани, как в
    # `build_terrain`: соседние грани делят узлы, а пересчёт на грань — это
    # четыре вызова на каждую из 36 864.
    heights = []
    contribs = []
    for by in axis:
        row_h = []
        row_c = []
        uz = -by
        for ux in axis:
            row_h.append(world_h(ux, uz))
            row_c.append(L.drift_contribution(ux, uz))
        heights.append(row_h)
        contribs.append(row_c)

    base = len(b.mb.verts)
    for j, by in enumerate(axis):
        for i, ux in enumerate(axis):
            b.mb.verts.append(Vector((ux, by, heights[j][i])))

    for j in range(n):
        for i in range(n):
            a = base + j * (n + 1) + i
            d = 0.25 * (contribs[j][i] + contribs[j][i + 1] +
                        contribs[j + 1][i + 1] + contribs[j + 1][i])
            mat = deep if d > 0.05 else snow
            b.mb.add_face((a, a + 1, a + n + 2, a + n + 1), mat, "surface")

    return b.build("Frontier_Ground", recalc=False)


def build_ground_collider(materials):
    """Коллайдер-прокси: та же ось, прореженная через `L.COLLIDER_DIV`.

    Второй объект того же FBX. В редакторе у него выключается `MeshRenderer`
    и включается `MeshCollider`, а у видимой земли коллайдера нет вовсе:
    73 тыс. трис в физике — cook-время и память впустую, потому что и внутри
    `DEPOT_PAD`, и на дне выемки обе сетки дают одну и ту же отметку.
    """
    b = level_lib.LevelBuilder(materials)
    axis = L.grid_axis(L.GROUND_N, L.GROUND_SIZE * 0.5,
                       L.GROUND_FOCUS)[::L.COLLIDER_DIV]
    n = len(axis) - 1
    snow = b._index("Level_Snow")

    heights = [[collider_h(ux, -by) for ux in axis] for by in axis]

    base = len(b.mb.verts)
    for j, by in enumerate(axis):
        for i, ux in enumerate(axis):
            b.mb.verts.append(Vector((ux, by, heights[j][i])))

    for j in range(n):
        for i in range(n):
            a = base + j * (n + 1) + i
            b.mb.add_face((a, a + 1, a + n + 2, a + n + 1), snow, "surface")

    return b.build("Frontier_Ground_Collider", recalc=False)


# ---------------------------------------------------------------- путь

#: Ось главной линии плюс стрелка: общая ломаная, потому что коридор, шпалы
#: и балласт считаются по одной траектории без шва на месте перехода.
CENTERLINE = L.TRACK_MAIN + L.TRACK_STUB[1:]

#: Прямоугольное сечение рельса в координатах кольца (px — вбок по +X,
#: py — вверх по +Z): 0.14 × 0.20 м, головка выходит на `RAIL_HEAD_Y`,
#: если ось трубки идёт на -0.10.
RAIL_PROFILE = [(-0.07, -0.10), (0.07, -0.10), (0.07, 0.10), (-0.07, 0.10)]

#: Поперечное сечение постели: балласт плюс борта заноса одним профилем.
#: Средняя полка (-0.50) — верх балласта, на неё ложатся шпалы; наружные
#: гребни (+0.28) стоят выше площадки (+0.02) и закрывают откос выемки,
#: поэтому путь читается прорезанным в снегу, а не положенным на стол.
#: Наружная граница постели — отвесная стенка вниз на 1,6 м от стола. Любое
#: пологое плечо (мы держали его в 13 % уклона) находится ровно там, где склон
#: выемки имеет такой же уклон, — две параллельные грани сходятся в пару
#: сантиметров и мерцают. Отвесную грань природный склон не повторяет: у плиты
#: предел 22°, а у выемки он ещё меньше. Видимым остаётся гребень заноса
#: (+0,28), вниз уходит погребённая стена.
BED_SKIRT = 1.60
BED_PROFILE = [
    (-L.CORRIDOR_FLAT + 0.4, L.BED_TOP_Y),
    (L.CORRIDOR_FLAT - 0.4, L.BED_TOP_Y),
    (L.CORRIDOR_FLAT - 0.1, L.BANK_TOP_Y),
    (L.CORRIDOR_FLAT + 1.6, L.BANK_TOP_Y),
    (L.CORRIDOR_FLAT + 1.6, -BED_SKIRT),
    (-L.CORRIDOR_FLAT - 1.6, -BED_SKIRT),
    (-L.CORRIDOR_FLAT - 1.6, L.BANK_TOP_Y),
    (-L.CORRIDOR_FLAT + 0.1, L.BANK_TOP_Y),
]

#: Шаг станций вдоль линии. 2.0 м — шаг шпалы; постель и рельсы тянутся по
#: тем же станциям, поэтому на стрелке они гнутся синхронно со шпалами.
STATION_STEP = L.SLEEPER_STEP

SIGN_EVERY = 12          # станций между путевыми знаками: 12 × 2 м = 24 м


def _offset(pts, dist):
    """Параллельный сдвиг ломаной в Unity: dist > 0 — на восток."""
    out = []
    n = len(pts)
    for i in range(n):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(tx, tz) or 1.0
        out.append((pts[i][0] + tz / ln * dist, pts[i][1] - tx / ln * dist))
    return out


def _stations(pts, step):
    """Ломаная Unity, прорезанная через `step` метров: (x, z, поворот, i).

    Поворот — градусы вокруг Blender-Z для `box_rot`: локальная +Y трубки
    должна смотреть вдоль пути, отсюда ``atan2(-tx, -tz)`` (Blender-y
    отрицателен Unity-z). Знак поворота для симметричной шпалы не важен,
    важен для знака и буфера.
    """
    out = []
    acc = 0.0
    next_at = 0.0
    total = 0.0
    for i in range(len(pts) - 1):
        ax, az = pts[i]
        bx, bz = pts[i + 1]
        seg = math.hypot(bx - ax, bz - az)
        if seg <= 1e-6:
            continue
        tx, tz = (bx - ax) / seg, (bz - az) / seg
        rot = math.degrees(math.atan2(-tx, -tz))
        if out:
            # Разрыв ±180 превращается в непрерывный ряд: иначе среднее двух
            # соседних поворотов на стрелке даёт 0° вместо 180°.
            while rot - out[-1][2] > 180.0:
                rot -= 360.0
            while rot - out[-1][2] < -180.0:
                rot += 360.0
        while next_at <= acc + seg + 1e-6:
            t = (next_at - acc) / seg
            out.append((ax + tx * seg * t, az + tz * seg * t, rot, i))
            next_at += step
        acc += seg
        total += seg
    if not out or abs(total - (next_at - step)) > 1e-6:
        out.append((pts[-1][0], pts[-1][1], out[-1][2] if out else 0.0,
                    len(pts) - 2))
    return out


def _sweep(b, pts, profile, mat, z_level):
    """Протянуть профиль по ломаной Unity на отметке `z_level`."""
    path = [(ux, -uz, z_level) for (ux, uz) in pts]
    b.tube(path, [(1.0, 1.0)] * len(path), mat, n=4, profile=profile,
           ref=(0.0, 0.0, 1.0), fan_caps=True)


def build_track(materials):
    """Путь proper-габарита: линия на 340 м в туман и спроектированный тупик.

    Два объекта в одном FBX. `Track_Line` — то, что видно из кадра прибытия и
    вдоль чего едет состав; `Track_Terminus` — буферный упор и стрелка, куда
    она уходит.

    Всё набрано в абсолюте раскладки, поэтому инстанс ставится в (0, 0, 0) без
    поворота и масштаба: `DepotTrack` был уменьшен в 0.25, чтобы «влез» под
    вагон, и от этого рельсы стали лентой с колеей 36 см. Здесь колея 5.90 м,
    снятая с авторинга колёс, и головка на y = 0, где стоит низ колеса.
    """
    line = level_lib.LevelBuilder(materials)
    st = _stations(CENTERLINE, STATION_STEP)
    # Постель и рельсы тянутся по *прорезанной* оси, а не по восьми узлам
    # ломаной: на стрелке шаг между узлами 5.7 м, и хорда из 8-метрового
    # профиля выглядела бы сломанной там, где шпалы уже стоят по дуге.
    fine = [(s[0], s[1]) for s in st]

    # Постель: балласт и борта заноса одним профилем по всей траектории.
    _sweep(line, fine, BED_PROFILE, "Level_SnowDeep", 0.0)

    # Шпалы и снежные шапки между ними. Обе врубаются в постель на `EMBED`:
    # иначе их нижняя грань лежит ровно на вершине балласта.
    sleeper_z = L.SLEEPER_TOP_Y - L.SLEEPER_DEPTH * 0.5
    cap_z = L.CAP_TOP_Y - L.CAP_DEPTH * 0.5
    for (ux, uz, rot, _i) in st:
        line.box_rot((ux, -uz, sleeper_z),
                     (L.SLEEPER_SIZE[0], L.SLEEPER_SIZE[1], L.SLEEPER_DEPTH),
                     (0.0, 0.0, rot), "Level_WoodDark", bevel=0.02)
    for k in range(len(st) - 1):
        ux, uz, rot, _i = st[k]
        nxt = st[k + 1]
        if abs(nxt[2] - rot) > 4.0:
            rot = (rot + nxt[2]) * 0.5
        cx = (ux + nxt[0]) * 0.5
        cz = (uz + nxt[1]) * 0.5
        line.box_rot((cx, -cz, cap_z), (4.6, STATION_STEP - L.SLEEPER_SIZE[1],
                                        L.CAP_DEPTH),
                     (0.0, 0.0, rot), "Level_Snow", bevel=0.03)

    # Рельсы: два прогона по смещённой оси, головка ровно на y = 0.
    for side in (-1.0, 1.0):
        _sweep(line, _offset(fine, side * L.GAUGE * 0.5), RAIL_PROFILE,
               "Level_Metal", (L.RAIL_HEAD_Y + L.RAIL_FOOT_Y) * 0.5)

    # Путевые знаки: ритм, читаемый на 250 м, и «редкие указатели» RD-04 уже
    # на линии. Сторона чередуется, чтобы линия не выглядела забором.
    for k in range(0, len(st), SIGN_EVERY):
        ux, uz, rot, _i = st[k]
        if uz > -18.0:
            continue                       # южнее депо: в кадре прибытия знаков нет
        side = 1.0 if (k // SIGN_EVERY) % 2 == 0 else -1.0
        px = ux + side * (L.CORRIDOR_FLAT + 2.9)
        pz = uz
        # Стойка уходит ниже дна постели: на внешнем склоне борта поверхность
        # на -0.57, и знак с началом от 0.0 висел бы над ней в полуметре.
        line.box_rot((px, -pz, 0.125), (0.16, 0.16, 1.95), (0.0, 0.0, rot),
                     "Level_Metal", bevel=0.01)
        line.box_rot((px, -pz, 1.24), (0.44, 0.10, 0.30), (0.0, 0.0, rot),
                     "Level_Orange", bevel=0.0)

    objs = [line.build("Track_Line")]

    # -- тупик ---------------------------------------------------------------
    term = level_lib.LevelBuilder(materials)
    bx, bz = L.BUFFER_STOP
    rot = _stations(L.TRACK_STUB, STATION_STEP)[-1][2]
    # Столбы уходят в балласт (верх постели -0.50, низ -0.78): иначе башмак,
    # висящий посреди выемки, читался бы игрушечным.
    for side in (-1.0, 1.0):
        ox, oz = _offset([(L.TRACK_STUB[-2][0], L.TRACK_STUB[-2][1]),
                          (bx, bz)], side * 1.7)[1]
        term.box_rot((ox, -oz, 0.35), (0.34, 0.34, 2.10), (0.0, 0.0, rot),
                     "Level_Metal", bevel=0.02)
    # Башмак на столбах и снежная шапка на нём.
    term.box_rot((bx, -bz, 0.60), (4.6, 0.5, 0.90), (0.0, 0.0, rot),
                 "Level_Rust", bevel=0.03)
    term.box_rot((bx, -bz, 1.12), (3.4, 0.24, 0.30), (0.0, 0.0, rot),
                 "Level_Snow", bevel=0.05)
    # Обрезок рельса, уведённый в снег за упором: линию не демонтировали, её
    # занесло.
    ex, ez = (bx - 3.2, bz - 1.4)
    erot = math.degrees(math.atan2(-(ex - bx), -(ez - bz)))
    term.box_rot(((bx + ex) * 0.5, -(bz + ez) * 0.5, -0.55),
                 (0.14, math.hypot(ex - bx, ez - bz), 0.20),
                 (0.0, 0.0, erot), "Level_Metal", bevel=0.0)
    # Переводной брус и наборные детали у начала стрелки.
    sx, sz = L.TRACK_STUB[1]
    term.box_rot((sx, -sz, -0.40), (5.6, 0.6, 0.24), (0.0, 0.0, 180.0),
                 "Level_WoodDark", bevel=0.02)
    term.box_rot((sx - 1.9, -sz, -0.36), (0.5, 1.6, 0.34), (0.0, 0.0, 180.0),
                 "Level_Rust", bevel=0.01)
    objs.append(term.build("Track_Terminus"))
    return objs


# ---------------------------------------------------------------- кластеры

#: Пояса наполнения. Полуоси кластера, число деревьев, ярусы хвои и разброс
#: масштаба — здесь, а не в раскладке: это дизайн леса, а не координаты мира.
#:
#: Дальний пояс называется «призрачным» и это не метафора: три фасетных кома
#: вместо хвои дают ~80 трис на дерево вместо 460. `LODGroup` для этого не
#: нужен (отступление зафиксировано в ADR 0035): у каждой полосы свой меш, и
#: туман обнуляет вклад дали сильнее, чем любой LOD.
GROVES = {
    "near": {"box": (16.0, 8.0), "count": 20, "tiers": 0,
             "scale": (0.85, 1.25), "seed": 0.4},
    "mid": {"box": (22.0, 11.0), "count": 26, "tiers": 3,
            "scale": (0.72, 1.10), "seed": 5.9},
    "far": {"box": (30.0, 15.0), "count": 44, "tiers": 0,
            "scale": (0.65, 1.15), "seed": 11.3},
}

#: Валуны: пояс у подножия гребня.
ROCKS = {"box": (26.0, 12.0), "count": 15, "seed": 3.2}

#: Низ деревьев и валунов утоплен в снег: у кластера плоское основание, а
#: рельеф под ним гуляет на десятки сантиметров, и щель между ними читалась
#: бы как «лес на стекле».
BURY = 0.35


def _hash(i: float, seed: float) -> float:
    """Детерминированный [0, 1) по целому индексу.

    Формула `frac(sin(i·12.9898)·43758.5453` из шейдерной традиции здесь не
    годится: она декоррелирует две *независимые* координаты решётки (как
    `level_lib.value_noise`), а при шаге по одному индексу аргумент синуса
    меняется плавно, и дробная часть смещена. На этом «случае» прогалины
    съедали до всех точек кластера — `Frontier_Grove_Mid_A` собирался пустым.
    Поэтому финализатор из Murmur: целые биты, перемешивание, никакой
    зависимости от порядка индекса.
    """
    x = (int(i) + 1) * 2654435761 + int((seed + 1.0) * 100003) * 40503
    x &= 0xFFFFFFFF
    x = ((x ^ (x >> 16)) * 2246822519) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 3266489917) & 0xFFFFFFFF
    x ^= x >> 16
    return (x & 0xFFFFFF) / float(0x1000000)


def _scatter(count: int, seed: float, hx: float, hy: float):
    """Точки внутри эллипса, с прогалинами: решётка с малым сдвигом.

    Прямоугольник даёт «лесной квадрат»: с плана и с холма такой кластер
    читается посадкой, а не краем леса. Эллипс + отказ от части точек по
    хэшу (прогалины) стоят ровно столько же, сколько решётка, и дают неровный
    край. Число деревьев получается *примерно* `count`: сетка подбирается под
    площадь эллипса, а прогалины снимают ещё ~15 %.
    """
    aspect = hx / hy
    rows = max(3, int(round(math.sqrt(count / aspect / 0.785))))
    cols = max(3, int(round(count / rows / 0.785)))
    dx = 2.0 * hx / cols
    dy = 2.0 * hy / rows
    pts = []
    for r in range(rows):
        for c in range(cols):
            # Индекс ячейки, а не счётчик оставленных точек: если солить по
            # «сколько уже набрал», первая же прогалина залипает — хэш
            # следующего узла не меняется, и он тоже отбрасывается. Так
            # `Frontier_Grove_Mid_A` и собирался пустым.
            k = r * cols + c
            px = -hx + dx * (c + 0.5)
            py = -hy + dy * (r + 0.5)
            if (px / hx) ** 2 + (py / hy) ** 2 > 1.0:
                continue
            if _hash(k, seed + 9.4) < 0.15:
                continue
            jx = (_hash(k, seed) - 0.5) * dx * 0.9
            jy = (_hash(k, seed + 3.7) - 0.5) * dy * 0.9
            pts.append((px + jx, py + jy))
    return pts


def _ghost_pine(b, x: float, y: float, scale: float, seed: float) -> None:
    """Дальняя сосна: комли вместо ярусов. Силуэт остаётся, меш уходит в 6 раз.

    Хвоя на 170–300 м не читается в принципе (туман 0.006 оставляет там 4–24 %
    контраста), поэтому её нет смысла тянуть в далях: нужен только силуэт —
    зазубренный верх и конус.
    """
    for k, (r, z) in enumerate(((1.60, 1.30), (1.10, 3.20), (0.58, 4.85))):
        b.ico((x, y, z * scale - BURY), r * scale, "Level_Pine",
              subdivisions=0, scale=(1.0, 1.0, 0.9), jitter=0.32,
              seeds=seed + k * 2.1)
    b.ico((x, y, 5.75 * scale - BURY), 0.34 * scale, "Level_Snow",
          subdivisions=0, scale=(1.0, 1.0, 1.5), jitter=0.20, seeds=seed + 7.0)


def _boulder(b, x: float, y: float, scale: float, seed: float) -> None:
    """Валун со снежной шапкой — та же пара тел, что у `level_parts.build_rock`."""
    b.ico((x, y, 0.34 * scale - BURY), 0.55 * scale, "Level_Stone",
          subdivisions=1, scale=(1.25, 0.80, 1.02), jitter=0.34, seeds=seed)
    b.ico((x + 0.03, y - 0.02, 0.68 * scale - BURY), 0.50 * scale,
          "Level_Snow", subdivisions=1, scale=(1.18, 0.78, 0.34),
          jitter=0.22, seeds=seed + 2.0)


def build_grove(materials, band: str, variant: int):
    """Один кластер леса: 20–44 дерева одним мешем.

    Ставится в сцену инстансом (см. ADR 0035: расстановка руками, не
    генератором). Основание плоское и утопленное, поэтому на склоне кластер
    садят по `L.ground_height` и на 0.35 м ниже — иначе между корнями и
    снегом появляется щель.
    """
    spec = GROVES[band]
    b = level_lib.LevelBuilder(materials)
    seed = spec["seed"] + variant * 7.13
    hx, hy = spec["box"]
    pts = _scatter(spec["count"], seed, hx, hy)
    for i, (px, py) in enumerate(pts):
        lo, hi = spec["scale"]
        s = lo + (hi - lo) * _hash(i, seed + 1.1)
        yaw = math.tau * _hash(i, seed + 2.3)
        if band == "far":
            _ghost_pine(b, px, py, s, seed + i * 0.37)
            continue
        level_parts.pine(b, seed + i * 0.37, (px, py), s, yaw, spec["tiers"])
        if band == "near":
            # Сугроб у корней прячет стык плоского основания со склоном.
            # Дальше 90 м полутораметровый ком не читается, а 20 трис на
            # дерево стоят половины всего пояса.
            b.ico((px, py, 0.02 * s - BURY), 1.15 * s, "Level_Snow",
                  subdivisions=1, scale=(1.35, 1.15, 0.42), jitter=0.28,
                  seeds=seed + i * 1.7)
    if band == "near":
        for k in range(2):
            rx = (0.55 + 0.45 * k) * hx * (-1 if k % 2 else 1)
            ry = (0.30 + 0.40 * k) * hy
            _boulder(b, rx, ry, 1.3 + 0.5 * _hash(k, seed + 5.5), seed + k * 3.3)
    return b.build("Frontier_Grove_%s_%s" % (band.capitalize(),
                                             "ABC"[variant % 3]))


def build_rock_band(materials, variant: int):
    """Гряда фасетных валунов: подножие гребня без них — ровная простыня."""
    b = level_lib.LevelBuilder(materials)
    seed = ROCKS["seed"] + variant * 4.71
    hx, hy = ROCKS["box"]
    for i, (px, py) in enumerate(_scatter(ROCKS["count"], seed, hx, hy)):
        s = 1.1 + 2.3 * _hash(i, seed + 1.9)
        _boulder(b, px, py, s, seed + i * 2.7)
    return b.build("Frontier_Rocks_%s" % "ABC"[variant % 3])


# ---------------------------------------------------------------- пропы

#: Пролёт телеграфа ставится вдоль пути без поворота: длинная ось набрана по
#: Blender-Y, что после экспорта есть Unity-Z. Для коридора дороги инстанс
#: поворачивается на азимут раскладки.
SPAN_POLES = (-24.0, -8.0, 8.0, 24.0)
POLE_H = 6.5
ARM_Z = 5.55
WIRE_R = 0.055              # 0.11 м в диаметре: тоньше стиля «не тоньше 6 см»
SAG = 0.85                  # провис между столбами, м


def _pole(b, y: float, seed: float) -> None:
    """Столб с траверсой, изоляторами и снежной шапкой. Основание — z = 0."""
    lean = 0.10 * (_hash(y, seed) - 0.5)
    b.tube([(0.0, y, -0.30), (0.0, y + lean * 0.4, 2.4),
            (lean * 0.7, y + lean, POLE_H)],
           [(0.20, 0.20), (0.155, 0.155), (0.11, 0.11)],
           "Level_WoodDark", n=6)
    b.box((0.0, y + lean * 0.75, ARM_Z), (2.05, 0.16, 0.14),
          "Level_WoodDark", bevel=0.02)
    b.box((0.0, y + lean * 0.75, ARM_Z + 0.14), (2.00, 0.13, 0.07),
          "Level_Snow", bevel=0.02)
    for sx in (-1, 1):
        b.tube([(sx * 0.82, y + lean * 0.75, ARM_Z + 0.07),
                (sx * 0.82, y + lean * 0.75, ARM_Z + 0.28)],
               [(0.075, 0.075), (0.06, 0.06)], "Level_Metal", n=6)
    b.box((0.0, y + lean, POLE_H + 0.05), (0.30, 0.30, 0.12),
          "Level_Snow", bevel=0.03)


def _wire(b, y0: float, y1: float, x: float, z: float) -> None:
    """Провод между двумя столбами с провисом посередине."""
    b.tube([(x, y0, z), (x, (y0 + y1) * 0.5, z - SAG), (x, y1, z)],
           [(WIRE_R, WIRE_R)] * 3, "Level_Metal", n=4)


def build_telegraph_span(materials):
    """Пролёт 48 м: четыре столба, траверсы, изоляторы и два провисших провода.

    Главный выигрыш шага: линия вдоль пути на юг и вдоль коридора дороги
    превращает пустое поле в обжитое место и показывает направление
    (`RD-04`, `RD-06` — «из депо видно, куда ехать»).
    """
    b = level_lib.LevelBuilder(materials)
    for i, y in enumerate(SPAN_POLES):
        _pole(b, y, 1.7 + i * 0.9)
    for i in range(len(SPAN_POLES) - 1):
        for sx in (-1, 1):
            _wire(b, SPAN_POLES[i], SPAN_POLES[i + 1], sx * 0.82,
                  ARM_Z + 0.28)
    return b.build("Frontier_Prop_Telegraph")


def build_signal_mast(materials):
    """Сигнальная мачта: основание, пост со скобами, траверса с линзой.

    Одна у терминуса, вторая в коридоре дороги — ровно одна тёплая точка в
    дали на кадр (`STYLE.md`), а не гирлянда.
    """
    b = level_lib.LevelBuilder(materials)
    b.box((0.0, 0.0, 0.35), (1.10, 1.10, 0.70), "Level_Rust", bevel=0.03)
    b.box((0.0, 0.0, 0.78), (0.72, 0.72, 0.14), "Level_Snow", bevel=0.03)
    b.tube([(0.0, 0.0, 0.60), (0.0, 0.0, 3.6), (0.0, 0.0, 7.1)],
           [(0.19, 0.19), (0.145, 0.145), (0.11, 0.11)], "Level_Metal", n=6)
    for z in (2.30, 3.85, 5.30):
        b.box((0.0, 0.0, z), (0.62, 0.10, 0.09), "Level_Rust", bevel=0.0)
    b.box((0.42, 0.0, 6.30), (1.15, 0.12, 0.12), "Level_Metal", bevel=0.02)
    b.tube([(0.86, 0.0, 6.30), (0.86, 0.0, 6.72)],
           [(0.17, 0.17), (0.17, 0.17)], "Level_Window", n=8)
    b.box((0.86, 0.0, 6.90), (0.40, 0.34, 0.12), "Level_Snow", bevel=0.03)
    b.box((0.0, 0.0, 7.16), (0.30, 0.30, 0.12), "Level_Snow", bevel=0.03)
    return b.build("Frontier_Prop_SignalMast")


def build_road_sign(materials):
    """Указатель: столб и две стрелочные плашки. Надписей нет — направление.

    Текст в low-poly на 250 м читался бы кашей и потребовал бы шрифта в
    атласе, то есть текстуры; здесь направление показывает сама форма —
    сужающийся конец плашки.
    """
    b = level_lib.LevelBuilder(materials)
    b.tube([(0.0, 0.0, -0.25), (0.0, 0.0, 1.6), (0.0, 0.0, 2.65)],
           [(0.14, 0.14), (0.115, 0.115), (0.09, 0.09)],
           "Level_WoodDark", n=6)
    for k, (z, sx, ln) in enumerate(((2.30, 1.0, 1.05), (1.72, -1.0, 0.85))):
        verts = [(0.0, -0.20, z - 0.17), (0.0, 0.20, z - 0.17),
                 (0.0, 0.20, z + 0.17), (0.0, -0.20, z + 0.17),
                 (sx * ln, 0.0, z + 0.17), (sx * ln, 0.0, z - 0.17)]
        faces = [(0, 1, 2, 3), (1, 5, 4, 2), (0, 3, 4, 5),
                 (0, 5, 1), (2, 4, 3)]
        b.add(verts, faces, "Level_Orange")
        b.box((sx * 0.16, 0.0, z + 0.20), (ln * 0.7, 0.24, 0.07),
              "Level_Snow", bevel=0.02)
    b.box((0.0, 0.0, 2.72), (0.26, 0.26, 0.10), "Level_Snow", bevel=0.03)
    return b.build("Frontier_Prop_RoadSign")


def build_wreck_hauler(materials):
    """Полузанесённый санно-гусеничный тягач с переваленным контейнером.

    «Брошенная техника» из `RD-04`: один объект на 60–90 м от депо, который
    читается как история места, а не как мусор. Ставится с наклоном и
    опусканием: низ не выровнен — тягач просело в снег, и это видно по тому,
    как сугроб перекрывает гусеницы.
    """
    b = level_lib.LevelBuilder(materials)
    # Корпус и кабина.
    b.box((0.0, 0.0, 1.05), (2.90, 5.20, 1.10), "Level_Rust", bevel=0.05)
    b.box((0.0, 2.05, 1.95), (2.50, 1.55, 0.95), "Level_Metal", bevel=0.06)
    b.box((0.0, 2.72, 2.05), (1.90, 0.14, 0.72), "Level_Window", bevel=0.02)
    b.box((0.0, 2.05, 2.50), (2.30, 1.40, 0.20), "Level_Snow", bevel=0.08)
    # Гусеничные тележки: низ утоплен, поэтому ноль здесь — не опора, а
    # граница снега.
    for sx in (-1, 1):
        b.box((sx * 1.35, 0.0, 0.30), (0.95, 5.60, 0.60), "Level_Rust",
              bevel=0.04)
        b.box((sx * 1.35, 0.0, 0.62), (0.70, 5.20, 0.14), "Level_Snow",
              bevel=0.03)
        for k in range(4):
            y = -1.95 + k * 1.30
            b.tube([(sx * 1.35, y, 0.10), (sx * 1.35, y, 0.50)],
                   [(0.34, 0.34), (0.34, 0.34)], "Level_Metal", n=8)
    # Переваленный контейнер: лёг на бок и наполовину в снегу.
    b.tube([(-1.05, -1.60, 0.55), (-1.35, 0.40, 0.75), (-1.50, 2.40, 0.95)],
           [(0.85, 0.85)] * 3, "Level_Metal", n=8)
    b.box((-1.30, 0.40, 1.42), (1.30, 3.60, 0.24), "Level_Snow", bevel=0.10)
    # Настил, мачта и обломки вокруг: следы того, что технику бросили не вчера.
    b.box((1.55, -1.30, 0.45), (0.20, 0.20, 1.60), "Level_Rust", bevel=0.02)
    b.box((-0.60, -3.30, 0.20), (1.60, 0.60, 0.22), "Level_Rust", bevel=0.02)
    _boulder(b, 1.90, -2.60, 0.60, 4.4)
    return b.build("Frontier_Prop_Wreck")
