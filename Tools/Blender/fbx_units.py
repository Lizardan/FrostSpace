"""Единицы и take-клипы из бинарного FBX: разбор и обязательная проверка.

Зачем свой разборщик: имя свойства встречается в файле дважды — сначала в
шаблоне Properties70 (там лежит описание типа, а не число), потом в самой
записи P со значением. Поиск по строке всегда попадает в шаблон и врёт.
Здесь узлы читаются по структуре бинарного FBX, поэтому видно именно число.

Формат: заголовок 27 байт, дальше записи узлов до нулевой записи.
Узел = EndOffset, NumProperties, PropertyListLen, NameLen, Name, свойства,
затем вложенные узлы до EndOffset. До версии 7500 смещения 4-байтные.

Контракт единиц — ADR 0002 и `docs/ASSETS.md`: 1 unit = 1 м, экспорт с
`apply_unit_scale=True` и `global_scale=1.0`. Замер выходных FBX проекта дал
`UnitScaleFactor=1.0` у всех файлов (строка «Что проверено» в `docs/BLENDER.md`).
Поэтому проверка обязательная: нет свойства, оно не число или число другое —
ошибка и код 1, а не справочная строка. Тот же разборщик отдаёт имена take
(`take_names`) — сборке роботов нужно сверить клипы выгруженного файла.

Использование:
    python fbx_units.py <файл.fbx> [<файл.fbx> ...]

ASCII FBX не поддерживается: для него нужен отдельный текстовый разбор.
"""

from __future__ import annotations

import os
import struct
import sys

#: Скалярные типы свойств: буква → (формат, размер)
_SCALARS = {
    b"Y": ("<h", 2),
    b"C": ("<B", 1),
    b"I": ("<i", 4),
    b"F": ("<f", 4),
    b"D": ("<d", 8),
    b"L": ("<q", 8),
}
_ARRAYS = (b"f", b"i", b"d", b"l", b"b")

#: Контракт метров: 1 unit = 1 м (ADR 0002, `docs/ASSETS.md`). Экспорт идёт с
#: `apply_unit_scale=True` и `global_scale=1.0`, поэтому в файле обязано быть
#: ровно 1.0. Замер всех выходных FBX проекта это подтвердил; иное значение —
#: не «другая конвенция», а масштаб, который Unity примет за сантиметры.
EXPECTED_UNIT_SCALE = 1.0

#: Допуск сравнения: свойство хранится как double, и единица может приехать как
#: 0.9999999999. Это погрешность представления, а не другая единица измерения.
UNIT_SCALE_TOL = 1e-6


class Node:
    __slots__ = ("name", "props", "children")

    def __init__(self, name: str, props: list, children: list):
        self.name = name
        self.props = props
        self.children = children

    def find(self, name: str):
        for child in self.children:
            if child.name == name:
                return child
        return None


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def _read_prop(data: bytes, pos: int):
    kind = data[pos:pos + 1]
    pos += 1
    if kind in _SCALARS:
        fmt, size = _SCALARS[kind]
        return struct.unpack_from(fmt, data, pos)[0], pos + size
    if kind == b"S" or kind == b"R":
        length = struct.unpack_from("<I", data, pos)[0]
        pos += 4
        raw = data[pos:pos + length]
        pos += length
        if kind == b"S":
            return raw.decode("utf-8", "replace"), pos
        return raw, pos
    if kind in _ARRAYS:
        length, _encoding, comp_len = struct.unpack_from("<III", data, pos)
        pos += 12
        # тело массива только пропускаем — значения единицы им не бывают
        return ("<%s×%d>" % (kind.decode(), length)), pos + comp_len
    # неизвестный тип: дальше идти нельзя, сообщаем об этом вызывающему
    raise ValueError("неизвестный тип свойства %r в позиции %d" % (kind, pos - 1))


def _read_nodes(data: bytes, pos: int, wide: bool):
    nodes = []
    while True:
        if wide:
            end, num, _plen = struct.unpack_from("<QQQ", data, pos)
            pos += 24
        else:
            end, num, _plen = struct.unpack_from("<III", data, pos)
            pos += 12
        name_len = data[pos]
        pos += 1
        if end == 0:
            break
        name = data[pos:pos + name_len].decode("utf-8", "replace")
        pos += name_len
        props = []
        for _ in range(num):
            value, pos = _read_prop(data, pos)
            props.append(value)
        children = []
        if pos < end:
            children, pos = _read_nodes(data, pos, wide)
        nodes.append(Node(name, props, children))
    return nodes, pos


