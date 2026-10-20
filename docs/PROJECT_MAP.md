# Карта проекта FrostSpace

Это **единственное** место с полным деревом структуры. Остальные документы ссылаются
сюда, а не копируют пути.

## Дерево

```
FrostSpace/
├─ AGENTS.md                     # обязательные правила для агентов
├─ README.md                     # краткий статус и вход
├─ Plans/                        # текущие многошаговые планы (README — правила); сюда же пишет Aura через junction `Assets/.Aura/plans`
├─ docs/                         # документация (этот файл — карта)
├─ Tools/
│  ├─ Blender/                   # сборка роботов, животных и уровня, пробы, мост, ядро
│  └─ Unity/                     # прямой клиент к мосту Unity MCP
├─ AssetSource/                  # сырые архивы ассетов, ВНЕ git и вне Unity
└─ Assets/
   ├─ .Aura/plans/               # планы работ всех агентов (README — правила); Unity dot-папки не импортирует
   ├─ Scenes/                    # ЕДИНСТВЕННАЯ папка сцен
   │  ├─ Boot.unity              # стартовая сцена, build index 0 (переход в игру ещё не реализован)
   │  └─ SnowLevel.unity         # игровой уровень, build index 1
   ├─ _Project/                  # всё, что пишем мы
   │  ├─ Scripts/                # ЕДИНСТВЕННЫЙ корень C#
   │  │  ├─ Features/            # сборка FrostSpace.Features
   │  │  │  ├─ Locomotion/       # RobotLocomotion, LocomotionDiagnostics
   │  │  │  ├─ Camera/           # TopDownCamera, CameraOcclusionFader; ns ...Features.View
   │  │  │  └─ Npc/              # NpcController, AirPatrolController (маршрут, паузы, антизастревание)
   │  │  ├─ Core/                # пока пусто
   │  │  └─ Infrastructure/      # пока пусто
   │  ├─ Editor/                 # сборка FrostSpace.Editor, только редактор
   │  │  ├─ RobotAssetPostprocessor.cs # клипы, loop, sRGB при импорте FBX роботов
   │  │  ├─ AnimalAssetPostprocessor.cs # клипы Idle/Move, loop, sRGB при импорте FBX животных
   │  │  └─ LevelAssetPostprocessor.cs # нормали, sRGB и палитра Level_* для уровня
   │  ├─ Tests/
   │  │  └─ EditMode/            # сборка FrostSpace.Tests.EditMode
   │  └─ Content/
   │     ├─ Animation/Controllers/  # R*.controller; RobotAnimals/<Species>/RA_<Species>.controller
   │     ├─ Models/Robots/          # R*.fbx
   │     ├─ Models/RobotAnimals/    # <Species>/RA_<Species>.fbx + SOURCE.md (происхождение ригов)
   │     ├─ Models/Level/           # FBX уровня: рельеф, дом, интерьер, деревья, пропы
   │     ├─ Prefabs/Robots/         # R*_Model.prefab — только визуал
   │     ├─ Prefabs/RobotAnimals/   # <Species>/RA_<Species>_Model.prefab — только визуал
   │     ├─ Materials/Level/        # материалы уровня Level_*
   │     ├─ VFX/Shaders/            # OcclusionReveal, OcclusionDebris
   │     └─ Art|Audio|UI|Data/      # по мере появления
   ├─ ThirdParty/<Pack>/         # импортированное из AssetSource, с SOURCE.md; пока пусто
   ├─ Settings/                  # URP-ассеты шаблона, не игровые данные
   ├─ Plugins/Roslyn/            # DLL для execute_code Unity MCP
   └─ InputSystem_Actions.inputactions
```

Папки `_Project/**` появляются вместе с первым содержимым: пустые каталоги git и Unity
не сохраняют.

## С чего начинается выполнение

1. Unity открывает `Assets/Scenes/Boot.unity` (build index 0) — стартовая сцена для
   инициализации сервисов; переход в `Assets/Scenes/SnowLevel.unity` (build index 1) пока
   не реализован, поэтому игра стартует прямо с уровня в редакторе.
2. Сцена `SnowLevel` — обычный сохранённый ассет: рельеф, дом с интерьером, деревья,
   забор, пропы (FBX из `Content/Models/Level/`), свет, туман, камера, `Player`, `NPC`.
   Меню-пересборки нет (ADR 0005): контент правится вручную в Unity.
3. `Player` — сценовый актёр: тег `Player`, `CharacterController`, `RobotLocomotion`;
   внутри — визуальный префаб `R1_PolarMechanic_Model`.
4. `NPC` — корень с актёрами `NPC_R2_Furnace`, `NPC_R3_Scout`, `NPC_R4_Hauler`,
   `NPC_R5_Ranger`: кинематический `CharacterController`, `NpcController` (маршрут из
   вейпоинтов) и визуальный ребёнок; без ввода и без обычной физики.
5. `NPC/Animals` — 13 актёров `AN_<Species>`: наземные с кинематическим
   `CharacterController` и `NpcController`, `AN_Eagle` и `AN_Piranha` — с
   `AirPatrolController` и маршрутом с высотой; визуальный ребёнок —
   `RA_<Species>_Model` (ADR 0009).

