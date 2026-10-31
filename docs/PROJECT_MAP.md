# Карта проекта FrostSpace

Это **единственное** место с полным деревом структуры. Остальные документы ссылаются
сюда, а не копируют пути.

## Дерево

```
FrostSpace/
├─ AGENTS.md                     # обязательные правила для агентов
├─ README.md                     # краткий статус и вход
├─ .Aura/Skills/                 # ЕДИНСТВЕННЫЙ источник скиллов (34 Unity + 2 проектных)
├─ .agents/skills/               # junction на .Aura/Skills — стандарт Agent Skills
├─ opencode.json                 # skills.paths -> .Aura/Skills (opencode видит канон напрямую)
├─ .qoder/repowiki/wiki_plan.yaml # правила генерации Repo Wiki Qoder; остальное в .qoder/ — локальное
├─ docs/                         # документация (этот файл — карта)
├─ Tools/
│  ├─ Agent/                     # LinkAgentSkills.ps1 — мосты к скиллам для всех агентов
│  ├─ Blender/                   # сборка роботов, животных и уровня, пробы, мост, ядро
│  └─ Unity/                     # прямой клиент к мосту Unity MCP
├─ AssetSource/                  # сырые архивы ассетов, ВНЕ git и вне Unity
└─ Assets/
   ├─ .Aura/plans/               # планы работ всех агентов (README — правила); Unity dot-папки не импортирует
   ├─ Scenes/                    # ЕДИНСТВЕННАЯ папка сцен
   │  ├─ Boot.unity              # стартовая сцена, build index 0: загрузка сервисов → MainMenu
   │  ├─ MainMenu.unity          # главное меню с диорамой, build index 1
   │  ├─ CharacterSetup.unity    # выбор из трёх корпусов, build index 2
   │  ├─ Train.unity             # интерьер, переноска, квест, диалоги R13, финал; build index 3
   │  ├─ Frontier.unity          # депо: прибытие поезда 8×24, боковой проём, охрана, R7; build index 4
   │  └─ Game.unity              # прежний уровень, build index 5, вне потока
   ├─ _Project/                  # всё, что пишем мы
   │  ├─ Scripts/                # ЕДИНСТВЕННЫЙ корень C#
   │  │  ├─ Features/            # сборка FrostSpace.Features
   │  │  │  ├─ Boot/             # BootScreen — консоль загрузки сервисов на старте
   │  │  │  ├─ Camera/           # PlayerCameraRig, CameraMode, CameraPose, InteriorZone, PlayerSilhouette,
   │  │  │  │                    # PlayerLocalView, CameraOcclusionFader/Debris/Group, ScreenAperture.
   │  │  │  │                    # Namespace папки — FrostSpace.Features.View (ADR 0010)
   │  │  │  ├─ Carry/            # Carryable (предмет), CarrySlot (гнездо), CarryController (E:
   │  │  │  │                    # взять, поставить, бросить)
   │  │  │  ├─ Character/        # RobotProfile (SO), CharacterCatalog (SO), CharacterSelection
   │  │  │  │                    # (PlayerPrefs + сброс статики), PlayerVisualBinder, CharacterSetupScreen,
   │  │  │  │                    # CharacterCardPreview (камера в RenderTexture), CharacterBackdrop, UiSnowfall
   │  │  │  ├─ Dialogue/         # DialogueAsset (граф реплик и вариантов), DialogueNode,
   │  │  │  │                    # DialogueChoice, DialogueSession (чистая логика графа),
   │  │  │  │                    # DialogueSpeaker (маркер на NPC), DialogueMarker (плашка E),
   │  │  │  │                    # DialogueController (E и блокировки ввода/камеры),
   │  │  │  │                    # DialogueScreen (панель диалога)
   │  │  │  ├─ Hud/              # PlayerHud (перекрестие, пул подписей, рамка цели, Q SCAN/SCANNING,
   │  │  │  │                    # счётчик FPS), ScanLabelView (одна подпись из пула),
   │  │  │  │                    # QuestTracker (АККУМУЛЯТОРЫ и текущая цель),
   │  │  │  │                    # ExperienceBar (уровни и полоса опыта)
   │  │  │  ├─ Locomotion/       # RobotLocomotion (базис WASD), PlayerAimPose, LocomotionDiagnostics
   │  │  │  ├─ Menu/             # MainMenuScreen, MenuViewSwitch (пять видов меню), MenuCameraOrbit,
   │  │  │  │                    # MenuCameraSweep (проезд равнины, лаборатории и депо), MenuRadarSweep,
   │  │  │  │                    # MenuRobotPose (позы роботов меню), MenuRobotWalker (маршрут),
   │  │  │  │                    # MenuRideScroll (прокрутка дороги RIDE), MenuRider (посадка наездника
   │  │  │  │                    # в меню), PauseMenuScreen (Esc: обзор, журнал, настройки), TechButtonFx, BeaconPulse
   │  │  │  ├─ Npc/              # NpcController, AirPatrolController (маршрут, паузы, антизастревание),
   │  │  │  │                    # AnimationCulling (AlwaysAnimate и честные границы скинных мешей)
   │  │  │  ├─ Progression/      # PlayerExperience (уровни, кривая опыта, награда квеста)
   │  │  │  ├─ Quest/            # QuestAsset (SO), QuestObjective, QuestLog (чистая логика),
   │  │  │  │                    # QuestEvent, QuestSnapshot, QuestDirector (сценовый владелец
   │  │  │  │                    # прогресса), ProgressSave (ключ FrostSpace.Save, ADR 0031)
   │  │  │  ├─ Ride/             # Hoverbike, HoverbikeGlow, Motorcycle, MotorcycleRideVisual,
   │  │  │  │                    # RiderPose (посадка верхом поверх клипа), RiderCockpit
   │  │  │  │                    # (данные посадки байка: наклон, шея, KneeOut, Seat Point),
   │  │  │  │                    # Mech (ездовый ходун), MechSeat (сиденье едет за Chest),
   │  │  │  │                    # MechSelection (общий адресат E: сканер, иначе ближайший),
   │  │  │  │                    # RideCameraProfile (радиус и пределы облёта)
   │  │  │  ├─ Scan/             # ObjectScanner (Q-сонар + ближайшая и прицельная детекция),
   │  │  │  │                    # ScanTarget (маркер и реестр целей), ScanPulse (кольцо по земле),
   │  │  │  │                    # ScanMath (чистая математика)
   │  │  │  └─ Story/            # TrainMotionRig (параллакс и снег), TrainSequenceDirector
   │  │  │                       # (отъезд камеры и переход), TitleCardScreen (белый титул),
   │  │  │                       # DepotArrivalDirector (прибытие поезда, створки, выход),
   │  │  │                       # PlayerEquipment (карта и сканер из диалога)
   │  │  └─ Core/                # сборка FrostSpace.Core: SceneController (персистентный владелец
   │  │                          # перехода и единственный вызывающий SceneManager),
   │  │                          # MusicManager (фоновая музыка), GameSettings (музыка и каналы детекции)
   │  ├─ Editor/                 # сборка FrostSpace.Editor, только редактор
   │  │  ├─ RobotAssetPostprocessor.cs   # клипы, loop, sRGB при импорте FBX роботов
   │  │  ├─ AnimalAssetPostprocessor.cs  # клипы Idle/Move, loop, sRGB при импорте FBX животных
   │  │  ├─ MechAssetPostprocessor.cs    # клипы Idle/Walk/Run, loop, sRGB при импорте ходунов
   │  │  ├─ LevelAssetPostprocessor.cs   # нормали, sRGB и палитра Level_* для уровня
   │  │  ├─ BuildWebGL.cs                # сборка WebGL только из CLI (без меню — ADR 0005)
   │  │  ├─ DialogueAssetEditor.cs       # кастомный Inspector диалога: узлы, переходы, валидация
   │  │  ├─ MechPrefabUpdater.cs         # идемпотентная раскладка чисел ходунов из mech_manifest.json
   │  │  └─ HoverbikeModelBuilder.cs     # сборка префаба Hoverbike_Model из FBX (ADR 0012)
   │  ├─ Tests/
   │  │  └─ EditMode/            # сборка FrostSpace.Tests.EditMode: CameraModeTests, ScanMathTests,
   │  │                          # RobotVisualPrefabTests, AnimalVisualPrefabTests,
   │  │                          # MechVisualPrefabTests, ScreenApertureTests, CharacterSelectionTests,
   │  │                          # DialogueSessionTests, DialogueAssetValidationTests,
   │  │                          # DialogueContentTests, DialogueFontTests, DialogueActionTests,
   │  │                          # QuestLogTests, QuestAssetTests, PlayerExperienceTests,
   │  │                          # PlayerEquipmentTests, ProgressSaveTests, CarrySlotTests,
   │  │                          # SceneFlowTests (поток сцен по Build Settings)
   │  └─ Content/
   │     ├─ Animation/Controllers/  # R*.controller; ME_*.controller (ходуны);
   │     │                          # RobotAnimals/<Species>/RA_<Species>.controller
   │     ├─ Models/Robots/          # R*.fbx (20)
   │     ├─ Models/RobotAnimals/    # <Species>/RA_<Species>.fbx + SOURCE.md (происхождение ригов)
   │     ├─ Models/Mechs/           # ME_*Walker.fbx (4) — наш корпус на ригах Ultimate Space Kit
   │     ├─ Models/Level/           # FBX уровня: рельеф, дом, интерьер, деревья, пропы
   │     ├─ Models/Train/           # TrainCar (TrainBody: интерьер и кабина 4,2 м;
   │     │                          # TrainDoorL/R — створки торцевой двери;
   │     │                          # TrainSideDoorL/R — створки бокового проёма),
   │     │                          # Prop_Battery, Prop_GravCart — вагон и груз среза «Поезд»
   │     ├─ Models/Menu/            # ShipWreck_Mesh.asset — процедурный меш диорамы меню
   │     ├─ Prefabs/Props/          # HoloPanel — голопанель на столе у кабины
   │     ├─ Prefabs/Robots/         # R*_Model.prefab — только визуал (20)
   │     ├─ Prefabs/RobotAnimals/   # <Species>/RA_<Species>_Model.prefab — только визуал (13)
   │     ├─ Prefabs/Ride/           # Hoverbike_Model (корпус, Visual, Cushion),
   │     │                          # Motorcycle_Model (FBX + контроллер, эффекты, свет),
   │     │                          # ME_*_Model (ходун целиком: модель, Mech, кабина)
   │     ├─ Materials/Level/        # материалы уровня Level_*
   │     ├─ Materials/Menu/         # материалы диорамы меню
   │     ├─ Materials/VFX/          # материалы эффектов
   │     ├─ VFX/Shaders/            # OcclusionReveal, OcclusionSilhouette, OcclusionDebris
   │     ├─ Audio/Resources/Music/  # Music_FrostSpaceTheme.mp3 (фоновая музыка через Resources)
   │     ├─ Data/                   # MenuOpenVolume.asset — Volume открытого вида меню;
   │     │                          # Dialogues/*.asset — графы диалогов роботов (ADR 0025);
   │     │                          # Quests/TrainBatteries.asset — квест «Погрузка» (ADR 0027);
   │     │                          # Characters/ — CharacterCatalog.asset и три RobotProfile
   │     ├─ Fonts/                  # LiberationSans Cyrillic SDF.asset — TMP-шрифт диалогов
   │     └─ Settings/               # зарезервировано под игровые настройки
   ├─ Settings/                  # URP-ассеты шаблона, не игровые данные
   ├─ Plugins/Roslyn/            # DLL для execute_code Unity MCP
   ├─ WebGLTemplates/FrostSpace/ # свой шаблон WebGL (PROJECT:FrostSpace)
   └─ InputSystem_Actions.inputactions
```

