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
// Границу владения задаёт метка `CellMesh` (ADR 0043): регулярный sync изменяет
// только явно помеченные объекты и никогда ничего не удаляет. Всё прочее в
// префабе — актёры депо, свет, слоты, авторское — неприкосновенно.
//
// Что делает по каждой клетке:
//   1. находит помеченный `CellMesh` объект с именем части FBX и обновляет меш,
//      материалы, коллайдер и локальный трансформ;
//   2. если помеченного объекта нет — создаёт его и помечает `CellMesh`;
//   3. непомеченный объект с именем части — конфликт владения: sync его НЕ
//      принимает и НЕ перезаписывает, а останавливается до записи;
//   4. меш/рендерер/коллайдер/метка на корне префаба — тоже конфликт: корень
//      держит `WorldCell` и актёров, геометрия приходит частями; не удаляем,
//      а просим человека пометить или убрать вручную;
//   5. печатает помеченные объекты, которых нет ни в одной части, но не удаляет;
//   6. проверяет `WorldCell` на корне.
//
// Сначала проверяются комплект и конфликты ВСЕХ затрагиваемых клеток, и только
// потом применяются изменения: при любом конфликте ни один префаб не меняется.
// Повторный запуск безопасен и ничего не меняет, если части не менялись.
//
// Режим проверки: `dryRun = true` ниже — только валидация комплекта и конфликтов,
// без записи префабов.

var sb = new System.Text.StringBuilder();

// true — только проверить комплект и конфликты, ничего не записывать.
const bool dryRun = false;

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

int cells = 0, updatedCells = 0, updatedMeshes = 0, addedMeshes = 0;
int createdPrefabs = 0, staleMeshes = 0;
var conflicts = new System.Text.StringBuilder();   // блокируют запись
var notes = new System.Text.StringBuilder();        // только печать

// Планы клеток: содержимое загружено один раз и выгружается в конце.
var planContents = new System.Collections.Generic.List<UnityEngine.GameObject>();
var planIsNew = new System.Collections.Generic.List<bool>();
var planParts = new System.Collections.Generic.List<System.Collections.Generic.List<UnityEngine.MeshFilter>>();
var planKeys = new System.Collections.Generic.List<string>();

// --- Фаза 1: комплект и конфликты всех клеток, без записи ---
foreach (var pair in byCell)
{
    var cellName = "WorldCell_" + pair.Key;

    // Имя → часть. Имя части совпадает с именем объекта в её FBX.
    var parts = new System.Collections.Generic.List<UnityEngine.MeshFilter>();
    foreach (var path in pair.Value)
    {
        var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
        if (model == null) { conflicts.AppendLine(path + ": FBX не загрузился"); continue; }
        var mf = model.GetComponent<UnityEngine.MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            conflicts.AppendLine(model.name + ": в части нет меша на корне");
            continue;
        }
        parts.Add(mf);
    }

    var prefabPath = prefabDir + "/" + cellName + ".prefab";
    var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    bool isNew = existing == null;
    UnityEngine.GameObject contents;
    if (isNew) contents = new UnityEngine.GameObject(cellName);
    else contents = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
    planContents.Add(contents);
    planIsNew.Add(isNew);
    planParts.Add(parts);
    planKeys.Add(pair.Key);

    // Корень не несёт геометрию конвейера: меш/рендерер/коллайдер/метка на нём —
    // авторское или наследие. Не удаляем (это стёрло бы авторское), а конфликтом
    // просим человека пометить или убрать вручную.
    if (contents.GetComponent<UnityEngine.MeshFilter>() != null ||
        contents.GetComponent<UnityEngine.MeshRenderer>() != null ||
        contents.GetComponent<UnityEngine.MeshCollider>() != null ||
        contents.GetComponent<FrostSpace.Features.Map.CellMesh>() != null)
    {
        conflicts.AppendLine(cellName + ": меш/рендерер/коллайдер/CellMesh на корне префаба — конфликт владения (не удалён)");
    }

    // Полная карта имён потомков: непомеченное совпадение с частью — конфликт,
    // обнаруженный ДО записи. Регулярный sync принимает только помеченные.
    var allByName = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UnityEngine.Transform>>();
    foreach (var t in contents.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t == contents.transform) continue;
        System.Collections.Generic.List<UnityEngine.Transform> matches;
        if (!allByName.TryGetValue(t.name, out matches))
        {
            matches = new System.Collections.Generic.List<UnityEngine.Transform>();
            allByName[t.name] = matches;
        }
        matches.Add(t);
    }
    var sourceNames = new System.Collections.Generic.HashSet<string>();
    foreach (var src in parts)
    {
        sourceNames.Add(src.name);
        System.Collections.Generic.List<UnityEngine.Transform> matches;
        if (!allByName.TryGetValue(src.name, out matches)) continue;
        foreach (var m in matches)
            if (m.GetComponent<FrostSpace.Features.Map.CellMesh>() == null)
                conflicts.AppendLine(cellName + ": непомеченный объект «" + src.name + "» совпадает с частью — конфликт (принадлежность задаёт метка CellMesh)");
    }

    // Устаревшие: помечены как наши, но такой части в FBX больше нет.
    // Не удаляем — вдруг это ручная правка, которую забыли пометить иначе.
    foreach (var cellMesh in contents.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
    {
        if (sourceNames.Contains(cellMesh.name)) continue;
        staleMeshes++;
        notes.AppendLine(cellName + ": лишний CellMesh «" + cellMesh.name + "» — части нет (не удалён)");
    }
}