Когда появится composition root, он должен жить в `Assets/_Project/Scripts/Core/`, а
точка входа — быть явной (сцена или bootstrap-объект), а не размазанной по `Awake`.

## Где что искать

| Что | Путь / инструмент |
|---|---|
| Версия Unity | `ProjectSettings/ProjectVersion.txt` |
| Пакеты | `Packages/manifest.json`, `Packages/packages-lock.json` |
| Render pipeline | `Assets/Settings/PC_RPAsset.asset`, `Mobile_RPAsset.asset`, `QualitySettings.asset` |
| Цветовое пространство | `ProjectSettings/ProjectSettings.asset` (`m_ActiveColorSpace`) |
| Play Mode / сериализация | `ProjectSettings/EditorSettings.asset` |
| Ввод | `Assets/InputSystem_Actions.inputactions` |
| Сцены в сборке | `ProjectSettings/EditorBuildSettings.asset` |
| Визуальные префабы роботов | `Assets/_Project/Content/Prefabs/Robots/R*_Model.prefab` |
| FBX и контроллеры роботов | `Content/Models/Robots/`, `Content/Animation/Controllers/` |
| Визуальные префабы животных | `Content/Prefabs/RobotAnimals/<Species>/RA_<Species>_Model.prefab` |
| FBX и контроллеры животных | `Content/Models/RobotAnimals/`, `Content/Animation/Controllers/RobotAnimals/` |
| Регистр животных (клипы, габарит, метрика) | `Tools/Blender/animal_manifest.json` |
| Происхождение ригов животных | `Content/Models/RobotAnimals/SOURCE.md` |
| Модели уровня (FBX) | `Content/Models/Level/`, палитра — `Content/Materials/Level/` |
| Сборка уровня (Blender) | `Tools/Blender/build_level.py` (+ `level_lib/parts/layout`), превью — `AssetSource/Previews/Level/` |
| Параметры игрока | компонент `RobotLocomotion` на `Player` (сцена `SnowLevel`) |
| Маршрут NPC и животных | вейпоинты в `NpcController` на `NPC_R*` и `AN_<Species>`; у `AN_Eagle`/`AN_Piranha` — `AirPatrolController` с высотой; точки — дочерние `WP_*` |
| Скрытие перекрытий | `CameraOcclusionFader` + `OcclusionRevealDebris`, шейдеры `Content/VFX/Shaders/`, группы — `CameraOcclusionFadeGroup` (ADR 0008) |
| Теги и слои | `ProjectSettings/TagManager.asset` (используется стандартный тег `Player`) |
| Roslyn для MCP | `Assets/Plugins/Roslyn/` |
| Планы работ | `Assets/.Aura/plans/` — единственное место; Aura пишет туда нативно (правила — `Assets/.Aura/plans/README.md`) |
| Blender-тулинг | `Tools/Blender/`, документация — `BLENDER.md` |
| Unity-мост | `Tools/Unity/unity_bridge.py`, документация — `UNITY_MCP.md` |
| Исходные архивы | `AssetSource/` (в git не входит) |
| Импортированные паки | `Assets/ThirdParty/<Pack>/` + `SOURCE.md` |

## Поисковые подсказки

- Код: `rg "class |struct |interface " Assets/_Project`.
- Сцены и префабы: `rg "guid:" Assets/Scenes Assets/_Project/Content`.
- Роботы: `rg "R[1-5]_" Assets/_Project/Content Tools/Blender`.
- Животные: `rg "RobotAnimals" Assets/_Project Tools/Blender`.
- Пакеты: `rg "com\\." Packages/manifest.json`.
- Blender-скрипты: `rg "bpy\\.|export_scene" Tools/Blender`.
- Уровень: `rg "SnowCabin|level_parts|Level_" Tools/Blender Assets/_Project/Content`.
- Unity-мост: `rg "FRAMING|read_console|refresh_unity" Tools/Unity docs`.

Не открывай `Library/`, `Temp/`, `Logs/`, `obj/` и сгенерированные IDE-файлы: это
производные данные, они не описывают проект. Исключение — `Logs/Editor.log` для
диагностики (`UNITY_EDITOR.md` §8).

## Своё и стороннее

| Источник | Что это |
|---|---|
| `Assets/_Project/` | наш код и контент |
| `Assets/ThirdParty/` | импортированные CC0-паки с `SOURCE.md` |
| `Assets/Settings/` | URP-ассеты шаблона Unity |
| `Assets/Plugins/Roslyn/` | сторонние DLL для Unity MCP |
| `Packages/` | пакеты Unity и git-пакет Unity MCP |
| `AssetSource/` | сырые архивы, не часть Unity-проекта |
| `Tools/` | наша внешняя автоматизация, не runtime |

## Границы, которые нельзя размывать

- **Один корень C#** — `Assets/_Project/Scripts/`.
- **Один корень сцен** — `Assets/Scenes/`.
- **`Assets/Settings` — только URP.** Игровые данные — в `_Project/Content/Data`.
- **`Tools/` — вне `Assets/`.** Иначе Unity начнёт импортировать `.py` и логи.
- **`AssetSource/` — вне git.** В репозиторий попадает только импортированное.
