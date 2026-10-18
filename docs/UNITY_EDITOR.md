# Unity Editor: перекомпиляция, авторинг и проверка

Документ — про работу **внутри живого редактора**: как заставить его пересобрать скрипты,
как писать ad-hoc Editor-код, как собирать сцены и как проверять результат, чтобы не
принять старое за новое. Транспорт, имена инструментов и golden loop — `UNITY_MCP.md`;
batch-режим и тесты — `DEVELOPMENT.md`.

Знания собраны из практики проекта-предшественника (Unity 6.5/6.6) и перенесены как
**проверяемые правила**, а не как факты про FrostSpace. Часть из них в этом проекте ещё
не воспроизводилась — это отмечено в таблице.

## Статус знаний

| Утверждение | Статус в FrostSpace |
|---|---|
| `UnityEngine.EntityId` существует, `Object.GetInstanceID()` помечен obsolete | проверено 2026-10-18 через `unity_reflect` |
| `refresh_unity` возвращает `compiling`, а не готовность | проверено |
| `AssetDatabase.Refresh()` сам не запускает компиляцию; `delayCall` не работает без фокуса | не воспроизводилось |
| Мост рвётся на domain reload и требует повторных подключений | не воспроизводилось: в этой сессии релоад переживал |
| После перекомпиляции Console очищается | не воспроизводилось |
| `execute_code` игнорирует asmdef и не принимает `using` | проверено частично: сниппет видит типы проектных сборок (`FrostSpace.Features`, `FrostSpace.Editor`); `using` в сниппетах не использовался |
| `PrefabUtility.SaveAsPrefabAsset` на инстансе FBX-модели даёт Variant | проверено 2026-10-18: 5 префабов `R*_Model` получили `PrefabAssetType.Variant` |
| Play Mode не тикает без фокуса окна; `runInBackground` не помогает | проверено 2026-10-18: `Time.frameCount` стоит даже при `runInBackground = 1` |
| `SetForegroundWindow` из фонового процесса блокируется Windows; ALT-трюк разблокирует | проверено 2026-10-18 |
| `PrintWindow` отдаёт устаревший кадр, редактор не перерисовывается без фокуса | не воспроизводилось |
| `Dictionary` не сериализуется в `EditorWindow`, `List` — сериализуется | не воспроизводилось |
| Уровень obsolete у `GetInstanceID` (warning или error) | не проверялся компиляцией |

Правило: перед тем как опереться на пункт со статусом «не воспроизводилось», выполни
дешёвую пробу и запиши фактический результат.

## 1. Перекомпиляция: единственный надёжный критерий

`AssetDatabase.Refresh()` **не** запускает компиляцию скриптов. `EditorApplication.delayCall`
**не выполняется**, пока окно редактора не в фокусе (проверено на другом проекте:
`update`-колбэк срабатывает, `delayCall` — нет).

Рабочий приём — одноразовый хук на `EditorApplication.update`:

```csharp
UnityEditor.EditorApplication.CallbackFunction act = null;
act = () => {
    UnityEditor.EditorApplication.update -= act;
    UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);
    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
};
UnityEditor.EditorApplication.update += act;
```

Дальше:

1. Дождаться, пока `Library/ScriptAssemblies/<Asm>.dll` станет **новее правившегося `.cs`**.
   Сравнивать с временем исходника, а не с прошлым mtime dll: если редактор уже был в
   фокусе, он мог пересобрать всё до запуска проверки.
2. После появления свежего dll подождать ещё ~20–25 с: dll пересобирается **до**
   перезагрузки домена.
3. Только потом читать Console и запускать тесты.

**После перекомпиляции Console очищается.** Старые записи с `file` + `line` легко принять
за свежие — сверяй номер строки с текущим содержимым файла.

**Мост рвётся на время domain reload** (`WinError 10061`) — это норма. Нужен повтор
20–40 раз по 3 секунды; один отказ не означает «мост умер» и не повод перезапускать Unity.

