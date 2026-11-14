// Замер посадки актёров трёх узлов: ступни против опоры, каждый в своей клетке.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/check_node_seating.cs
//
// Почему по клетке, а не по имени: станция держит все двадцать моделей роботов,
// поэтому `GameObject.Find("R7_Drifter")` возвращает первого тёзку — обычно
// станционного. 2026-11-12 проба внутри `place_depot_actors.cs` так показала
// «висит 1,65» у роботов депо, которые стояли ровно: мерился чужой актёр, а
// разрыв в точности равнялся сдвигу станции (−1,6482). Имя не идентифицирует
// актёра, когда моделей двадцать и они разложены по узлам.
//
// Отметки — из `Tools/Blender/reseat_marks.py` (он читает world_layout.py):
// пол депо −1,6606, поселения −0,6194, станции −6,0882. Скрипт ничего не меняет.
//
// Светильники на мачтах идут отдельным списком без вердикта: их нижний меш —
// плафон на высоте, а не ступня. Мерить плафон против земли смысла нет.
var sb = new System.Text.StringBuilder();

const string DEPOT_CELL = "WorldCell_-2_-2";
const string SETTLEMENT_CELL = "WorldCell_0_-2";
const string STATION_CELL = "WorldCell_0_0";
float RAIL_HEAD = -1.6606f;                  // головка рельса у депо
float TOL = 0.06f;                           // допуск посадки, метры

var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier (" + scene.name + ")";
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

UnityEngine.Transform cellsRoot = null;
foreach (var r0 in scene.GetRootGameObjects())
{
    foreach (var t in r0.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == "WorldCells") { cellsRoot = t; break; }
    if (cellsRoot != null) break;
}
if (cellsRoot == null) return "нет корня WorldCells";

System.Func<string, UnityEngine.Transform> cellOf = delegate (string name)
{
    for (int i = 0; i < cellsRoot.childCount; i++)
        if (cellsRoot.GetChild(i).name == name) return cellsRoot.GetChild(i);
    return null;
};

