// Разовая уборка устаревших CellMesh-объектов в префабах клеток.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/purge_stale_cell_meshes.cs
//
// Причина: пока клетка выгружалась одним FBX, у пустошей меш земли лежал на
// корне, и старая синхронизация завела под него дубликат-ребёнка с именем
// клетки. Теперь земля приходит частью `Ground_<i>_<j>`, а поселение —
// `Hut_*_<i>_<j>`, и старые имена (`WorldCell_<i>_<j>`, `Hut_Chimney`) остались
// в префабе сиротами: они держат меш, которого больше нет ни в одной части.
//
// Что делает: удаляет **только** объекты с меткой `CellMesh`, которых нет среди
// частей клетки, и только если у них нет детей. Актёры, свет, слоты и всё
// авторское метки не имеют и не трогаются.
//
// Синхронизация (`sync_cell_prefabs.cs`) этого не делает намеренно: метка может
// стоять на ручной правке. Здесь удаление точечное и разовое.

var sb = new System.Text.StringBuilder();
const string partsDir = "Assets/_Project/Content/Models/Level/Cells";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";
var partName = new System.Text.RegularExpressions.Regex(@"_(-?\d+)_(-?\d+)$");

if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

var byCell = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { partsDir }))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var mt = partName.Match(name);
    if (!mt.Success) continue;
    var key = mt.Groups[1].Value + "_" + mt.Groups[2].Value;
    System.Collections.Generic.HashSet<string> set;
    if (!byCell.TryGetValue(key, out set))
    {
        set = new System.Collections.Generic.HashSet<string>();
        byCell[key] = set;
    }
    set.Add(name);
}

int removed = 0;
int kept = 0;
var perCell = new System.Text.StringBuilder();

foreach (var pair in byCell)
{
    var prefabPath = prefabDir + "/WorldCell_" + pair.Key + ".prefab";
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath) == null) continue;

    var contents = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
    if (contents == null) continue;

    var doomed = new System.Collections.Generic.List<UnityEngine.GameObject>();
    foreach (var mark in contents.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        if (mark.transform == contents.transform) continue;
        if (pair.Value.Contains(mark.name)) continue;
        if (mark.transform.childCount > 0)
        {
            kept++;
            sb.AppendLine("оставлен (есть дети): " + contents.name + " / " + mark.name);
            continue;
        }
        doomed.Add(mark.gameObject);
    }

    foreach (var go in doomed) UnityEngine.Object.DestroyImmediate(go);
    if (doomed.Count > 0)
    {
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        removed += doomed.Count;
        perCell.Append(contents.name + ": " + doomed.Count + " ");
    }
    UnityEditor.PrefabUtility.UnloadPrefabContents(contents);
}

UnityEditor.AssetDatabase.SaveAssets();
sb.AppendLine("удалено устаревших объектов: " + removed + ", оставлено с детьми: " + kept);
if (perCell.Length > 0) sb.AppendLine(perCell.ToString().Trim());
return sb.ToString();
