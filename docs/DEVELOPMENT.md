# Разработка и проверка

## Окружение

| Компонент | Значение | Проверено |
|---|---|---|
| Unity | 6000.6.7f1 | да |
| Редактор | `F:\Unity\Editor\6000.6.7f1\Editor\Unity.exe` | да |
| Проект | `F:\Unity Projects\FrostSpace` | да |
| Python | 3.14.3 (для `Tools/`) | да |
| Скиллы | `.Aura/Skills/` + мост `.agents/skills/` | настроены `Tools/Agent/LinkAgentSkills.ps1` |
| Blender | 5.2.3 LTS, `G:\Steam\steamapps\common\Blender\blender.exe` | да |
| Unity MCP | порт `127.0.0.1:6400`, инструменты доступны | да |
| Blender MCP | сервер `mcp-for-blender==2.2.2`, аддон протокол 14, порт `9876`, только при запущенном GUI-Blender | версии согласованы; end-to-end не проверен |

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

```powershell
& "F:/Unity/Editor/6000.6.7f1/Editor/Unity.exe" -batchmode -quit -projectPath "F:/Unity Projects/FrostSpace" -logFile "F:/Unity Projects/FrostSpace/Temp/batch-import.log"
```

Тесты EditMode (не добавляй `-quit`: `-runTests` завершает процесс сам):

```powershell
& "F:/Unity/Editor/6000.6.7f1/Editor/Unity.exe" -runTests -batchmode -projectPath "F:/Unity Projects/FrostSpace" -testPlatform EditMode -testResults "F:/Unity Projects/FrostSpace/Temp/editmode.xml"
```

Тестов PlayMode в проекте нет — только EditMode (сборка `FrostSpace.Tests.EditMode`, 42 тест:
проверки префабов и клипов роботов и животных (включая carry-контракт R18), `ViewBlend`, экранный
вырез, карусель модулей). Флаг `-testPlatform PlayMode` применять не к чему: сборки `FrostSpace.Tests.PlayMode`
не существует. `total: 0` в отчёте означает, что тесты не найдены, а не что «всё прошло».

Сборка: build-метод есть — `FrostSpace.Editor.BuildWebGL.PerformBuild` (см. раздел
«WebGL и публикация»). Собирается только из CLI, меню в редакторе нет (ADR 0005).
Целевая платформа одна — WebGL.

## Пакеты: как они обновляются

`Packages/manifest.json` — источник правды по версиям. Обычные пакеты закреплены
точными версиями (`com.unity.render-pipelines.universal` = `17.6.0` и т.д.) и
обновляются только вручную: меняешь строку в манифесте.

Два пакета приходят из git и **следуют за веткой**:

```json
"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main",
"com.ramenvr.aura-unity": "https://github.com/ramenvr/AuraForUnityPlugin.git"
```

Так они обновляются сами: новая версия на GitHub приезжает при перерезолве.

**Как это работает на самом деле.** `Packages/packages-lock.json` фиксирует конкретный
коммит каждого git-пакета (`hash`). Пока lock не трогали, сборка воспроизводима и
новый коммит не подтягивается сам. Новый коммит подтягивается при перерезолве:

- Package Manager → `Resolve` (кнопка в окне);
- или удалить запись пакета из `packages-lock.json` и открыть проект;
- или изменить строку в `manifest.json`.

**Что это значит на практике:**

| Хочется | Как сделать |
|---|---|
| Обновить Unity MCP или плагин Aura до свежего | Package Manager → `Resolve`, затем закоммитить изменившийся `packages-lock.json` |
| Откатить на прошлую версию | В `packages-lock.json` вернуть прежний `hash` |
| Собрать ровно ту же версию, что и вчера | Ничего не делать: lock уже закрепляет коммит |

Плавающая ветка означает: обновление приезжает само, но может приехать неожиданно.
Если такое не устраивает — закрепи тег (`#v10.3.0`) вместо ветки, и обновлять придётся
вручную. Тогда в `manifest.json` вместо `#main` пишется конкретный тег.

## WebGL и публикация

