# Обзор проекта FrostSpace

## Статус

Проект находится на ранней стадии: есть играбельный вертикальный срез — снежный уровень
`Game`, двадцать роботов и 13 робо-животных из Blender, локомоция игрока, sci-fi экран
загрузки в `Boot`, главное меню с 3D-диорамой и EditMode-тесты. Полноценного игрового
цикла, целей и баланса нет.

Назначение игры, жанр, игровой цикл и целевые платформы **не зафиксированы**. Это не
повод придумывать их по названию проекта или набору пакетов. Если задача требует
продуктового решения, сначала зафиксируй допущение явно или спроси пользователя.

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
| Игровая сцена | `Assets/Scenes/Game.unity`, build index 2 | `EditorBuildSettings.asset`, Unity MCP |
| Содержимое Game | земля, дом, деревья, забор, камера, `Player`, `NPC` с роботами и животными | сцена `Assets/Scenes/Game.unity` |
| Сцена меню | `Assets/Scenes/MainMenu.unity`, build index 1: диорама `MenuDiorama`, облётная камера, `MenuScreen` UI | сцена `Assets/Scenes/MainMenu.unity` |
| Стартовая сцена | `Assets/Scenes/Boot.unity`, build index 0: `SceneController` (переход) + `BootFlow` (`BootScreen`) — загрузка сервисов → `MainMenu` | Unity MCP `manage_scene` |
| Переход между сценами | `Assets/_Project/Scripts/Core/SceneController.cs` — персистентный `DontDestroyOnLoad` с белым занавесом (ADR 0011) | `FrostSpace.Core` |
| Код | `FrostSpace.Core` (3 файла), `FrostSpace.Features` (8 фич: Boot, Camera, Hud, Locomotion, Menu, Npc, Ride, Scan), `FrostSpace.Editor` (6 файлов), `FrostSpace.Tests.EditMode` (52 теста) | `Assets/_Project/`, `run_tests` |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model` — варианты моделей | `Assets/_Project/Content/` |
| Робо-животные | 13 FBX (`RA_<Species>`), визуальные префабы `RA_<Species>_Model` — варианты моделей | `Assets/_Project/Content/`, ADR 0009 |
| Ездовые ходуны | 4 FBX (`ME_*Walker`) — наш корпус на ригах `Mech_*` пака, клипы печёт `author_gait`; префабы `ME_*_Model` — сразу транспорт с `Mech` и кабиной; сборка `Tools/Blender/build_mechs.py` | `Assets/_Project/Content/`, ADR 0024 |
| Транспорт | байки (`Hoverbike`, `Motorcycle`) — вид сверху с фиксированным курсом; ходуны (`Mech`) — облёт мышью, WASD от камеры; защёлка посадки общая | `Assets/_Project/Scripts/Features/Ride/` |
| Интерфейс | строится кодом: `BootScreen`, `MainMenuScreen` (меню и лента видов), `PlayerHud` (подписи сканера, FPS), `PauseMenuScreen` (пауза и настройки) | `Assets/_Project/Scripts/Features/` |
| Сканирование | Q — сонар (кольцо + контуры), ближайшая цель и цель в области прицела подписываются сами; тумблеры — в паузе | `Assets/_Project/Scripts/Features/Scan/`, ADR 0020 |
| Аудио | `MusicManager` + `Music_FrostSpaceTheme.mp3` из `Content/Audio/Resources/Music/` | `Assets/_Project/Scripts/Core/MusicManager.cs` |
| NPC | `NpcController` (ходьба по маршруту) и `AirPatrolController` (патруль) | `Assets/_Project/Scripts/Features/Npc/` |
| Сборка и CI | `BuildWebGL.PerformBuild` (только CLI) + `.github/workflows/webgl.yml` (game-ci, `unityVersion: auto`) | `Assets/_Project/Editor/BuildWebGL.cs` |
| Unity MCP | пакет установлен, порт 6400, инструменты работают | live-проверка 2026-10-22 |
| Blender | 5.2.3 LTS, аддон `blender_mcp` (протокол 14, сервер 2.2.2) установлен | `--version` и `install-addon` |
| Пайплайн ассетов | единый вход `Tools/Blender/build_all.py`: сборка → проверки → превью (ADR 0017) | прогон 2026-10-22 |
| Скиллы | 34 скилла Unity + проектный `plans`, мосты для всех агентов | `.Aura/Skills/`, `Tools/Agent/LinkAgentSkills.ps1` |

## Чего в проекте нет

- Игрового цикла: целей, условий победы/поражения, прогрессии, экономики, сюжета.
- Сохранений и сети. Ни серверной части, ни мультиплеера, ни облачных сервисов.
- Собственных ScriptableObject-конфигов и баланса — дизайн-параметры живут в
  `[SerializeField]` прямо на компонентах.
- PlayMode-тестов: тесты только EditMode (`Assets/_Project/Tests/EditMode/`, 52 шт.).
- Локализации: подписи интерфейса латинские, `com.unity.localization` не подключён.
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

Пока нет подтверждённого дизайна, действуют только технические допущения:

- 1 unit = 1 метр, если не появится причина для другого масштаба.
- 3D-сцена (в Boot есть 3D-камера и свет), но вид камеры и жанр не зафиксированы.
- Один проект — один основной игровой контент; мультиплатформенные ответвления не
  проектируются заранее.

Любое из этих допущений можно изменить только явным решением, а не «по умолчанию».
