// Удаление легаси депо и легаси наполнения из сцены `Frontier`.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/delete_legacy_depot.cs
//
// Решение владельца 2026-11-12: «Удалить». Группы лежат выключенными с
// 2026-11-03, рантайм на них не ссылается, новый кадр прибытия принят.
//
// Почему не `purge_legacy_depot.cs`: тот скрипт легаси СОЗДАЛ — перенёс
// двенадцать объектов депо и тридцать семь объектов наполнения под выключенные
// корни. Его повторный запуск не удаляет, а заводит два новых пустых корня с
// теми же именами: `GameObject.Find` не видит выключенное и докладывает «нет
// объекта». Удаление — отдельная операция и отдельный скрипт.
//
// Перед удалением: копия сцены в `Temp/backup/` и поиск ссылок из рантайм-
// компонентов в поддеревья легаси. Найденная ссылка — останов, а не удаление.
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

UnityEngine.Transform mainWorld = null;
foreach (var r0 in scene.GetRootGameObjects())
{
    if (r0.name != "FrontierWorld") continue;
    if (mainWorld == null || r0.transform.childCount > mainWorld.childCount)
        mainWorld = r0.transform;
}
if (mainWorld == null) return "нет FrontierWorld";

string[] legacyNames = { "Legacy_Depot", "Legacy_Fill" };
var roots = new System.Collections.Generic.List<UnityEngine.Transform>();
foreach (var n in legacyNames)
{
    UnityEngine.Transform found = null;
    for (int i = 0; i < mainWorld.childCount; i++)
        if (mainWorld.GetChild(i).name == n) { found = mainWorld.GetChild(i); break; }
    if (found == null) { sb.AppendLine("нет корня " + n + " — уже удалён?"); continue; }
    roots.Add(found);
    sb.AppendLine(string.Format("{0}: детей={1} активно={2}", n, found.childCount, found.gameObject.activeSelf));
}
if (roots.Count == 0) return sb + "\nудалять нечего";

// Поддеревья легаси целиком: ссылка на любого потомка тоже держит группу.
var inside = new System.Collections.Generic.HashSet<UnityEngine.Transform>();
foreach (var r in roots)
    foreach (var t in r.GetComponentsInChildren<UnityEngine.Transform>(true))
        inside.Add(t);
sb.AppendLine("объектов под легаси: " + inside.Count);

// Ссылки ищем только у рантайм-компонентов проекта: геометрия и пропы на
// других актёров ссылаться не могут, а обход всей сцены по SerializedObject
// на 121 клетке неоправданно дорог.
int refs = 0;
var refReport = new System.Text.StringBuilder();
foreach (var comp in UnityEngine.Object.FindObjectsByType<UnityEngine.Component>(FindObjectsSortMode.None))
{
    if (comp == null) continue;
    var ns = comp.GetType().Namespace;
    if (ns == null || !ns.StartsWith("FrostSpace")) continue;
    if (inside.Contains(comp.transform)) continue;
    var so = new UnityEditor.SerializedObject(comp);
    var it = so.GetIterator();
    bool enterChildren = true;
    while (it.NextVisible(enterChildren))
    {
        enterChildren = false;
        if (it.propertyType != UnityEditor.SerializedPropertyType.ObjectReference) continue;
        var target = it.objectReferenceValue as UnityEngine.Component;
        var go = it.objectReferenceValue as UnityEngine.GameObject;
        var tr = target != null ? target.transform : (go != null ? go.transform : null);
        if (tr == null || !inside.Contains(tr)) continue;
        refs++;
        refReport.AppendLine("  ССЫЛКА " + comp.GetType().Name + " на " + comp.gameObject.name +
                             " поле " + it.propertyPath + " -> " + tr.name);
    }
}
if (refs > 0) return sb + "\n" + refReport + "\nОСТАНОВ: ссылок " + refs + " — сначала убрать их";
sb.AppendLine("ссылок из рантайма в легаси: 0");

// Копия сцены до удаления. Сцену сначала сохраняем: иначе в копию не попадут
// несохранённые правки, и откатывать будет нечего.
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
var backupDir = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp/backup");
System.IO.Directory.CreateDirectory(backupDir);
var backup = System.IO.Path.Combine(backupDir, "Frontier_before_legacy_delete_2026-11-12.unity");
System.IO.File.Copy(scene.path, backup, true);
sb.AppendLine("копия сцены: " + backup);

foreach (var r in roots) UnityEngine.Object.DestroyImmediate(r.gameObject);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
sb.AppendLine("удалено корней: " + roots.Count + ", сохранено=" + saved);

// Депо обязано пережить удаление: актёры живут в клетке, а не в легаси.
sb.AppendLine("--- FrontierWorld после удаления ---");
for (int i = 0; i < mainWorld.childCount; i++)
{
    var c = mainWorld.GetChild(i);
    sb.AppendLine(string.Format("  {0} активно={1} детей={2}", c.name, c.gameObject.activeSelf, c.childCount));
}
UnityEngine.Transform depot = null;
for (int i = 0; i < mainWorld.childCount; i++)
{
    var cells = mainWorld.GetChild(i);
    if (cells.name != "WorldCells") continue;
    for (int k = 0; k < cells.childCount; k++)
        if (cells.GetChild(k).name == "WorldCell_-2_-2") depot = cells.GetChild(k);
}
if (depot == null) sb.AppendLine("НЕТ клетки депо — удаление задело содержимое");
else
{
    int actors = 0;
    for (int i = 0; i < depot.childCount; i++)
        if (depot.GetChild(i).GetComponent<FrostSpace.Features.Map.CellMesh>() == null) actors++;
    sb.AppendLine("клетка депо: детей=" + depot.childCount + " не-геометрии=" + actors);
}
return sb.ToString();