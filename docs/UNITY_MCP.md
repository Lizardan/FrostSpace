# Unity MCP: проверенный порядок работы

Документ описывает **управление живым Unity-редактором** через MCP. Сборка и batch-режим
— `DEVELOPMENT.md`. Перекомпиляция, `execute_code`, авторинг сцен и проверка результата —
`UNITY_EDITOR.md`. Blender-мост — `BLENDER.md`.

## Статус: подключён и проверен (2026-10-18)

| Параметр | Значение |
|---|---|
| Пакет | `com.coplaydev.unity-mcp` (git, `MCPForUnity#main`) |
| Resolved hash | `aa5fc638d623f56178d50329d9ad8c541a57fe66` (в `Packages/packages-lock.json`) |
| Сервер в клиенте | `unityMCP` |
| Порт | `127.0.0.1:6400` |
| Unity | 6000.6.6f1 |
| Активная сцена | `Assets/Scenes/SnowLevel.unity` (build index 0) |
| Состояние | редактор запущен, компиляция завершена, EditMode-тесты 3/3, ошибок в Console нет |

`manifest.json` ссылается на ветку `#main`, поэтому версия сервера может измениться при
перерезолве. Hash в lockfile фиксирует текущую; имена инструментов проверяй по схеме,
если пакет обновился.

## Имена инструментов

MCP отдаёт канонические имена (`read_console`). Клиент может добавлять префикс сервера
(`unityMCP_read_console`). В этом документе — канонические имена.

Унифицированные инструменты принимают `action` (`get`, `create`, `load`, ...);
остальные — плоские параметры. **Не выдумывай имена и параметры**: сверяйся со схемой
инструмента.

## Что проверено фактическим вызовом

| Вызов | Фактический результат |
|---|---|
| `manage_scene(action: "get_active")` | `SnowLevel`, `Assets/Scenes/SnowLevel.unity`, buildIndex 0 |
| `manage_scene(action: "get_hierarchy", page_size: 50, max_depth: 2)` | 10 корней (`Player`, `NPC`, дом, деревья, забор, свет...); `next_cursor: null` |
| `manage_scene(action: "get_build_settings")` | только `Assets/Scenes/SnowLevel.unity`, enabled, index 0 |
| `read_console(action: "get", types: ["error"], count: 20)` | `Retrieved 0 log entries` после сборки уровня и тестов |
| `run_tests` + `get_test_job` | 3 EditMode-теста — `succeeded` (2026-10-18) |
| `execute_code(action: "execute")` | работает, `compiler: roslyn`; видит типы проектных сборок (`FrostSpace.Features`, `FrostSpace.Editor`) |
| `manage_asset(action: "delete")` | папка `Assets/Screenshots` удалена (2026-10-18) |
| `refresh_unity(mode: "if_dirty", scope: "all", compile: "request", wait_for_ready: true)` | `refresh_triggered: true`, `compile_started: true`, `resulting_state: compiling` |
| `unity_reflect(action: "get_type", class_name: "UniversalAdditionalCameraData")` | тип найден в `Unity.RenderPipelines.Universal.Runtime` |
| `manage_tools(action: "list_groups")` | группы и состав инструментов получены |

Важно: `refresh_unity` вернул `compiling`, а не `idle`. Это не готовность. После него
нужно дождаться завершения компиляции/domain reload и только потом читать Console и
запускать тесты. В проверке через 6 секунд `read_console` уже вернул 0 ошибок, а
`manage_scene` — активную сцену.

## Группы инструментов

`manage_tools(action: "list_groups")` в этой сессии:

| Группа | Инструменты | enabled |
|---|---|---|
| `core` | 25: сцены, скрипты, ассеты, редактор, камера, материалы, префабы, build, `read_console`, `refresh_unity`, `batch_execute` | true |
| `testing` | `run_tests`, `get_test_job` | используются (`true`) |
| `docs` | `unity_reflect`, `unity_docs` | `unity_reflect` используется (`true`) |
| `scripting_ext` | `execute_code`, `manage_scriptable_object` | `execute_code` используется (`true`) |
| `asset_gen` | `generate_*`, `import_model*` | false |
| `animation` | `manage_animation` | false |
| `probuilder` | `manage_probuilder` | false |
| `profiling` | `manage_profiler` | false |
| `ui` | `manage_ui` | false |
| `vfx` | `manage_shader`, `manage_texture`, `manage_vfx` | false |

Изначально витрина показывала `enabled: false` для этих групп, но вызовы проходили:
клиент получил список при подключении. `unity_reflect`, `run_tests` и `execute_code`
вызывались успешно. После перезапуска клиента витрина может измениться; тогда включи
группу через `manage_tools(action: "activate", group: "<name>")`.

## Golden loop

Порядок обязателен. Нарушение даёт ложный «зелёный» результат.

```
1. manage_scene(get_active) / проверить, что редактор готов
2. refresh_unity(compile: "request", wait_for_ready: true)
3. дождаться завершения компиляции (не «compiling»)
4. read_console(action: "get", types: ["error"])
5. только если ошибок нет -> run_tests -> get_test_job до succeeded/failed
6. manage_scene / manage_asset / screenshot — по задаче
7. записать: что проверено, что нет
```

