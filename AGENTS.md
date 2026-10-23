# AGENTS.md

## Что это

**FrostSpace** — Unity-проект на ранней стадии: есть играбельный вертикальный срез
(снежный уровень `Game`, роботы, локомоция игрока), sci-fi экран загрузки, главное меню
с 3D-диорамой и белые переходы между сценами. Назначение игры, жанр и целевые
платформы пока не зафиксированы; не выдумывай их по набору пакетов или ассетов.
Текущая реализация — источник истины, пока не доказано обратное.

Этот файл — обязательная точка входа для любого AI-агента. Читай его первым, затем
только те документы из `docs/`, которые нужны текущей задаче.

Он одинаков для всех рантаймов, которые работают с репозиторием: Aura, opencode,
Claude Code, Codex CLI, Gemini CLI, Cursor, MiniMax Code, WorkBuddy, Freebuff и любой
другой агент, который открывает проект. Формат — открытый стандарт `AGENTS.md`, а
скиллы лежат в формате Agent Skills (`SKILL.md` с YAML-frontmatter) и подключены к
всем рантаймам через мосты — см. раздел «Скиллы».

Правило ориентации: **сначала `AGENTS.md`, потом `docs/` по задаче, потом скилл под
тему.** Не начинай с обхода репозитория.

## Язык

- Пользователю и в документации — **русский**; код, имена, команды и пути — в оригинале.
- Документы и комментарии — русский; идентификаторы — английский.

## Факты проекта

| Параметр | Значение |
|---|---|
| Unity | **6000.6.7f1**, редактор `F:\Unity\Editor\6000.6.7f1\Editor\Unity.exe` |
| Render pipeline | URP **17.6.0**, Quality `Default` (единый) |
| Цвет | Linear |
| Input | только Input System (`Assets/InputSystem_Actions.inputactions`) |
| Play Mode | domain reload **выключен**, scene reload включён; Play тикает только при фокусе окна (`runInBackground` не заменяет) |
| Сцены | Поток `Boot` (0, загрузка сервисов) → `MainMenu` (1, диорама и UI) → `Game` (2, игровой уровень); переходы — только через `Core/SceneController.cs` (ADR 0011) |
| Код | `Core/` (2 файла), `Features/` (8 фич: Boot, Camera, Hud, Locomotion, Menu, Npc, Ride, Scan), `Editor/` (5 файлов: 3 постпроцессора FBX + сборка WebGL + сборщик ховербайка), `Tests/EditMode/` (5 файлов, 42 тест); asmdef `FrostSpace.Core`, `FrostSpace.Features`, `FrostSpace.Editor`, `FrostSpace.Tests.EditMode` |
| Unity MCP | пакет `com.coplaydev.unity-mcp`, порт `127.0.0.1:6400` |
| Blender | **5.2.3 LTS**, аддон `blender_mcp` (протокол 14), порт `9876` (только GUI) |
| Исходники ассетов | `AssetSource/` — вне git, Unity их не видит |

Точные версии пакетов — `Packages/manifest.json` и `Packages/packages-lock.json`;
настройки — `ProjectSettings/`. Не переписывай эту таблицу по памяти: сверяйся с файлами.

**Источник правды о версиях и именах.** Эта таблица, `docs/PROJECT_OVERVIEW.md` и
`README.md` не должны расходиться. Правь все три одновременно; перед правкой
сверяйся с файлами: `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`,
`ProjectSettings/EditorBuildSettings.asset`, `Assets/Scenes/`.

**Domain reload выключен.** Любое мутабельное статическое состояние обязано
сбрасываться через `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`,
иначе второй запуск Play Mode будет работать на старых данных.

## Модель работы

| Задача | Режим |
|---|---|
| Сборка/экспорт ассетов, тесты геометрии, замеры, регрессии | **headless-скрипт Blender** |
| Визуальная проверка или интерактивная правка в Blender | **GUI + MCP**: `python Tools/Blender/gui_session.py run <файл>.py --auto` поднимает сессию и закрывает её |
| Компиляция, Console, тесты, сцена, скриншот в Unity | **живой Unity MCP** |
| Чистая проверка, CI, сборка при закрытом редакторе | **Unity batch-mode** |
| Долгий или unattended-прогон | headless/batch, не MCP |

