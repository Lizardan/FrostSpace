# Обзор проекта FrostSpace

## Статус

Проект находится на ранней стадии. Реализован прежний вертикальный срез: снежный
уровень `Game`, двадцать роботов и 13 робо-животных из Blender, локомоция игрока,
sci-fi экран загрузки в `Boot`, главное меню с 3D-диорамой и EditMode-тесты.

Сюжет и целевой поток сцен **зафиксированы как замысел** и реализованы частично:
`GAME_DESIGN.md` описывает игру про робота-курьера аккумуляторов и путь
`MainMenu → CharacterSetup → Train → Frontier`. Выбор героя уже в коде
(`CharacterSetup`, три корпуса, ADR 0028); в `Train` есть интерьер вагона, переноска,
квест «Погрузка», диалоги R13, HUD-трекер и финал с аварией и титулом (`TR-09`, `TR-10`).
`Frontier` — депо: поезд того же масштаба приезжает снаружи, открывает боковые створки,
выходят R13 и тележка, работают охрана и R7 (`DP-01`…`DP-04`). Прогресс квеста
пишется в `FrostSpace.Save`, в меню есть `ПРОДОЛЖИТЬ` (ADR 0031); полный прогон
`Boot → Frontier` и WebGL-сборка проверены.
Реализация — источник истины о том, что уже есть; GDD — источник истины о том, что
задумано.

Жанр — сюжетное приключение с доставкой груза и транспортом; целевая платформа —
WebGL (единственная, подтверждена сборкой). Баланс и точные характеристики корпусов
не заданы.

## Что подтверждено

