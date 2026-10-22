# Blender: работа, MCP и грабли

Документ описывает **инструментарий**, а не дизайн конкретной игры (стиль и палитры —
`STYLE.md`). Контракт перехода ассетов в Unity — `ASSETS.md`. Запуск Unity-проверок —
`DEVELOPMENT.md`.

## Статус

| Параметр | Значение | Проверено |
|---|---|---|
| Версия | Blender **5.2.3 LTS** (`build 2026-10-22`, hash `e13b728c6880`) | да |
| Путь | `G:\Steam\steamapps\common\Blender\blender.exe` | да |
| Аддоны | `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons` | да |
| MCP-аддон | `blender_mcp.py`, протокол аддона **14** | установлен пакетом 2.2.2 |
| MCP-сервер | пакет `mcp-for-blender==2.2.2` | `--help` и `install-addon` проверены |
| Порт моста | **9876** | по конфигурации; в этой сессии не слушал |
| Наш мост | `Tools/Blender/blender_bridge.py` | mock-тест |
| Загрузчик | `Tools/Blender/blender_boot.py` | headless-отказ проверен |
| **Единый вход ассетов** | `Tools/Blender/build_all.py` (ADR 0017) | сборка, проверки и превью прогнаны на 5.2.3 |
| Роботы | `robot_lib.py`, `robot_designs.py`, `build_robots.py`, `measure_locomotion.py` | сборка и замер прогоняются headless |

Путь и версия — **параметр окружения**, а не константа в коде. На другой машине они
будут другими; скрипты должны принимать их через env/CLI.

## Модель работы: headless — основной путь

| Задача | Режим | Почему |
|---|---|---|
| Сборка, экспорт FBX/GLB, тесты геометрии, замеры | **headless-скрипт** | быстро, воспроизводимо, без UI, можно в CI |
| Регрессия после смены Blender | **headless + selftest** | сравнение чисел, а не «на глаз» |
| Визуальная проверка, разбор сцены, скриншот вьюпорта | **GUI + MCP** | видно глазами, можно потыкать |
| Мелкая живая правка в открытом Blender | **MCP** | не надо перезапускать |
| Долгий/unattended-прогон | **headless** | GUI и модальные окна только мешают |

Правила:

- **Скрипт — источник истины.** Всё, что должно воспроизводиться, живёт в скрипте.
- **MCP — витрина и живая правка.** Он не заменяет скрипт и не обязателен для сборки.
- **Качество не зависит от транспорта.** Headless и GUI используют один `bpy` и одну
  сборку Blender; разница только в цикле обратной связи.
- Если пользователь просит показать шаг — покажи через GUI; иначе не открывай окно
  без необходимости.

## Единая точка входа: `build_all.py`

**Это единственный поддерживаемый способ произвести ассет** (ADR 0017). Остальные
`build_*` остаются рабочими командами для точечной отладки, но пайплайн — здесь.

```powershell
python Tools/Blender/build_all.py --list                    # реестр ассетов, Blender не запускается
python Tools/Blender/build_all.py --asset hoverbike         # сборка одного ассета
python Tools/Blender/build_all.py --asset robots --ids R1 --jump
python Tools/Blender/build_all.py --asset level --ids terrain house --check
python Tools/Blender/build_all.py --all --check             # сборка всего + инварианты
python Tools/Blender/build_all.py --asset robots --ids R1 --preview
python Tools/Blender/build_all.py --asset level --preview --skip-build
python Tools/Blender/build_all.py --all --dry-run           # напечатать команды
```

| Флаг | Что делает |
|---|---|
| `--asset <id>` / `--all` | какие ассеты гнать (повторяемый `--asset`); `--list` показывает id |
| `--ids <имена...>` | аргументы скрипта сборки (имена роботов, виды, секции) **и** фильтр превью/проверок по имени файла |
| `--jump` | прокинуть `--jump` в `build_robots.py` (клипы прыжка) |
| `--check` | `selftest.py` + `fbx_units.py` по всем файлам + профильные шаги (локомоция роботов) |
| `--preview` | рендер PNG в `AssetSource/Previews/...` |
| `--skip-build` | не пересобирать: только превью/проверки по готовым файлам |
| `--dry-run` | напечатать команды, ничего не запускать |
| `--blender <путь>` | явный путь; иначе `BLENDER_EXE` → `PATH` → типовые каталоги |
| `--timeout <с>` | таймаут шага (по умолчанию сборка 1800, превью 900, проверка 600) |

