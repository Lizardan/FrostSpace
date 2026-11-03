// Проверка, что префабы клеток соответствуют FBX, а сцена — префабам.
//
// Зачем отдельный шаг: префаб клетки — производная от FBX, который собирает
// `Tools/Blender/build_world_cells.py`. Между сборкой FBX и пересборкой
// префаба проходит произвольное время, и расхождение молчаливое: меш в FBX
// переименован — ссылка в префабе повисает и меш исчезает из кадра; меш
// добавлен — в префабе его нет. 2026-11-03 так потерялись 15 мешей поселения
// в клетке (0,-2) и по одной дороге в клетках (0,-1) и (-1,-1).
//
// Проверка ничего не меняет: только читает префабы, FBX и сцену.
//
// Запуск:
//     python Tools/Unity/run_snippet.py Tools/Unity/verify_cell_prefabs.cs

var sb = new System.Text.StringBuilder();

const string modelDir = "Assets/_Project/Content/Models/Level";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";

var guids = UnityEditor.AssetDatabase.FindAssets("t:Model WorldCell_", new string[] { modelDir });
sb.AppendLine("FBX клеток: " + guids.Length + " (ожидалось 121)");

int nullMeshes = 0;
int missingFromPrefab = 0;
int extraInPrefab = 0;
int cellsBroken = 0;
int nullColliders = 0;
int noWorldCell = 0;
int prefabTris = 0;
int fbxTris = 0;
int cellMeshes = 0;
int prefabMeshes = 0;
int authoredMeshes = 0;
var broken = new System.Text.StringBuilder();

for (int g = 0; g < guids.Length; g++)
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var parts = name.Split('_');
    int ci, cj;
    if (parts.Length != 3 || !int.TryParse(parts[1], out ci) || !int.TryParse(parts[2], out cj))
    {
        broken.AppendLine(name + ": имя не разбирается как WorldCell_<x>_<z>");
        cellsBroken++;
        continue;
    }

    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabDir + "/" + name + ".prefab");
    if (model == null || prefab == null)
    {
        broken.AppendLine(name + ": нет FBX или префаба");
        cellsBroken++;
        continue;
    }

    var fbxNames = new System.Collections.Generic.HashSet<string>();
    foreach (var mf in model.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
    {
        if (mf.sharedMesh == null) continue;
        fbxNames.Add(mf.sharedMesh.name);
        fbxTris += mf.sharedMesh.triangles.Length / 3;
        cellMeshes++;
    }

    var prefabNames = new System.Collections.Generic.HashSet<string>();
    int localNull = 0;
    var cellFilters = new System.Collections.Generic.HashSet<UnityEngine.MeshFilter>();
    foreach (var cm in prefab.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        var mf = cm.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null) { localNull++; continue; }
        cellFilters.Add(mf);
        if (mf.sharedMesh == null) { localNull++; continue; }
        prefabNames.Add(mf.sharedMesh.name);
        prefabTris += mf.sharedMesh.triangles.Length / 3;
        prefabMeshes++;
    }

    // Авторские меши (актёры, добавленные руками объекты) в сверку с FBX не
    // входят: префаб клетки — владелец содержимого, а не только геометрии.
    foreach (var mf in prefab.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        if (!cellFilters.Contains(mf) && mf.sharedMesh != null) authoredMeshes++;

    int missing = 0;
    foreach (var n in fbxNames) if (!prefabNames.Contains(n)) missing++;
    int extra = 0;
    foreach (var n in prefabNames) if (!fbxNames.Contains(n)) extra++;

    int localNullColliders = 0;
    foreach (var cm in prefab.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        var mc = cm.GetComponent<UnityEngine.MeshCollider>();
        if (mc == null || mc.sharedMesh == null) localNullColliders++;
    }

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
        broken.AppendLine(string.Format("{0}: пустых мешей={1} потеряно из FBX={2} лишних={3} коллайдеров без меша={4} индекс клетки={5}",
            name, localNull, missing, extra, localNullColliders, indexOk ? "ок" : "НЕВЕРЕН"));
    }
}

sb.AppendLine(string.Format("префабы: мешей={0} трис={1} | FBX: мешей={2} трис={3}",
    prefabMeshes, prefabTris, cellMeshes, fbxTris));
sb.AppendLine(string.Format("авторских мешей в префабах (актёры и добавленное): {0} — в сверку с FBX не входят", authoredMeshes));
sb.AppendLine(string.Format("пустых мешей={0} потеряно из FBX={1} лишних в префабе={2} коллайдеров без меша={3} без WorldCell={4}",
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
            && extraInPrefab == 0 && nullColliders == 0 && noWorldCell == 0
            && instances == 121 && prefabTris == fbxTris;
sb.AppendLine(allGood ? "ИТОГ: клетки и сцена сходятся" : "ИТОГ: есть расхождения");
return sb.ToString();