Сборка и публикация на GitHub Pages описаны отдельно — `DEPLOY.md` (раннер, workflow,
гашение процесса, ловушки). Здесь только то, что относится к самой сборке.

- **Сборка.** `Assets/_Project/Editor/BuildWebGL.cs`, метод
  `FrostSpace.Editor.BuildWebGL.PerformBuild`. Меню нет — только CLI. Настройки: IL2CPP,
  Brotli + decompression fallback (Pages не позволяет задавать заголовки, иначе браузер
  не распакует `.unityweb`), включённое кэширование данных. Выход — `Builds/WebGL`
  (в git не попадает), отчёт — `Builds/webgl-result.json`.
- **Страница.** `Assets/WebGLTemplates/FrostSpace/index.html` — свой шаблон: канвас
  растянут на всё окно (`position: fixed; inset: 0`), без футера Unity с логотипом и
  кнопкой полноэкранного режима. Вместо него — экран загрузки с полосой прогресса.
  Включается в `BuildWebGL`: `PlayerSettings.WebGL.template = "PROJECT:FrostSpace"`.
- **Почему файл-результат, а не код возврата.** `Unity.exe` на Windows — лончер: он
  завершается сразу с кодом 1, а реальный редактор продолжает работу. И workflow, и
  человек должны ждать `Builds/webgl-result.json`, а не код возврата.
- **Локальная проверка** (редактор должен быть закрыт, иначе `Temp/UnityLockfile`):

  ```powershell
  & "F:/Unity/Editor/6000.6.7f1/Editor/Unity.exe" -quit -batchmode -projectPath "F:/Unity Projects/FrostSpace" -buildTarget WebGL -executeMethod FrostSpace.Editor.BuildWebGL.PerformBuild
  ```

## Blender

Blender не обязателен для Unity-разработки. Если задача про 3D-ассеты — читай
`BLENDER.md`. Кратко:

```bash
# единственный вход пайплайна: сборка + проверки + превью (ADR 0017)
python Tools/Blender/build_all.py --list                  # реестр ассетов
python Tools/Blender/build_all.py --asset hoverbike --check
python Tools/Blender/build_all.py --asset robots --ids R1 --preview

# живой MCP — только GUI, окно живёт на время шага
python Tools/Blender/gui_session.py run Tools/Blender/probes/gui_ping.py --auto
python Tools/Blender/gui_session.py status   # поднят ли мост
python Tools/Blender/gui_session.py stop     # закрыть, если сессия осталась
```

Профильные команды (`blender --background --python build_robots.py -- R1` и т.п.) остаются
рабочими для точечной отладки, но штатный путь — `build_all.py`: он сам находит Blender и
пишет лог в `Tools/Blender/logs/`. Headless — основной путь; GUI нужен только для
визуального/интерактивного контроля. Подробности и грабли — в `BLENDER.md`.

## Проверки, которые реально доступны