Реестр `PIPELINE` внутри скрипта — единственное место, где записано соответствие
«ассет → сборочный скрипт → выходные файлы → превью → проверки». Новый ассет без записи
в реестре для пайплайна не существует. Лог каждого прогона — `Tools/Blender/logs/`.

**Правило приёмки.** Ассет считается сделанным, только если он появился из прогона
`build_all.py`, прошёл `--check` и оставил превью. Разовая правка в GUI-Blender —
черновик, пока не закреплена скриптом и не перепроверена headless.

### Что MCP даёт и чего не даёт

Проверено по таблице команд аддона 2.2.2: **команд моделирования, риггинга и анимации в
нём нет**. Единственная команда, меняющая геометрию, — `execute_code` (тот же `bpy`,
что и в headless). Остальное — инспекция (`get_scene_info`, `list_scene_items`,
`material_info`, `pick_viewport_object`, `get_viewport_screenshot`), интроспекция API
(`bpy_api_lookup`, `describe_node_type`), библиотеки и генераторы (Poly Haven, Sketchfab,
Poly Pizza, Hyper3D, Tripo, Hunyuan3D, Meshy) и `export_scene`.

| Задача | Чем решается |
|---|---|
| Сборка, риг, клипы, экспорт, регрессии | **`build_all.py`** (headless) |
| Довести форму, посмотреть варианты, разобрать чужой `.blend` | GUI + MCP (черновик) |
| Готовый статичный проп или текстуры/HDRI | библиотеки и генераторы аддона либо `generate_model_from_*` у Aura (без Blender) |

Анимацию и скиннинг библиотеки не дают вообще: AI-меши приходят с трисовой топологией
без управляемых UV и весов — это форма, а не модель под риг.

## Окружение и запуск

```powershell
$env:BLENDER = "G:/Steam/steamapps/common/Blender/blender.exe"
& $env:BLENDER --version
```

### Headless

```powershell
& $env:BLENDER --background --factory-startup --python script.py -- arg1 arg2
```

`--background` полностью функционален: меши, риг, анимация, рендер, экспорт. Это
штатный путь для сборки и проверок. `--factory-startup` для headless допустим: он не
трогает пользовательские настройки, но сбрасывает список аддонов — включай нужный аддон
после сброса, если он требуется скрипту.

На этой машине EEVEE в headless работает на GPU. Если на другой машине рендер не
поднимается — используй Workbench/Cycles, это не повод отказываться от headless-сборки.

### GUI

```powershell
& $env:BLENDER --python Tools/Blender/blender_boot.py
```

Из агентского шелла GUI нельзя запускать как обычный дочерний процесс: на Windows
отделённый процесс может умереть вместе с job-объектом родителя. Запускай GUI фоновой
задачей или из терминала пользователя. Признак готовности — **слушающий порт**, а не
старт процесса:

```powershell
netstat -ano | Select-String "127.0.0.1:9876.*LISTENING"
```

`blender_boot.py` пишет `BLENDER_READY` или `BLENDER_FAIL` в stdout и в
`Tools/Blender/blender_boot.log` (лог игнорируется git). Лог нужен потому, что у
GUI-приложения Windows stdout может не доехать до перенаправленного файла.

**Не используй `--factory-startup` для GUI-сессии с MCP**: он сбрасывает список
аддонов, и аддон придётся включать заново.

## MCP: только GUI

Аддон `blender_mcp` в `_blendermcp_ensure_server_running` начинается с:

```python
if bpy.app.background:
    return None
```

То есть в фоновом режиме сервер **не поднимается по замыслу автора**. Причина не в
капризе: команды исполняются на главном потоке через `bpy.app.timers`, а скрипт,
удерживающий фоновый процесс живым, этот поток как раз и блокирует.