**Если MCP недоступен**, редактор перекомпилирует скрипты при получении фокуса окна.
Ждать не по таймеру, а по критерию `dll_mtime > cs_mtime`; после свежего dll — ещё пауза
на релоад.

**Domain reload выключен только для входа в Play Mode.** Перекомпиляция скриптов всё равно
перезагружает домен редактора, поэтому кэши Editor-окон и статические поля могут
сброситься (см. §4).

## 2. `execute_code`: ad-hoc Editor-код

Инструмент живёт в группе `scripting_ext` (по умолчанию может быть выключена). Параметр
`action` обязателен, иначе `'action' parameter is required`.

| Ограничение | Следствие |
|---|---|
| Компилятор может быть `codedom`, а не полноценный Roslyn | `var` внутри `if/else` даёт «use of unassigned local variable»; лямбды в `+=` требуют точного типа делегата |
| Директиву `using` писать нельзя | Сниппет вставляется внутрь тела метода. Только полные имена: `UnityEditor.SceneManagement.EditorSceneManager`, `UnityEditor.Animations.AnimatorController` |
| Код компилируется против **всех** загруженных сборок и игнорирует asmdef | Можно использовать типы URP и редакторных пакетов без ссылок в проекте. Но «скомпилировалось в `execute_code`» ≠ «разрешено в рантайм-сборке» |
| Есть блокировка опасных шаблонов | `File.Delete`, `Directory.Delete`, `AssetDatabase.DeleteAsset`, `MoveAssetToTrash`, `EditorApplication.Exit`, `Process.Start/Kill`, `while(true)`, `for(;;)` |
| Удалять ассеты из сниппета нельзя | Только `manage_asset(action: "delete")`. Перезапись — через `CreateAsset`/`SaveAsPrefabAsset` |
| Удаление можно разблокировать параметром `safety_checks: false` | Работает (`AssetDatabase.DeleteAsset` прошёл на старых префабах), но это осознанный обход: применяй только для конкретной операции и фиксируй в отчёте |
| Сниппет строкой в shell ломается на кавычках и `$` | Передавай содержимое файла, а не одну длинную строку |

Если в ответе пришло `"compiler": "roslyn"` — ограничения `codedom` не действуют.
Ориентируйся на фактический ответ, а не на предположение.

## 3. Сцены и префабы

Менять `.unity`, `.prefab`, `.mat`, `.asset` можно только инструментами Unity. Порядок:

1. `EditorSceneManager.SaveOpenScenes()` → `OpenScene(path, OpenSceneMode.Single)`.
2. Строить объекты через `new GameObject`, `AddComponent`, `PrefabUtility`.
3. `EditorSceneManager.MarkSceneDirty(scene)` → `SaveScene(scene, path)`.
4. `AssetDatabase.SaveAssets()` + `Refresh()`.

Приватные `[SerializeField]` задаются только через `SerializedObject`:

```csharp
var so = new SerializedObject(component);
var prop = so.FindProperty("_target");   // имя поля как в C#
if (prop == null) { /* поля нет — записать в отчёт, не молчать */ }
prop.objectReferenceValue = other;
so.ApplyModifiedPropertiesWithoutUndo();
```

Массивы: `prop.arraySize = n; prop.GetArrayElementAtIndex(i)...`.
**Интерфейсы сериализовать нельзя** — в инспекторе держать `MonoBehaviour[]`, в рантайме
приводить к интерфейсу.

**Префаб из FBX-модели — вариант.** `PrefabUtility.SaveAsPrefabAsset(instanceRoot, path)`
на инстансе модели даёт `PrefabAssetType.Variant`: префаб подхватывает изменения импорта
FBX. Проверено на 20 роботах (2026-10-18): пять принятых и 15 второй партии.

**Префаб — визуал, логика — на сценовом объекте.** В префабе только меш и `Animator`;
`CharacterController`, ввод и игровая логика живут на сценовом актёре (ADR 0004).
Проверяется EditMode-тестом, а не договорённостью.

