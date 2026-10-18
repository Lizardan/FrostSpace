# Разработка и проверка

## Окружение

| Компонент | Значение | Проверено |
|---|---|---|
| Unity | 6000.6.6f1 | да |
| Редактор | `F:\Unity\Editor\Unity.exe` | да |
| Проект | `F:\Unity Projects\FrostSpace` | да |
| Python | 3.14.3 (для `Tools/`) | да |
| Blender | 5.2.2 LTS, `G:\Steam\steamapps\common\Blender\blender.exe` | да |
| Unity MCP | порт `127.0.0.1:6400`, инструменты доступны | да |
| Blender MCP | порт `9876`, только при запущенном GUI-Blender | аддон установлен, мост проверен mock-тестом |

Версия Unity берётся из `ProjectSettings/ProjectVersion.txt`, а не из памяти. Путь к
редактору может отличаться на другой машине: ищи его через Unity Hub, не хардкодь в
скриптах.

## Unity: два режима

### 1. Живой редактор + Unity MCP (для итераций)

Пока Unity открыт, используй MCP: компиляция, Console, тесты, сцена, скриншоты. Это
быстрее перезапуска редактора и не требует закрывать проект. Порядок и ограничения —
`UNITY_MCP.md`; перекомпиляция, `execute_code`, авторинг сцен и проверка результата —
`UNITY_EDITOR.md`.

### 2. Batch-mode (для чистой проверки, CI и сборки)

Batch требует, чтобы редактор был **закрыт**: иначе `Temp/UnityLockfile` блокирует запуск.

Проверка импорта/компиляции:

```bash
"F:/Unity/Editor/Unity.exe" -batchmode -quit \
  -projectPath "F:/Unity Projects/FrostSpace" \
  -logFile "F:/Unity Projects/FrostSpace/Temp/batch-import.log"
```

Тесты EditMode (не добавляй `-quit`: `-runTests` завершает процесс сам):

```bash
"F:/Unity/Editor/Unity.exe" -runTests -batchmode \
  -projectPath "F:/Unity Projects/FrostSpace" \
  -testPlatform EditMode \
  -testResults "F:/Unity Projects/FrostSpace/Temp/editmode.xml"
```

Тесты PlayMode — то же с `-testPlatform PlayMode`. EditMode-тесты живут в
`FrostSpace.Tests.EditMode` (проверки префабов и клипов роботов). `total: 0` в отчёте
означает, что тесты не найдены, а не что «всё прошло».

Сборка: готового build-метода и build-профиля нет. Не выдумывай команду; используй
Unity MCP `manage_build` или сначала создай Editor-метод сборки отдельной задачей.

## WebGL и публикация

Играбельная сборка: **https://lizardan.github.io/FrostSpace/** — обновляется сама
после пуша в `main`.

- **Сборка.** `Assets/_Project/Editor/BuildWebGL.cs`, метод
  `FrostSpace.Editor.BuildWebGL.PerformBuild`. Меню нет — только CLI. Настройки: IL2CPP,
  Brotli + decompression fallback (Pages не позволяет задавать заголовки, иначе браузер
  не распакует `.unityweb`), включённое кэширование данных. Выход — `Builds/WebGL`
  (в git не попадает), отчёт — `Builds/webgl-result.json`.
- **Почему файл-результат, а не код возврата.** `Unity.exe` на Windows — лончер: он
  завершается сразу с кодом 1, а реальный редактор продолжает работу. Workflow ждёт
  `Builds/webgl-result.json`, иначе считает сборку упавшей и убивает процесс.
- **Workflow.** `.github/workflows/webgl.yml`: job `build` на self-hosted раннере
  (`self-hosted, Windows, X64, unity`), job `deploy` — `actions/deploy-pages`.
- **Раннер.** `F:\actions-runner-frostspace`, автозапуск — `FrostSpace WebGL Runner.cmd`
  в папке `Startup` пользователя. ПК должен быть включён: при выключенном раннере job
  ждёт в очереди.
- **Кэш импорта.** В `actions/checkout` стоит `clean: false`: иначе action вычищает
  workspace и удаляет `Library/`, и каждая сборка импортирует проект с нуля (~11 минут).
  С кэшем инкрементальная сборка занимает 2–3 минуты.
- **Не отменяй прогон вручную.** Отмена убивает Unity посреди `git clean`/импорта и
  оставляет битый `Library/PackageCache` (симптом — ошибки компиляции в
  `com.unity.collab-proxy`). Лечится удалением `Library` и `Temp` в
  `F:\actions-runner-frostspace\_work\FrostSpace\FrostSpace` и повторным запуском.
- **Страница.** `Assets/WebGLTemplates/FrostSpace/index.html` — свой шаблон: канвас
  растянут на всё окно (`position: fixed; inset: 0`), без футера Unity с логотипом и
  кнопкой полноэкранного режима. Вместо него — экран загрузки с полосой прогресса,
  который гаснет после старта. Включается в `BuildWebGL`:
  `PlayerSettings.WebGL.template = "PROJECT:FrostSpace"`.
- **Почему не game-ci.** Unity 6 не пишет `.ulf`, который game-ci требует для
  Personal-лицензии; локальный редактор уже активирован, поэтому сборка идёт на этой
  машине.
