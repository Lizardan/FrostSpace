// Разовая уборка: в префабе клетки (−2,−2) остался объект «Depot_-2_-2».
//
// До ADR 0038 двор депо был одним мешем, и он лежал в префабе под этим именем.
// Теперь двор разбит на десять частей (`DepotApron_`, `DepotPlatform_`, …), и
// `sync_cell_prefabs.cs` добавил их, но старое имя не тронул: синк по имени
// только обновляет и добавляет, а удаляет — человек. Этот файл и есть точечное
// удаление одного имени: пересобирать префаб с нуля нечем — `rebuild_cell_prefabs.cs`
// удалён (ADR 0043 п.4).
//
// Защита: удаляется только объект без детей. Если у него появятся дети, значит
// под этим именем лежит авторское — скрипт откажется и скажет об этом.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/purge_stale_depot_mesh.cs

var sb = new System.Text.StringBuilder();
const string path = "Assets/_Project/Content/Prefabs/World/WorldCell_-2_-2.prefab";
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path) == null)
    return "нет префаба: " + path;

var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
if (root == null) return "префаб не открылся: " + path;

var stale = new System.Collections.Generic.List<UnityEngine.GameObject>();
foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
{
    if (t == root.transform || t.name != "Depot_-2_-2") continue;
    if (t.childCount > 0)
    {
        sb.AppendLine("ОТКАЗ: у «Depot_-2_-2» есть дети (" + t.childCount + ") — это не след конвейера");
        continue;
    }
    stale.Add(t.gameObject);
}

foreach (var go in stale) UnityEngine.Object.DestroyImmediate(go);
if (stale.Count > 0) UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);

sb.AppendLine("удалено объектов: " + stale.Count + " (" + path + ")");
return sb.ToString();
