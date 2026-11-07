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
│  ├─ Audio/                     # build_sfx.py — процедурные звуки прибытия
│  ├─ Blender/                   # сборка роботов, животных и уровня, пробы, мост, ядро
│  ├─ Unity/                     # прямой клиент к мосту Unity MCP и сниппеты правки сцены
│  └─ analysis/                  # разбор кадров и палитр: compare_robot_color, pair_studio_game,
│                                # palette_variants_montage, probe_albedo_transfer
├─ AssetSource/                  # сырые архивы ассетов, ВНЕ git и вне Unity
└─ Assets/
   ├─ .Aura/plans/               # планы работ всех агентов (README — правила); Unity dot-папки не импортирует
   ├─ Scenes/                    # ЕДИНСТВЕННАЯ папка сцен
   │  ├─ Boot.unity              # стартовая сцена, build index 0: загрузка сервисов → MainMenu
   │  ├─ MainMenu.unity          # главное меню с диорамой, build index 1
   │  ├─ CharacterSetup.unity    # выбор из трёх корпусов, build index 2
   │  ├─ Train.unity             # интерьер, переноска, квест, диалоги R13, финал; build index 3
   │  ├─ Frontier.unity          # карта 4,4×4,4 км: депо с прибытием поезда, поселение,
   │  │                          # станция; build index 4
   │  └─ Game.unity              # прежний уровень, build index 5, вне потока
   ├─ _Project/                  # всё, что пишем мы
   │  ├─ Scripts/                # ЕДИНСТВЕННЫЙ корень C#
   │  │  ├─ Features/            # сборка FrostSpace.Features
   │  │  │  ├─ Boot/             # BootScreen — консоль загрузки сервисов на старте
   │  │  │  ├─ Camera/           # PlayerCameraRig, CameraMode, CameraPose, InteriorZone,
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
   │  │  │  ├─ Interior/         # CabinDoor (створки кабины разъезжаются по подходу),
   │  │  │  │                    # HoloPanel (фазы скан-полосы через MaterialPropertyBlock)
   │  │  │  ├─ Inventory/        # EquipmentBoard (чистая модель: слоты и ячейки сетки),
   │  │  │  │                    # EquipmentKind (сонар и карта), EquipmentTarget (тип мишени),
   │  │  │  │                    # EquipmentInfo (подписи снаряжения), EquipmentScreen (окно по `I`)
   │  │  │  ├─ Locomotion/       # RobotLocomotion (базис WASD), PlayerAimPose, LocomotionDiagnostics
   │  │  │  ├─ Map/              # WorldLayout + WorldLayoutData (разбор world_layout.json),
   │  │  │  │                    # WorldGrid (границы клеток по закону середины ADR 0036),
   │  │  │  │                    # WorldCell + CellMesh (метка геометрии из FBX),
   │  │  │  │                    # WorldResidency (включает окрестность игрока),
   │  │  │  │                    # FarLayer (силуэты якорей на 380 м), MiniMap +
   │  │  │  │                    # MiniMapProjection (миникарта, RD-05)
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
   │  │  │  └─ Story/            # TrainMotionRig (параллакс и снег), TrainSequenceDirector, TrainCrashRig (авария финала)
   │  │  │                       # (финал рейса без отъезда к окну), TitleCardScreen (текст,
   │  │  │                       # снег и гул поверх чужого чёрного), TitleSnow (снег титра),
   │  │  │                       # CutsceneMover (проводка актёра по кривой), DepotArrivalDirector
   │  │  │                       # (прибытие, створки, очередь выхода), CameraKey/CameraTrack +
   │  │  │                       # PathCurve (проезд камеры по длине дуги в Features/Camera),
   │  │  │                       # PlayerEquipment (карта и сканер из диалога)
   │  │  └─ Core/                # сборка FrostSpace.Core: SceneController (персистентный владелец
   │  │                          # перехода и единственный вызывающий SceneManager),
   │  │                          # MusicManager (фоновая музыка), GameSettings (музыка и каналы детекции)
   │  ├─ Editor/                 # сборка FrostSpace.Editor, только редактор
   │  │  ├─ RobotAssetPostprocessor.cs   # клипы, loop, sRGB при импорте FBX роботов
   │  │  ├─ AnimalAssetPostprocessor.cs  # клипы Idle/Move, loop, sRGB при импорте FBX животных
   │  │  ├─ MechAssetPostprocessor.cs    # клипы Idle/Walk/Run, loop, sRGB при импорте ходунов
   │  │  ├─ LevelAssetPostprocessor.cs   # нормали, sRGB и палитра Level_* для уровня
   │  │  ├─ AudioArrivalImportPostprocessor.cs # моно 44,1 кГц и петли для Content/Audio/Arrival
   │  │  ├─ BuildWebGL.cs                # сборка WebGL только из CLI (без меню — ADR 0005)
   │  │  ├─ DialogueAssetEditor.cs       # кастомный Inspector диалога: узлы, переходы, валидация
   │  │  ├─ MechPrefabUpdater.cs         # идемпотентная раскладка чисел ходунов из mech_manifest.json
   │  │  └─ HoverbikeModelBuilder.cs     # сборка префаба Hoverbike_Model из FBX (ADR 0012)
   │  ├─ Tests/
   │  │  └─ EditMode/            # сборка FrostSpace.Tests.EditMode, 32 файла и 267 тестов:
   │  │                          # CameraModeTests, CameraTrackTests, ScreenApertureTests,
   │  │                          # ScanMathTests, RobotVisualPrefabTests, AnimalVisualPrefabTests,
   │  │                          # MechVisualPrefabTests, CharacterSelectionTests,
   │  │                          # DialogueSessionTests, DialogueAssetValidationTests,
   │  │                          # DialogueContentTests, DialogueFontTests, DialogueActionTests,
   │  │                          # QuestLogTests, QuestAssetTests, PlayerExperienceTests,
   │  │                          # PlayerEquipmentTests, ProgressSaveTests, CarrySlotTests,
   │  │                          # EquipmentBoardTests, EquipmentSceneTests, CabinDoorTests,
   │  │                          # HoloPanelTests, SceneFlowTests (поток сцен по Build Settings),
   │  │                          # WorldGridTests, WorldLayoutTests (раскладка и 18 инвариантов),
   │  │                          # MiniMapProjectionTests (RD-05), FrontierFarLayerTests,
   │  │                          # FrontierSceneSyncTests (небо и камера сцены),
   │  │                          # SettlementContentTests, StationContentTests (узлы карты),
   │  │                          # TrainCrashTests (финал рейса)
   │  └─ Content/
   │     ├─ Animation/Controllers/  # R*.controller; ME_*.controller (ходуны);
   │     │                          # RobotAnimals/<Species>/RA_<Species>.controller
   │     ├─ Models/Robots/          # R*.fbx (20)
   │     ├─ Models/RobotAnimals/    # <Species>/RA_<Species>.fbx + SOURCE.md (происхождение ригов)
   │     ├─ Models/Mechs/           # ME_*Walker.fbx (4) — наш корпус на ригах Ultimate Space Kit
   │     ├─ Models/Level/           # FBX уровня: рельеф, дом, интерьер, деревья, пропы
   │     │  └─ Cells/               # 351 часть клетки: `Ground_`, `Fill_`, `Rail_`, `Ties_`,
   │     │                          # `Signs_`, `Road_<n>_`, `Posts_<n>_`, `Siding_`, `Depot*_`,
   │     │                          # `Hut_*_` (поселение), `Station_*_` — один объект = один FBX
   │     │                          # (ADR 0039)
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
   │     ├─ Prefabs/World/          # WorldCell_<i>_<j>.prefab (121) — клетка как префаб из
   │     │                          # частей, актёры и свет узлов внутри неё (ADR 0039, 0043)
   │     ├─ Materials/Level/        # материалы уровня Level_*
   │     ├─ Materials/Menu/         # материалы диорамы меню
   │     ├─ Materials/VFX/          # материалы эффектов
   │     ├─ VFX/Shaders/            # OcclusionReveal, OcclusionDebris, SkyNightStars,
   │     │                          # ScanHighlight, ScanPulse, HoverCushion, HoloPanel,
   │     │                          # MenuAurora, MenuLightCone, Smoke (дым/пыль/искры)
   │     ├─ Audio/Arrival/          # звуки прибытия и аварии (настраивает
   │     │                          # AudioArrivalImportPostprocessor.cs; источник —
   │     │                          # Tools/Audio/build_sfx.py)
   │     ├─ Audio/Resources/Music/  # Music_FrostSpaceTheme.mp3 (фоновая музыка через Resources)
   │     ├─ Data/                   # MenuOpenVolume.asset — Volume открытого вида меню;
   │     │                          # Dialogues/*.asset — графы диалогов роботов (ADR 0025);
   │     │                          # Quests/TrainBatteries.asset — квест «Погрузка» (ADR 0027);
   │     │                          # Characters/ — CharacterCatalog.asset и три RobotProfile;
   │     │                          # World/world_layout.json — TextAsset раскладки Stage 3
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

