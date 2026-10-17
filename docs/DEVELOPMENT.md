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
`UNITY_MCP.md`.

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

Тесты PlayMode — то же с `-testPlatform PlayMode`. Сейчас тестов нет: результат будет
`total: 0`, это не ошибка, а отсутствие тестов.

Сборка: готового build-метода и build-профиля нет. Не выдумывай команду; используй
Unity MCP `manage_build` или сначала создай Editor-метод сборки отдельной задачей.

## Blender

Blender не обязателен для Unity-разработки. Если задача про 3D-ассеты — читай
`BLENDER.md`. Кратко:

```bash
# сборка/проверка без GUI
"/g/Steam/steamapps/common/Blender/blender.exe" --background --factory-startup \
  --python Tools/Blender/selftest.py

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
| Unity MCP сцена | `manage_scene(get_active)` | Boot, 3 корневых объекта |
| Unity MCP Build Settings | `manage_scene(get_build_settings)` | Boot, index 0 |
| Тесты Unity | `run_tests` + `get_test_job` | не запускались: тестов нет |
| Сборка Unity | `manage_build` | не запускалась |

## Что не проверено

- Реальный end-to-end мост к живому Blender: GUI-Blender не запускался. Mock-тест
  проверяет клиент, но не совместимость с аддоном. Команда для проверки:
  `python Tools/Blender/blender_bridge.py ping` при открытом Blender.
- Unity `run_tests`: в проекте нет тестов, поэтому проверять нечего.
- Unity build: нет build-метода и целевой платформы.
- Импорт моделей из `AssetSource/`: архивы не распакованы, в `Assets/` ничего не добавлено.

## Правила проверки

- Не заявляй «компилируется», «тест прошёл» или «сцена работает», если проверка не
  запускалась. Пиши `Not verified` и причину.
- Не запускай тесты при ошибках компиляции: Unity может вернуть ложный `Passed` на
  старой сборке.
- После правки скриптов: `refresh_unity` → `read_console(errors)` → только потом тесты.
- После правки Blender-утилит: `selftest.py` и профильная проба, а не только `py_compile`.
- Долгие batch-прогоны не запускай при открытом редакторе: сначала закрой Unity.
