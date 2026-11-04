// Установка 121 инстанса клеток в сцену `Frontier`.
//
// ВНИМАНИЕ: скрипт РАЗРУШАЮЩИЙ — он удаляет всех детей корня `WorldCells` и
// создаёт их заново. Запускать только когда сцену действительно надо собрать
// с нуля (пустой корень, чужая раскладка инстансов). Обычная правка мира
// делается без него: префабы клеток пересобираются отдельно.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/install_cell_instances.cs
//
// Пересборку самих префабов из частей FBX делает `sync_cell_prefabs.cs` — она
// безопасна и не трогает сцену. Проверка «префаб ↔ FBX ↔ сцена» —
// `verify_cell_prefabs.cs`.
//
// История: выполнен 2026-11-03 при переходе на чанки; тогда же сцена была
// пересобрана и проверена — 121 инстанс, 121 префаб, индексы клеток и
// позиции совпали с раскладкой.

var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

// WorldCells выключен, а `GameObject.Find` невидимого не вернёт: ищем по
// всем корням сцены вместе с неактивными детьми.
UnityEngine.GameObject root = null;
foreach (var r0 in scene.GetRootGameObjects())
{
    foreach (var t in r0.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == "WorldCells") { root = t.gameObject; break; }
    if (root != null) break;
}
if (root == null) return "нет корня WorldCells — сначала стадия разбора сцены";
var player = UnityEngine.GameObject.Find("Player");
if (player == null) return "нет Player";

// Старые инстансы клеток (эпоха «одна сетка на клетку») удаляются: меши в
// префабе теперь другие, и смешивать два поколения в одном корне значит
// получить два слоя земли на одной клетке.
int removed = 0;
for (int i = root.transform.childCount - 1; i >= 0; i--)
{
    var c = root.transform.GetChild(i);
    UnityEngine.Object.DestroyImmediate(c.gameObject);
    removed++;
}

string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Model WorldCell_",
    new string[] { "Assets/_Project/Content/Models/Level" });
if (guids.Length != 121)
    sb.AppendLine("ВНИМАНИЕ: клеток в проекте " + guids.Length + ", ожидалось 121");

int made = 0, colliders = 0, bad = 0;
for (int g = 0; g < guids.Length; g++)
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (model == null) { bad++; continue; }

    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var parts = name.Split('_');
    int ci, cj;
    if (parts.Length != 3 || !int.TryParse(parts[1], out ci) || !int.TryParse(parts[2], out cj))
    { bad++; continue; }

    var prefabPath = "Assets/_Project/Content/Prefabs/World/" + name + ".prefab";
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    // Префаб пересобирается всегда, а не «если чего-то не хватает»: FBX клетки
    // теперь несёт несколько мешей (земля, путь, шпалы, дороги, двор), и у
    // префаба прошлого поколения этих детей нет вовсе — он молча показывал бы
    // только землю.
    {
        var temp = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(model);
        temp.name = name;
        var cell = temp.GetComponent<FrostSpace.Features.Map.WorldCell>();
        if (cell == null) cell = temp.AddComponent<FrostSpace.Features.Map.WorldCell>();
        cell.Setup(new UnityEngine.Vector2Int(ci, cj));
        // Коллайдер на каждый меш клетки: земля, путь, шпалы, дороги, двор.
        // Ходовая часть игрока должна идти по рельсу и упираться в стену
        // коробки, а не проходить сквозь них, как сквозь декорацию.
        foreach (var mf in temp.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var go = mf.gameObject;
            if (go.GetComponent<UnityEngine.MeshCollider>() == null)
                go.AddComponent<UnityEngine.MeshCollider>();
            colliders++;
        }
        prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
        UnityEngine.Object.DestroyImmediate(temp);
        made++;
    }

    var inst = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
    inst.transform.SetParent(root.transform, false);
    // Смещение сцены: депо (-2,-2) стоит в нуле, поэтому +2*BLK.
    inst.transform.localPosition = new UnityEngine.Vector3(ci * 400f + 800f, 0f, cj * 400f + 800f);
    inst.transform.localRotation = UnityEngine.Quaternion.identity;
    inst.transform.localScale = UnityEngine.Vector3.one;
}

var res = root.GetComponent<FrostSpace.Features.Map.WorldResidency>();
if (res == null) res = root.AddComponent<FrostSpace.Features.Map.WorldResidency>();
var so = new UnityEditor.SerializedObject(res);
so.FindProperty("_layout").objectReferenceValue =
    UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
        "Assets/_Project/Content/Data/World/world_layout.json");
so.FindProperty("_player").objectReferenceValue = player.transform;
so.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
int tris = 0;
foreach (var mf in root.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
{
    if (mf.sharedMesh == null) continue;
    tris += mf.sharedMesh.triangles.Length / 3;
}
sb.AppendLine(string.Format("удалено старых инстансов={0} префабов пересобрано={1} битых={2}",
    removed, made, bad));
sb.AppendLine(string.Format("детей у WorldCells={0} коллайдеров поставлено={1} трис в клетках={2} сохранено={3}",
    root.transform.childCount, colliders, tris, saved));
return sb.ToString();