## С чего начинается выполнение

Целевой поток — ADR 0026: `Boot → MainMenu → CharacterSetup → Train → Frontier`.
Ниже — **фактическое** состояние на 2026-11-05; `Frontier` — депо с прибытием поезда
и сюжетом `DP-01`…`DP-04`, плюс наполненные поселение и станция.

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
   и снегом, `Main Camera` с `PlayerCameraRig`; `TrainSequence` + `TitleCard` дают финал
   рейса (камера стоит и наезжает на полметра, R13 уходит к проёму, провал света кабины,
   дальние тормоза заранее) и титул со снегом перед `Frontier`.
5. `Assets/Scenes/Frontier.unity` (build index 4) — депо: снежная земля, двор
   130×230 м в капитальной стене с зубцами, аркой на дорогу и двумя путевыми
   разрывами в мачтовых столбах, грузовой перрон (настил +1,04 м над головкой) с
   тремя доками (один
   завален), коробка с сорванной кровлей, ангар с обвалённым углом, козловой
   портал над разобранным узлом, поезд `TrainCar` (8×24) с путём, мачта,
   `Player`, `R16_Warden` ×2, `R8_Bulwark`, `R9_Tinker`, `R20_Cogsmith` и
   `R7_Drifter`. Всё содержимое депо лежит в чанке `WorldCell_-2_-2` (ADR 0043,
   0040). `DepotArrival` ведёт прибытие одним непрерывным
   проездом камеры (~16.6 с) по длине дуги: встречаем состав, набирая ход, и в конце
   закручиваемся вокруг уже вышедшего игрока, встаём за спину. Очередь через один
   проём: R13 вперёд-налево и разворот вдоль состава, тележка по прямой дальше,
   игрок последним пару шагов прямо; управление возвращается ровно в позе рига. R7 выдаёт карту и
   сканер, арка ведёт к дороге. Карта целиком — 121 префаб клетки под корнем
   `WorldCells` (часть активна по резидентности `WorldResidency`); кроме депо узлы
   наполнены в поселении (0,−2) и на станции (0,0), остальные клетки — открытая
   равнина с лесом и валами (ADR 0036, 0039, 0041, 0043).
