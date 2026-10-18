# Карта проекта FrostSpace

Это **единственное** место с полным деревом структуры. Остальные документы ссылаются
сюда, а не копируют пути.

## Дерево

```
FrostSpace/
├─ AGENTS.md                     # обязательные правила для агентов
├─ README.md                     # краткий статус и вход
├─ Plans/                        # текущие многошаговые планы (README — правила)
├─ docs/                         # документация (этот файл — карта)
├─ Tools/
│  ├─ Blender/                   # сборка роботов, пробы, мост, lowpoly-ядро
│  └─ Unity/                     # прямой клиент к мосту Unity MCP
├─ AssetSource/                  # сырые архивы ассетов, ВНЕ git и вне Unity
└─ Assets/
   ├─ Scenes/                    # ЕДИНСТВЕННАЯ папка сцен
   │  ├─ Boot.unity              # заготовка шаблона, в игре не участвует
   │  └─ SnowLevel.unity         # игровой уровень, build index 0
   ├─ _Project/                  # всё, что пишем мы
   │  ├─ Scripts/                # ЕДИНСТВЕННЫЙ корень C#
   │  │  ├─ Features/            # сборка FrostSpace.Features
   │  │  │  ├─ Locomotion/       # RobotLocomotion, LocomotionDiagnostics
   │  │  │  └─ Camera/           # TopDownCamera (namespace FrostSpace.Features.View)
   │  │  ├─ Core/                # пока пусто
   │  │  └─ Infrastructure/      # пока пусто
   │  ├─ Editor/                 # сборка FrostSpace.Editor, только редактор
   │  │  └─ RobotAssetPostprocessor.cs # клипы, loop, sRGB при импорте FBX
   │  ├─ Tests/
   │  │  └─ EditMode/            # сборка FrostSpace.Tests.EditMode
   │  └─ Content/
   │     ├─ Animation/Controllers/  # R*.controller
   │     ├─ Models/Robots/          # R*.fbx
   │     ├─ Prefabs/Robots/         # R*_Model.prefab — только визуал
   │     ├─ Materials/Level/        # материалы уровня
   │     └─ Art|Audio|UI|VFX|Data/  # по мере появления
   ├─ ThirdParty/<Pack>/         # импортированное из AssetSource, с SOURCE.md; пока пусто
   ├─ Settings/                  # URP-ассеты шаблона, не игровые данные
   ├─ Plugins/Roslyn/            # DLL для execute_code Unity MCP
   └─ InputSystem_Actions.inputactions
```

Папки `_Project/**` появляются вместе с первым содержимым: пустые каталоги git и Unity
не сохраняют.

## С чего начинается выполнение

1. Unity открывает `Assets/Scenes/SnowLevel.unity` (build index 0) — игровой уровень;
   `Boot.unity` — заготовка и в игре не участвует.
2. Сцена `SnowLevel` — обычный сохранённый ассет: земля, дом, деревья, забор, свет, туман,
   камера, `Player`, `NPC`. Меню-пересборки нет (ADR 0005): контент правится вручную в Unity.
3. `Player` — сценовый актёр: тег `Player`, `CharacterController`, `RobotLocomotion`;
   внутри — визуальный префаб `R1_PolarMechanic_Model`.
4. `NPC` — корень с актёрами `NPC_R2_Furnace`, `NPC_R3_Scout`, `NPC_R4_Hauler`,
   `NPC_R5_Ranger`: только `Transform` и визуальный ребёнок, без ввода и физики.

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
| Параметры игрока | компонент `RobotLocomotion` на `Player` (сцена `SnowLevel`) |
| Теги и слои | `ProjectSettings/TagManager.asset` (используется стандартный тег `Player`) |
| Roslyn для MCP | `Assets/Plugins/Roslyn/` |
| Планы работ | `Plans/` (правила — `Plans/README.md`) |
| Blender-тулинг | `Tools/Blender/`, документация — `BLENDER.md` |
| Unity-мост | `Tools/Unity/unity_bridge.py`, документация — `UNITY_MCP.md` |
| Исходные архивы | `AssetSource/` (в git не входит) |
| Импортированные паки | `Assets/ThirdParty/<Pack>/` + `SOURCE.md` |

## Поисковые подсказки

- Код: `rg "class |struct |interface " Assets/_Project`.
- Сцены и префабы: `rg "guid:" Assets/Scenes Assets/_Project/Content`.
- Роботы: `rg "R[1-5]_" Assets/_Project/Content Tools/Blender`.
- Пакеты: `rg "com\\." Packages/manifest.json`.
- Blender-скрипты: `rg "bpy\\.|export_scene" Tools/Blender`.
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