Следствия:

- MCP работает только при запущенном GUI-Blender.
- Модальный оператор в открытом Blender (`bpy.ops.*` из UI, любой диалог) вешает
  главный поток, и MCP-команды не отвечают до клика. Симптом — «мост не ответил»,
  причина — не мост.
- Headless-сборка не требует MCP и не должна от него зависеть.

### Регистрация сервера

Сервер MCP один, но **регистраций по одной на клиент**. Команда в конфиге каждого
клиента одинаковая; пример:

```json
{
  "mcpServers": {
    "blenderMCP": {
      "command": "C:/Users/<user>/.local/bin/uvx.exe",
      "args": ["--from", "mcp-for-blender==2.2.2", "mcp-for-blender"],
      "env": {
        "PYTHONUTF8": "1",
        "BLENDER_PORT": "9876"
      }
    }
  }
}
```

- Пакет `blender-mcp` — устаревшее имя того же сервера; не ставь его отдельно.
- Обёртки `.cmd` и локальные клоны сервера не нужны.
- **Инструменты привязываются к сессии клиента при старте.** Правка конфига сама по
  себе не даёт инструментов; нужен перезапуск клиента. Пока перезапуска нет, работай
  через `blender_bridge.py` — протокол тот же.
- Установка аддона (если его ещё нет):

  ```powershell
  $env:BLENDERMCP_ADDONS_DIR = "C:/Users/<user>/AppData/Roaming/Blender Foundation/Blender/5.2/scripts/addons"
  $env:PYTHONUTF8 = "1"
  uvx --from mcp-for-blender==2.2.2 mcp-for-blender install-addon
  ```

  `$env:PYTHONUTF8 = "1"` обязателен. Без него CLI может упасть на печати результата
  (`UnicodeEncodeError` на cp1251), хотя аддон уже скопирован. Проверяй каталог
  аддонов, а не только код возврата.

  Проверено 2026-10-22 на 5.2.3 → пакет 2.2.2: аддон `blender_mcp.py` установлен,
  `ADDON_PROTOCOL_VERSION = 14` (у 2.1.3 было 13). В 2.2.2 добавился генератор Meshy
  (`create_meshy_job` / `refine_meshy_job` / `import_generated_asset_meshy`).
  После обновления аддона GUI-Blender надо перезапустить или сделать
  disable/enable у `Interface: MCP for Blender`.

### Команды аддона

Проверены вызовом в проекте-предшественнике: `ping`, `get_scene_info`, `execute_code`.
В FrostSpace end-to-end не проверялись: GUI-Blender не запускался (см. статус ниже).

Есть, но требуют сверки со схемой перед первым использованием:
`get_world_state_snapshot`, `get_object_info`, `list_scene_items`,
`get_viewport_screenshot`, `pick_viewport_object`, `export_scene`,
`describe_node_type`, `bpy_api_lookup`, `get_addon_info`, `drain_human_activity`.

`bpy_api_lookup` и `describe_node_type` — аналог Unity `unity_reflect`. Не полагайся на
память о `bpy`: в 5.x API меняется.

## Мост `blender_bridge.py`

```powershell
python Tools/Blender/blender_bridge.py ping
python Tools/Blender/blender_bridge.py scene
python Tools/Blender/blender_bridge.py call get_object_info '{"name": "Cube"}'
python Tools/Blender/blender_bridge.py code snippet.py
```

Протокол — **сырой JSON без кадрирования**, в обе стороны:

```
клиент -> аддон : {"type": "<команда>", "params": {...}}   # без длины и \n
аддон -> клиент : {"status": "success", "result": {...}}   # тоже без кадрирования
```

Аддон разбирает поток инкрементально (`buffer += data`, пробует `json.loads`), ответ
читается так же. Это **не** протокол Unity: там 8-байтная длина кадра и приветствие
`WELCOME UNITY-MCP 1 FRAMING=1`.

Адрес/порт/таймаут:

```powershell
$env:BLENDER_HOST = "127.0.0.1"
$env:BLENDER_PORT = "9876"
$env:BLENDER_TIMEOUT = "300"
python Tools/Blender/blender_bridge.py ping
```

Если ответа нет за таймаут — сначала проверь, запущен ли GUI-Blender и не заблокирован
ли он модальным окном. Это не ошибка моста.

## Golden loop проверки ассетов

Один вход вместо семи команд:

```powershell
python Tools/Blender/build_all.py --all --check --preview
```

Что внутри и что можно запускать отдельно:

```
1. сборка       build_all.py --asset <id>            # его же --dry-run показывает команду
2. инварианты   build_all.py --check                  # selftest.py + fbx_units.py
3. локомоция    build_all.py --asset robots --check    # measure_locomotion.py по каждому FBX
4. габарит      probes/probe_fbx_import.py -- <fbx>   # ручная проба, когда нужна
5. превью       build_all.py --asset <id> --preview --skip-build  # PNG для глаз
6. MCP          GUI-Blender                          # только если нужен визуальный контроль
7. отчёт        Tools/Blender/logs/build_all_*.log    # пишется сам
```

`Tools/Blender/selftest.py` — минимальный прогон этого цикла для low-poly ядра: собирает
меш, проверяет топологию, экспортирует FBX, импортирует обратно и сравнивает габарит.
Он входит в `--check` всегда, независимо от выбранных ассетов.

## Blender 5.x: проверенные грабли

| Симптом | Причина | Что делать |
|---|---|---|
| `action.fcurves` не существует / `AttributeError` | В 5.x действия слоёные | Кривые: `action.layers[i].strips[j].channelbags[k].fcurves`; проверить `probes/probe_action_api.py` |
| Видео не рендерится, в `image_settings.file_format` нет FFMPEG | В 5.2 вывод видео убран из настроек изображения | Рендерить PNG-секвенцию и кодировать внешним ffmpeg; путь к ffmpeg — параметр |
| Файл не найден, хотя путь выглядит правильным | Относительные пути Blender разрешает от своего CWD | В скриптах `os.path.abspath`; пути передавать явно |
| После `read_factory_settings()` пропал аддон | Сброс настроек очищает список аддонов | Включать аддон **после** сброса |
| Оператор вернул `CANCELLED` в headless | Часть операторов требует GUI-контекста (например, QuadriFlow) | Не считать это багом скрипта; для таких шагов использовать GUI или другой алгоритм |
| Объект «на ребре» после Unity-импорта | Статика экспортирована без `bake_space_transform=True` | Включить флаг и проверить габарит после импорта |
| Модель приезжает в Unity **отражённой по X** при верном габарите | `bake_space_transform` даёт левостороннюю конверсию осей (детерминант −1); bbox-проверка отражение не ловит | Статику экспортировать только через `export_static` (зеркало X + `reverse_faces`); сверять асимметричный признак — ADR 0007 |
| Габарит не сходится | glTF Y-up vs Blender Z-up; мерили в разных пространствах | Мерить после импорта или с учётом конверсии осей |
| Превью Quaternius пустое | `Alpha = 0` при `HASHED` | Принудительная непрозрачность только для превью; в Unity не переносить |
| Кириллица падает на печати | Windows-консоль cp1251 | `$env:PYTHONUTF8 = "1"`; в наших скриптах stdout переконфигурируется в UTF-8 |

## Уровень FrostSpace

Уровень (`Game`) собран тем же способом, что и роботы: геометрию делает скрипт,
Unity получает готовые FBX.

```
Tools/Blender/                                          Unity: LevelAssetPostprocessor
build_level.py -> Content/Models/Level/*.fbx (19 шт.) -> плоские нормали, sRGB->linear,
(level_lib.py, level_parts.py, level_layout.py)          палитра Level_*
```

- `level_lib.py` — палитра, сборщик меша, `export_static`, отчёт; `level_parts.py` —
  детали (дом, деревья, бочки, забор, интерьер); `level_layout.py` — раскладка и перевод
  координат `to_blender(x, z) = (x, −z)`; `build_level.py` — сборка всех файлов.
