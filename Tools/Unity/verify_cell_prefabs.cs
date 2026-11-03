// Проверка, что префабы клеток соответствуют частям FBX, а сцена — префабам.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/verify_cell_prefabs.cs
//
// Клетка — префаб, собранный из частей (`Models/Level/Cells/<Часть>_<i>_<j>.fbx`).
// Между сборкой частей и синхронизацией префаба проходит произвольное время, и
// расхождение молчаливое: часть переименована — объект в префабе повисает;
// часть добавлена — в префабе её нет. 2026-11-03 так потерялись 15 частей
// поселения и по одной дороге в двух клетках.
//
// Проверка ничего не меняет: только читает части, префабы и сцену.
//
// Отдельно ловится возврат файла-чанка: `WorldCell_<i>_<j>.fbx` в корне
// `Models/Level/` — это старая архитектура, где дороги лежали внутри одного
// файла, и она не должна вернуться (ADR 0039).

var sb = new System.Text.StringBuilder();

const string modelDir = "Assets/_Project/Content/Models/Level";
const string partsDir = modelDir + "/Cells";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";
var partName = new System.Text.RegularExpressions.Regex(@"_(-?\d+)_(-?\d+)$");

if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

// Старый файл-чанк не должен вернуться: дороги внутри одного FBX.
var oldChunks = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { modelDir });
int chunkFiles = 0;
var chunkList = new System.Text.StringBuilder();
foreach (var guid in oldChunks)
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != modelDir) continue;
    var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
    if (!baseName.StartsWith("WorldCell_")) continue;
    chunkFiles++;
    chunkList.AppendLine("файл-чанк вернулся: " + path);
}

var byCell = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<UnityEngine.GameObject>>();
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { partsDir }))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var mt = partName.Match(name);
    if (!mt.Success)
    {
        chunkList.AppendLine(name + ": часть без суффикса клетки — не попадёт в префаб");
        continue;
    }
    var key = mt.Groups[1].Value + "_" + mt.Groups[2].Value;
    System.Collections.Generic.List<UnityEngine.GameObject> list;
    if (!byCell.TryGetValue(key, out list))
    {
        list = new System.Collections.Generic.List<UnityEngine.GameObject>();
        byCell[key] = list;
    }
    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (model != null) list.Add(model);
}

int nullMeshes = 0, missingFromPrefab = 0, extraInPrefab = 0, cellsBroken = 0;
int nullColliders = 0, noWorldCell = 0, prefabTris = 0, partsTris = 0;
int cellMeshes = 0, prefabMeshes = 0, authoredMeshes = 0;
var broken = new System.Text.StringBuilder();

foreach (var pair in byCell)
{
    var names = pair.Key.Split('_');
    int ci = int.Parse(names[0]), cj = int.Parse(names[1]);
    var cellName = "WorldCell_" + pair.Key;
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabDir + "/" + cellName + ".prefab");
    if (prefab == null)
    {
        broken.AppendLine(cellName + ": нет префаба");
        cellsBroken++;
        continue;
    }

    var partNames = new System.Collections.Generic.HashSet<string>();
    foreach (var model in pair.Value)
    {
        var mf = model.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { nullMeshes++; continue; }
        partNames.Add(model.name);
        partsTris += mf.sharedMesh.triangles.Length / 3;
        cellMeshes++;
    }

    var prefabNames = new System.Collections.Generic.HashSet<string>();
    int localNull = 0, localNullColliders = 0;
    var cellFilters = new System.Collections.Generic.HashSet<UnityEngine.MeshFilter>();
    foreach (var cm in prefab.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        var mf = cm.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { localNull++; continue; }
        cellFilters.Add(mf);
        prefabNames.Add(cm.name);
        prefabTris += mf.sharedMesh.triangles.Length / 3;
        prefabMeshes++;
        var mc = cm.GetComponent<UnityEngine.MeshCollider>();
        if (mc == null || mc.sharedMesh == null) localNullColliders++;
    }

    // Авторские меши (актёры, добавленные руками объекты) в сверку с частями не
    // входят: префаб клетки — владелец содержимого, а не только геометрии.
    foreach (var mf in prefab.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        if (!cellFilters.Contains(mf) && mf.sharedMesh != null) authoredMeshes++;

    int missing = 0;
    foreach (var n in partNames) if (!prefabNames.Contains(n)) missing++;
    int extra = 0;
    foreach (var n in prefabNames) if (!partNames.Contains(n)) extra++;

    var wc = prefab.GetComponent<FrostSpace.Features.Map.WorldCell>();
    bool indexOk = wc != null && wc.Cell.x == ci && wc.Cell.y == cj;
    if (wc == null) noWorldCell++;

    nullMeshes += localNull;
    missingFromPrefab += missing;
    extraInPrefab += extra;
    nullColliders += localNullColliders;

    if (localNull > 0 || missing > 0 || extra > 0 || localNullColliders > 0 || !indexOk)
    {
        cellsBroken++;
        broken.AppendLine(string.Format("{0}: пустых мешей={1} потеряно из частей={2} лишних={3} коллайдеров без меша={4} индекс клетки={5}",
            cellName, localNull, missing, extra, localNullColliders, indexOk ? "ок" : "НЕВЕРЕН"));
    }
}

