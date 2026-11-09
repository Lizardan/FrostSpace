// Установка инстансов клеток в сцену `Frontier` из существующих префабов.
//
// ВНИМАНИЕ: скрипт РАЗРУШАЮЩИЙ — он удаляет всех детей корня `WorldCells` и
// расставляет инстансы заново. Запускать только когда сцену действительно надо
// собрать с нуля (пустой корень, чужая раскладка инстансов). Обычная правка мира
// делается без него.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/install_cell_instances.cs
//
// Источник клетки — существующий префаб `Prefabs/World/WorldCell_<i>_<j>.prefab`
// (ADR 0043): скрипт только расставляет его инстансы и НЕ пересоздаёт префаб —
// авторское содержимое префаба неприкосновенно. Пересборку геометрии из частей
// FBX делает `sync_cell_prefabs.cs`; проверка «префаб ↔ FBX ↔ сцена» —
// `verify_cell_prefabs.cs`.
//
// Комплект и координаты берутся из раскладки `world_layout.json` через
// `WorldLayout` (ADR 0036): ожидаемый набор — все клетки сетки, позиция инстанса
// в осях сцены — центр клетки минус смещение депо. Полный комплект проверяется
// ДО первого изменения сцены: при неполном или битом наборе сцена не меняется и
// не сохраняется.
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

// Раскладка — единственный источник комплекта клеток и их координат.
const string layoutPath = "Assets/_Project/Content/Data/World/world_layout.json";
var layoutAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(layoutPath);
FrostSpace.Features.Map.WorldLayout layout;
if (!FrostSpace.Features.Map.WorldLayout.TryParse(layoutAsset, out layout))
    return "раскладка не читается: " + layoutPath + " — сцена не тронута";
var grid = layout.Grid;
const string prefabDir = "Assets/_Project/Content/Prefabs/World";

// Фаза 1: полный ожидаемый комплект ДО первого изменения сцены. Префаб должен
// существовать, загружаться и нести WorldCell с индексом, совпадающим с именем:
// резидентность узнаёт клетку по индексу, и битый префаб молча сломал бы её.
var prefabs = new System.Collections.Generic.List<UnityEngine.GameObject>();
var positions = new System.Collections.Generic.List<UnityEngine.Vector3>();
var issues = new System.Text.StringBuilder();
for (int cj = grid.CellMin; cj <= grid.CellMax; cj++)
{
    for (int ci = grid.CellMin; ci <= grid.CellMax; ci++)
    {
        var cellName = "WorldCell_" + ci + "_" + cj;
        var prefabPath = prefabDir + "/" + cellName + ".prefab";
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
        if (prefab == null) { issues.AppendLine(cellName + ": нет префаба " + prefabPath); continue; }
        var wc = prefab.GetComponent<FrostSpace.Features.Map.WorldCell>();
        if (wc == null) { issues.AppendLine(cellName + ": в префабе нет WorldCell"); continue; }
        if (wc.Cell.x != ci || wc.Cell.y != cj)
        { issues.AppendLine(cellName + ": индекс WorldCell " + wc.Cell + " не совпадает с именем"); continue; }

        // Центр клетки в мировых осях минус смещение депо = позиция в осях сцены
        // (ноль сцены — двор депо). При текущей раскладке это ci*400+800.
        var center = grid.CenterOf(new UnityEngine.Vector2Int(ci, cj));
        var scenePos = center - layout.Depot;
        prefabs.Add(prefab);
        positions.Add(new UnityEngine.Vector3(scenePos.x, 0f, scenePos.y));
    }
}
if (issues.Length > 0)
{
    sb.AppendLine("комплект неполон — сцена НЕ изменена и НЕ сохранена:");
    sb.Append(issues.ToString());
    sb.AppendLine("ожидалось клеток: " + grid.CellCount + ", годных префабов: " + prefabs.Count);
    return sb.ToString();
}

// Фаза 2: комплект полный — пересобираем расстановку. Старые инстансы удаляются:
// смешивать два поколения в одном корне значит получить два слоя земли.
int removed = 0;
for (int i = root.transform.childCount - 1; i >= 0; i--)
{
    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
    removed++;
}

int made = 0;
bool failed = false;
for (int i = 0; i < prefabs.Count; i++)
{
    var inst = UnityEditor.PrefabUtility.InstantiatePrefab(prefabs[i]) as UnityEngine.GameObject;
    if (inst == null)
    {
        failed = true;
        sb.AppendLine("не удалось создать инстанс " + prefabs[i].name + " — расстановка прервана");
        break;
    }
    inst.transform.SetParent(root.transform, false);
    inst.transform.localPosition = positions[i];
    inst.transform.localRotation = UnityEngine.Quaternion.identity;
    inst.transform.localScale = UnityEngine.Vector3.one;
    made++;
}

bool saved = false;
if (!failed && made == prefabs.Count)
{
    var res = root.GetComponent<FrostSpace.Features.Map.WorldResidency>();
    if (res == null) res = root.AddComponent<FrostSpace.Features.Map.WorldResidency>();
    var so = new UnityEditor.SerializedObject(res);
    so.FindProperty("_layout").objectReferenceValue = layoutAsset;
    so.FindProperty("_player").objectReferenceValue = player.transform;
    so.ApplyModifiedPropertiesWithoutUndo();

    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
}
else
{
    sb.AppendLine("расстановка неполна — сцена НЕ сохранена; перезагрузите сцену, чтобы вернуть прежнее состояние");
}

int tris = 0;
foreach (var mf in root.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
{
    if (mf.sharedMesh == null) continue;
    tris += mf.sharedMesh.triangles.Length / 3;
}
sb.AppendLine(string.Format("удалено старых инстансов={0} расставлено={1} ожидалось={2}",
    removed, made, grid.CellCount));
sb.AppendLine(string.Format("детей у WorldCells={0} трис в клетках={1} сохранено={2}",
    root.transform.childCount, tris, saved));
return sb.ToString();
