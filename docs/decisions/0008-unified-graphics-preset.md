# 0008. Единый пресет графики

- Статус: Accepted
- Дата: 2026-10-19

## Контекст

В `ProjectSettings/QualitySettings.asset` было два уровня: `PC` (редактор, Standalone,
PS4/PS5/GameCore/Xbox) и `Mobile` (Android, iOS, Switch, **WebGL**). Уровень `PC`
содержал `excludedTargetPlatforms: [Android, iPhone, tvOS, WebGL, Switch, Switch2]`,
поэтому WebGL к нему не попадал и фактически жил на `Mobile`.

Каждому уровню соответствовал свой URP-ассет: `PC_RPAsset` (2048 тени, 4 каскада,
soft, Forward+) и `Mobile_RPAsset` (1024, 1 каскад, без soft, дополнительные тени
выключены, `renderScale 0.8`). В WebGL это давало:
- жёсткие низкодетализированные тени;
- явное различие с редактором (там же, но на PC-уровне);
- «из коробки» в вебе был хуже, чем мог быть на тех же ассетах.

Параллельно: в сцене нет ни одного Volume, `m_Antialiasing: 0`, SSAO-фича
объявлена в рендерере, но без Volume не активна.

## Решение

- Один уровень качества `Default`, один URP-ассет `FrostSpace_RPAsset`, один
  рендерер `FrostSpace_Renderer` (Forward, не Forward+: в WebGL2 нет compute).
  `Mobile_RPAsset` и `Mobile_Renderer` удалены.
- `m_PerPlatformDefaultQuality` правится только вручную: `QualitySettings.SetQualityLevel`
  пишет индекс для активной платформы. `BuildWebGL.PerformBuild` вызывает
  `QualitySettings.SetQualityLevel(0, true)` — в батче активная цель WebGL.
- Камера `SnowLevel`: SMAA, качество High (антиалиасинг без полноэкранного MSAA,
  который на WebGL2 дорог по bandwidth).
- SSAO-фича выключена (нет Volume → не активна; включённая фича заставляет URP
  держать промежуточную текстуру).
- URP-ассет: мягкие тени Medium (5x5 tent), `renderScale 1`, `m_RequireDepthTexture
  / m_RequireOpaqueTexture` выключены, HDR включён.

Настройка применяется скриптом-источником `Tools/Unity/snippets/apply_graphics_preset.cs`
(запуск через `run_snippet.py --no-safety`, иначе `execute_code` блокирует удаление
ассетов). Повторный запуск идемпотентен.

## Что не проверено в этой сессии

- Полная сборка WebGL — не запускалась, изменение FPS не измерено.
- `m_PerPlatformDefaultQuality` остаётся с устаревшими `1` для WebGL/PS4/PS5/Standalone
  — SetQualityLevel(0) в редакторе не переписывает файл, а публичного API для
  точечной правки нет. На практике Unity клэмпит индекс к количеству уровней
  (проверено: `GetQualityLevel()` возвращает 0 при 1 уровне и Standalone:1). Для
  WebGL `BuildWebGL.PerformBuild` фиксирует значение в памяти на время сборки.

## Грабли по дороге

- `SerializedProperty.intValue` кидает «type is not a supported int value» для
  полей, у которых `propertyType == Float` или `Boolean` (даже если имя
  выглядит целочисленным). Помощники `SetInt`/`QInt` теперь проверяют
  `propertyType` и пишут через нужный сеттер.
- `m_PerPlatformDefaultQuality` — нативный Generic, у него нет детей в
  SerializedObject; `FindPropertyRelative` тоже возвращает null. Прав только
  `QualitySettings.SetQualityLevel`.
- `AssetDatabase.LoadAllAssetRepresentationsAtPath` не возвращает sub-ассеты
  (рендерер-фичи), нужен `LoadAllAssetsAtPath`.
- `m_RendererFeatureMap` Unity записывает в формате с суффиксом `l` в конце
  шестнадцатеричной строки — это не ошибка, не трогать.
- `manage_camera` рендерит отдельной камерой инструмента, post-обработка и
  настройки игровой камеры туда не попадают; снимок пригоден только для
  проверки «сцена вообще рисуется» (см. `docs/UNITY_MCP.md`).