| Параметр | Значение | Источник |
|---|---|---|
| Unity | 6000.6.7f1 | `ProjectSettings/ProjectVersion.txt` |
| Редактор | `F:\Unity\Editor\6000.6.7f1\Editor\Unity.exe` | Unity Hub / фактический запуск |
| Render pipeline | URP 17.6.0 | `Packages/manifest.json`, `Assets/Settings/*_RPAsset` |
| Качество | один уровень `Default`, один URP-ассет | `ProjectSettings/QualitySettings.asset` (ADR 0016) |
| Цветовое пространство | Linear | `ProjectSettings/ProjectSettings.asset` |
| Ввод | только Input System | `activeInputHandler: 1` |
| Сериализация | Force Text | `ProjectSettings/EditorSettings.asset` |
| Play Mode | domain reload выключен, scene reload включён | `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 1` |
| Игровая сцена | `Assets/Scenes/Frontier.unity`, build index 4 — депо: прибытие поезда, выход R13, двор в капитальной стене с аркой, грузовой перрон с доками, охрана, ремонтники и R7; `Game.unity` — build index 5, прежний срез, вне потока | `EditorBuildSettings.asset`, Unity MCP, ADR 0040 |
| Сюжет и целевой поток | `docs/GAME_DESIGN.md`: робот-курьер, `MainMenu → CharacterSetup → Train → Frontier`; реализованы выбор корпуса, переноска, квест «Погрузка», диалоги R13, финал `Train` и сюжет депо `DP-01`…`DP-04` | `docs/GAME_DESIGN.md`, ADR 0026–0029 |
| Содержимое Game | земля, дом, деревья, забор, камера, `Player`, `NPC` с роботами и животными | сцена `Assets/Scenes/Game.unity` |
| Сцена меню | `Assets/Scenes/MainMenu.unity`, build index 1: диорама `MenuDiorama`, облётная камера, `MenuScreen` UI | сцена `Assets/Scenes/MainMenu.unity` |
| Стартовая сцена | `Assets/Scenes/Boot.unity`, build index 0: `SceneController` (переход) + `BootFlow` (`BootScreen`) — загрузка сервисов → `MainMenu` | Unity MCP `manage_scene` |
| Переход между сценами | `Assets/_Project/Scripts/Core/SceneController.cs` — персистентный `DontDestroyOnLoad` с белым занавесом (ADR 0011) | `FrostSpace.Core` |
| Код | `FrostSpace.Core` (3 файла), `FrostSpace.Features` (17 фич: Boot, Camera, Carry, Character, Dialogue, Hud, Interior, Inventory, Locomotion, Menu, Npc, Progression, Quest, Ride, Scan, Story, Map), `FrostSpace.Editor` (8 файлов), `FrostSpace.Tests.EditMode` (264 теста) | `Assets/_Project/`, `run_tests` |
| Поезд среза | один вагон `TrainCar.fbx` 8×24 м на обе сцены (ADR 0032, 0033, 0034): интерьер, кабина 4,2 м с кокпитом, водителем и голопанелью, боковой проём, автодверь, раздвижные створки, тележки, фары | `Assets/_Project/Content/Models/Train/`, `Tools/Blender/build_train.py` |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model` — варианты моделей | `Assets/_Project/Content/` |
| Робо-животные | 13 FBX (`RA_<Species>`), визуальные префабы `RA_<Species>_Model` — варианты моделей | `Assets/_Project/Content/`, ADR 0009 |
| Ездовые ходуны | 4 FBX (`ME_*Walker`) — наш корпус на ригах `Mech_*` пака, клипы печёт `author_gait`; префабы `ME_*_Model` — сразу транспорт с `Mech` и кабиной; сборка `Tools/Blender/build_mechs.py` | `Assets/_Project/Content/`, ADR 0024 |
| Транспорт | байки (`Hoverbike`, `Motorcycle`) — вид сверху с фиксированным курсом; ходуны (`Mech`) — облёт мышью, WASD от камеры; защёлка посадки общая | `Assets/_Project/Scripts/Features/Ride/` |
| Интерфейс | строится кодом и русифицирован, кроме декоративных подписей: `BootScreen`, `MainMenuScreen` (меню и лента видов), `PlayerHud` (подписи сканера, FPS), `PauseMenuScreen` (обзор, журнал, настройки-шестерёнка), `QuestTracker` (текущий квест), `ExperienceBar` (уровни и опыт), `DialogueScreen` (панель разговора) | `Assets/_Project/Scripts/Features/` |
| Диалоги | граф реплик в `DialogueAsset` (`Content/Data/Dialogues/`), авторский инспектор, E у робота в радиусе 4 м; на время разговора NPC стоит и поворачивается, игрок и камера заморожены | `Assets/_Project/Scripts/Features/Dialogue/`, ADR 0025 |
| Квест «Погрузка» | `TrainBatteries.asset`, `TrainCompanion_Intro/Done`, `QuestTracker`; Play-проверка 2026-10-29: диалог → квест → 4 слота → похвала → завершение | `Content/Data/`, `Features/Quest/`, `Assets/Scenes/Train.unity` |
| Сохранение прогресса | один ключ `FrostSpace.Save` (`JSON`, `version 1`): сцена, корпус, квест и счётчик; запись на каждом изменении квеста, `ПРОДОЛЖИТЬ` в меню, `НАЧАТЬ` стирает | `Assets/_Project/Scripts/Features/Quest/ProgressSave.cs`, ADR 0031 |
| Сканирование | Q — сонар (кольцо + контуры), ближайшая цель и цель в области прицела подписываются сами; тумблеры — в паузе | `Assets/_Project/Scripts/Features/Scan/`, ADR 0020 |
| Аудио | `MusicManager` + `Music_FrostSpaceTheme.mp3` из `Content/Audio/Resources/Music/` | `Assets/_Project/Scripts/Core/MusicManager.cs` |
| NPC | `NpcController` (ходьба по маршруту) и `AirPatrolController` (патруль) | `Assets/_Project/Scripts/Features/Npc/` |
| Сборка и CI | `BuildWebGL.PerformBuild` (только CLI) + `.github/workflows/webgl.yml` (game-ci, `unityVersion: auto`) | `Assets/_Project/Editor/BuildWebGL.cs` |
| Unity MCP | пакет установлен; сервер слушает 6400 или следующий свободный (2026-10-29 — 6401), мост `Tools/Unity/unity_bridge.py` работает | live-проверка 2026-10-29 |
| Blender | 5.2.3 LTS, аддон `blender_mcp` (протокол 14, сервер 2.2.2) установлен | `--version` и `install-addon` |
| Пайплайн ассетов | единый вход `Tools/Blender/build_all.py`: сборка → проверки → превью (ADR 0017) | прогон 2026-10-22 |
| Мир Stage 3 | раскладка 6,6×6,6 км из 121 клетки по 600 м, якоря 320/260 м, восемь руин 15–60 м, тёплый каркас 0,4 % карты (дороги и площадки; рельсовая линия холодна) и запас остывания 90 с — всё числами в `world_layout.py` (18 инвариантов), в Unity приходит `world_layout.json` как `TextAsset`. Небо — почти чёрное, редкие яркие звёзды и одна аномалия (`SkyNightStars`) | `Tools/Blender/world_layout.py`, `Content/Data/World/`, `Content/Models/Level/Heritage_*.fbx`, ADR 0036 |
| Скиллы | 34 скилла Unity + 2 проектных (`plans`, `frostspace-3d-assets`), мосты для всех агентов | `.Aura/Skills/`, `Tools/Agent/LinkAgentSkills.ps1` |

## Чего в проекте нет

- Продолжения на карте: финал `Train` (`TR-09`, `TR-10`), сюжет депо
  `DP-01`…`DP-04` и сохранение прогресса квеста (`FrostSpace.Save`, ADR 0031)
  реализованы; миникарта читает раскладку мира и стоит в сцене (`Features/Map`, `RD-05`), прицеп, дорога и станция в коде отсутствуют. Выбор корпуса,
  переноска, квест «Погрузка», диалоги R13, HUD-трекер и опыт реализованы.
- Сети. Ни серверной части, ни мультиплеера, ни облачных сервисов.
- Собственного баланса и игровых правил: дизайн-параметры живут в `[SerializeField]`
  прямо на компонентах. Исключение — авторские данные: `DialogueAsset` и `QuestAsset`
  (ScriptableObject), потому что это данные, а не состояние.
- PlayMode-тестов: тесты только EditMode (`Assets/_Project/Tests/EditMode/`, 264 шт.).
- Локализации: подписи меню и HUD намеренно латинские, `com.unity.localization`
  не подключён; реплики диалогов — русские и лежат прямо в ассетах.
- Импортированных сторонних моделей: каталога `Assets/ThirdParty/` нет, исходники —
  архивами в `AssetSource/`.

Всё перечисленное — **не реализовано**, а не «сделано, но не описано». Не ссылайся на
несуществующие системы в документации или отчётах. При этом интерфейс, аудио, логика
NPC, CI и build-метод **есть** — см. таблицу выше.

## Ограничения и факты, которые влияют на решения

- **Linear color space.** Цвета материалов задаются в sRGB и конвертируются; иначе
  картинка молча темнеет. Подробности — `ASSETS.md`.
- **Root Motion не используется.** Клипы роботов in-place, движение — kinematic через
  `CharacterController`; стопа согласуется с корпусом через `Animator.speed` (ADR 0004).
- **Префаб — визуал, логика — на сценовом актёре.** `R*_Model.prefab` не содержат
  `CharacterController`, ввода и `RobotLocomotion`; всё это — только на `Player`.
- **Play в редакторе тикает только при фокусе окна.** `runInBackground = 1` не заменяет
  фокус: без него `Time.frameCount` стоит на месте (`UNITY_EDITOR.md` §5).
- **Domain reload выключен.** Мутабельная статика переживает запуск Play Mode; её нужно
  сбрасывать через `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- **Input System only.** Старый `Input.GetAxis` не работает; используй
  `InputSystem_Actions.inputactions` и сгенерированный C#-класс.
