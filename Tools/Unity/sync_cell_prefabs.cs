// Неразрушающая синхронизация префабов клеток с FBX.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/sync_cell_prefabs.cs
//
// Чем отличается от `rebuild_cell_prefabs.cs`: тот собирает префаб с нуля и
// стирает всё авторское (актёров, свет, добавленные компоненты). Этот — только
// обновляет помеченные `CellMesh` объекты по имени меша и никогда ничего не
// удаляет. Границу владения задаёт метка: `CellMesh` принадлежит конвейеру,
// всё остальное в префабе — авторское.
//
// Что делает по каждой из 121 клетки:
//   1. находит объект с тем же именем, что меш в FBX, и обновляет меш,
//      материалы, коллайдер и локальный трансформ;
//   2. если объекта нет — создаёт его и помечает `CellMesh`;
//   3. если у существующего объекта метки нет — принимает его в конвейер
//      (adopt), а не плодит дубликат;
//   4. печатает помеченные объекты, которых нет в FBX (устаревшие), но не удаляет;
//   5. проверяет `WorldCell` на корне.
//
// Повторный запуск безопасен и ничего не меняет, если FBX не менялся.

var sb = new System.Text.StringBuilder();

const string modelDir = "Assets/_Project/Content/Models/Level";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";

var guids = UnityEditor.AssetDatabase.FindAssets("t:Model WorldCell_", new string[] { modelDir });
sb.AppendLine("FBX клеток: " + guids.Length + " (ожидалось 121)");

int cells = 0;
int updatedCells = 0;
int updatedMeshes = 0;
int addedMeshes = 0;
int adoptedMeshes = 0;
int staleMeshes = 0;
int createdPrefabs = 0;
var issues = new System.Text.StringBuilder();