Папки `_Project/**` появляются вместе с первым содержимым: пустые каталоги git и Unity
не сохраняют. Каталога `Assets/ThirdParty/` пока нет — он появится вместе с первым
импортированным пакетом (`SOURCE.md` обязателен).

## Планируемые пути (ещё не созданы)

По ADR 0026–0029 появятся следующие пути. Это **план**, а не факт: пока файла нет,
ссылаться на него как на существующий нельзя.

| Путь | Что появится |
|---|---|
| `Assets/_Project/Scripts/Features/Map/` | миникарта и маркеры (Stage 3) |

## С чего начинается выполнение

Целевой поток — ADR 0026: `Boot → MainMenu → CharacterSetup → Train → Frontier`.
Ниже — **фактическое** состояние на 2026-10-29; `Frontier` — депо с прибытием поезда
и сюжетом `DP-01`…`DP-04`.

1. Unity открывает `Assets/Scenes/Boot.unity` (build index 0): корневой `SceneController`
   уходит в `DontDestroyOnLoad`, `BootScreen` три секунды показывает консоль загрузки
   сервисов и отдаёт переход `SceneController` → `MainMenu`.
2. `Assets/Scenes/MainMenu.unity` (build index 1) — `MenuFlow` с `MainMenuScreen`,
   пять видов меню (`MenuOpen`, `MenuRepair`, `MenuStorage`, `MenuTrainDepot`,
   `MenuRide`), пять камер видов, снег, Volume.
   НАЧАТЬ → белый переход → `CharacterSetup`.
