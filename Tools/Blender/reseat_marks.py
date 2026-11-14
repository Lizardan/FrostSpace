"""Таблица головки рельса и полы узлов — источник чисел для скриптов посадки.

    python Tools/Blender/reseat_marks.py

Отметки сцены выводятся из раскладки, а не хранятся руками (ADR 0041 §8).
Прежний генератор лежал в `Temp/reseat_marks.py` и был утрачен: `Temp/` чистится
Unity, поэтому инструмент живёт здесь. Blender не нужен — `world_layout.py`
считается чистым Python.

Печатает:
  * `HH` для `Tools/Unity/place_depot_actors.cs` — головка главного пути по оси
    сцены `Frontier` (ось пути во дворе депо — x = 0);
  * полы узлов для `place_settlement_content.cs` и `place_station_content.cs`;
  * разброс головки по двору: ровный двор обязан давать одну отметку.

Привычки меток (перрон +1,04, терраса +0,97, пол кузова +1,00) живут в самом
скрипте посадки: это авторские высоты над головкой, а не география.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import world_layout as W

#: Диапазон и шаг таблицы головки в осях сцены `Frontier` — те же, что в
#: `place_depot_actors.cs`: двор депо плюс начало волны стола за его краями.
Z_FROM = -80.0
Z_TO = 80.0
Z_STEP = 5.0

#: Узлы, чьи полы нужны скриптам посадки актёров.
NODE_KEYS = ("depot", "settlement", "station")


def rail_head(z: float, x: float = 0.0) -> float:
    """Головка главного пути в точке сцены `Frontier`."""
    rail = W.TRANSPORT[0]
    (wx, wz) = W.to_world(x, z)
    (_, s, _) = W.project_to_line(wx, wz, rail["pts"])
    return W.level_at(rail["table"], s)


def head_table() -> list:
    steps = int(round((Z_TO - Z_FROM) / Z_STEP))
    return [rail_head(Z_FROM + i * Z_STEP) for i in range(steps + 1)]


def cs_floats(values) -> str:
    return ", ".join("%.4ff" % v for v in values)


def main() -> int:
    heads = head_table()
    spread = max(heads) - min(heads)

    print("Головка главного пути по оси сцены Frontier, z %.0f…%.0f м, шаг %.0f м"
          % (Z_FROM, Z_TO, Z_STEP))
    print("Разброс по двору: %.4f м" % spread)
    print()
    print("float[] HZ = new float[] { %s };"
          % cs_floats([Z_FROM + i * Z_STEP for i in range(len(heads))]))
    print("float[] HH = new float[] { %s };" % cs_floats(heads))
    print()

    for key in NODE_KEYS:
        level = W.PAD_LEVEL0.get(key)
        if level is None:
            print("Пол узла %-12s — узел без площадки (PAD_LEVEL0 молчит)" % key)
            continue
        print("Пол узла %-12s %8.4f   // PAD_LEVEL0[\"%s\"]" % (key, level, key))

    print()
    print("Сдвиг относительно прежних отметок скриптов посадки:")
    stale = {"depot": -1.060, "settlement": -0.175, "station": -4.440}
    for key in NODE_KEYS:
        level = W.PAD_LEVEL0.get(key)
        if level is None:
            continue
        print("  %-12s %+.4f м" % (key, level - stale[key]))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())