6. Сцена `Game` (build index 5, вне потока) — обычный сохранённый ассет: рельеф,
   дом с интерьером, деревья, забор, пропы (FBX из `Content/Models/Level/`), свет, туман,
   камера, `Player`, `NPC`. Меню-пересборки нет (ADR 0005): контент правится вручную в Unity.
7. `Player` — сценовый актёр: тег `Player`, `CharacterController`, `RobotLocomotion`;
   внутри — визуальный префаб `R1_PolarMechanic_Model`.
8. `NPC` — корень с актёрами `NPC_R2_Furnace`, `NPC_R3_Scout`, `NPC_R4_Hauler`,
   `NPC_R5_Ranger`: кинематический `CharacterController`, `NpcController` (маршрут из
   вейпоинтов), `DialogueSpeaker` (диалог по E, ADR 0025) и визуальный ребёнок; без
   ввода и без обычной физики.
9. `NPC/Animals` — 13 актёров `AN_<Species>`: наземные с кинематическим
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
| Край мира у депо (Blender) | `Tools/Blender/build_frontier.py` (секции `ground/track/grove/prop`) + `frontier_layout.py` (координаты, стек пути, `SCENE_INSTANCES`), `frontier_parts.py` (геометрия); проверка — `check_frontier.py`, превью — `render_frontier_previews.py` → `AssetSource/Previews/Frontier/`; FBX — `Content/Models/Level/Frontier_*.fbx` (ADR 0035) |
| Клетки мира | `Tools/Blender/world_cell_parts.py` — решётка 15 м ровно по границам клетки, высоты из `world_height`, без исключений; `transport_parts.py` — путь, шпалы, знаки и дороги кусками клетки; `depot_parts.py` — депо (плиты двора, платформа, рампа, коробка, навес, кран, ангар, двор, лес и боковой путь) содержимым клетки (−2,−2); `build_world_cells.py` — сборка частей `Cells/<Часть>_<i>_<j>.fbx` (один объект — один файл) со сверкой каждой вершины с мировым полем, общим ребром соседей (`_edge_check`) и габаритом двора в клетке (`_depot_check`); `render_cells_previews.py` — стык, выемка, двор глазами, выезд через ворота, ортоплан, косой вид на клетку депо, план карты → `AssetSource/Previews/Cells/`; в Unity — `Content/Models/Level/Cells/*.fbx` → `Content/Prefabs/World/WorldCell_*.prefab`, собранный из частей (`WorldCell` на корне, `MeshCollider` на каждую часть), корень `WorldCells` во `Frontier` и `Features/Map/WorldResidency.cs`, который включает только окрестность игрока (ADR 0036 п.2); префаб клетки — владелец содержимого (ADR 0043): актёры депо лежат в `WorldCell_-2_-2.prefab`, поселение — в `WorldCell_0_-2.prefab` (группа `Settlement`), станция — в `WorldCell_0_0.prefab` (группа `Station`); `Tools/Blender/settlement_parts.py` — поселение (ограда с двумя воротами, загоны, электропост, метеоплощадка, изба, мастерская), `Tools/Blender/station_parts.py` — станция (платформа с навесами и буфером вдоль выпрямленной оси узла, пять отсеков, гигантский ангар, обломок корабля, ограда жилого комплекса); содержимое узлов ставят скрипты `Tools/Unity/place_settlement_content.cs` и `Tools/Unity/place_station_content.cs`, тексты диалогов — `Tools/Unity/make_settlement_dialogues.cs` и `Tools/Unity/make_station_dialogues.cs`, проверки узлов — `Tests/EditMode/SettlementContentTests.cs` и `StationContentTests.cs`; `Features/Map/CellMesh.cs` помечает геометрию из FBX, `Tools/Unity/sync_cell_prefabs.cs` обновляет только её и не удаляет авторское |
| Дальний слой | `Tools/Blender/far_parts.py` — профиль силуэта по долям высоты (`STEPS`, `RINGS`, `tier_width`), нормировка габарита `_fit`; `build_far.py` — сборка `Far_*.fbx` со сверкой ширины и высоты с `far_layer()` в допуске 1 см; `render_far_previews.py` — кадры со двора и форма каждой карточки → `AssetSource/Previews/Far/`; в Unity — `Content/Models/Level/Far_*.fbx` и корень `FarLayer` во `Frontier`, который переставляет их за игроком (`Features/Map/FarLayer.cs`), страховка — `Tests/EditMode/FrontierFarLayerTests.cs` (ADR 0036 п.6) |
| Раскладка мира Stage 3 | `Tools/Blender/world_layout.py` — единственные числа (клетки 400 м, 121 клетка −5…+5, якоря, руины, узлы, линии, тёплый каркас, туман, `ANOMALY`, `far_layer`, клетки, `line_spans` и транспортный стол`) и 18 инвариантов; сборка JSON — `build_world.py`, проверка — `check_world.py`, план и высоты — `render_world_previews.py` → `AssetSource/Previews/World/`; выход в Unity — `Content/Data/World/world_layout.json` как `TextAsset` (ADR 0036) |
| Наследие и руины (Blender) | `Tools/Blender/heritage_parts.py` (`slide/pylon/crane/span/hangar/tanks/bridge/yard`, общие `_railing`/`_ladder`/`_lamp`/`_drifts`) + `build_heritage.py` (вершина меша сверяется с `world_layout`), проверка — `check_frontier.py` по файлу, превью — `render_heritage_previews.py` → `AssetSource/Previews/Heritage/`; FBX — `Content/Models/Level/Heritage_*.fbx` |
| Миникарта | `Assets/_Project/Scripts/Features/Map/{MiniMap,MiniMapProjection}.cs`: кодостроенный canvas `sortingOrder = 25` (между трекером 20 и диалогом 40) в правом верхнем углу — левый занят трекером; фон — тёмный диск-«радар» из процедурной `Texture2D` (спрайтов в проекте нет, на снегу прибор без него не читается), обод из насечек, линии и узлы из `world_layout.json` с отсечением по кругу, маркер цели прижат к ободу вне дальности; гейт — `PlayerEquipment.IsEquipped(EquipmentKind.Map)` (`DP-03`, `DP-06`: выданная, но снятая карта миникарты не даёт), бит `RD-05`. Висит на `Player` в сцене `Frontier`; сцена набрана в осях депо, перевод делает `WorldLayout.ToWorld` |
| Окно экипировки | `Assets/_Project/Scripts/Features/Inventory/{EquipmentKind,EquipmentBoard,EquipmentInfo,EquipmentTarget,EquipmentScreen}.cs`: доска `EquipmentBoard` держит «где предмет» (свой слот или ячейка) и единственная решает, проходить ходу или нет — обмен возможен только ячейка↔ячейка, потому что вытесненный в чужой слот предмет некуда деть; окно — кодостроенный canvas `sortingOrder = 45` (между диалогом 40 и паузой 50), слева превью корпуса через `CharacterCardPreview`, внизу два слота 72 px, справа сверху описание, снизу сетка 8 колонок по 56 px с зазором 6; перетаскивание — `IBeginDrag/IDrag/IEndDrag/IDrop` на `EquipmentTarget`; блокировка та же тройка, что у диалога (`SetControlLock`, `SetPaused`, `Player` map off), и снимается в обратном порядке; `I` и `Esc` читаются `Keyboard.current`, как `Esc` паузы. Висит на `Player` в `Frontier` — в `Train` и `Game` `PlayerEquipment` нет, вешать окно не на что. Бит `DP-06` |
| Небо колонии | шейдер `Content/VFX/Shaders/SkyNightStars.shader` (полоса мглы, редкие яркие звёзды, одна аномалия) + материал `Content/Materials/Level/Level_SkyStars.mat`, назначенный скайбоксом сцены `Frontier`; направление аномалии задаёт `world_layout.ANOMALY`, страховка — `FrontierSceneSyncTests.SkyIsNightAndCameraDrawsIt` (`STYLE.md` §«Небо», ADR 0036) |
| Параметры игрока | компонент `RobotLocomotion` на `Player` (сцена `Game`): скорости, прыжок (буфер, coyote time, присед) |
| Маршрут NPC и животных | вейпоинты в `NpcController` на `NPC_R*` и `AN_<Species>`; у `AN_Eagle`/`AN_Piranha` — `AirPatrolController` с высотой; точки — дочерние `WP_*` |
| Режимы камеры | `PlayerCameraRig` + `CameraMode` на `Main Camera`; интерьер — `InteriorZone` на корне здания (гистерезис выхода `_interiorExitMargin`); голова в 1-м лице — `PlayerLocalView` (ADR 0010, 0012; x-ray силуэт убран — ADR 0037) |
| Сканирование | `ObjectScanner` на `Main Camera` (Q-сонар, ближайшая и прицельная детекция) + `ScanTarget` на целях (реестр `ScanTarget.All`), кольцо — `ScanPulse`, математика — `ScanMath`, показ — `PlayerHud` + `ScanLabelView` (ADR 0020) |
| Меню паузы и настройки | `PauseMenuScreen` на `Player`/`GameFlow` (Esc): обзор, журнал квеста, настройки-шестерёнка, продолжение и выход в меню; значения — `GameSettings` (`PlayerPrefs`: музыка, детекция ближайшего, детекция по прицелу) (ADR 0020, 0027) |
| Опыт и уровни | `PlayerExperience` на `Player`: уровень, опыт, кривая до следующего; `ExperienceBar` внизу экрана показывает `УР. N` / `опыт / требование (проценты)` / `УР. N+1`; квест даёт 250 XP |
| Диалоги | данные — `Content/Data/Dialogues/*.asset` (`DialogueAsset`), авторство — `Editor/DialogueAssetEditor.cs`; на сцене — `DialogueSpeaker` на `NPC_R*` в `Game` и на `R13_Prospector` в `Train`, `DialogueController` + `DialogueScreen` на `GameFlow`/`Player` (E — действие `Player/Interact`); `DialogueMarker` показывает «E»; шрифт кириллицы — `Content/Fonts/LiberationSans Cyrillic SDF.asset` (ADR 0025, 0027) |
| Ховербайк | `Hoverbike` + `HoverbikeGlow` + `RiderCockpit` в `Features/Ride/`; префаб — `Content/Prefabs/Ride/Hoverbike_Model` (корень с `CharacterController` и `RiderCockpit`, дочерние `Visual` с якорями `Seat`/`Seat Point`/`Grip L/R`/`Foot L/R`, `Cushion`, `ExitPoint`, `EngineGlow`); сборка — `Editor/HoverbikeModelBuilder.cs` (ADR 0012) |
| Мотоцикл | `Motorcycle` + `MotorcycleRideVisual` в `Features/Ride/`, поза наездника — `RiderPose`; префаб — `Content/Prefabs/Ride/Motorcycle_Model`; FBX — `Tools/Blender/build_motorcycle.py` (ADR 0013) |
| Ездовые ходуны | `Mech` + `MechSeat` + `RideCameraProfile` в `Features/Ride/`; префабы — `Content/Prefabs/Ride/ME_*_Model` (корень — сразу транспорт: `Animator`, `CharacterController`, `Mech`, `ScanTarget` категории `WALKER`, `RiderCockpit`; дочерние `Seat` с якорями `Seat Point`/`Grip L/R`/`Foot L/R` и `ExitPoint`); клипы — `Content/Animation/Controllers/ME_*.controller`; сборка — `Tools/Blender/build_mechs.py`, регистр — `Tools/Blender/mech_manifest.json` (ADR 0024) |
| Виды главного меню | `MenuViewSwitch` на `MenuFlow` (массив видов: корень + камера + подпись), правая вертикальная карусель `01 STORAGE … 05 RIDE` — `MenuViewCarousel`, собирается `MainMenuScreen` (ADR 0018); открытый вид — `MenuCameraSweep` + `Content/Data/MenuOpenVolume.asset` (ADR 0013); склад, депо и проезд — `Tools/Blender/build_menu_storage.py` / `build_menu_train.py` / `build_menu_ride.py` через `build_all.py` (ADR 0017, 0018); цикл поезда — `MenuTrainCycle`, прокрутка местности — `MenuRideScroll`, ходьба роботов — `MenuRobotWalker` (носильщик R18 играет `CarryWalk`) |
| Живая проверка постановочных кадров | `Tools/Unity/check_arrival_shot.cs` (прибытие, 10 кадров по расписанию заставки) и `Tools/Unity/check_crash_look.cs` (авария, кадры из штатной камеры по фазам); вывод — `Logs/verify/<дата>/`, ловушки метода — `UNITY_EDITOR.md` §5 (ADR 0044, 0045) |
| Дым, пыль и искры | шейдер `Content/VFX/Shaders/Smoke.shader` (процедурная кромка, без текстур), материалы `SmokeDark` / `SmokeGrey` / `DustSnow` / `SparkWarm`; системы аварии — в `Train.unity` под `TrainCar` (`CrashSmoke*`, `CrashDust`, `CrashSparks`), дым состава — в `WorldCell_-2_-2.prefab` на `DepotTrain` (`TrainSmoke*`); постановка — `TrainCrashRig`, переключение плотности — `DepotArrivalDirector.SetSmoke` (ADR 0044, 0045, `STYLE.md` §«Дым, пыль и искры») |
| Скрытие перекрытий | `CameraOcclusionFader` (только в режиме прицела) + `OcclusionRevealDebris`, шейдеры `Content/VFX/Shaders/`, группы — `CameraOcclusionFadeGroup` (ADR 0008, 0010) |
| Теги и слои | `ProjectSettings/TagManager.asset` (используется стандартный тег `Player`) |
| Roslyn для MCP | `Assets/Plugins/Roslyn/` |
| Скиллы | `.Aura/Skills/` — единственный источник; opencode — `opencode.json` (`skills.paths`), остальные рантаймы — мост `.agents/skills/` |
| Вики Qoder | правила генерации — `.qoder/repowiki/wiki_plan.yaml`; сгенерированные страницы локальны, не документация и не источник истины |
| Планы работ | `Assets/.Aura/plans/` — единственное место; Aura пишет туда нативно (правила — `Assets/.Aura/plans/README.md`) |
| Blender-тулинг | `Tools/Blender/`, документация — `BLENDER.md` |
| Пайплайн ассетов | `Tools/Blender/build_all.py` — единый вход: сборка → проверки → превью; реестр `PIPELINE` внутри файла, логи — `Tools/Blender/logs/` (ADR 0017) |
| Unity-мост | `Tools/Unity/unity_bridge.py`, документация — `UNITY_MCP.md` |
| Звук прибытия | `Tools/Audio/build_sfx.py` — генератор WAV (ветер, рельсы, тормоза, створки, шаги, удар, тележка, дрон), `--check`; выход — `Assets/_Project/Content/Audio/Arrival/`, импорт — `Editor/AudioArrivalImportPostprocessor.cs` |
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
