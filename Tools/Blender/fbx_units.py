"""Реальные значения UnitScaleFactor из бинарного FBX.

Зачем свой разборщик: имя свойства встречается в файле дважды — сначала в
шаблоне Properties70 (там лежит описание типа, а не число), потом в самой
записи P со значением. Поиск по строке всегда попадает в шаблон и врёт.
Здесь узлы читаются по структуре бинарного FBX, поэтому видно именно число.

Формат: заголовок 27 байт, дальше записи узлов до нулевой записи.
Узел = EndOffset, NumProperties, PropertyListLen, NameLen, Name, свойства,
затем вложенные узлы до EndOffset. До версии 7500 смещения 4-байтные.

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
        except Exception as exc:  # разборщик — диагностика, падать нельзя
            print("  %-46s ОШИБКА РАЗБОРА: %s" % (target, exc))
            failed += 1
            continue
        print("  %-46s FBX v%d  UnitScaleFactor=%s  OriginalUnitScaleFactor=%s"
              % (target, info["version"],
                 _scalar(info["records"].get("UnitScaleFactor")),
                 _scalar(info["records"].get("OriginalUnitScaleFactor"))))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