- **Статика экспортируется только через `export_static`**: он зеркалит X и переворачивает
  грани. Причина и проверка — ADR 0007; это не косметика, а исправление отражения.
- Рельеф — 90×90 м, заносы встроены в меш; зоны ходьбы выровнены, по ним же пересажены
  дом, деревья и пропы после пересборки.
- Превью: `render_level_previews.py` рендерит виды в `AssetSource/Previews/Level/`
  (вне Unity и вне git) — по ним проверяется стиль и композиция; правила и чистка
  временных кадров — `STYLE.md`.
- В `Assets/` не должно быть `.blend` и скриншотов: уровень живёт в скриптах и FBX.

## Ховербайк FrostSpace

Статика того же конвейера, но отдельным скриптом: файл нужен один, в раскладку уровня он
не входит, поэтому `build_level.py` его не трогает.

```bat
$env:PYTHONUTF8 = "1"
"%BLENDER%" --background --factory-startup --python Tools/Blender/build_hoverbike.py
```

- Геометрия — `hoverbike_parts.py` (`level_lib.LevelBuilder`, палитра `Level_*`), выход —
  `Assets/_Project/Content/Models/Level/Hoverbike.fbx`, постпроцессор уровня подставит
  материалы как остальному статическому уровню.
- Нос смотрит в Blender −Y: после `export_static` (зеркало X) это Unity +Z, то есть
  `transform.forward` байка. Ориентацию проверяй асимметричной деталью — подножкой
  и выхлопом по правому борту: bbox отражение по X не ловит (ADR 0007).
- Низ корпуса поднят на `hoverbike_parts.CLEARANCE` (0.53 м): это парение, а не ошибка —
  подушка под днищем рисуется шейдером `HoverCushion`. Детали при этом все посажены на
  корпус; правило «ничего не парит» — про них (`STYLE.md`).
- Контроль (2026-10-20, Blender 5.2.2): `verts=448 tris=820 shells=19`, габарит
  `(1.040, 3.014, 0.726)` м, non-manifold = 0.
- Префабы собираются меню `FrostSpace → Ride`: `Build Hoverbike Mock` (вариант A из
  примитивов) и `Build Hoverbike Prefab B` (обёртка над FBX: корень + компоненты +
  подушка). Код игры от варианта не зависит.

## Мотоцикл FrostSpace

```bat
$env:PYTHONUTF8 = "1"
"%BLENDER%" --background --factory-startup --python Tools/Blender/build_motorcycle.py
```

- Геометрия — `motorcycle_parts.py` (`level_lib.LevelBuilder`, палитра `Level_*`), выход —
  `Content/Models/Level/Motorcycle.fbx`: три узла `MotorcycleBody`, `MotorcycleWheelF`,
  `MotorcycleWheelR`. Колесо крутится в Unity, поэтому у него **свой pivot**: меш
  строится вокруг нуля, а `obj.location` ставится в ось колеса.
- **Не сдвигай вершины колеса на `−center`.** Первая версия сдвигала: объект уезжал в
  `+center`, меш — в `−center`, и оба колеса рисовались в центре рамы, а крылья висели
  в воздухе. Проверка после импорта: `mesh.bounds.center ≈ (0, 0, 0)` в локальных
  координатах, `transform.localPosition` — ось (0, 0.38, ±0.78/0.74).
- Нос — Blender −Y → Unity +Z (`export_static`, зеркало X по ADR 0007).
- Контроль (2026-10-21, Blender 5.2.2): body 1 696 тр., каждое колесо 464 тр.,
  габарит колеса `(0.245, 0.760, 0.741)` м, общий bbox `0.76 × 1.13 × 2.55` м.
- Префаб `Prefabs/Ride/Motorcycle_Model` собирается в редакторе: корень +
  `CharacterController` + `Motorcycle` + `MotorcycleRideVisual`, дети `Visual`
  (FBX, внутри него `Seat`), `ExitPoint`, `Headlight`, `BrakeLight`, `SnowSprayL/R`,
  `Exhaust`. `Seat` живёт **внутри** `Visual`: иначе наездник не качается и не кренится
  вместе с байком.
