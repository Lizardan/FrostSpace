// Неразрушающая синхронизация префабов клеток с частями FBX.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/sync_cell_prefabs.cs
//
// Клетка — это **префаб**, собранный из частей. Сам FBX-чанка больше нет:
// земля, путь, шпалы, дороги, части двора и поселения лежат отдельными файлами
// в `Models/Level/Cells/<Имя>_<i>_<j>.fbx`, а префаб `WorldCell_<i>_<j>.prefab`
// держит по объекту на часть. Причина — дорога внутри чанка не переезжает,
// не отключается и не меняется отдельно от земли, а префаб перестаёт быть
// местом авторской правки (ADR 0039).
//
// Границу владения задаёт метка `CellMesh`: этот скрипт трогает только
// помеченные объекты и никогда ничего не удаляет. Всё прочее в префабе —
// актёры депо, свет, слоты, авторское — неприкосновенно.
//
// Что делает по каждой клетке:
//   1. находит объект с тем же именем, что часть FBX, и обновляет меш,
//      материалы, коллайдер и локальный трансформ;
//   2. если объекта нет — создаёт его и помечает `CellMesh`;
//   3. если у существующего объекта метки нет — принимает его в конвейер;
//   4. снимает меш с корня префаба: раньше земля лежала на корне FBX-чанка,
//      теперь она приходит частью `Ground_<i>_<j>`, и корень с мешем дал бы
//      две совпадающие поверхности;
//   5. печатает помеченные объекты, которых нет ни в одной части, но не удаляет;
//   6. проверяет `WorldCell` на корне.
//
// Повторный запуск безопасен и ничего не меняет, если части не менялись.

var sb = new System.Text.StringBuilder();

const string partsDir = "Assets/_Project/Content/Models/Level/Cells";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";
var partName = new System.Text.RegularExpressions.Regex(@"_(-?\d+)_(-?\d+)$");

if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

var guids = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { partsDir });
var byCell = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
foreach (var guid in guids)
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var mt = partName.Match(name);
    if (!mt.Success) continue;
    var key = mt.Groups[1].Value + "_" + mt.Groups[2].Value;
    System.Collections.Generic.List<string> list;
    if (!byCell.TryGetValue(key, out list))
    {
        list = new System.Collections.Generic.List<string>();
        byCell[key] = list;
    }
    list.Add(path);
}

sb.AppendLine("частей FBX: " + guids.Length + ", клеток: " + byCell.Count);

int cells = 0, updatedCells = 0, updatedMeshes = 0, addedMeshes = 0, adoptedMeshes = 0;
int createdPrefabs = 0, staleMeshes = 0, clearedRoots = 0;
var issues = new System.Text.StringBuilder();

