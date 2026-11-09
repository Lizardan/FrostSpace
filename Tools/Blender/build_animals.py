"""Собирает робо-животных FrostSpace и экспортирует FBX с двумя клипами.

Запуск:
    blender --background --factory-startup --python Tools/Blender/build_animals.py -- Wolf
    blender --background --factory-startup --python Tools/Blender/build_animals.py

Без аргументов собираются все типы из `animal_lib.SPECIES` плюс паук.

Манифест `animal_manifest.json` — регистр всей партии: записи сливаются по
видам, незатронутые и неудачные виды сохраняют прежние данные, файл заменяется
атомарно. Прогон с ошибкой сборки возвращает ненулевой код, даже если часть
видов собрана и манифест обновлён.
"""

from __future__ import annotations

import json
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

import animal_designs  # noqa: E402
import animal_lib  # noqa: E402
import robot_lib  # noqa: E402

MANIFEST = os.path.join(HERE, "animal_manifest.json")


def read_manifest() -> list:
    """Прежний регистр партии: нет файла — пустой список, повреждённый — ошибка.

    Молча начинать с нуля при нечитаемом JSON значило бы стереть партию тем же
    дефектом, который правим, поэтому повреждение не проглатывается.
    """
    if not os.path.exists(MANIFEST):
        return []
    with open(MANIFEST, "r", encoding="utf-8") as handle:
        entries = json.load(handle)
    if not isinstance(entries, list):
        raise RuntimeError("манифест %s: ожидался список записей, получен %s"
                           % (MANIFEST, type(entries).__name__))
    return entries


def merge_manifest(previous: list, results: list) -> list:
    """Слить свежие записи с прежними по `species`, сохранив порядок реестра.

    Манифест — регистр всей партии, его читает `check_animal_clips.py`.
    Выборочный прогон (`-- Wolf`) обновляет только свой вид, а неудачная сборка
    оставляет виду прежнюю запись: терять остальные 12 видов нельзя.
    """
    merged = list(previous)
    index = {entry["species"]: pos for pos, entry in enumerate(merged)
             if isinstance(entry, dict) and "species" in entry}
    for entry in results:
        key = entry["species"]
        if key in index:
            merged[index[key]] = entry
        else:
            index[key] = len(merged)
            merged.append(entry)
    return merged


def write_manifest(entries: list) -> None:
    """Атомарная замена: читатель не должен увидеть половину JSON.

    Приём тот же, что у `level_lib.export_static`: временный файл рядом и
    `os.replace`. Право переименования в каталоге есть даже когда файл занят.
    """
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    tmp = os.path.join(os.path.dirname(MANIFEST),
                       "." + os.path.basename(MANIFEST) + ".tmp")
    try:
        with open(tmp, "w", encoding="utf-8") as handle:
            json.dump(entries, handle, ensure_ascii=False, indent=2)
        os.replace(tmp, MANIFEST)
    except OSError:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise


def _unity_scale(meta):
    cfg = meta["cfg"]
    axis = {"height": 2, "width": 0, "length": 1}[cfg["metric"]]
    source = max(meta["unity_size"][axis], 1e-4)
    return cfg["target"] / source, source


def build(species: str) -> dict:
    if species == "Spider":
        arm, meta = animal_designs.make_spider()
        name, obj, mats, _M = animal_designs.build_spider(arm, meta)
        source_mesh = None
        unity_scale = 1.0
        source = meta["cfg"]["target"]
        meta["clip_speed"] = 0.80
        animal_lib.bake_root_motion(arm, meta, in_place=True)
    else:
        arm, source_mesh, meta = animal_lib.load(species)
        animal_lib.prepare_clips(arm, meta)
        if species == "Eagle":
            name, obj, mats, _M = animal_designs.build_eagle(arm, source_mesh)
        elif species == "Piranha":
            name, obj, mats, _M = animal_designs.build_piranha(
                arm, source_mesh)
        else:
            name, obj, mats, _M = animal_designs.build_quadruped(
                species, arm, source_mesh)
        unity_scale, source = _unity_scale(meta)
        animal_lib.measure_speed(arm, meta)
        animal_lib.bake_root_motion(arm, meta, in_place=True)

    robot_lib.bind(obj, arm)

    # Обе оболочки должны уехать в Unity с одной системой координат: иначе тело
    # и броня рисуются со сдвигом (ADR 0022). В Blender сдвиг не виден.
    # Порядок важен: bind() вешает родителя и matrix_parent_inverse, поэтому
    # обнуление трансформа идёт после него.
    animal_lib.bake_mesh_transform(source_mesh)
    animal_lib.bake_mesh_transform(obj)

    stats = robot_lib.stats(obj, arm)

    path = os.path.join(animal_lib.OUT, species, name + ".fbx")
    robot_lib.export_fbx(arm, [source_mesh, obj] if source_mesh else obj, path)
    clip_speed = round(meta.get("clip_speed", 1.0), 3)
    return {
        "species": species,
        "name": name,
        "path": path,
        "verts": stats["verts"],
        "tris": stats["tris"],
        "clips": {k: v["frames"] for k, v in meta["clips"].items()},
        "clip_speed_source": clip_speed,
        "clip_speed_unity": round(clip_speed * unity_scale, 3),
        "unity_scale": round(unity_scale, 5),
        "source_size": round(source, 3),
        "target": meta["cfg"]["target"],
        "metric": meta["cfg"]["metric"],
        "mode": meta["cfg"]["mode"],
    }


def main() -> int:
    robot_lib.configure_stdout()
    print("Blender", bpy.app.version_string)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    selected = {a.lower() for a in args}
    species = list(animal_lib.SPECIES) + ["Spider"]
    if selected:
        species = [s for s in species if s.lower() in selected]
    if not species:
        raise RuntimeError("ни один тип не выбран: %s" % args)

    results = []
    failed = []
    for item in species:
        try:
            results.append(build(item))
        except Exception:  # noqa: BLE001
            failed.append(item)
            print("FAILED", item)
            traceback.print_exc()

    print("\nSUMMARY")
    for r in results:
        print("%-8s verts=%-5d tris=%-5d clips=%s scale=%.4f speed=%.2f" % (
            r["species"], r["verts"], r["tris"], r["clips"],
            r["unity_scale"], r["clip_speed_unity"]))

    if results:
        merged = merge_manifest(read_manifest(), results)
        write_manifest(merged)
        print("MANIFEST %s: записей %d, обновлено %d"
              % (MANIFEST, len(merged), len(results)))
    if failed:
        # Прежние записи неудачных и незатронутых видов остаются в регистре,
        # но сам прогон обязан считаться упавшим.
        print("ПРОВАЛ сборки: %s (прежние записи манифеста сохранены)"
              % ", ".join(failed))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