Правила:
- **Скрипт — источник истины.** Всё воспроизводимое живёт в `Tools/` или в проектном
  генераторе, а не в разовой команде MCP.
- **Единый вход ассетов — `Tools/Blender/build_all.py`** (ADR 0017). Сборка, `--check` и
  `--preview` идут только через него; реестр ассетов — `PIPELINE` в этом файле, флаги —
  `docs/BLENDER.md` §«Единая точка входа».
- **Ассет считается сделанным**, только если он появился из прогона `build_all.py`,
  прошёл `--check` и оставил превью. Разовая правка в GUI-Blender — черновик.
- **MCP — глаза и руки, не замена скрипту.** Результат MCP-правки, который должен
  сохраниться, переносится в скрипт и перепроверяется headless.
- **Качество не зависит от транспорта.** Headless и GUI используют один `bpy`;
  качество даёт проверка, а не окно.
- **Не запускай GUI-Blender и Unity batch без необходимости.** GUI открывает окно;
  batch при живом редакторе падает на `Temp/UnityLockfile`.
- **Код-режим MCP: одна операция на сниппет, без ожиданий внутри кода.** Таймеров в
  песочнице нет; незавершающееся ожидание вешает сессию (детали — `docs/UNITY_MCP.md`,
  «Зависания сессии агента»).

## Карта документации

- `docs/PROJECT_OVERVIEW.md`, `ARCHITECTURE.md`, `PROJECT_MAP.md` — проект, слои, пути.
- `docs/DEVELOPMENT.md`, `ASSETS.md`, `BLENDER.md`, `UNITY_MCP.md`, `UNITY_EDITOR.md` — работа и инструменты.
- `docs/DEPLOY.md` — публикация WebGL на GitHub Pages: раннер, деплой, гашение процесса.
- `docs/STYLE.md` — визуальный стиль и дизайн-код ассетов: правила, палитры, проверка.
- `docs/README.md` — индекс; `docs/decisions/README.md` — индекс ADR (принятые решения
  и их причины), сами решения — `docs/decisions/0*.md`.
- `Assets/.Aura/plans/README.md` — правила ведения текущих планов; `Assets/.Aura/plans/` — сами планы.

Не дублируй содержимое этих документов в новых файлах: ссылайся на источник.

## Скиллы (готовые инструкции)

Скиллы проекта лежат **в одном месте**: `.Aura/Skills/<имя>/SKILL.md` + вложенные
`references/`, `resources/`, `scripts/`. Это инструкции, а не код, и они не привязаны
к конкретной модели или агенту.

- **34 официальных скилла Unity** (`Unity-Technologies/skills`, commit `9d28210`) —
  список и версия в `.Aura/Skills/.aura-unity-skills.json`. Каталог вендорный: правки
  в нём теряются при переустановке; свои инструкции держи в `docs/`, а не здесь.
- **1 проектный скилл `plans`** — правила ведения планов; он не вендорный, при
  переустановке 34 официальных его нельзя затирать.
- **1 проектный скилл `frostspace-3d-assets`** — как в проекте производятся 3D-ассеты:
  единственный вход `build_all.py`, реестр `PIPELINE`, приёмка «сборка + `--check` + превью»,
  поиск Blender, палитра `Level_*`, `export_static`. Читать перед любой задачей про
  Blender, FBX, меши и новые 3D-ассеты.

### Как их находят разные агенты

Единый источник — `.Aura/Skills`. Файлы не копируются: рантаймы либо указывают на
канон прямо, либо получают junction-мост (ссылку на каталог).

| Рантайм | Как видит скиллы | Что для этого нужно |
|---|---|---|
| AuraForUnity | `.Aura/Skills` напрямую | ничего — это и есть канон |
| OpenCode | `opencode.json` → `skills.paths: ["./.Aura/Skills"]` | `opencode.json` в корне репозитория (в git) |
| MiniMax Code | `{{DATA_DIR}}/skills/<имя>` | project-scope у него нет, только глобальный каталог → junction'и |
| WorkBuddy | `AGENTS.md` → «прочитай `.Aura/Skills/<имя>/SKILL.md`» | ничего, правило уже в этом файле |
| Freebuff | то же | ничего |
| Codex, Gemini CLI, Cursor, Kiro | `.agents/skills` — стандарт Agent Skills | junction-мост (на случай, если такой рантайм появится) |
| Claude Code | `.claude/skills` | **не используется в проекте** — папки нет, создаётся только флагом `-Claude` |