- **Unity MCP подключён к живому редактору.** Пока редактор открыт, batch-mode недоступен
  из-за `Temp/UnityLockfile`; для итераций используй MCP, для чистой проверки — batch.
- **Blender MCP работает только в GUI.** Headless-сборка идёт скриптами; это штатный путь,
  а не обходной. Единственный вход сборки ассетов — `Tools/Blender/build_all.py` (ADR 0017);
  MCP — черновик и визуальный контроль, а не пайплайн.
- **`AssetSource/` не в git.** Это сырые архивы; в проект импортируется только нужное.

## Исходники ассетов

В `AssetSource/` лежат CC0-архивы Quaternius (природа, постройки, интерьер, персонажи,
животные, еда, мебель, RPG-предметы) и архив Ultimate Modular Men. Их наличие
**не подтверждает** жанр или визуальный стиль проекта; это просто доступный материал.

При импорте: только нужные модели в `Assets/ThirdParty/<Pack>/`, с `SOURCE.md` (источник,
лицензия, дата, что взято). Не тащить пак целиком. Подробности — `ASSETS.md`.

## Допущения

Дизайн зафиксирован как замысел (`GAME_DESIGN.md`), но не проверен игрой. Технические
допущения, которые действуют до первого среза:

- 1 unit = 1 метр, если не появится причина для другого масштаба.
- 3D-сцена от третьего лица; вид сверху — только на байках, облёт — на ходунах
  (ADR 0010, 0012, 0024).
- Один проект — один основной игровой контент; мультиплатформенные ответвления не
  проектируются заранее.
- Провизорные множители трёх корпусов — заглушка, а не баланс; правит владелец.

Любое из этих допущений можно изменить только явным решением, а не «по умолчанию».