// Нижняя точка геометрии корня в мировых координатах. Для skinned берётся
// rest-поза меша: аниматор в edit-mode не тикает, и поза всё равно исходная.
System.Func<UnityEngine.Transform, float> feetOf = delegate (UnityEngine.Transform root)
{
    float feet = float.PositiveInfinity;
    foreach (var sk in root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
        if (sk.sharedMesh == null) continue;
        var b = sk.sharedMesh.bounds;
        var c = sk.transform.localToWorldMatrix.MultiplyPoint(new UnityEngine.Vector3(0f, b.min.y, 0f));
        if (c.y < feet) feet = c.y;
    }
    foreach (var mf in root.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
    {
        if (mf.sharedMesh == null) continue;
        var b = mf.sharedMesh.bounds;
        var c = mf.transform.localToWorldMatrix.MultiplyPoint(new UnityEngine.Vector3(0f, b.min.y, 0f));
        if (c.y < feet) feet = c.y;
    }
    return feet;
};

var hits = new UnityEngine.RaycastHit[64];

// Самая верхняя опора ПОД ступнёй, не считая самого актёра. RaycastNonAlloc, а
// не один луч: первый hit сверху — это кровля отсека или навес, а не пол.
// Имя опоры отдаётся наружу через захваченную переменную, а не парсится из
// строки: ToString/Parse на культуре с запятой — лишний способ ошибиться.
string supportName = "";
System.Func<UnityEngine.Transform, float, float> supportOf =
    delegate (UnityEngine.Transform root, float feet)
{
    var p = root.position;
    var origin = new UnityEngine.Vector3(p.x, feet + 0.6f, p.z);
    int count = UnityEngine.Physics.RaycastNonAlloc(
        new UnityEngine.Ray(origin, UnityEngine.Vector3.down), hits, 8f);
    float best = float.NegativeInfinity;
    supportName = "";
    for (int i = 0; i < count; i++)
    {
        var t = hits[i].collider.transform;
        if (t == root || t.IsChildOf(root)) continue;
        float y = origin.y - hits[i].distance;
        if (y > best) { best = y; supportName = hits[i].collider.gameObject.name; }
    }
    return best;
};

System.Func<string, bool> isFixture = delegate (string n)
{
    return n.Contains("Lamp") || n.Contains("Light") || n.Contains("Spill")
        || n.Contains("Fill") || n.Contains("Snowfall") || n.Contains("Mast");
};

int hanging = 0, sinking = 0, level = 0, noSupport = 0;

System.Action<string, string, string> measureCell = delegate (string cellName, string groupName, string title)
{
    sb.AppendLine("=== " + title + " (" + cellName + ") ===");
    var cell = cellOf(cellName);
    if (cell == null) { sb.AppendLine("  нет клетки в сцене"); return; }

    UnityEngine.Transform scope = cell;
    if (!string.IsNullOrEmpty(groupName))
    {
        var g = cell.Find(groupName);
        if (g == null) { sb.AppendLine("  нет группы " + groupName + " — содержимое не долетело до сцены"); return; }
        scope = g;
    }

    var lamps = new System.Text.StringBuilder();
    for (int i = 0; i < scope.childCount; i++)
    {
        var actor = scope.GetChild(i);
        if (actor.GetComponent<FrostSpace.Features.Map.CellMesh>() != null) continue;
        float feet = feetOf(actor);
        if (float.IsPositiveInfinity(feet)) continue;      // свет и объёмы мерить нечем
        if (actor.name == "AN_Eagle")
        {
            lamps.AppendLine("  AN_Eagle            кружит над двором — опоры нет по замыслу");
            continue;
        }
        if (actor.name == "DepotTrain") continue;   // мерится отдельно: колесо над рельсом
        if (isFixture(actor.name))
        {
            lamps.AppendLine(string.Format("  {0,-20} низ меша {1:0.000} — светильник/вышка, не ступня", actor.name, feet));
            continue;
        }
        float best = supportOf(actor, feet);
        if (float.IsNegativeInfinity(best))
        {
            noSupport++;
            sb.AppendLine(string.Format("  {0,-20} низ {1:0.000} — ОПОРЫ НЕТ", actor.name, feet));
            continue;
        }
        float gap = feet - best;
        if (actor.name == "MenuWreckEngine")
        {
            // place_station_content сажает агрегат на 0,20 м ниже пола: он лежит
            // в снегу и обломках. Это замысел, а не дефект посадки.
            lamps.AppendLine(string.Format("  {0,-20} низ {1:0.000}  опора {2:0.000} — утоплен на 0,20 намеренно",
                actor.name, feet, best));
            level++;
            continue;
        }
        string verdict;
        if (gap > TOL) { verdict = "ВИСИТ " + gap.ToString("0.000"); hanging++; }
        else if (gap < -TOL) { verdict = "ТОНЕТ " + (-gap).ToString("0.000"); sinking++; }
        else { verdict = "вровень"; level++; }
        sb.AppendLine(string.Format("  {0,-20} низ {1:0.000}  опора {2:0.000} ({3})  {4}",
            actor.name, feet, best, supportName, verdict));
    }
    if (lamps.Length > 0) sb.Append(lamps.ToString());
};

measureCell(DEPOT_CELL, null, "ДЕПО");
measureCell(SETTLEMENT_CELL, "Settlement", "ПОСЕЛЕНИЕ");
measureCell(STATION_CELL, "Station", "СТАНЦИЯ");

// Состав: низ КОЛЕСА против головки рельса. Мерить низ всего меша лучом нельзя:
// луч из корня вагона идёт между рельсами и попадает в шпалу (−1,861), а самая
// низкая точка кузова лежит не над рельсом. Отсюда было ложное «висит 0,150».
// Эталон — frontier_layout.py: колея 5.90, рельсы в x = -10.95 и -5.05, низ
// колеса = головка. Колесо — десятигранник (`build_train._bogies`, n=10), поэтому
// его низшая вершина на 1,7 см выше идеальной окружности: это огранка, не посадка.
sb.AppendLine("=== СОСТАВ: колесо против головки рельса " + RAIL_HEAD.ToString("0.0000") + " ===");
var depotCell = cellOf(DEPOT_CELL);
var train = depotCell != null ? depotCell.Find("DepotTrain") : null;
if (train == null) sb.AppendLine("  нет DepotTrain в клетке депо");
else
{
    float[] railX = { -10.95f, -5.05f };
    UnityEngine.MeshFilter body = null;
    foreach (var mf in train.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        if (mf.name == "TrainBody") body = mf;
    if (body == null || body.sharedMesh == null) sb.AppendLine("  нет меша TrainBody");
    else
    {
        var verts = body.sharedMesh.vertices;
        var m = body.transform.localToWorldMatrix;
        float lowWheel = float.PositiveInfinity, lowAll = float.PositiveInfinity;
        UnityEngine.Vector3 wheelAt = UnityEngine.Vector3.zero, allAt = UnityEngine.Vector3.zero;
        for (int v = 0; v < verts.Length; v++)
        {
            var w = m.MultiplyPoint(verts[v]);
            if (w.y < lowAll) { lowAll = w.y; allAt = w; }
            foreach (var rx in railX)
                if (UnityEngine.Mathf.Abs(w.x - rx) < 0.45f)
                {
                    if (w.y < lowWheel) { lowWheel = w.y; wheelAt = w; }
                    break;
                }
        }
        float d = lowWheel - RAIL_HEAD;
        string verdict = UnityEngine.Mathf.Abs(d) <= TOL ? "вровень"
            : (d > 0f ? "ВИСИТ " + d.ToString("0.000") : "ТОНЕТ " + (-d).ToString("0.000"));
        sb.AppendLine(string.Format("  колесо (x={0:0.00}, z={1:0.00}) низ {2:0.0000}  к головке {3}  {4}",
            wheelAt.x, wheelAt.z, lowWheel, (d >= 0f ? "+" : "") + d.ToString("0.0000"), verdict));
        sb.AppendLine(string.Format("  низ всего меша {0:0.0000} в (x={1:0.00}, z={2:0.00}) — не колесо, между рельсами",
            lowAll, allAt.x, allAt.z));
        if (UnityEngine.Mathf.Abs(d) > TOL) hanging++; else level++;
    }
}

// Префаб против сцены: скрипты наполнения пересоздают группу, а сцена могла
// остаться с прежним набором или с осиротевшими overrides.
sb.AppendLine("=== ГРУППЫ: префаб против сцены ===");
System.Action<string, string> compareGroup = delegate (string cellName, string groupName)
{
    var cell = cellOf(cellName);
    var path = "Assets/_Project/Content/Prefabs/World/" + cellName + ".prefab";
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (cell == null || prefab == null) { sb.AppendLine("  " + cellName + ": нет клетки или префаба"); return; }
    var inScene = cell.Find(groupName);
    var inPrefab = prefab.transform.Find(groupName);
    if (inScene == null || inPrefab == null)
    {
        sb.AppendLine("  " + cellName + "/" + groupName + ": в сцене=" + (inScene != null) + " в префабе=" + (inPrefab != null));
        return;
    }
    var sceneNames = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < inScene.childCount; i++) sceneNames.Add(inScene.GetChild(i).name);
    var prefabNames = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < inPrefab.childCount; i++) prefabNames.Add(inPrefab.GetChild(i).name);
    int missing = 0, extra = 0;
    var diff = new System.Text.StringBuilder();
    foreach (var n in prefabNames) if (!sceneNames.Contains(n)) { missing++; diff.AppendLine("    нет в сцене: " + n); }
    foreach (var n in sceneNames) if (!prefabNames.Contains(n)) { extra++; diff.AppendLine("    лишнее в сцене: " + n); }
    int added = UnityEditor.PrefabUtility.GetAddedGameObjects(cell.gameObject).Count;
    int removed = UnityEditor.PrefabUtility.GetRemovedGameObjects(cell.gameObject).Count;
    sb.AppendLine(string.Format("  {0}/{1}: детей сцена={2} префаб={3} потеряно={4} лишних={5} добавлено в сцене={6} удалено={7}",
        cellName, groupName, inScene.childCount, inPrefab.childCount, missing, extra, added, removed));
    if (diff.Length > 0) sb.Append(diff.ToString());
};
compareGroup(SETTLEMENT_CELL, "Settlement");
compareGroup(STATION_CELL, "Station");

sb.AppendLine(string.Format("ИТОГ: вровень={0} висит={1} тонет={2} без опоры={3}", level, hanging, sinking, noSupport));
return sb.ToString();