Папки `.agents/` и `.claude/` — **мосты**, а не хранилище скиллов. Физические файлы лежат
в `.Aura/Skills`; в мостах — только ссылка на каталог (junction). Правка файлов в мосте
меняет канон, копий нет нигде.

- `.agents/` — открытый стандарт Agent Skills: читают Codex, Gemini CLI, Cursor, Kiro
  (opencode — как фолбэк, основной у него `opencode.json`). К Claude Code отношения
  не имеет, название `.agents` — стандарт, а не папка Claude.
- `.claude/` — читает **только** Claude Code. В этом проекте Claude Code не используется,
  поэтому папки нет и создавать её не нужно. Флаг `-Claude` существует лишь на этот случай.

Мосты создаёт скрипт `Tools/Agent/LinkAgentSkills.ps1` (идемпотентен):

```powershell
pwsh -File Tools/Agent/LinkAgentSkills.ps1          # .agents\skills
pwsh -File Tools/Agent/LinkAgentSkills.ps1 -Global  # + каталог MiniMax Code
pwsh -File Tools/Agent/LinkAgentSkills.ps1 -Claude   # + .claude\skills (если нужен Claude Code)
pwsh -File Tools/Agent/LinkAgentSkills.ps1 -Check    # только проверка
```

Мосты перечислены в `.gitignore`: в git попадает только `.Aura/Skills` и `opencode.json`.
**Aura читает `.Aura/Skills` напрямую — её пайплайн мосты не затрагивают.**

### Как пользоваться

- Перед задачей по теме скилла прочитай `.Aura/Skills/<имя>/SKILL.md` и нужные файлы
  из его папки. Путь одинаков для всех агентов — читай по нему, если рантайм не
  подхватил нативный discovery.
- Скилл задаёт порядок работы и проверок по теме, но не отменяет этот файл и `docs/`;
  при расхождении главнее `AGENTS.md` и фактическая реализация в проекте.

### Что релевантно этому проекту

Проект — 3D, URP 17.6, единственная цель — WebGL, ввод только Input System,
интерфейс на uGUI + TextMeshPro.

| Скилл | Когда нужен |
|---|---|
| `unity-cli` | Editor, лицензии, установка, version control, запуск Unity из CLI |
| `unity-pipeline` | живой Editor: перекомпиляция, тесты, C# в рантайме, аудит проекта |
| `unity-package-management` | версии пакетов, headless/CI-установка, выбор пакетов |
| `urp-postprocessing` | Volume, bloom, tonemapping, цветокоррекция |
| `physics-3d-collision` | `CharacterController`, триггеры, рейкасты, проходы сквозь коллайдеры |
| `shader-graph-create-custom-node` | кастомные шейдеры из HLSL |
| `validate-urp-render-graph-renderer-feature` | проверка рендерер-фич |
| `ui-ugui` | Canvas, `RectTransform`, prefab UI — меню и HUD строятся кодом на uGUI |
| `ui` | маршрутизатор: если не назвал фреймворк |
| `optimize-web` | вес и скорость WebGL-сборки |
| `generate-editor-search-query` | поиск ассетов и объектов в редакторе |
| `project-auditor-fixes` | аудит проекта (нужен пакет `com.unity.project-auditor`, не установлен) |
| `plans` | планы многошаговых задач |

**По ситуации:** `initialize-ai-navigation` (если заводим NavMesh вместо своего
патрулирования), `optimize-audio` и `audio-setup-mixers` (если появится Audio Mixer),
`optimize-text-mesh-pro` (шрифты), `asset-transformer-toolkit` (импорт FBX),
`build-gtk` (свой Editor-инструмент на Graph Toolkit), `build-live-game` (live-сервисы),
`localization` (i18n; сейчас интерфейс намеренно латинский).

**Не применять в этом проекте** — 2D (`2d-pixel-perfect`, `create-2d-physics`,
`create-tile-palette-or-rule-tiles`, `manage-sprite-atlas`, `sprite-editor`,
`sprite-segment-3x3grid`), монетизация и реклама (`implement-in-app-purchases`,
`levelplay-unity-integration`), сеть (`setup-multiplayer-services`,
`setup-vivox-voice-chat`), `new-unity-project` (проект уже создан),
`migrate-birp-to-urp` (проект уже на URP), `ui-imgui` и `ui-uitk` (интерфейс на uGUI).

