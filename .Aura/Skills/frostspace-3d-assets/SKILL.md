---
name: frostspace-3d-assets
description: "Производство 3D-ассетов FrostSpace: сборка FBX/GLB, новые модели, пропы и декорации. Использовать перед любой задачей про Blender, FBX, меши, палитру Level_*, пересборку моделей или новый 3D-контент."
when-to-use: "Пользователь просит сделать, пересобрать или поправить 3D-модель, FBX, проп или декорацию, или спрашивает, как в проекте производятся 3D-ассеты."
---
# 3D-ассеты FrostSpace

Этот скилл фиксирует **как** в проекте производится 3D-контент. Он не отменяет `AGENTS.md` и
`docs/` — при расхождении главнее они и фактический код в `Tools/Blender/`.

## Один вход: `build_all.py`

Пайплайн ассетов — ADR 0017. Единственная поддерживаемая команда производства:

```powershell
python Tools/Blender/build_all.py --list                      # реестр, Blender не запускается
python Tools/Blender/build_all.py --asset <id> --check        # сборка + проверки
python Tools/Blender/build_all.py --asset <id> --preview      # + рендер превью
python Tools/Blender/build_all.py --all --check
python Tools/Blender/build_all.py --asset <id> --dry-run      # напечатать команды
```

Шаги всегда в одном порядке: **сборка → `--check` → `--preview`**. Скрипт запускается
системным Python, не внутри Blender.

**Приёмка ассета:** он появился из прогона `build_all.py`, прошёл `--check` и оставил превью
(если у ассета есть шаг превью). Разовая правка в GUI-Blender — черновик, пока не закреплена
скриптом и не перепроверена headless.

## Реестр `PIPELINE` — единственный источник соответствий

Внутри `build_all.py` есть словарь `PIPELINE`: `id → {script, outputs, previews, checks}`.
`outputs` — glob-шаблоны относительно корня проекта.

- `--asset <id>` проверяется по ключам `PIPELINE`; `--all` идёт по ним же.
- `--check` = `selftest.py` (глобально) + `fbx_units.py` по **каждому** output + профильные проверки.
- **Новый ассет без записи в `PIPELINE` для пайплайна не существует:** его нельзя собрать
  через `--asset`, его файлы не проверяются, о нём не знает следующий агент и CI.

Поэтому порядок работы: сначала добавить запись в `PIPELINE`, потом писать `build_*.py`.

## Blender: headless — основной путь

ADR 0003. MCP-аддон **в фоне не поднимается** (`bpy.app.background` → сервер не стартует),
поэтому в headless его нет и он там не нужен.

- Сборка: `blender --background --factory-startup --python script.py -- args`
- Живой вьюпорт: `python Tools/Blender/gui_session.py run <файл>.py --auto` (поднял, выполнил,
  закрыл). `start`/`status`/`stop` — если окно нужно под рукой. Без необходимости GUI не открывать.

Путь к Blender — параметр окружения, не константа. Порядок поиска в `find_blender()`:
`--blender <путь>` → env `BLENDER_EXE` → `PATH` → `Program Files\Blender Foundation\Blender*`
→ `%LOCALAPPDATA%\Programs\Blender Foundation\Blender*` → перебор дисков
`X:\Steam\steamapps\common\Blender\blender.exe` и `X:\SteamLibrary\...`.
(`docs/BLENDER.md` местами пишет `$env:BLENDER` — это имя для прямых вызовов blender.exe; в
`build_all.py` читается `BLENDER_EXE`.)

## Каталоги

| Что | Куда |
|---|---|
| FBX/GLB моделей | `Assets/_Project/Content/Models/<раздел>/` (`MODELS` в `build_all.py`) |
| Превью | `AssetSource/Previews/<раздел>/` (`PREVIEWS`) |
| Логи прогонов | `Tools/Blender/logs/` |

## Авторинг геометрии

Статика уровня и декорации меню — `level_lib.py`:

```python
import level_lib
level_lib.configure_stdout()
level_lib.clear_scene()
materials = level_lib.make_level_materials()
b = level_lib.LevelBuilder(materials)
b.box(center, size, "Level_Wood", bevel=0.02)
b.box_rot(center, size, rot_deg, "Level_Metal")
b.tube(points, radii, "Level_Rust", n=4, caps=(False, False))
b.ico(center, radius, "Level_Snow", subdivisions=2, jitter=0.14, seeds=3.1)
b.band(center, radius, z0, z1, "Level_Metal")
obj = b.build("MenuThing")
level_lib.report([obj])
level_lib.export_static([obj], os.path.join(OUT, "MenuThing.fbx"))
```

Правила:

- **Выгружать только через `export_static`** — он зеркалит X и выворачивает грани (ADR 0007).
  Ориентация после него: Blender −Y (нос/фасад) → Unity +Z, Blender +Z → Unity +Y.
- Имена материалов меша обязаны совпадать с ассетами палитры (`Level_Wood`, `Level_Rust`,
  `Level_Metal`, `Level_Snow`, `Level_SnowDeep`, `Level_Pine`, `Level_PineSnow`, `Level_Stone`,
  `Level_Orange`, `Level_Roof`, `Level_Window`, `Level_WoodDark`). Постпроцессор
  `LevelAssetPostprocessor` заменяет материалы только на файлах под `/Content/Models/Level/`.
- Один FBX может содержать несколько объектов — если у части нужен свой pivot (тарелка радара,
  маячок), их выгружают отдельными объектами, а не сливают в один меш.
- Держать вес в духе существующих ассетов: `MenuDepotGate` — 3 573 верт., `MenuStationMast` —
  4 124 верт. Мелкие детали на дистанции меню не читаются и только стоят кадров.
- Ничего тоньше ~0.06 м: на дистанции это мерцающая линия.

## Проверки

- `--check` обязателен: он ловит нарушение единиц внутри FBX и регрессии геометрии.
- Визуальная приёмка — кадром в Unity (`manage_camera` / `capture_editor_screenshot`) или
  превью из `--preview`, если у ассета есть шаг превью. У меню-ассетов шага превью нет:
  `render_level_previews.py` рендерит только именованные виды уровня.

## Что делать перед новой моделью

1. Посмотреть `python Tools/Blender/build_all.py --list` и найти похожий ассет.
2. Добавить/поправить `build_*.py` на `level_lib` (не хардкодить пути — `OUT` рядом со скриптом).
3. Зарегистрировать ассет в `PIPELINE` (`script`, `outputs`, `previews`, `checks`).
4. Прогнать `--asset <id> --check`; при необходимости `--preview`.
5. В Unity поставить FBX в сцену через Editor-API (ADR 0005: сборщиков контента в `Assets/` нет).