3. `Assets/Scenes/CharacterSetup.unity` (build index 2) — камера, два источника света
   и `CharacterSetup` с `CharacterSetupScreen`: слева список трёх корпусов с мини-превью,
   справа крупный 3D-герой на голографическом кольце, статы и описание
   (`CharacterCardPreview` рендерит корпус в `RenderTexture`), `ПОДТВЕРДИТЬ` пишет
   выбор в `CharacterSelection` и грузит `Train`.
4. `Assets/Scenes/Train.unity` (build index 3) — интерьер вагона 8×24 м
   (`TrainCar`: пол, стены, потолок, торцевая дверь, боковой проём со створками и нос-кабина),
   `Sun` и два `CabinLamp`,
   `Player` с `PlayerVisualBinder`, `CarryController`, `DialogueController`/`DialogueScreen`,
   `QuestDirector` и `QuestTracker`, четыре аккумулятора и тележка с четырьмя слотами,
   `R13_Prospector` с `DialogueSpeaker`/`DialogueMarker`, `TrainMotionRig` с параллаксом
   и снегом, `Main Camera` с `PlayerCameraRig`; `TrainSequence` + `TitleCard` дают
   отъезд камеры и белый титул перед `Frontier`.
5. `Assets/Scenes/Frontier.unity` (build index 4) — депо: снежная земля, платформа,
   навес, поезд `TrainCar` (8×24) с путём, ворота, мачта, `Player`, `R16_Warden` ×2,
   `R8_Bulwark` и `R7_Drifter`. `DepotArrival` проигрывает прибытие поезда, плавный
   раздвиг створок и выход R13 с тележкой через боковой проём; управление возвращается
   игроку в вагоне, на платформу он выходит сам. R7 выдаёт карту и сканер, ворота ведут
   к будущей дороге.
