"""Проверка раскладки мира: инварианты модуля, свежесть JSON и поля высот.

Запуск: `python Tools/Blender/check_world.py` (Blender не нужен) или через
пайплайн: `python Tools/Blender/build_all.py --asset world --check`.

Что именно здесь проверяется и почему:

- **Инварианты `world_layout.invariants()`** — числам раскладки. Часть из них
  родилась из реальных ошибок наброска: магистраль, уходящая из чаши под азимутом
  вне прорывов гребня, и восьмиметровый каньон дороги на открытой плите.
- **Свежесть JSON** — файл в `Assets/` обязан побито совпадать с модулем. Иначе
  миникарта в игре рисует раскладку, которой больше нет, и расход вскрывается
  поздно: по меше, который разошёлся с данными.
- **Поле высот** — конечность и отсутствие NaN на всей сетке чанков: террейн по
  этим числам идёт мешем, и NaN в одной точке превращает клетку в чёрный провал.
"""

from __future__ import annotations

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import world_layout as W  # noqa: E402
import build_world  # noqa: E402

#: Шаг проб высот по карте. 50 м — мельче решётки чанка (30 м) чуть-чуть не
#: берём: цель проверки не геометрия меша, а поле, и разрыв вида ловится и тут.
HEIGHT_PROBE = 50.0

#: Максимальный уклон поля, доля (0.40 — это ~22°). Круче — обрыв, а обрывы в
#: мире объявлены ровно один: разлом, и его собственный уклон проверяет модуль.
#: Поперёк дороги такой разрыв мерить бесполезно: «каньон» от прорыва гребня
#: шириной 2*r*tg(11°) на километре — 380 м, и обе пробы уходят в него вместе.
MAX_SLOPE = 0.40

#: Доля карты, которую может занимать тёплый каркас. Выше — остыванию негде
#: наступать, и «вне тепла холодно» становится чистой формальностью.
WARM_MAX_SHARE = 0.03


def check_invariants(fails: list) -> None:
    for (name, ok, detail) in W.invariants():
        print("  %s %s — %s" % ("OK  " if ok else "FAIL", name, detail))
        if not ok:
            fails.append(name)


def check_manifest(fails: list) -> None:
    path = os.path.join(build_world.root(), build_world.OUT)
    if not os.path.exists(path):
        print("  FAIL world_layout.json не существует: %s" % build_world.OUT)
        fails.append("world_layout.json существует")
        return
    with open(path, encoding="utf-8") as stream:
        on_disk = json.load(stream)
    fresh = json.loads(json.dumps(W.manifest(), ensure_ascii=False))
    same = on_disk == fresh
    print("  %s JSON побито совпадает с модулем — %d КБ, узлов %d" % (
        "OK  " if same else "FAIL", os.path.getsize(path) // 1024,
        len(on_disk.get("nodes", []))))
    if not same:
        fails.append("world_layout.json свежий")
        for key in sorted(set(on_disk) | set(fresh)):
            if on_disk.get(key) != fresh.get(key):
                print("     расходится: %s" % key)


def check_heights(fails: list) -> None:
    """Поле конечно везде и без неоговорённых обрывов."""
    step = HEIGHT_PROBE
    n = int((W.BB[1] - W.BB[0]) / step) + 1
    bad = 0
    slope = 0.0
    where = (0.0, 0.0)
    skipped = 0
    for i in range(n):
        x = W.BB[0] + i * step
        for j in range(n):
            z = W.BB[2] + j * step
            h = W.world_height(x, z)
            if not math.isfinite(h):
                bad += 1
                continue
            for (dx, dz) in ((step, 0.0), (0.0, step)):
                if x + dx > W.BB[1] or z + dz > W.BB[3]:
                    continue
                if W.in_crack(x, z) or W.in_crack(x + dx, z + dz):
                    skipped += 1
                    continue
                d = abs(W.world_height(x + dx, z + dz) - h) / step
                if d > slope:
                    slope = d
                    where = (x, z)
    ok = bad == 0 and slope <= MAX_SLOPE
    print("  %s поле высот — нечисловых %d, круче всего %.2f (%.0f°) в (%.0f,%.0f), "
          "допуск %.2f, пропущено проб из-за разлома %d" % (
              "OK  " if ok else "FAIL", bad, slope, math.degrees(math.atan(slope)),
              where[0], where[1], MAX_SLOPE, skipped))
    if bad:
        fails.append("поле высот без NaN")
    if slope > MAX_SLOPE:
        fails.append("уклон поля в допуске")


def check_warm(fails: list) -> None:
    """Тёплый каркас: холодное — вне тепла, и тепла не слишком много."""
    step = HEIGHT_PROBE
    n = int((W.BB[1] - W.BB[0]) / step) + 1
    warm = tot = 0
    for i in range(n):
        x = W.BB[0] + i * step
        for j in range(n):
            z = W.BB[2] + j * step
            tot += 1
            if W.is_warm(x, z):
                warm += 1
    share = warm / float(tot)
    hot_cold = [n_["k"] for n_ in W.NODES if not n_["warm"] and W.is_warm(n_["x"], n_["z"])]
    cold_warm = [n_["k"] for n_ in W.NODES if n_["warm"] and n_["rect"]
                 and not W.is_warm(n_["x"], n_["z"])]
    ok = share <= WARM_MAX_SHARE and not hot_cold and not cold_warm
    print("  %s тёплый каркас — %.1f%% карты (допуск %.1f%%), холодных узлов в тепле: "
          "%s, тёплых узлов вне тепла: %s" % (
              "OK  " if ok else "FAIL", share * 100.0, WARM_MAX_SHARE * 100.0,
              ", ".join(hot_cold) or "нет", ", ".join(cold_warm) or "нет"))
    if not ok:
        fails.append("тёплый каркас по размеру и составу")


def main() -> int:
    W.configure_stdout()
    print("проверка мира: %s" % W.FIELD_NOTE)
    fails: list = []
    print("[1] инварианты раскладки")
    check_invariants(fails)
    print("[2] данные для Unity")
    check_manifest(fails)
    print("[3] поле высот")
    check_heights(fails)
    print("[4] тёплый каркас")
    check_warm(fails)
    if fails:
        print("\nПРОВАЛ: %d — %s" % (len(fails), "; ".join(fails)))
        return 1
    print("\nOK: мир сходится: %d клеток, %d с контентом, %d узлов, %d якорей" % (
        (W.CELL_MAX - W.CELL_MIN + 1) ** 2,
        len(W.content_cells()), len(W.NODES), len(W.ANCHORS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