// Конфликт хотя бы в одной клетке — не меняем ни одну.
if (conflicts.Length > 0)
{
    UnloadPlans();
    sb.AppendLine("клеток проверено: " + planContents.Count + " (ожидалось 121)");
    sb.AppendLine("устаревших CellMesh: " + staleMeshes);
    if (notes.Length > 0) sb.Append(notes.ToString());
    sb.AppendLine("КОНФЛИКТЫ — ни один префаб не изменён:");
    sb.Append(conflicts.ToString());
    return sb.ToString();
}

if (dryRun)
{
    UnloadPlans();
    sb.AppendLine("клеток проверено: " + planContents.Count + " (ожидалось 121)");
    sb.AppendLine("устаревших CellMesh: " + staleMeshes);
    if (notes.Length > 0) sb.Append(notes.ToString());
    sb.AppendLine("dry-run: комплект и конфликты проверены, изменения НЕ применены");
    return sb.ToString();
}

// --- Фаза 2: применение изменений (конфликтов нет) ---
try
{
    for (int idx = 0; idx < planContents.Count; idx++)
    {
        var contents = planContents[idx];
        var parts = planParts[idx];
        var key = planKeys[idx];
        var kn = key.Split('_');
        int ci = int.Parse(kn[0]), cj = int.Parse(kn[1]);
        var cellName = "WorldCell_" + key;
        var prefabPath = prefabDir + "/" + cellName + ".prefab";

        bool changed = planIsNew[idx];
        if (planIsNew[idx]) createdPrefabs++;

        // Карта ТОЛЬКО помеченных объектов — граница владения (ADR 0043).
        var byName = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
        foreach (var cm in contents.GetComponentsInChildren<FrostSpace.Features.Map.CellMesh>(true))
        {
            if (cm.transform == contents.transform) continue;
            if (!byName.ContainsKey(cm.name)) byName[cm.name] = cm.transform;
        }

        foreach (var src in parts)
        {
            var meshName = src.name;
            var srcRenderer = src.GetComponent<UnityEngine.MeshRenderer>();

            UnityEngine.Transform dst;
            if (byName.TryGetValue(meshName, out dst))
            {
                var dstFilter = dst.GetComponent<UnityEngine.MeshFilter>();
                if (dstFilter == null)
                {
                    dstFilter = dst.gameObject.AddComponent<UnityEngine.MeshFilter>();
                    changed = true;
                }
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
        cells++;
    }

    UnityEditor.AssetDatabase.SaveAssets();
    UnityEditor.AssetDatabase.Refresh();
}
finally
{
    UnloadPlans();
}

sb.AppendLine("клеток собрано: " + cells + " (ожидалось 121)");
sb.AppendLine("префабов создано: " + createdPrefabs + ", обновлено: " + updatedCells);
sb.AppendLine("мешей обновлено: " + updatedMeshes + ", добавлено: " + addedMeshes);
sb.AppendLine("устаревших CellMesh: " + staleMeshes);
if (notes.Length > 0) sb.Append(notes.ToString());
return sb.ToString();

void UnloadPlans()
{
    for (int i = 0; i < planContents.Count; i++)
    {
        if (planIsNew[i]) UnityEngine.Object.DestroyImmediate(planContents[i]);
        else UnityEditor.PrefabUtility.UnloadPrefabContents(planContents[i]);
    }
}

bool SameMaterials(UnityEngine.Material[] a, UnityEngine.Material[] b)
{
    if (a == null || b == null) return a == b;
    if (a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++)
        if (a[i] != b[i]) return false;
    return true;
}