6. Сцена `Game` (build index 5, вне потока) — обычный сохранённый ассет: рельеф,
   дом с интерьером, деревья, забор, пропы (FBX из `Content/Models/Level/`), свет, туман,
   камера, `Player`, `NPC`. Меню-пересборки нет (ADR 0005): контент правится вручную в Unity.
7. `Player` — сценовый актёр: тег `Player`, `CharacterController`, `RobotLocomotion`;
   внутри — визуальный префаб `R1_PolarMechanic_Model`.
7. `NPC` — корень с актёрами `NPC_R2_Furnace`, `NPC_R3_Scout`, `NPC_R4_Hauler`,
   `NPC_R5_Ranger`: кинематический `CharacterController`, `NpcController` (маршрут из
   вейпоинтов), `DialogueSpeaker` (диалог по E, ADR 0025) и визуальный ребёнок; без
   ввода и без обычной физики.
8. `NPC/Animals` — 13 актёров `AN_<Species>`: наземные с кинематическим
   `CharacterController` и `NpcController`, `AN_Eagle` и `AN_Piranha` — с
   `AirPatrolController` и маршрутом с высотой; визуальный ребёнок —
   `RA_<Species>_Model` (ADR 0009).

Когда появится composition root, он должен жить в `Assets/_Project/Scripts/Core/`, а
точка входа — быть явной (сцена или bootstrap-объект), а не размазанной по `Awake`.

## Где что искать