- Обломки и занос открытого вида меню — `build_wreck_parts.py` и `build_menu_open.py`
  (те же `Level_*`; в сцене масштабируются как пропы).

## Виды главного меню FrostSpace

Декорации трёх новых видов меню (станция, каньон, депо) — тот же статический конвейер,
но отдельным скриптом: в раскладку уровня они не входят.

```bat
$env:PYTHONUTF8 = "1"
"%BLENDER%" --background --factory-startup --python Tools/Blender/build_menu_views.py
```

- Четыре FBX в `Content/Models/Level/`: `MenuStationMast` (мачта 17 м: фундамент,
  ферма, площадка с ограждением, прожектор, лестница, антенна), `MenuIceWall` (ледяная
  стена с пластами и полками снега), `MenuCanyonBridge` (висячий мост с провисом
  настила, стойками, тросами и подкосами), `MenuDepotGate` (пилоны, створки с рёбрами,
  козырёк, фонари и рельсы).
- **Мачта — три объекта в одном FBX.** `MastDish` и `MastBeacon` выгружаются отдельными
  объектами со своим `origin`: тарелку в Unity вращает `MenuRadarSweep`, а маячку
  подменяется материал на `Menu_TechGlow`. Слей их в общий меш — и ни то, ни другое
  не заработает, не пересобрав FBX.
- Ориентация: фасад ворот смотрит в Blender −Y, то есть после `export_static`
  (зеркало X, ADR 0007) — в Unity +Z, на камеру вида. Ось симметрии мачты совпадает
  с X = 0, поэтому зеркало не сдвигает pivot тарелки.
- Контроль (замер в Unity по `MeshFilter`, модели 2026-10-21):

  | FBX | верт./тр. | мешей | габарит, м | min.y |
  |---|---|---|---|---|
  | `MenuStationMast` | 4 124 / 1 962 | 3 | 8.33 × 17.73 × 5.67 | −0.34 |
  | `MenuIceWall` | 1 613 / 684 | 1 | 12.89 × 14.09 × 4.54 | −0.69 |
  | `MenuCanyonBridge` | 2 728 / 1 276 | 1 | 9.82 × 2.93 × 2.82 | −0.79 |
  | `MenuDepotGate` | 3 573 / 1 608 | 1 | 12.40 × 9.59 × 8.85 | −0.30 |

  `min.y` у всех ниже нуля: модели сажаются в сцену на `−min.y`, а не на 0.
- Постановка в сцене — через инструменты Editor (ADR 0005). Стыки крупных моделей
  считай по геометрии: у гранёных торцов настоящая граница отступает от габарита на
  2–3 м, поэтому сегменты стены каньона ставятся с перекрытием ~8 м (ADR 0014).

## Роботы FrostSpace

Анимация в проекте уже есть — это двадцать роботов (`R1_PolarMechanic` … `R20_Cogsmith`); R6–R20 — вторая партия, принята 2026-10-18.

Конвейер:

```
Tools/Blender/                                     Unity: RobotAssetPostprocessor
build_robots.py -> Content/Models/Robots/R*.fbx -> клипы Walk/Idle/Sprint, loop, sRGB
(robot_lib.py, robot_designs.py)
```

- Сборка по именам: `build_robots.py -- R6 R7` собирает только эти дизайны, без
  аргументов — все 20. Пересборка перезаписывает FBX и не трогает Unity-контент.
- `robot_lib.stats(obj, arm)` проверяет не только вершины без веса, но и группы с
  именами несуществующих костей: опечатка в имени кости иначе даёт «зависшую» деталь.
- Обзор партии: `render_robot_lineup.py -- R6_Smelter …` (idle, спереди и сзади) и
  `make_robot_sheet.py -- --cols 5 R6_Smelter …` (контактный лист). Оба — headless,
  без внешних зависимостей.