## Структура (кратко)

- `Assets/Scenes/` — единственная папка сцен (`Boot`, `MainMenu`, `Game`).
- `Assets/_Project/Scripts/` — единственный корень C#: `Core/`, `Features/<Feature>/`.
- `Assets/_Project/Editor/`, `Assets/_Project/Tests/EditMode/`.
- `Assets/_Project/Content/{Animation,Audio,Data,Materials,Models,Prefabs,Settings,VFX}/`.
- `Assets/_Project/Content/Prefabs/{Ride,RobotAnimals,Robots}/` — визуальные префабы.
- `Assets/ThirdParty/` — импортированное из `AssetSource/`, с `SOURCE.md` (каталога пока
  нет: появится вместе с первым импортированным пакетом).
- `Assets/Settings/` — только URP-ассеты шаблона; `Assets/Plugins/` — сторонние DLL.
- `Assets/WebGLTemplates/FrostSpace/` — свой шаблон WebGL (`PROJECT:FrostSpace`).
- `Tools/Blender/`, `Tools/Unity/`, `Tools/Agent/` — автоматизация вне `Assets/`, Unity их
  не импортирует.
- `.Aura/Skills/` — единственный источник скиллов; `.agents/skills/` — junction-мост к нему
  (в git не идёт). `.claude/skills/` в проекте нет: Claude Code не используется.

Полное дерево и назначение путей — только в `docs/PROJECT_MAP.md`. Пустые папки не
создавай: структура появляется вместе с первым содержимым.

## Порядок работы

1. Прочитай `AGENTS.md`, затем `docs/README.md`.
2. Открой только документацию, относящуюся к задаче.
3. Найди текущую реализацию: символы, типы, вызывающие места, сериализованные ссылки,
   связанные сцены и префабы. Не открывай файлы целиком без необходимости.
4. Для многофайлового изменения создай план в `Assets/.Aura/plans/` и критерии приёмки (см. ниже).
5. Выбери минимальную безопасную реализацию. Не делай несвязанный рефакторинг.
6. Соблюдай существующие конвенции, стиль C# и сериализацию Unity.
7. Выполни самые дешёвые проверки, которые имеют смысл.
8. Обнови карту/архитектуру, если изменились пути, границы или процесс; устаревший
   документ удаляй, а не оставляй «на всякий случай».
9. В конце честно перечисли: что изменено, что проверено, что не проверено и какие риски.

Если задача неоднозначна — изучи реализацию; спрашивай только при реальной блокировке.

## Планы в работе

- `Assets/.Aura/plans/` — единственное место планов проекта (очередь текущих многошаговых
  работ). Подробные правила — `Assets/.Aura/plans/README.md`.
- Перед многошаговой задачей создай `Assets/.Aura/plans/YYYY-MM-DD-<тема>.md` и укажи в нём цель,
  этапы с чекбоксами, затрагиваемые файлы, критерии приёмки и риски.
- По ходу работы отмечай выполненные пункты и веди «Журнал выполнения». Если сессия
  прервалась, следующий агент читает план и продолжает с первого невыполненного пункта.
- После выполнения и проверки удали файл плана; `README.md` в папке остаётся.
- Один файл — один текущий план; архив завершённых планов в папке не ведём.
- Путь выбран под Aura: `create_or_edit_plan` пишет только в `Assets/.Aura/plans/`, изменить это
  нельзя. Поэтому все агенты и люди кладут планы туда же — один путь, без зеркал и ссылок.
  Папка начинается с точки: Unity её не импортирует, `.meta` не нужны, в сборку не попадает.
  Поиск по проекту (`rg`, `git grep`) скрытые каталоги по умолчанию пропускает — путь указывай явно.

## Безопасность и ограничения

- **Никогда не редактируй `.unity`, `.prefab`, `.mat`, `.asset` как YAML-текст.** Только
  Unity Editor или Unity MCP.
- **Никогда не удаляй и не переименовывай `.meta`.** Отсутствующий `.meta` ломает GUID-
  ссылки у всей команды.
- Не перемещай существующие ассеты и не меняй `ProjectSettings/`, `Packages/manifest.json`
  или версию Unity без конкретной причины и явного согласования.