Ловушки, которые ловятся только запуском:

- **`AssetDatabase` не знает о файлах, скопированных мимо Unity.** `AssetImporter.GetAtPath`
  вернёт `null`, настройка импорта молча ничего не сделает. Перед работой —
  `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)`.
- **`AddComponent` вызывает `Awake` немедленно.** Компонент, которому в `Awake` нужны
  зависимости, нельзя собрать из теста: тест не успевает их передать. Ленивая подготовка
  в первом `LateUpdate` + `EnsureReady()` снимает ограничение.
- **Mesh с вершинами в мировых координатах нельзя вешать на дочерний объект движущегося
  родителя.** Такие меши живут в корне сцены с единичным трансформом.
- **Порядок `LateUpdate` между компонентами не определён.** Если один копирует трансформ
  другого, копия может оказаться на кадр позади. Лечится иерархией (копия — дочерний
  объект оригинала), а не Script Execution Order.
- **Правки в Play Mode сбрасываются при выходе.** Постоянные значения задавать в Edit Mode
  через `SerializedObject` + `SaveScene`, в Play только читать.
- Смена слоёв проекта — через `TagManager.asset`, а не правкой YAML.

## 4. EditorWindow и IMGUI

- `GUILayoutUtility.GetLastRect()` сразу после `BeginScrollView` **бросает** «You cannot
  call GetLast immediately after beginning a group». Ширину брать probe-прямоугольником:
  `GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)).width`.
- `LabelField` без ограничения ширины **расширяет группу, а не переносится**. Лечится
  `GUILayout.MaxWidth`.
- **Кириллица + `EditorStyles.boldLabel` = пустой текст.** У встроенного динамического
  шрифта нет жирного начертания с кириллицей. Выделять `fontSize` и цветом.
- `EditorUtility.SetDirty` / `AssetDatabase.SaveAssets` **нельзя** звать внутри открытой
  GUILayout-группы. Копить флаг и применять в конце `OnGUI`.
- `EditorGUILayout.Popup(GUIContent.none, …)` даёт попап без подписи; проверять перегрузку
  и что `GUIStyle` ссылается на существующее имя стиля, иначе каждый repaint пишет ошибку.
- **`List<T>` Unity сериализует, `Dictionary<,>` — нет.** После перезагрузки домена
  (перекомпиляция при открытом окне) кэш-список вернётся заполненным, а словарь — пустым:
  `dict[key]` бросит `KeyNotFoundException`. Помечать кэши `[System.NonSerialized]`,
  пересобирать в `OnEnable` и подстраховываться `??=`: инициализаторы полей на пути
  десериализации могут не выполниться.

## 5. Превью, скриншоты, камера

**Пустое превью** (`PreviewRenderUtility`) — почти всегда клип-плоскости не подстроены под
дистанцию кадра. Камера ставится на `distance = radius * factor`, а `near/far` остаются
0.01–50, и модель крупнее ~20 единиц уезжает за `far`.

```csharp
camera.nearClipPlane = Mathf.Max(0.01f, distance * 0.05f);
camera.farClipPlane  = Mathf.Max(50f, distance * 4f);
```

Проверять числом, а не «на глаз»: посчитать непрозрачные пиксели текстуры. `0` означает
«не отрендерилось ничего».

```csharp
var px = tex.GetPixels32();
var n = 0;
for (var i = 0; i < px.Length; i++) { if (px[i].a > 8) n++; }
return "opaque=" + n;
```

Полезно сравнивать два объекта в одном вызове: «готовый» и «сырой» — сразу видно, баг в
коде рендера или в конкретном ассете.

**Скриншот сцены:**

- `manage_scene(action: "screenshot")` кладёт файл в `Assets/Screenshots/`. После проверки
  папку убрать, иначе она попадёт в импорт и сборку.
- В Edit Mode объёмные эффекты (пост-обработка, рантайм-слои) **не работают** — снимать в
  Play Mode, иначе проверка врёт.