- Координаты костей для новых деталей — `probes/probe_rig_points.py` (rest-точки после
  `widen_rig`); проверка «где у модели перед» — `probes/probe_orientation.py`.

- Скелет — T-поза ActionForge; детали жёстко привязаны к костям (rigid skinning), поэтому
  автоматические веса и протечки здесь не применяются.
- Клипы in-place; скорость стопы в контакте меряется `measure_locomotion.py` (Blender).
  Числа и контракт — в `ASSETS.md`.
- Разделение «визуальный префаб / сценовый актёр» — ADR 0004.

## Робо-животные FrostSpace

13 робо-животных для `Game`: 12 видов — гибрид на ригах CC0-паков Quaternius
(оболочка источника + наш череп и броня), паук собран целиком процедурно. Источник,
лицензия и что именно взято — `SOURCE.md` рядом с моделями; контракт ассетов — `ASSETS.md`;
причина решения — ADR 0009.

```
Tools/Blender/                                                Unity: AnimalAssetPostprocessor
build_animals.py -> Content/Models/RobotAnimals/<S>/RA_<S>.fbx -> клипы Idle/Move, loop, sRGB
(animal_lib.py, animal_designs.py)      + animal_manifest.json
```

- Сборка по именам: `build_animals.py -- Wolf Cow`; без аргументов — все 13. Пересборка
  перезаписывает FBX и обновляет манифест (`target`, `metric`, длины клипов, скорость стопы).
- `animal_lib.py` — извлечение рига из архива `AssetSource/*.zip`, роли костей по именам
  (они обезличены: у Wolf/Cat/Dog голова — `Bone.003`, у фермерских — `Head`), два take
  (`idle_loop`, `move_loop`), обнуление корневого смещения, нормировка размера, замер
  скорости стопы в контакте.
- `animal_designs.py` — гибрид: срез органической головы по весам, квадратный череп с
  визором, панели корпуса, броня суставов и хвоста; паук — свой риг и своя геометрия.
- **Стоячий хвост клипа.** У фермерской пачки Quaternius `Walk` вдвое длиннее цикла:
  после шагов идут ключи стояния (линейный дрейф ~0.0002 на кадр). `_static_tail` режет
  хвост ≥ 8 кадров (≥ 25 % длины), но только если поза на срезе замыкает петлю с остатком
  ≤ 5 % амплитуды; из равно хороших кандидатов берётся самый ранний. Гейт «нет пауз» —
  `probes/probe_animal_cycles.py` (смещение костей по кадрам, HOLDS, разрыв петли).
- Превью — `render_animal_previews.py` (rest/idle/move каждого вида) →
  `AssetSource/Previews/Animals/`; крупные планы головы и спины —
  `probes/probe_animal_geometry.py` (принимает один FBX за прогон).
- Разведка ригов — `probes/probe_animal_rigs.py` (кости, rest-точки, клипы, оси, fps),
  целостность мешей — `probes/probe_animal_mesh_health.py`. Первый проход «голый риг +
  процедурная геометрия» отклонён: модель читалась как стол на ножках.

Общие правила ниже остаются в силе для будущих персонажей:

- **Автоматические веса не гарантируют полноту.** Bone-heat на многокомпонентном меше
  может оставить вершины без весов. Проверяй отчёт и добивай сирот; не считай
  «auto weights» готовым результатом.
- **Наивные веса по расстоянию дают протечку.** Кость руки может проходить рядом с
  вершинами торса; при distance-весах торс поедет за рукой. Используй оболочки/
  сегментацию и проверяй позой, а не только числами.
- **Для шага и бега задавай траекторию стопы.** Чисто FK-нога при повороте бедра
  поднимает/проскальзывает стопу; аналитическая IK по траектории стопы решает это
  предсказуемо.
- **Ключи на все деформирующие кости во всех клипах.** Иначе Unity сохраняет значение
  из предыдущего клипа, и поза «протекает».
- **Ретаргет с разными rest-позами** копирует абсолютную ориентацию кости, а не дельту
  от rest. Иначе рука из T-позы уходит за спину.
- **≤4 влияния на вершину** — требование Unity-скина; проверяй перед экспортом.