- **Локальная проверка** (редактор должен быть закрыт, иначе `Temp/UnityLockfile`):

  ```bash
  "F:/Unity/Editor/6000.6.6f1/Editor/Unity.exe" -quit -batchmode \
    -projectPath "F:/Unity Projects/FrostSpace" -buildTarget WebGL \
    -executeMethod FrostSpace.Editor.BuildWebGL.PerformBuild
  ```

## Blender

Blender не обязателен для Unity-разработки. Если задача про 3D-ассеты — читай
`BLENDER.md`. Кратко:

```bash
# сборка/проверка без GUI
"/g/Steam/steamapps/common/Blender/blender.exe" --background --factory-startup \
  --python Tools/Blender/selftest.py

# роботы: пересборка FBX (перезаписывает Content/Models/Robots — запускать осознанно)
"/g/Steam/steamapps/common/Blender/blender.exe" --background --factory-startup \
  --python Tools/Blender/build_robots.py -- R1

# роботы: замер скорости стопы в клипах
"/g/Steam/steamapps/common/Blender/blender.exe" --background --factory-startup \
  --python Tools/Blender/measure_locomotion.py -- <file.fbx>

# живой MCP — только GUI
"/g/Steam/steamapps/common/Blender/blender.exe" --python Tools/Blender/blender_boot.py
```

Headless-прогон — основной путь; GUI нужен только для визуального/интерактивного
контроля. Подробности и грабли — в `BLENDER.md`.

## Проверки, которые реально доступны

| Проверка | Команда | Статус |
|---|---|---|
| Синтаксис Python-утилит | `python -m compileall -q Tools` | выполняется |
| Low-poly ядро + FBX roundtrip | `blender --background --factory-startup --python Tools/Blender/selftest.py` | PASS на 5.2.2 |
| Единицы внутри FBX | `python Tools/Blender/fbx_units.py <file.fbx>` | работает |
| Разбор FBX после импорта | `blender --background ... probes/probe_fbx_import.py -- <file.fbx>` | работает |
| API действий Blender 5.x | `blender --background ... probes/probe_action_api.py` | работает |
| Анализ PNG | `blender --background ... inspect_image.py -- <file.png>` | работает |
| Unity ping через мост | `python Tools/Unity/unity_bridge.py ping` | `pong` |
| Unity MCP Console | `read_console(types: ["error"])` | 0 ошибок |
| Unity MCP сцена | `manage_scene(get_active)` | `SnowLevel`, `Player` + `NPC`, index 0 |
| Unity MCP Build Settings | `manage_scene(get_build_settings)` | `SnowLevel`, index 0 |
| Тесты Unity | `run_tests` + `get_test_job` | 4 EditMode — PASS (2026-10-18), покрывают все 20 роботов |
| Play-замер локомоции | `LocomotionDiagnostics` на `Player`, Console по `PROBE` | базлайн 2026-10-18: walk 0.22, sprint 0.55 м/с скольжения; занос ≤ 8.8° — **на ровном рельефе** |
| Баланс шага на фасетном рельефе | длина клипа × клиповая скорость = путь за цикл (живой замер) | PASS (2026-10-18): клип 1.333 с × 0.912 м/с = 1.217 м против 1.215 м пути — стопа стоит |
| Замер клипов роботов | `blender ... measure_locomotion.py -- R1_PolarMechanic.fbx` | walk 0.93, sprint 8.92 м/с |
| Пересборка роботов | `blender ... build_robots.py -- R6 R7` | PASS (2026-10-18): 15 FBX второй партии, 0 вершин без веса |
| Превью партии роботов | `blender ... render_robot_lineup.py -- <имена>` | PASS (2026-10-18): idle, спереди/сзади |
| Контактный лист | `blender ... make_robot_sheet.py -- --cols 5 <имена>` | PASS (2026-10-18): 2400 px, с подписями |
| Сборка Unity | `manage_build` | не запускалась: нет build-метода и платформы |

## Что не проверено

- Реальный end-to-end мост к живому Blender: GUI-Blender не запускался. Mock-тест
  проверяет клиент, но не совместимость с аддоном. Команда для проверки:
  `python Tools/Blender/blender_bridge.py ping` при открытом Blender.
- PlayMode-тестов нет: движение проверяет стенд `LocomotionDiagnostics` (`PROBE`) вручную,
  визуальную оценку заноса и кросфейдов делает человек.
  **Стенд меряет контакт относительно минимальной высоты стопы за прогон, поэтому на
  фасетном рельефе и заносах его «скольжение» завышено** — в окно 3 см попадают фазы не
  только опоры. На фасетном уровне проверяй баланс шага: клип `L` секунд при скорости
  клипа `v` сдвигает стопу на `L·v` за цикл, и столько же должен проходить персонаж
  (`скорость × L / animSpeed`); расхождение = скольжение. Стенд гоняй на ровной зоне.
- Unity build: нет build-метода и целевой платформы.
- Импорт моделей из `AssetSource/`: архивы не распакованы, `Assets/ThirdParty/` пуст.

## Правила проверки

- Не заявляй «компилируется», «тест прошёл» или «сцена работает», если проверка не
  запускалась. Пиши `Not verified` и причину.
- Не запускай тесты при ошибках компиляции: Unity может вернуть ложный `Passed` на
  старой сборке.
- После правки скриптов: `refresh_unity` → `read_console(errors)` → только потом тесты.
- После правки Blender-утилит: `selftest.py` и профильная проба, а не только `py_compile`.
- Долгие batch-прогоны не запускай при открытом редакторе: сначала закрой Unity.