- **Скриншот может быть из устаревшей сессии.** После тестов редактор остаётся в сцене,
  которую загрузил тест, и Play запускает её, а не уровень. Перед выводом проверить:

```csharp
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return scene.path + " frame=" + Time.frameCount + " scale=" + Time.timeScale
    + " paused=" + UnityEditor.EditorApplication.isPaused;
```

`frameCount` обязан расти между двумя вызовами.

**Play Mode не тикает без фокуса окна.** Пока окно редактора не в фокусе, `Time.frameCount`
стоит на месте, даже если в `PlayerSettings.runInBackground` включён (проверено
2026-10-18). Для Play-проб сначала верни фокус окну (приём ниже) и убедись, что
`frameCount` растёт, и только потом доверяй логам.

**Скриншот окна редактора (Windows).** `PrintWindow(PW_RENDERFULLCONTENT)` отдаёт
устаревший кадр; живой кадр — только `BitBlt` с экранного DC. Редактор не перерисовывает
окна, пока не в фокусе: нужен minimize → maximize → `SwitchToThisWindow` →
`SetForegroundWindow` с проверкой `GetForegroundWindow() == hwnd`. **Windows блокирует
`SetForegroundWindow` из фонового процесса**; рабочий приём — послать ALT
(`keybd_event(VK_MENU, 0, 0, 0)` down/up) и сразу вызвать `SetForegroundWindow`;
приём проверен 2026-10-18. Плавающие `EditorWindow` не видны как отдельные HWND — их
координаты брать из `window.position` и резать кадр.

## 6. Как проверять, а не обманывать себя

- **Числа важнее картинки.** Тёмная сцена не позволяет судить по скриншоту: проверять
  форму геометрии через публичное свойство, состояние компонентов через `execute_code`,
  остаток пути следования по времени, а не «сдвинулось ли за кадр».
- **Многослойный эффект: снимать выход СВОЕГО слоя.** Правка идёт через вершины → шейдер
  промежуточной текстуры → композит → кадр, и итоговый скриншот не говорит, кто виноват.
  Дамп промежуточной `RenderTexture`:

  ```csharp
  var tex = /* приватное поле */;
  var prev = RenderTexture.active;
  RenderTexture.active = tex;
  var img = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
  img.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
  img.Apply();
  RenderTexture.active = prev;
  System.IO.File.WriteAllBytes("Temp/dump.png", img.EncodeToPNG());
  ```

  Дальше мерить профиль яркости на стороне Python (`PIL`), а не глазами.
- **Замер по итоговому кадру обманчив, если под ним шумная текстура.** Мерить слой без
  текстуры.
- **Параметр доехал до ассета, но не до рантайм-копии.** Компонент часто клонирует
  материал (`new Material(source)`), и правка ассета на картинку не влияет. Проверять
  фактическую копию и то, что каждое свойство шейдера кем-то присваивается.
- **Если замер противоречит ожиданию — сначала перепроверить его в новой сессии Play,**
  и только потом искать дефект. Тесты оставляют редактор в чужой сцене, а правки в Play
  Mode сбрасываются при выходе.
- **Стенд/диагностику добавлять после стабилизации перехода в Play.** Компонент,
  добавленный в момент `is_changing`, может пережить переход, но не получить `Awake`/
  корутину: Console пуст, хотя `frameCount` растёт. Ждать готовности Play и проверять
  стартовый лог стенда (`PROBE start`) до доверия числам.

## 7. Unity 6.6: EntityId и устаревшие API

`GetInstanceID()` в этой версии помечен obsolete с сообщением «Use GetEntityId instead».
Таблица замен — по практике проекта-предшественника; перед использованием проверяй
фактическую сигнатуру через `unity_reflect`.

