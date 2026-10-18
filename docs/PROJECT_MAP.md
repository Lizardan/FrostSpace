# Карта проекта FrostSpace

Это **единственное** место с полным деревом структуры. Остальные документы ссылаются
сюда, а не копируют пути.

## Дерево

```
FrostSpace/
├─ AGENTS.md                     # обязательные правила для агентов
├─ README.md                     # краткий статус и вход
├─ docs/                         # документация (этот файл — карта)
├─ Tools/
│  ├─ Blender/                   # headless-сборка, мост, пробы, lowpoly-ядро
│  └─ Unity/                     # прямой клиент к мосту Unity MCP
├─ AssetSource/                  # сырые архивы ассетов, ВНЕ git и вне Unity
└─ Assets/
   ├─ Scenes/                    # ЕДИНСТВЕННАЯ папка сцен
   │  └─ Boot.unity              # текущая стартовая сцена
   ├─ _Project/                  # всё, что пишем мы
   │  ├─ Scripts/                # ЕДИНСТВЕННЫЙ корень C#
   │  │  ├─ Core/
   │  │  ├─ Features/<Feature>/
   │  │  └─ Infrastructure/
   │  ├─ Editor/                 # только #if UNITY_EDITOR
   │  ├─ Tests/
   │  │  ├─ EditMode/
   │  │  └─ PlayMode/
   │  └─ Content/
   │     ├─ Art/  Audio/  Materials/  Models/
   │     ├─ Prefabs/  UI/  VFX/
   │     └─ Data/                # ScriptableObject-конфиги и баланс
   ├─ ThirdParty/<Pack>/         # импортированное из AssetSource, с SOURCE.md
   ├─ Settings/                  # URP-ассеты шаблона, не игровые данные
   ├─ Plugins/Roslyn/            # DLL для execute_code Unity MCP
   └─ InputSystem_Actions.inputactions
```

Папки `_Project/**` пока могут отсутствовать: они создаются с первым содержимым.
Не создавай пустые каталоги «для структуры» — git и Unity их не сохраняют.

## С чего начинается выполнение

1. Unity открывает `Assets/Scenes/Boot.unity` (build index 0).
2. В сцене три корневых объекта: `Main Camera`, `Directional Light`, `Global Volume`.
3. Скриптов и bootstrap-логики **нет**. Значит «старт игры» сейчас = пустая сцена.

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
| Теги и слои | `ProjectSettings/TagManager.asset` (пока стандартные) |
| Roslyn для MCP | `Assets/Plugins/Roslyn/` |
| Blender-тулинг | `Tools/Blender/`, документация — `BLENDER.md` |
| Unity-мост | `Tools/Unity/unity_bridge.py`, документация — `UNITY_MCP.md` |
| Исходные архивы | `AssetSource/` (в git не входит) |
| Импортированные паки | `Assets/ThirdParty/<Pack>/` + `SOURCE.md` |

## Поисковые подсказки

- Код: `rg "class |struct |interface " Assets/_Project` (когда появится).
- Сцены и префабы: `rg "guid:" Assets/Scenes Assets/_Project/Content`.
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