foreach (var pair in byCell)
{
    var names = pair.Key.Split('_');
    int ci = int.Parse(names[0]), cj = int.Parse(names[1]);
    var cellName = "WorldCell_" + pair.Key;

    // Имя → часть. Имя части совпадает с именем объекта в её FBX.
    var parts = new System.Collections.Generic.List<UnityEngine.MeshFilter>();
    foreach (var path in pair.Value)
    {
        var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
        if (model == null) { issues.AppendLine(path + ": FBX не загрузился"); continue; }
        var mf = model.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            issues.AppendLine(model.name + ": в части нет меша на корне");
            continue;
        }
        parts.Add(mf);
    }

    var prefabPath = prefabDir + "/" + cellName + ".prefab";
    var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    bool isNew = existing == null;
    UnityEngine.GameObject contents;
    if (isNew)
    {
        contents = new UnityEngine.GameObject(cellName);
        createdPrefabs++;
    }
    else
    {
        contents = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
    }

    bool changed = isNew;

    // Земля раньше лежала мешем на корне префаба (у пустошей только она и была).
    // Теперь она приходит частью `Ground_<i>_<j>`: корневой меш обязан уйти,
    // иначе клетка получает две совпадающие поверхности и двойной коллайдер.
    if (contents.GetComponent<UnityEngine.MeshFilter>() != null)
    {
        var oldCollider = contents.GetComponent<UnityEngine.MeshCollider>();
        if (oldCollider != null) UnityEngine.Object.DestroyImmediate(oldCollider);
        var oldRenderer = contents.GetComponent<UnityEngine.MeshRenderer>();
        if (oldRenderer != null) UnityEngine.Object.DestroyImmediate(oldRenderer);
        UnityEngine.Object.DestroyImmediate(contents.GetComponent<UnityEngine.MeshFilter>());
        clearedRoots++;
        changed = true;
    }
    // Метка владения принадлежит частям, а не корню: корень держит `WorldCell`
    // и актёров. Оставленная на нём метка читалась бы как «часть, которой нет».
    var rootMark = contents.GetComponent<FrostSpace.Features.Map.CellMesh>();
    if (rootMark != null)
    {
        UnityEngine.Object.DestroyImmediate(rootMark);
        changed = true;
    }
    // На корне не может быть и коллайдера с рендерером без меша.
    var strayCollider = contents.GetComponent<UnityEngine.MeshCollider>();
    if (strayCollider != null)
    {
        UnityEngine.Object.DestroyImmediate(strayCollider);
        changed = true;
    }
    var strayRenderer = contents.GetComponent<UnityEngine.MeshRenderer>();
    if (strayRenderer != null)
    {
        UnityEngine.Object.DestroyImmediate(strayRenderer);
        changed = true;
    }

    var byName = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
    foreach (var t in contents.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t == contents.transform) continue;
        if (!byName.ContainsKey(t.name)) byName[t.name] = t;
    }

    var sourceNames = new System.Collections.Generic.HashSet<string>();
    foreach (var src in parts)
    {
        var meshName = src.name;
        sourceNames.Add(meshName);
        var srcRenderer = src.GetComponent<UnityEngine.MeshRenderer>();

        UnityEngine.Transform dst;
        if (byName.TryGetValue(meshName, out dst) && dst.GetComponent<UnityEngine.MeshFilter>() != null)
        {
            var dstFilter = dst.GetComponent<UnityEngine.MeshFilter>();
            var dstRenderer = dst.GetComponent<UnityEngine.MeshRenderer>();
            if (dstFilter.sharedMesh != src.sharedMesh)
            {
                dstFilter.sharedMesh = src.sharedMesh;
                updatedMeshes++;
                changed = true;
            }
            if (dstRenderer != null && srcRenderer != null &&
                !SameMaterials(dstRenderer.sharedMaterials, srcRenderer.sharedMaterials))
            {
                dstRenderer.sharedMaterials = srcRenderer.sharedMaterials;
                changed = true;
            }
            if (dst.localPosition != src.transform.localPosition)
            {
                dst.localPosition = src.transform.localPosition;
                changed = true;
            }
            if (dst.localRotation != src.transform.localRotation)
            {
                dst.localRotation = src.transform.localRotation;
                changed = true;
            }
            if (dst.localScale != src.transform.localScale)
            {
                dst.localScale = src.transform.localScale;
                changed = true;
            }
            var collider = dst.GetComponent<UnityEngine.MeshCollider>();
            if (collider == null)
            {
                collider = dst.gameObject.AddComponent<UnityEngine.MeshCollider>();
                changed = true;
            }
            if (collider.sharedMesh != dstFilter.sharedMesh)
            {
                collider.sharedMesh = dstFilter.sharedMesh;
                changed = true;
            }
            if (dst.GetComponent<FrostSpace.Features.Map.CellMesh>() == null)
            {
                dst.gameObject.AddComponent<FrostSpace.Features.Map.CellMesh>();
                adoptedMeshes++;
                changed = true;
            }
        }
        else
        {
            var go = new UnityEngine.GameObject(meshName);
            go.transform.SetParent(contents.transform, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;

            var filter = go.AddComponent<UnityEngine.MeshFilter>();
            filter.sharedMesh = src.sharedMesh;

            var renderer = go.AddComponent<UnityEngine.MeshRenderer>();
            if (srcRenderer != null) renderer.sharedMaterials = srcRenderer.sharedMaterials;

            var collider = go.AddComponent<UnityEngine.MeshCollider>();
            collider.sharedMesh = src.sharedMesh;

            go.AddComponent<FrostSpace.Features.Map.CellMesh>();
            addedMeshes++;
            changed = true;
        }
    }

    // Устаревшие: помечены как наши, но такой части в FBX больше нет.
    // Не удаляем — вдруг это ручная правка, которую забыли пометить иначе.
    foreach (var cellMesh in contents.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        if (sourceNames.Contains(cellMesh.name)) continue;
        staleMeshes++;
        issues.AppendLine(cellName + ": лишний CellMesh «" + cellMesh.name + "» — части нет (не удалён)");
    }

    var cell = contents.GetComponent<FrostSpace.Features.Map.WorldCell>();
    if (cell == null)
    {
        cell = contents.AddComponent<FrostSpace.Features.Map.WorldCell>();
        changed = true;
    }
    if (cell.Cell.x != ci || cell.Cell.y != cj)
    {
        cell.Setup(new UnityEngine.Vector2Int(ci, cj));
        changed = true;
    }

    if (changed)
    {
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        updatedCells++;
    }

    if (isNew) UnityEngine.Object.DestroyImmediate(contents);
    else UnityEditor.PrefabUtility.UnloadPrefabContents(contents);

    cells++;
}

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

sb.AppendLine("клеток собрано: " + cells + " (ожидалось 121)");
sb.AppendLine("префабов создано: " + createdPrefabs + ", обновлено: " + updatedCells);
sb.AppendLine("мешей обновлено: " + updatedMeshes + ", добавлено: " + addedMeshes +
              ", принято в конвейер: " + adoptedMeshes);
sb.AppendLine("снято мешей с корня префаба: " + clearedRoots);
sb.AppendLine("устаревших CellMesh: " + staleMeshes);
if (issues.Length > 0) sb.AppendLine(issues.ToString());
return sb.ToString();

bool SameMaterials(UnityEngine.Material[] a, UnityEngine.Material[] b)
{
    if (a == null || b == null) return a == b;
    if (a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++)
        if (a[i] != b[i]) return false;
    return true;
}