sb.AppendLine("частей FBX: " + (cellMeshes + nullMeshes) + ", клеток: " + byCell.Count + " (ожидалось 121)");
if (chunkFiles > 0) sb.AppendLine(chunkList.ToString());
sb.AppendLine(string.Format("префабы: мешей={0} трис={1} | части: мешей={2} трис={3}",
    prefabMeshes, prefabTris, cellMeshes, partsTris));
sb.AppendLine("авторских мешей в префабах (актёры и добавленное): " + authoredMeshes + " — в сверку с частями не входят");
sb.AppendLine(string.Format("пустых мешей={0} потеряно из частей={1} лишних в префабе={2} коллайдеров без меша={3} без WorldCell={4}",
    nullMeshes, missingFromPrefab, extraInPrefab, nullColliders, noWorldCell));
sb.AppendLine("битых клеток: " + cellsBroken);
if (cellsBroken > 0) sb.AppendLine(broken.ToString());

// --- сцена ---
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier")
{
    sb.AppendLine("активна не Frontier (" + scene.name + ") — сцену не проверял");
    return sb.ToString();
}

UnityEngine.Transform cellsRoot = null;
foreach (var r0 in scene.GetRootGameObjects())
{
    foreach (var t in r0.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == "WorldCells") { cellsRoot = t; break; }
    if (cellsRoot != null) break;
}
if (cellsRoot == null)
{
    sb.AppendLine("нет корня WorldCells — сцену не проверял");
    return sb.ToString();
}

int instances = cellsRoot.childCount;
int sceneBroken = 0;
int sceneTris = 0;
var sceneReport = new System.Text.StringBuilder();

for (int i = 0; i < cellsRoot.childCount; i++)
{
    var inst = cellsRoot.GetChild(i);
    var expected = inst.name;
    var parts = expected.Split('_');
    int ci, cj;
    bool posOk = false;
    if (parts.Length == 3 && int.TryParse(parts[1], out ci) && int.TryParse(parts[2], out cj))
        posOk = Mathf.Abs(inst.localPosition.x - (ci * 600f + 1200f)) < 0.01f
             && Mathf.Abs(inst.localPosition.z - (cj * 600f + 1200f)) < 0.01f;

    var wc = inst.GetComponent<FrostSpace.Features.Map.WorldCell>();
    bool indexOk = wc != null;

    var source = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inst.gameObject);
    string sourceName = source != null ? System.IO.Path.GetFileNameWithoutExtension(source) : "<не префаб>";

    int instMeshes = 0;
    foreach (var cm in inst.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        var mf = cm.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null || mf.sharedMesh == null) continue;
        instMeshes++;
        sceneTris += mf.sharedMesh.triangles.Length / 3;
    }

    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabDir + "/" + expected + ".prefab");
    int prefabMeshCount = 0;
    if (prefab != null)
        foreach (var cm in prefab.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
        {
            var mf = cm.GetComponent<UnityEngine.MeshFilter>();
            if (mf != null && mf.sharedMesh != null) prefabMeshCount++;
        }

    if (!posOk || !indexOk || sourceName != expected || instMeshes != prefabMeshCount)
    {
        sceneBroken++;
        sceneReport.AppendLine(string.Format("{0}: позиция={1} индекс={2} источник={3} мешей в сцене={4} в префабе={5}",
            expected, posOk ? "ок" : "НЕВЕРНА", indexOk ? "ок" : "НЕТ", sourceName, instMeshes, prefabMeshCount));
    }
}

sb.AppendLine("сцена: инстансов=" + instances + " трис=" + sceneTris + " расхождений=" + sceneBroken);
if (sceneBroken > 0) sb.AppendLine(sceneReport.ToString());

bool allGood = cellsBroken == 0 && sceneBroken == 0 && nullMeshes == 0 && missingFromPrefab == 0
            && extraInPrefab == 0 && nullColliders == 0 && noWorldCell == 0 && chunkFiles == 0
            && byCell.Count == 121 && instances == 121 && prefabTris == partsTris;
sb.AppendLine(allGood ? "ИТОГ: клетки и сцена сходятся" : "ИТОГ: есть расхождения");
return sb.ToString();