- Не добавляй пакеты, DI-контейнеры, event bus, service locator и «абстракции на будущее»
  без доказанной пользы.
- Не добавляй Editor-меню и скрипты, которые пересобирают или перезаписывают контент
  (уровень, префабы роботов и животных, контроллеры): правка — только вручную в Unity
  (ADR 0005). Исключение, зафиксированное ADR 0012: префаб транспорта
  `Hoverbike_Model` собирается скриптом `Editor/HoverbikeModelBuilder.cs` из FBX в
  Blender — потому что модель и посадка задаются скриптом, а не руками.
- **`.blend` не правится как текст.** Не держи `.blend` внутри `Assets/` — Unity попытается
  импортировать его как модель.
- Не хардкодь машинные пути и версии Blender/Unity в коде: используй env/CLI/документацию.
- Не запускай разрушительные команды (`git reset --hard`, `git clean`, `rm -rf`).
- Не коммить и не пушь, если об этом не попросили.
- Секреты и токены не попадают в репозиторий, логи и сообщения.

## Код

- Namespace: `FrostSpace.Core`, `FrostSpace.Features.<Feature>`,
  `FrostSpace.Infrastructure`, `FrostSpace.Editor`, `FrostSpace.Tests`.
  Исключение: папка `Features/Camera/` использует `FrostSpace.Features.View` — так
  зафиксировано в ADR 0010; не переименовывай молча.
- Имя файла = имя основного типа. Один тип — одна ответственность.
- Поля, видимые дизайнеру, — `[SerializeField] private`; публичных полей в рантайме нет.
- Поля — `_camelCase`, методы — `PascalCase`, интерфейсы — `I` + `PascalCase`.
- Editor-код — только в `Assets/_Project/Editor/`. Изоляцию от рантайма даёт
  `includePlatforms: ["Editor"]` в `FrostSpace.Editor.asmdef`, поэтому оборачивать
  эти файлы в `#if UNITY_EDITOR` не нужно.
- `ScriptableObject` — для дизайн-данных, не для runtime-состояния.
- Явные ссылки и `Initialize` важнее `FindObjectOfType`; без менеджеров-богов и циклов.
- `.asmdef` — один на слой (`FrostSpace.Core`, `FrostSpace.Features`,
  `FrostSpace.Editor`, `FrostSpace.Tests.*`), не на папку; ссылки между сборками — явно.
- Не оставляй объявленных событий без подписчиков: это «абстракция на будущее».
- Визуальный префаб — только модель и `Animator`; `CharacterController`, ввод и
  `RobotLocomotion` живут на сценовом актёре (`Player`, `NPC_*`) — ADR 0004.

## Проверка

**Unity:**
1. `refresh_unity` → `read_console(types: ["error"])`; сомневаешься в свежести
   сборки — `docs/UNITY_EDITOR.md` §1.
2. Только если ошибок нет — `run_tests` → дождаться `get_test_job`. Не тестируй при
   ошибках компиляции: будет ложный `Passed` на старой сборке.

**Blender:**
1. `python Tools/Blender/build_all.py --asset <id> --check` — сборка и инварианты
   (`selftest.py`, `fbx_units.py`, профильные шаги); лог — `Tools/Blender/logs/`.
2. Превью: `--preview` (или `--preview --skip-build` по готовым файлам) — то, что числами
   не видно.
3. Профильные пробы: `probes/probe_action_api.py`, `probes/probe_fbx_import.py`; единицы
   и габариты — `fbx_units.py`; рендер-разбор — `inspect_image.py`.
4. GUI-MCP — только если нужен визуальный/интерактивный результат; его результат обязан
   вернуться в скрипт и перепровериться headless. Живую сессию не держи открытой:
   `python Tools/Blender/gui_session.py run <файл>.py --auto` — поднялся, выполнил, закрылся.

**Общее:** не заявляй, что код компилируется, сцена работает или тест проходит, если
проверка фактически не выполнялась. Не удалось — напиши `Not verified` и причину.

## Формат финального ответа

1. Что изменено (1–3 предложения).
2. Затронутые файлы/модули.
3. Какие проверки реально запускались и с каким результатом.
4. Что не проверено и почему.
5. Риски, техдолг, допущения.
6. Ручные шаги проверки, если нужны.