| Проверка | Команда | Статус |
|---|---|---|
| Синтаксис Python-утилит | `python -m compileall -q Tools` | выполняется |
| Пайплайн ассетов (сборка+проверки) | `python Tools/Blender/build_all.py --asset <id> --check` | PASS (2026-10-22): сборка hoverbike, `selftest`, `fbx_units`, локомоция R1 |
| Low-poly ядро + FBX roundtrip | `blender --background --factory-startup --python Tools/Blender/selftest.py` | PASS на 5.2.3 |
| Единицы внутри FBX | `python Tools/Blender/fbx_units.py <file.fbx>` | работает |
| Разбор FBX после импорта | `blender --background ... probes/probe_fbx_import.py -- <file.fbx>` | работает |
| API действий Blender 5.x | `blender --background ... probes/probe_action_api.py` | работает |
| Анализ PNG | `blender --background ... inspect_image.py -- <file.png>` | работает |
| Unity ping через мост | `python Tools/Unity/unity_bridge.py ping` | `pong` |
| Unity MCP Console | `read_console(types: ["error"])` | 0 ошибок |
| Unity MCP сцена | `manage_scene(get_active)` | `Boot` (index 0) — активная сцена редактора; `MainMenu` — index 1 |
| Unity MCP Build Settings | `manage_scene(get_build_settings)` | `Boot` 0, `MainMenu` 1, `Game` 2 |
| Тесты Unity | `run_tests` + `get_test_job` | 42 EditMode — PASS (2026-10-23): префабы и клипы роботов/животных (включая carry-контракт R18), `ViewBlend`, вырез, карусель модулей |
| Play-замер локомоции | `LocomotionDiagnostics` на `Player`, Console по `PROBE` | базлайн 2026-10-18: walk 0.22, sprint 0.55 м/с скольжения; занос ≤ 8.8° — **на ровном рельефе** |
| Play-замер прыжка | тот же стенд, строка `PROBE jump` | PASS (2026-10-21): height 1.19 м при цели 1.2, airtime 1.07 с |
| Баланс шага на фасетном рельефе | длина клипа × клиповая скорость = путь за цикл (живой замер) | PASS (2026-10-18): клип 1.333 с × 0.912 м/с = 1.217 м против 1.215 м пути — стопа стоит |
| Замер клипов роботов | `blender ... measure_locomotion.py -- R1_PolarMechanic.fbx` | walk 0.93, sprint 8.92 м/с |
| Инварианты клипов животных | `python Tools/Blender/build_all.py --asset animals --check --skip-build` | PASS (2026-10-22): 13 FBX — длины клипов = манифест, 0 вершин без веса, скорость стопы 100 % от `clip_speed_source` |
| Пересборка роботов | `blender ... build_robots.py -- R6 R7` | PASS (2026-10-18): 15 FBX второй партии, 0 вершин без веса |
| Превью партии роботов | `blender ... render_robot_lineup.py -- <имена>` | PASS (2026-10-18): idle, спереди/сзади |
| Контактный лист | `blender ... make_robot_sheet.py -- --cols 5 <имена>` | PASS (2026-10-18): 2400 px, с подписями |
| Мотоцикл в Play | префаб `Motorcycle_Model` в `Game`, Console по `[Motorcycle]` | PASS (2026-10-21): посадка/сход, разгон до 17 м/с, вид сверху; колёса проверены по `mesh.bounds.center ≈ 0` |
| Сборка Unity (WebGL) | `BuildWebGL.PerformBuild` | выполняется в CI (`.github/workflows/webgl.yml`); локальный запуск — только при закрытом редакторе |

## Что не проверено

- Мост к живому Blender проверен 2026-10-22 через `gui_session.py` (`start` → `ping`/`execute_code`
  → `stop`), но только эти две команды: `get_viewport_screenshot`, `pick_viewport_object`,
  `export_scene` и остальные — без сверки со схемой.
- PlayMode-тестов нет: движение проверяет стенд `LocomotionDiagnostics` (`PROBE`) вручную,
  визуальную оценку заноса и кросфейдов делает человек.
  **Стенд меряет контакт относительно минимальной высоты стопы за прогон, поэтому на
  фасетном рельефе и заносах его «скольжение» завышено** — в окно 3 см попадают фазы не
  только опоры. На фасетном уровне проверяй баланс шага: клип `L` секунд при скорости
  клипа `v` сдвигает стопу на `L·v` за цикл, и столько же должен проходить персонаж
  (`скорость × L / animSpeed`); расхождение = скольжение. Стенд гоняй на ровной зоне.
- Unity build: локальный WebGL-сборка после правок не запускалась (метод и workflow есть,
  последняя локальная проверка — до 2026-10-22).
- Импорт моделей из `AssetSource/`: архивы не распакованы, каталога `Assets/ThirdParty/` нет.

## Правила проверки

- Не заявляй «компилируется», «тест прошёл» или «сцена работает», если проверка не
  запускалась. Пиши `Not verified` и причину.
- Не запускай тесты при ошибках компиляции: Unity может вернуть ложный `Passed` на
  старой сборке.
- После правки скриптов: `refresh_unity` → `read_console(errors)` → только потом тесты.
- После правки Blender-утилит: `build_all.py --check` (или `selftest.py` + профильная проба),
  а не только `py_compile`.
- Долгие batch-прогоны не запускай при открытом редакторе: сначала закрой Unity.