| Что | Путь / инструмент |
|---|---|
| Мир: география, Тишина, холод, быт и речь | `docs/WORLD.md` |
| Сюжет, поток сцен, системы, критерии готовности | `docs/GAME_DESIGN.md` |
| Версия Unity | `ProjectSettings/ProjectVersion.txt` |
| Пакеты | `Packages/manifest.json`, `Packages/packages-lock.json` |
| Render pipeline | `Assets/Settings/FrostSpace_RPAsset.asset`, `FrostSpace_Renderer.asset`, `QualitySettings.asset` (единый уровень `Default`, ADR 0016) |
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
| Сборка уровня (Blender) | `Tools/Blender/build_level.py` + `level_layout.py` (позиции), `level_lib.py` (материалы и палитра), `level_parts.py` (геометрия); превью — `AssetSource/Previews/Level/` |
| Параметры игрока | компонент `RobotLocomotion` на `Player` (сцена `Game`): скорости, прыжок (буфер, coyote time, присед) |
| Маршрут NPC и животных | вейпоинты в `NpcController` на `NPC_R*` и `AN_<Species>`; у `AN_Eagle`/`AN_Piranha` — `AirPatrolController` с высотой; точки — дочерние `WP_*` |
| Режимы камеры | `PlayerCameraRig` + `CameraMode` на `Main Camera`; интерьер — `InteriorZone` на корне здания (гистерезис выхода `_interiorExitMargin`); силуэт — `PlayerSilhouette`, голова — `PlayerLocalView` (ADR 0010, 0012) |
| Сканирование | `ObjectScanner` на `Main Camera` (Q-сонар, ближайшая и прицельная детекция) + `ScanTarget` на целях (реестр `ScanTarget.All`), кольцо — `ScanPulse`, математика — `ScanMath`, показ — `PlayerHud` + `ScanLabelView` (ADR 0020) |
| Меню паузы и настройки | `PauseMenuScreen` на `Player`/`GameFlow` (Esc): обзор, журнал квеста, настройки-шестерёнка, продолжение и выход в меню; значения — `GameSettings` (`PlayerPrefs`: музыка, детекция ближайшего, детекция по прицелу) (ADR 0020, 0027) |
| Опыт и уровни | `PlayerExperience` на `Player`: уровень, опыт, кривая до следующего; `ExperienceBar` внизу экрана показывает `УР. N` / `опыт / требование (проценты)` / `УР. N+1`; квест даёт 250 XP |
| Диалоги | данные — `Content/Data/Dialogues/*.asset` (`DialogueAsset`), авторство — `Editor/DialogueAssetEditor.cs`; на сцене — `DialogueSpeaker` на `NPC_R*` в `Game` и на `R13_Prospector` в `Train`, `DialogueController` + `DialogueScreen` на `GameFlow`/`Player` (E — действие `Player/Interact`); `DialogueMarker` показывает «E»; шрифт кириллицы — `Content/Fonts/LiberationSans Cyrillic SDF.asset` (ADR 0025, 0027) |
| Ховербайк | `Hoverbike` + `HoverbikeGlow` + `RiderCockpit` в `Features/Ride/`; префаб — `Content/Prefabs/Ride/Hoverbike_Model` (корень с `CharacterController` и `RiderCockpit`, дочерние `Visual` с якорями `Seat`/`Seat Point`/`Grip L/R`/`Foot L/R`, `Cushion`, `ExitPoint`, `EngineGlow`); сборка — `Editor/HoverbikeModelBuilder.cs` (ADR 0012) |
| Мотоцикл | `Motorcycle` + `MotorcycleRideVisual` в `Features/Ride/`, поза наездника — `RiderPose`; префаб — `Content/Prefabs/Ride/Motorcycle_Model`; FBX — `Tools/Blender/build_motorcycle.py` (ADR 0013) |
| Ездовые ходуны | `Mech` + `MechSeat` + `RideCameraProfile` в `Features/Ride/`; префабы — `Content/Prefabs/Ride/ME_*_Model` (корень — сразу транспорт: `Animator`, `CharacterController`, `Mech`, `ScanTarget` категории `WALKER`, `RiderCockpit`; дочерние `Seat` с якорями `Seat Point`/`Grip L/R`/`Foot L/R` и `ExitPoint`); клипы — `Content/Animation/Controllers/ME_*.controller`; сборка — `Tools/Blender/build_mechs.py`, регистр — `Tools/Blender/mech_manifest.json` (ADR 0024) |
| Виды главного меню | `MenuViewSwitch` на `MenuFlow` (массив видов: корень + камера + подпись), правая вертикальная карусель `01 STORAGE … 05 RIDE` — `MenuViewCarousel`, собирается `MainMenuScreen` (ADR 0018); открытый вид — `MenuCameraSweep` + `Content/Data/MenuOpenVolume.asset` (ADR 0013); склад, депо и проезд — `Tools/Blender/build_menu_storage.py` / `build_menu_train.py` / `build_menu_ride.py` через `build_all.py` (ADR 0017, 0018); цикл поезда — `MenuTrainCycle`, прокрутка местности — `MenuRideScroll`, ходьба роботов — `MenuRobotWalker` (носильщик R18 играет `CarryWalk`) |
| Скрытие перекрытий | `CameraOcclusionFader` (только в режиме прицела) + `OcclusionRevealDebris`, шейдеры `Content/VFX/Shaders/`, группы — `CameraOcclusionFadeGroup` (ADR 0008, 0010) |
| Теги и слои | `ProjectSettings/TagManager.asset` (используется стандартный тег `Player`) |
| Roslyn для MCP | `Assets/Plugins/Roslyn/` |
| Скиллы | `.Aura/Skills/` — единственный источник; opencode — `opencode.json` (`skills.paths`), остальные рантаймы — мост `.agents/skills/` |
| Вики Qoder | правила генерации — `.qoder/repowiki/wiki_plan.yaml`; сгенерированные страницы локальны, не документация и не источник истины |
| Планы работ | `Assets/.Aura/plans/` — единственное место; Aura пишет туда нативно (правила — `Assets/.Aura/plans/README.md`) |
| Blender-тулинг | `Tools/Blender/`, документация — `BLENDER.md` |
| Пайплайн ассетов | `Tools/Blender/build_all.py` — единый вход: сборка → проверки → превью; реестр `PIPELINE` внутри файла, логи — `Tools/Blender/logs/` (ADR 0017) |
| Unity-мост | `Tools/Unity/unity_bridge.py`, документация — `UNITY_MCP.md` |
| Исходные архивы | `AssetSource/` (в git не входит) |
| Импортированные паки | `Assets/ThirdParty/<Pack>/` + `SOURCE.md` (каталога пока нет) |

