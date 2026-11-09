"""Собирает роботов FrostSpace и экспортирует FBX с клипами.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- R1
    blender --background --factory-startup --python Tools/Blender/build_robots.py
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- --jump -- R2
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- --carry -- R18
    blender --background --factory-startup --python Tools/Blender/build_robots.py -- --carry --carry-idle -- R1 R10 R15

Без аргументов собираются все дизайны из `robot_designs.BUILDERS`.

Набор клипов робота — данные дизайна (`robot_designs.REQUIRED_CLIPS`, ADR 0028):
базовые walk/idle/sprint у всех, jump + carry + carry_idle у трёх выбираемых
корпусов (R1, R10, R15), carry у носильщика R18. Флаги `--jump`, `--carry`,
`--carry-idle` — явный запрос добавить клипы сверх обязательного набора (например,
прыжок NPC для пробы); урезать набор робота они не могут, иначе штатная
пересборка без флагов удалила бы take, которые ждёт `RobotAssetPostprocessor`.
Флаг без списка роботов собирает тех, кому эта группа обязательна по данным.

Риг на прогон один и несёт объединение клипов выбранных роботов, поэтому при
экспорте дорожки вне набора робота глушатся: NPC не получают jump и carry
(вариант «клипы у всех 20» отклонён ADR 0028). После экспорта каждый FBX
сверяется с ожидаемым набором take бинарным разборщиком (`fbx_units.take_names`):
недостача или лишний take роняют прогон, а не отдают Unity файл, из которого
постпроцессор соберёт пустые или лишние клипы.
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import fbx_units  # noqa: E402  (чистый Python: разбор бинарного FBX без bpy)
import robot_designs  # noqa: E402
import robot_lib  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Project",
                                   "Content", "Models", "Robots"))


def _check_takes(name: str, path: str, expected) -> None:
    """Сверить набор take выгруженного FBX с ожидаемым для этого робота.

    Проверяется артефакт, а не намерение сборки: разбор бинарный, обратный
    импорт в Blender ради имён не нужен. Недостача обязательного take — пустые
    клипы и состояния в Unity; лишний take — клипы NPC, отклонённые ADR 0028.
    И то и другое роняет прогон: `build_all.py` увидит ненулевой код и traceback
    и не пометит шаг как PASS.
    """
    actual = fbx_units.take_names(path)
    missing = [take for take in expected if take not in actual]
    extra = [take for take in actual if take not in expected]
    if missing or extra:
        raise RuntimeError(
            "%s: набор take не совпал с ожидаемым: нет [%s], лишние [%s] (в файле: %s)"
            % (name, ", ".join(missing) or "-", ", ".join(extra) or "-",
               ", ".join(actual) or "нет"))
    print("TAKES %s: %s" % (name, ", ".join(actual)))


def main() -> int:
    robot_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    flags = {a.lower() for a in args if a.startswith("--")}
    selected = {a.lower() for a in args if not a.startswith("--")}
    flag_group = (("--jump", "jump"), ("--carry", "carry"),
                  ("--carry-idle", "carry_idle"))
    # Флаг без списка роботов — запрос собрать тех, кому группа обязательна
    # по данным дизайна, а не одного R1/R18, как было до трёх корпусов.
    if not selected:
        for flag, group in flag_group:
            if flag in flags:
                selected.update(rid.lower()
                                for rid in robot_designs.robots_requiring(group))

    # Риг на прогон один, поэтому в него идёт объединение обязательных наборов
    # выбранных роботов. Флаг добавляет группу сверх объединения, но не отменяет
    # её: контракт клипов принадлежит роботу, а не командной строке (ADR 0028).
    groups = robot_designs.required_groups(selected)
    include_jump = "--jump" in flags or "jump" in groups
    include_carry = "--carry" in flags or "carry" in groups
    include_carry_idle = "--carry-idle" in flags or "carry_idle" in groups

    # Явно запрошенные флагами клипы добавляются каждому собираемому роботу сверх
    # его обязательного набора. `--carry-idle` подразумевает carry: стойка
    # строится из позы переноски (см. `robot_lib.load_rig`).
    requested = []
    for flag, group in flag_group:
        if flag in flags:
            requested.extend(robot_lib.CLIP_GROUPS[group])
    if "--carry-idle" in flags:
        requested.extend(robot_lib.CLIP_GROUPS["carry"])

    arm, _actions = robot_lib.load_rig(include_jump=include_jump,
                                      include_carry=include_carry,
                                      include_carry_idle=include_carry_idle)
    built = []
    for builder in robot_designs.BUILDERS:
        name, obj, _mats = builder(arm)
        if selected and not any(name.lower() == s or
                                name.lower().startswith(s + "_") for s in selected):
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        robot_lib.bind(obj, arm)
        robot_lib.stats(obj, arm)
        path = os.path.join(OUT, name + ".fbx")
        # Риг несёт объединение клипов прогона, а файл робота — только его набор:
        # лишние дорожки глушатся на время экспорта (ADR 0028, клипы не у всех 20).
        takes = tuple(dict.fromkeys(robot_designs.required_takes(name)
                                    + tuple(requested)))
        robot_lib.export_fbx(arm, obj, path, takes=takes)
        _check_takes(name, path, takes)
        built.append(path)

    if not built:
        raise RuntimeError("ни один робот не собран")
    print("BUILT", len(built))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
