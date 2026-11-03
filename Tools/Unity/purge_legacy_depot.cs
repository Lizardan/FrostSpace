// Выключение легаси депо и легаси наполнения в сцене `Frontier`.
//
// ВНИМАНИЕ: разовая операция. Выполнена 2026-11-03 — легаси лежит в сцене
// выключенным (`Legacy_Depot`, `Legacy_Fill`), и удалить его можно только с
// подтверждения владельца (пункт приёмки в плане «Карта по слоям»). Повторный
// запуск скрипту ничего не сломает, но и ничего не изменит.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/purge_legacy_depot.cs

var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

// Хозяин — тот FrontierWorld, где уже живут FarLayer и клетки: два корня с одним
// именем в сцене были следствием прежней сборки, и оставлять их нельзя.
UnityEngine.Transform mainWorld = null;
foreach (var r0 in scene.GetRootGameObjects())
{
    if (r0.name != "FrontierWorld") continue;
    if (mainWorld == null || r0.transform.childCount > mainWorld.childCount)
        mainWorld = r0.transform;
}
if (mainWorld == null) return "нет FrontierWorld";

var legacyDepot = new UnityEngine.GameObject("Legacy_Depot");
legacyDepot.transform.SetParent(mainWorld, false);
var legacyFill = new UnityEngine.GameObject("Legacy_Fill");
legacyFill.transform.SetParent(mainWorld, false);

// 1. Легаси старого депо: авторская земля, путь с тупиком и примитивные
//    постройки двора. Не удаляются — складываются под выключенный корень:
//    разбор сцены необратим, а пока новый кадр прибытия не принят с экрана,
//    старый материал должен оставаться под рукой.
string[] depotNames = { "DepotPlatform", "PlatformEdge", "AwningRoof", "AwningEdge",
                        "AwningPost_L-1", "AwningPost_R-1", "AwningPost_L1", "AwningPost_R1",
                        "DepotGate", "DepotMast", "Frontier_Ground", "Frontier_Track" };
int moved = 0;
foreach (var n in depotNames)
{
    var go = UnityEngine.GameObject.Find(n);
    if (go == null) { sb.AppendLine("  нет объекта " + n); continue; }
    go.transform.SetParent(legacyDepot.transform, false);
    moved++;
}

// 2. Легаси наполнения: пояса леса, гряды и пропы вокруг прежнего депо. Они
//    расставлены по старым полярным поясам и по старым высотам: на новом
//    рельефе часть утонула бы или висела. Слой D разложит их по клеткам.
int filled = 0;
for (int i = mainWorld.childCount - 1; i >= 0; i--)
{
    var t = mainWorld.GetChild(i);
    string n = t.name;
    if (n.StartsWith("Frontier_Grove") || n.StartsWith("Frontier_Rocks") ||
        n.StartsWith("Frontier_Prop"))
    {
        t.SetParent(legacyFill.transform, false);
        filled++;
    }
}

// 3. Опустевший дубликат корня: после переноса земли и пути в нём не осталось
//    ничего.
int emptied = 0;
foreach (var r0 in scene.GetRootGameObjects())
{
    if (r0.name != "FrontierWorld") continue;
    if (r0.transform.childCount != 0) continue;
    UnityEngine.Object.DestroyImmediate(r0);
    emptied++;
}

// 4. Клетки включаются: резидентность сама погасит то, что вне радиуса.
UnityEngine.GameObject cells = null;
foreach (var t in mainWorld.GetComponentsInChildren<UnityEngine.Transform>(true))
    if (t.name == "WorldCells") { cells = t.gameObject; break; }
if (cells == null) return "нет WorldCells — сначала install_cells.cs";
cells.SetActive(true);
legacyDepot.SetActive(false);
legacyFill.SetActive(false);

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
sb.AppendLine(string.Format("легаси депо перенесено={0} наполнения={1} пустых корней удалено={2}",
    moved, filled, emptied));
sb.AppendLine(string.Format("WorldCells активно={0} детей={1} сохранено={2}",
    cells.activeSelf, cells.transform.childCount, saved));
sb.AppendLine("--- FrontierWorld после разбора ---");
for (int i = 0; i < mainWorld.childCount; i++)
{
    var c = mainWorld.GetChild(i);
    sb.AppendLine(string.Format("  {0} активно={1} детей={2}", c.name, c.gameObject.activeSelf, c.childCount));
}
sb.AppendLine("--- активные корни сцены ---");
foreach (var r0 in scene.GetRootGameObjects())
    sb.AppendLine(string.Format("  {0} активно={1} детей={2}", r0.name, r0.activeSelf, r0.transform.childCount));
return sb.ToString();