## Поисковые подсказки

- Код: `rg "class |struct |interface " Assets/_Project`.
- Сцены и префабы: `rg "guid:" Assets/Scenes Assets/_Project/Content`.
- Роботы: `rg "R[1-5]_" Assets/_Project/Content Tools/Blender`.
- Животные: `rg "RobotAnimals" Assets/_Project Tools/Blender`.
- Пакеты: `rg "com\." Packages/manifest.json`.
- Blender-скрипты: `rg "bpy\.|export_scene" Tools/Blender`.
- Уровень: `rg "SnowCabin|level_parts|Level_" Tools/Blender Assets/_Project/Content`.
- Unity-мост: `rg "FRAMING|read_console|refresh_unity" Tools/Unity docs`.

Не открывай `Library/`, `Temp/`, `Logs/`, `obj/` и сгенерированные IDE-файлы: это
производные данные, они не описывают проект. Исключение — `Logs/Editor.log` для
диагностики (`UNITY_EDITOR.md` §8).

## Своё и стороннее

| Источник | Что это |
|---|---|
| `Assets/_Project/` | наш код и контент |
| `Assets/Settings/` | URP-ассеты шаблона Unity |
| `Assets/Plugins/Roslyn/` | сторонние DLL для Unity MCP |
| `Assets/WebGLTemplates/FrostSpace/` | наш шаблон WebGL |
| `Packages/` | пакеты Unity и git-пакет Unity MCP |
| `AssetSource/` | сырые архивы, не часть Unity-проекта |
| `Tools/` | наша внешняя автоматизация, не runtime |

## Границы, которые нельзя размывать

- **Один корень C#** — `Assets/_Project/Scripts/`.
- **Один корень сцен** — `Assets/Scenes/`.
- **Один источник скиллов** — `.Aura/Skills/`; `.agents/skills/` — только ссылка на него,
  а opencode смотрит канон напрямую через `opencode.json`.
- **`Assets/Settings` — только URP.** Игровые данные — в `_Project/Content/Data`.
- **`Tools/` — вне `Assets/`.** Иначе Unity начнёт импортировать `.py` и логи.
- **`AssetSource/` — вне git.** В репозиторий попадает только импортированное.
