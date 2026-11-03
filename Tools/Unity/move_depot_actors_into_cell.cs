// Перенос актёров депо из сцены в префаб клетки (−2,−2).
//
//   python Tools/Unity/run_snippet.py Tools/Unity/move_depot_actors_into_cell.cs
//
// Идемпотентен: если актёр уже часть инстанса клетки, он пропускается.
//
// Что делает:
//   1. отцепляет игрока от вагона — игрок сценовый и в префаб попасть не должен;
//   2. снимает временный держатель (`ChunkSuspension`, `SuspendedActor`, корень
//      `SuspendedActors`): после переезда актёров гасит сама резидентность;
//   3. вкладывает актёров в инстанс `WorldCell_-2_-2`;
//   4. `ApplyPrefabInstance` переносит их в ассет, не пересоздавая объекты —
//      ссылки директора прибытия остаются живыми;
//   5. сохраняет сцену.
//
// Директор `DepotArrival` остаётся в сцене: он ссылается на камеру и игрока,
// а префаб на сценовые объекты ссылаться не может.

var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

var cells = UnityEngine.GameObject.Find("WorldCells");
if (cells == null) return "нет корня WorldCells";
var inst = cells.transform.Find("WorldCell_-2_-2");
if (inst == null) return "нет инстанса WorldCell_-2_-2";

// --- 1. Игрок — сценовый ------------------------------------------------
var player = UnityEngine.GameObject.Find("Player");
var train = UnityEngine.GameObject.Find("DepotTrain");
if (player != null && train != null && player.transform.IsChildOf(train.transform))
{
    player.transform.SetParent(null, true);
    sb.AppendLine("игрок отцеплен от вагона");
}

// --- 2. Снять временный держатель ---------------------------------------
var holder = UnityEngine.GameObject.Find("SuspendedActors");
if (holder != null)
{
    UnityEngine.Object.DestroyImmediate(holder);
    sb.AppendLine("держатель SuspendedActors удалён");
}

string[] names =
{
    "DepotTrain", "R7_Drifter", "R16_Warden_A", "R16_Warden_B", "R8_Bulwark",
    "DepotLamp", "DepotDoorSpill", "DepotDoorFill", "DepotArrival"
};

// Режиссёр прибытия — тоже содержимое депо, но в префаб он едет без ссылок на
// сцену: игрока, камеру и полосу опыта он находит в Awake (в классе уже есть
// fallback через FindAnyObjectByType). Префаб на сценовые объекты ссылаться
// не может, поэтому ссылки очищаются до применения.
var arrival = UnityEngine.GameObject.Find("DepotArrival");
if (arrival != null)
{
    var director = arrival.GetComponent<FrostSpace.Features.Story.DepotArrivalDirector>();
    if (director != null)
    {
        var directorSo = new UnityEditor.SerializedObject(director);
        string[] sceneRefs = { "_locomotion", "_player", "_rig", "_xpBar" };
        foreach (var field in sceneRefs)
        {
            var prop = directorSo.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = null;
        }
        directorSo.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("у директора очищены ссылки на сцену");
    }
}

int moved = 0;
int skipped = 0;
foreach (var name in names)
{
    var go = UnityEngine.GameObject.Find(name);
    if (go == null)
    {
        sb.AppendLine("НЕТ объекта: " + name);
        continue;
    }

    // Уже в этом инстансе — второй раз не трогаем.
    var root = UnityEditor.PrefabUtility.GetNearestPrefabInstanceRoot(go);
    if (root == inst.gameObject)
    {
        skipped++;
        continue;
    }

    go.transform.SetParent(inst, true);
    moved++;
}

// --- 3. В ассет ----------------------------------------------------------
UnityEditor.PrefabUtility.ApplyPrefabInstance(inst.gameObject,
    UnityEditor.InteractionMode.AutomatedAction);

// --- 4. Сохранить --------------------------------------------------------
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scene.path);
UnityEditor.AssetDatabase.SaveAssets();

// --- 5. Отчёт ------------------------------------------------------------
int inPrefab = 0;
var prefabAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/_Project/Content/Prefabs/World/WorldCell_-2_-2.prefab");
foreach (var name in names)
{
    var found = FindDeep(prefabAsset.transform, name);
    if (found != null) inPrefab++;
    else sb.AppendLine("В ПРЕФАБЕ НЕТ: " + name);
}

var arrivalDirector = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
var so = arrivalDirector != null ? new UnityEditor.SerializedObject(arrivalDirector) : null;
var trainProp = so != null ? so.FindProperty("_train") : null;
sb.AppendLine("перенесено: " + moved + ", уже было: " + skipped + ", в префабе: " + inPrefab + "/" + names.Length);
sb.AppendLine("ссылка директора на вагон: " + (trainProp != null && trainProp.objectReferenceValue != null
    ? "жива (" + trainProp.objectReferenceValue.name + ")"
    : "ПОТЕРЯНА"));
sb.AppendLine("сцена сохранена: " + scene.path);
return sb.ToString();

UnityEngine.Transform FindDeep(UnityEngine.Transform root, string name)
{
    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == name) return t;
    return null;
}