| Устаревшее | Замена |
|---|---|
| `obj.GetInstanceID()` | `obj.GetEntityId()` |
| `EditorUtility.InstanceIDToObject(id)` | `EditorUtility.EntityIdToObject(entityId)` |
| `AssetDatabase.GetAssetPath(int id)` | `GetAssetPath(EntityId)` или `GetAssetPath(Object)` |
| `[OnOpenAsset]` с `(int, int)` | `(EntityId entityId, int line)` |
| `EditorApplication.hierarchyWindowItemOnGUI` | `hierarchyWindowItemByEntityIdOnGUI`, делегат `(EntityId, Rect)` |
| `DragAndDrop.AddDropHandler/RemoveDropHandler` | `AddDropHandlerV2/RemoveDropHandlerV2` |
| `HierarchyProperty` | `HierarchyIterator` |
| `AdvancedDropdownItem.children` | `childList` |
| `Scene.handle` как `int` | тип `SceneHandle` |
| `PlayerSettings.*ForGroup(BuildTargetGroup, …)` | `NamedBuildTarget.FromBuildTargetGroup(group)` |
| `PlayerSettings.SetIconsForTargetGroup` | `SetIcons(NamedBuildTarget, icons, IconKind.Application)` |
| `AnimatorUpdateMode.AnimatePhysics` | `Fixed` **плюс** `animator.animatePhysics = true` |
| `TMP_Text.enableWordWrapping` | `textWrappingMode` (если используем TMP) |

`EntityId` — структура в `UnityEngine`: `EntityId.None`, **`IsValid()` — метод**,
`GetHashCode()`, `ToULong()`; `Equals` и `GetRawData` в этой версии сами помечены obsolete.
Хранить как `int` нельзя (обрезает старшие 32 бита); словари — `Dictionary<EntityId, T>`.

**`#pragma warning disable 618` и `-nowarn:0619` не подавляют CS0619**, потому что это
`[Obsolete(msg, error: true)]`. Нужна реальная замена вызова.

Точные сигнатуры:

1. `unity_reflect(action: "get_member", class_name: "X", member_name: "Y")` — быстрее всего.
2. XML-документация установки: `<UnityInstall>/Editor/Data/Managed/**/*.xml`. Путь
   установки виден в первых строках `Logs/Editor.log`.

**Где можно править, а где нет:**

- `Packages/<name>/` — embedded-пакет (`file:` в `packages-lock.json`), правится.
- `Library/PackageCache/<name>@hash/` — кэш, **не править**, перезапишется.
- Готовые `.dll` перекомпилировать нельзя. Их вызовы удалённого API компилируются, но в
  рантайме бросают `NotImplementedException`; окно редактора падает в `OnGUI` и уходит в
  **бесконечный цикл перерисовки**, `Editor.log` растёт на мегабайты в секунду, компиляция
  и `delayCall` перестают выполняться. Лечение — обновить пакет/плагин, а не удалять dll
  наугад и не прятать ошибки фильтром лога.

## 8. Диагностика Console и Editor.log

- `Editor.log` содержит историю всех компиляций (единственное исключение из правила
  «не открывать `Logs/`» — это диагностика). Читать **только новую часть** от offset,
  снятого в начале работы, иначе старые ошибки портят картину.
- Файл может вырасти до сотен МБ: читать чанками по 8 МБ с перекрытием 4 КБ, иначе блок,
  разорванный на границе чанка, потеряется.
- Свежесть сборки проверять по mtime `Library/ScriptAssemblies/<Asm>.dll`: не обновился —
  компиляции не было.
- Уникальные `error CS` агрегировать словарём по строке целиком: одна и та же ошибка часто
  повторяется десятки раз.
- Перед разбором упавшего теста проверить, ссылается ли он на изменённые типы. Если нет —
  падение было до правки и к ней отношения не имеет.
- Рантайм-ошибки группировать по **типу исключения + первому не-Unity кадру стека**, а не
  по тексту сообщения.
- `NullReferenceException` в `OnDestroy` — почти всегда порядок уничтожения объектов;
  проверка `field != null` у `UnityEngine.Object` ловит и уничтоженный объект.
- Ошибки из `async void` нельзя поймать своим `try/catch` — исключение уходит в консоль
  мимо вызывающего кода.
