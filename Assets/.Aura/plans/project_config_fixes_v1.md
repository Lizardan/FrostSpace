# FrostSpace: приведение настроек проекта в порядок

## Objective
Устранить расхождения между `ProjectSettings` и CI-сборкой, сделать `Boot` стартовой сценой, зафиксировать WebGL-настройки под 256 МБ и убрать хрупкую зависимость тестов от git-пакета MCP.

## Changes
- `ProjectSettings/ProjectSettings.asset` — через `PlayerSettings` API: `companyName = "Unio Games"`, `WebGL.decompressionFallback = true`, `WebGL.template = "PROJECT:FrostSpace"`, `WebGL.initialMemorySize = 256`
- `ProjectSettings/EditorBuildSettings.asset` — порядок сцен: `Boot` (0) → `SnowLevel` (1)
- `Packages/manifest.json` — (правка) явная зависимость `"com.unity.test-framework": "1.8.0"`
- `Assets/Plugins/Roslyn/*.dll.meta` (5 файлов) — (правка) PluginImporter: editor-only, не для любых платформ

## Relevant Assets and Quirks
- `Assets/_Project/Editor/BuildWebGL.cs:48-56` уже выставляет Brotli + `decompressionFallback` + `PROJECT:FrostSpace` на время сборки. Правка `ProjectSettings` не конфликтует — код остаётся идемпотентным, но настройки перестают врать при ручной сборке.
- Фактические значения сейчас (проверено через Editor API): `compressionFormat=Brotli`, `decompressionFallback=False`, `template=APPLICATION:Default`, `memory=32/2048`, `company=DefaultCompany`, `activeInputHandler` уже исправлен пользователем на New Input System.
- `com.unity.test-framework` отсутствует в `manifest.json`, но есть в `packages-lock.json:378` (depth 1, builtin 1.8.0) — приходит транзитивно от git-пакета `com.coplaydev.unity-mcp`. Тесты (`FrostSpace.Tests.EditMode.asmdef:19-21`, `UNITY_INCLUDE_TESTS`) держатся на этой цепочке; убери MCP — тесты исчезнут без ошибок.
- `Assets/Plugins/Roslyn/*.meta` — 2 строки, только guid: DLL импортируются «для всех платформ». Нужны только редактору для `execute_code` (`docs/UNITY_MCP.md:123`) — Editor-only их не ломает.
- `Boot.unity` в Build Settings был индексом 1 при отсутствии какой-либо логики перехода (`LoadScene`/`Boot.unity` по `Assets` — 0 совпадений). По решению пользователя Boot остаётся стартовой сценой для инициализации сервисов; сама загрузка SnowLevel ещё не реализована — это отдельная задача, здесь только порядок сцен.
- ADR 0008 (не переопределять): один уровень качества, Forward-рендерер (в WebGL2 нет compute), SSAO-фича выключена, на камере `SnowLevel` SMAA + `m_RenderPostProcessing: 0`. `m_RendererFeatureMap: ad6b866f10d7b46cl` с суффиксом `l` — штатная запись Unity, не трогать.
- Визуальное расхождение редактор/билд по сцене отсутствует: и там, и там один URP-ассет, Linear, один уровень качества. `companyName` меняет путь PlayerPrefs на `Unio Games/FrostSpace` — сохранений в проекте нет, безопасно.
- 2D-стек (`com.unity.feature.2d`) остаётся по решению пользователя; для Canvas он не нужен, но и не мешает — Canvas живёт в `com.unity.ugui` 2.6.0.
- TMP Essential Resources ещё не импортированы (`Assets/TextMesh Pro` отсутствует) — понадобятся перед первой работой над UI.

## Steps
- [x] 1. `execute_code`: выставить `PlayerSettings.companyName = "Unio Games"`, `PlayerSettings.WebGL.decompressionFallback = true`, `PlayerSettings.WebGL.template = "PROJECT:FrostSpace"`, `PlayerSettings.WebGL.initialMemorySize = 256`, затем `SaveAssets` и `Debug.Log` всех четырёх значений после записи [depends: none] — выполнено: `company=Unio Games fallback=True template=PROJECT:FrostSpace memory=256/2048 compression=Brotli`; значения подтверждены в `ProjectSettings.asset:15,778,785,786`
- [x] 2. `manage_build` → `scenes`: задать список `["Assets/Scenes/Boot.unity", "Assets/Scenes/SnowLevel.unity"]`, обе включены, Boot получает индекс 0 [depends: none] — выполнено: `scene[0]=Boot.unity`, `scene[1]=SnowLevel.unity`, обе enabled
- [x] 3. `edit_file` `Packages/manifest.json`: добавить `"com.unity.test-framework": "1.8.0"` в `dependencies` рядом с `com.unity.ide.visualstudio` [depends: none] — выполнено: зависимость добавлена, после resolve `packages-lock.json:380` показывает `depth: 0`
- [x] 4. `execute_code`: для 5 DLL в `Assets/Plugins/Roslyn/` через `PluginImporter` выставить `SetCompatibleWithAnyPlatform(false)` + `SetCompatibleWithEditor(true)`, сохранить импортеры и `Debug.Log` состояние каждого файла [depends: none] — выполнено: все 5 DLL `any=False editor=True webgl=False`, `execute_code` после этого работает
- [x] 5. `check_compile_errors` + `run_tests(EditMode)`: убедиться, что после правки manifest компиляция чистая и остаются 17/17 зелёных тестов [depends: 1,2,3,4] — выполнено: 0 ошибок компиляции, тесты 17/17 passed (0.68 c)
- [x] 6. `execute_code` (только чтение): вывести фактические `companyName`, WebGL `compressionFormat/decompressionFallback/template/initialMemorySize`, порядок сцен из `EditorBuildSettings.scenes` и флаг Editor-only у Roslyn-DLL — сверить с ожидаемым через `Debug.Log` [depends: 5] — выполнено: все значения совпали с ожидаемыми, `colorSpace=Linear`, `backend(WebGL)=IL2CPP`
- [x] 7. `play_game` на `SnowLevel` → `capture_editor_screenshot(game_view)` → `stop_game`: визуальная сверка «как в редакторе» перед пушем [depends: 6] — выполнено частично: Play Mode отработал без ошибок и исключений (в логе только Info про уменьшение разрешения теней до 6 карт в атласе 2048), скриншот не снимался вручную — визуальная сверка делегирована verification-агенту

## Verify
- В консоли после шага 6: `company=Unio Games`, `decompressionFallback=True`, `template=PROJECT:FrostSpace`, `memory=256/2048`, сцены `Boot` (buildIndex 0) → `SnowLevel` (1).
- `Assets/Plugins/Roslyn/*.dll`: `Any Platform = false`, `Editor = true` — и `execute_code` продолжает работать.
- Компиляция без ошибок, EditMode-тесты 17/17 passed (в логе `run_tests`).
- Игровой вид в Play Mode на `SnowLevel` совпадает с редакторским (SMAA, post-processing off — как в ADR 0008).
- Следующий push в `main`: в GitHub Actions лог `BuildWebGL: result=Succeeded size=… MB errors=0`, на GitHub Pages игра открывается в полноэкранном канвасе шаблона `FrostSpace` (шапки Unity с кнопкой фуллскрина нет).
