# Обзор проекта FrostSpace

## Статус

Проект находится на ранней стадии: есть играбельный вертикальный срез — снежный уровень
`SnowLevel`, двадцать роботов из Blender, локомоция игрока и EditMode-тесты. Полноценного
игрового цикла, целей и баланса нет.

Назначение игры, жанр, игровой цикл и целевые платформы **не зафиксированы**. Это не
повод придумывать их по названию проекта или набору пакетов. Если задача требует
продуктового решения, сначала зафиксируй допущение явно или спроси пользователя.

## Что подтверждено

| Параметр | Значение | Источник |
|---|---|---|
| Unity | 6000.6.6f1 | `ProjectSettings/ProjectVersion.txt` |
| Редактор | `F:\Unity\Editor\Unity.exe` | Unity Hub / фактический запуск |
| Render pipeline | URP 17.6.0 | `Packages/manifest.json`, `Assets/Settings/*_RPAsset` |
| Качество | `PC` и `Mobile`, текущий `PC` | `ProjectSettings/QualitySettings.asset` |
| Цветовое пространство | Linear | `ProjectSettings/ProjectSettings.asset` |
| Ввод | только Input System | `activeInputHandler: 2` |
| Сериализация | Force Text | `ProjectSettings/EditorSettings.asset` |
| Play Mode | domain reload выключен, scene reload включён | `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 1` |
| Игровая сцена | `Assets/Scenes/SnowLevel.unity`, build index 0 | `EditorBuildSettings.asset`, Unity MCP |
| Содержимое SnowLevel | земля, дом, деревья, забор, камера, `Player`, `NPC` с роботами | сцена `Assets/Scenes/SnowLevel.unity` |
| Сцена-заготовка | `Assets/Scenes/Boot.unity`: Main Camera, Directional Light, Global Volume | Unity MCP `manage_scene` |
| Код | `FrostSpace.Features` (локомоция, камера), `FrostSpace.Editor`, 3 EditMode-теста | `Assets/_Project/`, `run_tests` |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model` — варианты моделей | `Assets/_Project/Content/` |
| Unity MCP | пакет установлен, порт 6400, инструменты работают | live-проверка 2026-10-18 |
| Blender | 5.2.2 LTS, аддон `blender_mcp` установлен | фактический запуск |

## Чего в проекте нет

- Игрового цикла, целей, UI, сохранений, сети и аудио.
- Логики NPC: акторы `NPC_*` статичны и играют Idle, `NpcController` не написан.
- Собственных ScriptableObject-конфигов и баланса.
- CI и build-метода; из Editor-кода — только постпроцессор импорта FBX.
- Импортированных сторонних моделей: `Assets/ThirdParty/` пуст, исходники — архивами в `AssetSource/`.

Всё перечисленное — **не реализовано**, а не «сделано, но не описано». Не ссылайся на
несуществующие системы в документации или отчётах.

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
  а не обходной.
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