- `UnityProjectNotLinkedException` (Unity Services) — пустые `cloudProjectId` и
  `organizationId` в `ProjectSettings.asset`. Не вызывать инициализацию сервисов, пока
  проект не привязан.
- Массовые `warning CS`, появившиеся «вдруг», обычно означают, что впервые за долгое время
  пересобралась отдельная asmdef-сборка. Это не красные ошибки.
- После исправления одной ошибки компилятор может выдать новую пачку: ранее заблокированные
  сборки начинают компилироваться. Итерация неизбежна — не считать это регрессией.
- Финальная приёмка компиляции: 0 `error CS`, 0 `warning CS`, и `Editor.log` **не растёт**
  при активном окне (замерять размер 3–6 раз по 5 с). Рост в сотни КБ/с — цикл жив.

## 9. Чек-лист «не забуксовать»

| Симптом | Первая проверка | Действие |
|---|---|---|
| Тесты зелёные, но правка не влияет | mtime `Library/ScriptAssemblies/*.dll` vs `.cs` | Пересобрать, дождаться релоада, повторить тесты |
| Console пуст после перекомпиляции | Время последней компиляции | Старые записи стёрты; сверять строки с файлом |
| Мост отвалился | Один отказ или серия | Повтор 20–40 × 3 с; Unity не перезапускать |
| `execute_code` сыпет ошибками разбора | Есть ли `using` в сниппете | Убрать, перейти на полные имена |
| «name does not exist in the current context» | Namespace типа | Полное имя (`UnityEditor.SceneManagement...`) |
| Скриншот не тот | `GetActiveScene().path`, `frameCount` | Перезапустить Play в нужной сцене |
| Эффект не виден | Итоговый кадр или промежуточный слой | Дамп промежуточной `RenderTexture` |
| Настройка не влияет | Ассет или рантайм-копия материала | Проверить `new Material(source)` и `SetFloat` |
| Превью пустое | `nearClipPlane` / `farClipPlane` | Пересчитать от дистанции |
| Кириллица в окне пустая | `EditorStyles.boldLabel` | Заменить на `fontSize` + цвет |
| `KeyNotFoundException` после перекомпиляции | `Dictionary` в `EditorWindow` | `[NonSerialized]`, пересборка в `OnEnable`, `??=` |
| Замер противоречит ожиданию | Сессия Play и активная сцена | Повторить в чистой сессии, потом искать баг |
| Тест упал после правки | Ссылается ли тест на изменённые типы | Если нет — падение было до правки |
| `Editor.log` растёт без остановки | Размер файла за 5 с | Искать цикл перерисовки из готовой DLL; обновлять плагин, не прятать ошибки |
| Удаление ассета из `execute_code` не сработало | Список блокировок | `manage_asset(action: "delete")`; обход `safety_checks: false` — только осознанно |
| Play «застыл»: `frameCount` не растёт | Фокус окна редактора | Вернуть фокус (ALT-трюк, §5); `runInBackground` не лечит |
| Play-стенд молчит | Стартовый лог; момент добавления компонента | Добавлять после стабилизации Play, а не в переходе (§6) |
| API «не существует» | Память или реальность | `unity_reflect` / XML установки, не угадывать |
| После фикса появились новые ошибки | Какие сборки были заблокированы | Продолжать итерации, это норма |

## 10. Что сюда намеренно не перенесено

- Обходы для Odin Inspector и конкретных сторонних DLL — узко для другого проекта.
- Подавление ошибок через подмену `ILogHandler` — это скрытие, а не исправление.
- 2D-физика (`RaycastNonAlloc`, `Collider2D.bounds.IntersectRay`) — пригодится только если
  проект станет 2D.
- Синтетические клавиши для Play Mode — предпочитай штатные инструменты Unity MCP; из
  Win32 в практике пригодился только возврат фокуса окну (ALT + `SetForegroundWindow`,
  §5): без фокуса Play в редакторе не тикает вообще.