def parse(path: str):
    with open(path, "rb") as f:
        data = f.read()
    if not data.startswith(b"Kaydara FBX Binary"):
        raise ValueError("не бинарный FBX (ASCII не поддерживается)")
    version = struct.unpack_from("<I", data, 23)[0]
    nodes, _ = _read_nodes(data, 27, version >= 7500)
    return nodes, version


def _scalar(record):
    """Достать число из записи P: [тип, ..., значение]."""
    if not record:
        return None
    for value in reversed(record):
        if isinstance(value, (int, float)):
            return value
    return record


def unit_records(path: str) -> dict:
    nodes, version = parse(path)
    out = {"version": version, "records": {}, "units": None}
    for node in nodes:
        if node.name != "GlobalSettings":
            continue
        props70 = node.find("Properties70")
        if props70 is None:
            continue
        for record in props70.children:
            if record.name != "P" or not record.props:
                continue
            key = record.props[0]
            if isinstance(key, str):
                out["records"][key] = record.props[1:]
    out["units"] = out["records"].get("UnitScaleFactor")
    return out


def check_unit_scale(value) -> float:
    """Проверить UnitScaleFactor против контракта метров и вернуть число.

    Бросает `ValueError`, если свойства нет, оно не число или отличается от
    `EXPECTED_UNIT_SCALE`. Печать без исключения оставляла бы сломанный
    масштаб в шаге `--check` с кодом 0, то есть с виду прошедшим пайплайном.
    """
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError("UnitScaleFactor отсутствует или не число: %r" % (value,))
    if abs(float(value) - EXPECTED_UNIT_SCALE) > UNIT_SCALE_TOL:
        raise ValueError("UnitScaleFactor=%s, ожидается %s (1 unit = 1 м, ADR 0002)"
                         % (value, EXPECTED_UNIT_SCALE))
    return float(value)


def take_names(path: str) -> list[str]:
    """Имена take-клипов бинарного FBX: узел `Takes` → дочерние `Take`.

    Сборке нужно сверить выгруженный файл с обязательным набором клипов, а
    обратный импорт в Blender ради имён — лишний тяжёлый шаг. Запись `Current`
    клипом не является: она хранит имя текущего take, поэтому не попадает
    в список. Нет узла `Takes` — пустой список (клипов в файле нет).
    """
    nodes, _version = parse(path)
    for node in nodes:
        if node.name != "Takes":
            continue
        return [child.props[0] for child in node.children
                if child.name == "Take" and child.props
                and isinstance(child.props[0], str)]
    return []


def main() -> int:
    configure_stdout()
    targets = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    if not targets:
        print("укажи хотя бы один .fbx")
        return 2

    failed = 0
    for target in targets:
        path = os.path.abspath(target)
        if not os.path.exists(path):
            print("  %-46s ОТСУТСТВУЕТ" % target)
            failed += 1
            continue
        try:
            info = unit_records(path)
        except Exception as exc:  # noqa: BLE001
            # Разборщик не роняет весь прогон: файл помечается ошибкой,
            # остальные проверяются. Код возврата всё равно ненулевой.
            print("  %-46s ОШИБКА РАЗБОРА: %s" % (target, exc))
            failed += 1
            continue
        scale = _scalar(info["records"].get("UnitScaleFactor"))
        # OriginalUnitScaleFactor — справка: контракт метров (ADR 0002)
        # фиксирует UnitScaleFactor, его и проверяем обязательно.
        print("  %-46s FBX v%d  UnitScaleFactor=%s  OriginalUnitScaleFactor=%s"
              % (target, info["version"], scale,
                 _scalar(info["records"].get("OriginalUnitScaleFactor"))))
        try:
            check_unit_scale(scale)
        except ValueError as exc:
            print("     НЕВЕРНЫЕ ЕДИНИЦЫ: %s" % exc)
            failed += 1
    if failed:
        print("ПРОВАЛ: %d файл(ов) не прошли проверку единиц" % failed)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