## Чего нельзя

- ❌ Править `.blend` как текст — только через `bpy` или GUI.
- ❌ Держать `.blend` внутри `Assets/` — Unity попытается импортировать его как модель.
- ❌ Заявлять «мост работает», не сделав реального вызова с ответом.
- ❌ Хардкодить версию Blender или машинный путь в код.
- ❌ Запускать MCP-режим через `--background`.
- ❌ Использовать `--factory-startup` для GUI-сессии, где нужен аддон.
- ❌ Ожидать ответа MCP, пока в Blender открыт модальный диалог.
- ❌ Тащить в проект чужие аддоны и пайплайны «за компанию».

## Что проверено, а что нет

| Проверка | Результат |
|---|---|
| `selftest.py` на Blender 5.2.2 | **PASS**, drift 0.0000 м, 72 трис, 0 non-manifold |
| `probe_action_api.py` | **PASS**: `HAS fcurves: False`, 1 layer, 1 channelbag |
| `fbx_units.py` на экспортированном FBX | **PASS**: `UnitScaleFactor=1.0` |
| `probe_fbx_import.py` | **PASS**: видит `scale=0.01`, поворот 90°, мировой габарит верный |
| `measure_locomotion.py` на `R1_PolarMechanic.fbx` | **PASS** (2026-10-18): walk 0.927, sprint 8.917 м/с |
| `probe_fbx_import.py` на `R1_PolarMechanic.fbx` | **PASS** (2026-10-18): компенсация 0.01/90°, габарит верный |
| `build_level.py` (уровень, 19 FBX) | **PASS** (2026-10-18): 19 файлов, 26698 трис; рельеф 90×90, дом/пропы в габарите |
| Ориентация уровня после импорта в Unity | **PASS** (2026-10-18): крыльцо −1.40, фундамент +0.09, дверь −0.94, труба +1.42 — отражения нет (ADR 0007) |
| `render_level_previews.py` | **PASS** (2026-10-18): 10 видов в `AssetSource/Previews/Level/` |
| `build_robots.py` (вторая партия) | **PASS** (2026-10-18): 15 FBX, 2136–4604 трис, 0 вершин без веса |
| `render_robot_lineup.py` | **PASS** (2026-10-18): 40 кадров (20 роботов × 2 вида) |
| `make_robot_sheet.py` | **PASS** (2026-10-18): контактные листы 2400 px |
| `build_animals.py` (13 животных) | **PASS** (2026-10-19): 13 FBX, у каждого 2 клипа Idle/Move, манифест обновлён |
| `probes/probe_animal_cycles.py` | **PASS** (2026-10-19): 13 клипов без HOLDS и разрыва петли — пауз в шаге нет |
| `probes/probe_animal_geometry.py` | **PASS** (2026-10-19): 13 прогонов по одному FBX; открытых границ, вырожденных и нулевых граней нет |
| `probes/probe_animal_mesh_health.py` | **PASS** (2026-10-19): `RESULT PASS`, 0 вершин без веса |
| `render_animal_previews.py` | **PASS** (2026-10-19): rest/idle/move каждого вида в `AssetSource/Previews/Animals/` |
| `build_robots.py` (R1–R5) | не запускался в этой сессии: FBX уже собраны |
| `inspect_image.py` на PNG | **PASS**: размер, уникальные цвета, яркость |
| `blender_boot.py` в `--background` | **PASS**: корректный отказ, код 0 |
| `blender_bridge.py` с mock-сервером | **PASS**: ping/call получают ответ |
| Реальный мост к GUI-Blender | **Not verified**: GUI не запускался |
| GUI MCP: viewport screenshot / pick | **Not verified** |

## Происхождение знаний

Правила перенесены из проверок проекта-предшественника FrostWorm (MEMORY.md и runbook
по Blender) и **обезличены**: убраны имена костей, замеры конкретной модели, FWMT/D19,
Mixar/Modly/ComfyUI. Оставлены только воспроизводимые правила и способы проверки.
Дата переноса: 2026-10-18.