for (int g = 0; g < guids.Length; g++)
{
    var modelPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
    var name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
    var parts = name.Split('_');
    int ci, cj;
    if (parts.Length != 3 || !int.TryParse(parts[1], out ci) || !int.TryParse(parts[2], out cj))
    {
        issues.AppendLine(name + ": имя не разбирается как WorldCell_<x>_<z> — пропущен");
        continue;
    }

    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(modelPath);
    if (model == null)
    {
        issues.AppendLine(name + ": FBX не загрузился — пропущен");
        continue;
    }

    var prefabPath = prefabDir + "/" + name + ".prefab";
    var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);

    UnityEngine.GameObject contents;
    bool isNew = existing == null;
    if (isNew)
    {
        contents = new UnityEngine.GameObject(name);
        createdPrefabs++;
    }
    else
    {
        contents = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
    }

    var byName = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
    foreach (var t in contents.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t == contents.transform) continue;
        if (!byName.ContainsKey(t.name)) byName[t.name] = t;
    }

    var sourceNames = new System.Collections.Generic.HashSet<string>();
    bool changed = isNew;

    // У клеток-пустошей меш земли лежит на самом корне FBX, а не на ребёнке.
    // Корень обрабатывается отдельно: иначе инструмент создал бы дочерний
    // дубликат с тем же мешем, и клетка получила бы две совпадающие поверхности.
    var rootFilter = model.GetComponent<UnityEngine.MeshFilter>();
    if (rootFilter != null && rootFilter.sharedMesh != null)
    {
        sourceNames.Add(model.name);

        var rootOnPrefab = contents.GetComponent<UnityEngine.MeshFilter>();
        if (rootOnPrefab == null)
        {
            rootOnPrefab = contents.AddComponent<UnityEngine.MeshFilter>();
            changed = true;
        }
        if (rootOnPrefab.sharedMesh != rootFilter.sharedMesh)
        {
            rootOnPrefab.sharedMesh = rootFilter.sharedMesh;
            updatedMeshes++;
            changed = true;
        }

        var rootRenderer = model.GetComponent<UnityEngine.MeshRenderer>();
        var rootRendererOnPrefab = contents.GetComponent<UnityEngine.MeshRenderer>();
        if (rootRendererOnPrefab == null && rootRenderer != null)
        {
            rootRendererOnPrefab = contents.AddComponent<UnityEngine.MeshRenderer>();
            changed = true;
        }
        if (rootRendererOnPrefab != null && rootRenderer != null &&
            !SameMaterials(rootRendererOnPrefab.sharedMaterials, rootRenderer.sharedMaterials))
        {
            rootRendererOnPrefab.sharedMaterials = rootRenderer.sharedMaterials;
            changed = true;
        }

        var rootCollider = contents.GetComponent<UnityEngine.MeshCollider>();
        if (rootCollider == null)
        {
            rootCollider = contents.AddComponent<UnityEngine.MeshCollider>();
            changed = true;
        }
        if (rootCollider.sharedMesh != rootFilter.sharedMesh)
        {
            rootCollider.sharedMesh = rootFilter.sharedMesh;
            changed = true;
        }

        if (contents.GetComponent<FrostSpace.Features.Map.CellMesh>() == null)
        {
            contents.AddComponent<FrostSpace.Features.Map.CellMesh>();
            adoptedMeshes++;
            changed = true;
        }
    }

    foreach (var srcFilter in model.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
    {
        if (srcFilter.sharedMesh == null) continue;
        if (srcFilter.transform == model.transform) continue; // корень уже обработан

        var src = srcFilter.transform;
        var meshName = src.name;
        sourceNames.Add(meshName);

        UnityEngine.Transform dst;
        if (byName.TryGetValue(meshName, out dst) && dst.GetComponent<UnityEngine.MeshFilter>() != null)
        {
            var dstFilter = dst.GetComponent<UnityEngine.MeshFilter>();
            var dstRenderer = dst.GetComponent<UnityEngine.MeshRenderer>();
            var srcRenderer = src.GetComponent<UnityEngine.MeshRenderer>();

            if (dstFilter.sharedMesh != srcFilter.sharedMesh)
            {
                dstFilter.sharedMesh = srcFilter.sharedMesh;
                updatedMeshes++;
                changed = true;
            }

            if (dstRenderer != null && srcRenderer != null &&
                !SameMaterials(dstRenderer.sharedMaterials, srcRenderer.sharedMaterials))
            {
                dstRenderer.sharedMaterials = srcRenderer.sharedMaterials;
                changed = true;
            }

            if (dst.localPosition != src.localPosition)
            {
                dst.localPosition = src.localPosition;
                changed = true;
            }

            if (dst.localRotation != src.localRotation)
            {
                dst.localRotation = src.localRotation;
                changed = true;
            }

            if (dst.localScale != src.localScale)
            {
                dst.localScale = src.localScale;
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
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;

            var filter = go.AddComponent<UnityEngine.MeshFilter>();
            filter.sharedMesh = srcFilter.sharedMesh;

            var srcRenderer = src.GetComponent<UnityEngine.MeshRenderer>();
            var renderer = go.AddComponent<UnityEngine.MeshRenderer>();
            if (srcRenderer != null)
                renderer.sharedMaterials = srcRenderer.sharedMaterials;

            var collider = go.AddComponent<UnityEngine.MeshCollider>();
            collider.sharedMesh = srcFilter.sharedMesh;

            go.AddComponent<FrostSpace.Features.Map.CellMesh>();
            addedMeshes++;
            changed = true;
        }
    }

    // Устаревшие: помечены как наши, но в FBX такого меша больше нет.
    // Не удаляем — вдруг это ручная правка, которую забыли пометить иначе.
    foreach (var cellMesh in contents.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        if (sourceNames.Contains(cellMesh.name)) continue;
        staleMeshes++;
        issues.AppendLine(name + ": лишний CellMesh «" + cellMesh.name + "» — в FBX его нет (не удалён)");
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

    if (isNew)
        UnityEngine.Object.DestroyImmediate(contents);
    else
        UnityEditor.PrefabUtility.UnloadPrefabContents(contents);

    cells++;
}

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

sb.AppendLine("клеток обработано: " + cells);
sb.AppendLine("префабов создано: " + createdPrefabs + ", обновлено: " + updatedCells);
sb.AppendLine("мешей обновлено: " + updatedMeshes + ", добавлено: " + addedMeshes +
              ", принято в конвейер: " + adoptedMeshes);
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