**Не переходи к тестам, пока в Console есть ошибки компиляции.** Unity может запустить
тесты на старой сборке и вернуть ложный `Passed`.

## Тесты

`run_tests` **асинхронный**:

```json
{ "mode": "EditMode", "include_failed_tests": true }
```

Возвращает `job_id`. Опрос:

```json
{ "job_id": "<id>", "include_failed_tests": true, "wait_timeout": 60 }
```

Только `status: succeeded`/`failed` является результатом. `job_id` — не результат.
`total: 0` — не «тесты прошли», а «тесты не найдены»: сверь, что сборка тестов
(`FrostSpace.Tests.EditMode`) видна и в ней есть тесты.

Для PlayMode после domain reload может понадобиться `init_timeout` (например, 120000).
Точные параметры сверяй со схемой.

## Ловушки

| Ловушка | Что делать |
|---|---|
| Инструмента `recompile` не существует | Использовать `refresh_unity(compile: "request")` |
| `run_tests` вернул `job_id`, а не результат | Дождаться `get_test_job` |
| `manage_scene(get_hierarchy)` без `page_size` даёт огромный ответ | Всегда передавать `page_size` |
| `manage_scene(action: "load")` в Play Mode падает | Сначала `manage_editor(action: "stop")` |
| `execute_menu_item` не ждёт компиляции | После правки скрипта дождаться компиляции; иначе выполнится старый код |
| `execute_code` падает на старом компиляторе | Нужны Roslyn DLL в `Assets/Plugins/Roslyn/`; в проекте они есть. Ограничения и запрет `using` — `UNITY_EDITOR.md` §2 |
| Тесты «зелёные», а правка не влияет | Проверить mtime `Library/ScriptAssemblies/*.dll` — `UNITY_EDITOR.md` §1 |
| `batch_execute` ограничен | Лимит 25 команд за вызов (сверяйся со схемой) |
| Скриншот камеры попадает в `Assets/Screenshots/` | После проверки удалить папку, иначе попадёт в импорт/сборку |
| `apply_text_edits` соблазняет править YAML | Нельзя: `.unity`, `.prefab`, `.mat`, `.asset` — только через инструменты Unity |
| `HTTP 429` в логе сервера | Телеметрия, на работу не влияет (наблюдение из другого проекта) |
| `total: 0` в тестах | Тесты не найдены: проверь asmdef и классы тестов, это не «прошло» |
| Play не тикает: `frameCount` стоит | Вернуть фокус окну редактора (ALT-трюк, `UNITY_EDITOR.md` §5); `runInBackground` не помогает |
| Play-стенд (диагностика) молчит | Добавлять компонент после стабилизации перехода в Play, а не в момент `is_changing`; проверить стартовый лог |

Пункты про загрузку сцены в Play Mode, `execute_menu_item`, лимит `batch_execute` и
папку скриншотов перенесены из проверок проекта-предшественника; в FrostSpace они **не
воспроизводились**. При первом использовании проверь фактическое поведение.

## Проверка API и документации

Не полагайся на память об Unity API, особенно для пакетов (URP, Input System,
Cinemachine, ProBuilder):

```
unity_reflect(action: "search", query: "X")
unity_reflect(action: "get_type", class_name: "X")
unity_reflect(action: "get_member", class_name: "X", member_name: "Y")
unity_docs(action: "get_doc", class_name: "X")
unity_docs(action: "get_manual", slug: "execution-order")
```

`unity_reflect` проверен на `UniversalAdditionalCameraData`. `unity_docs` в этой сессии
не вызывался — сверяйся со схемой.

## Если MCP-инструментов нет в клиенте

Unity уже слушает 6400. Пока инструменты не появились, используй прямой клиент:

```bash
python Tools/Unity/unity_bridge.py ping
python Tools/Unity/unity_bridge.py call manage_scene '{"action": "get_active"}'
```

Протокол: приветствие `WELCOME UNITY-MCP 1 FRAMING=1`, дальше 8-байтная big-endian длина
кадра + UTF-8 JSON. Это **не** протокол Blender: там сырой JSON без длины.

Проверено: `ping` → `{"status": "success", "result": {"message": "pong"}}`.

## Что не проверено в этом проекте

- `manage_build` / сборка — не запускалась, целевая платформа не выбрана.
- `manage_prefabs`, `import_model_file`, `manage_animation`, `manage_probuilder` —
  не вызывались.
- `unity_docs` — не вызывался (используется `unity_reflect`).
- `manage_camera(action: "screenshot")` — не вызывался.
- Поведение после перезапуска клиента и перерезолва пакета — не проверялось.

Если используешь что-то из этого впервые — сверься со схемой, выполни один пробный
вызов и запиши фактический результат в отчёт.

## Правила

- Не выдумывай имена инструментов и параметры.
- Не запускай тесты при ошибках компиляции.
- Не заявляй «тесты прошли», не дождавшись `get_test_job`.
- Не заявляй «сборка собралась», не собрав её.
- Не правь YAML ассетов вручную.
- Не удаляй `.meta` и `Assets/Plugins/Roslyn/` без проверки.
- Результат, который должен сохраниться, переноси в скрипт/Editor-инструмент, а не
  оставляй только в ad-hoc MCP-вызове.
