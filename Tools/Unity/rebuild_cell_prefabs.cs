// Пересборка префабов клеток мира из FBX.
//
// Зачем скрипт: префаб клетки — производная от FBX, который собирает
// `Tools/Blender/build_world_cells.py`. Между сборкой FBX и пересборкой
// префаба проходит произвольное время, и расхождение молчаливое: меш в FBX
// переименован — ссылка в префабе повисает, меш пропадает из кадра; меш
// добавлен — в префабе его нет. 2026-11-03 так потерялись 15 мешей
// поселения в клетке (0,-2) и по одной дороге в клетках (0,-1) и (-1,-1).
//
// Что делает: пересобирает префаб каждой клетки из её FBX заново, ставит
// компонент `WorldCell` с индексом клетки и `MeshCollider` на каждый меш
// (по нему ходит игрок, и он должен упираться в стену коробки депо, а не
// проходить сквозь неё).
//
// Чего не делает: не трогает сцену. Инстансы префабов обновляются сами, и
// разбор сцены — необратимая операция, в которой нет нужды.
//
// Запуск:
//     python Tools/Unity/run_snippet.py Tools/Unity/rebuild_cell_prefabs.cs
//     python Tools/Unity/run_snippet.py Tools/Unity/verify_cell_prefabs.cs

var sb = new System.Text.StringBuilder();

if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

const string modelDir = "Assets/_Project/Content/Models/Level";
const string prefabDir = "Assets/_Project/Content/Prefabs/World";

var guids = UnityEditor.AssetDatabase.FindAssets("t:Model WorldCell_", new string[] { modelDir });
if (guids.Length != 121)
    sb.AppendLine("ВНИМАНИЕ: FBX клеток " + guids.Length + ", ожидалось 121");

int made = 0;
int bad = 0;
int colliders = 0;
int meshes = 0;
int tris = 0;

// Страховка: пересборка стирает всё, чего нет в FBX. Если в префабах уже
// живут авторские объекты (актёры, свет, добавленные компоненты), запускать
// её нельзя — это молча уничтожит работу. Штатное обновление геометрии —
// `sync_cell_prefabs.cs`, он трогает только помеченные `CellMesh`.
int authored = 0;
var authoredCells = new System.Text.StringBuilder();
for (int g = 0; g < guids.Length; g++)
{
    var probePath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
    var probeName = System.IO.Path.GetFileNameWithoutExtension(probePath);
    var probe = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabDir + "/" + probeName + ".prefab");
    if (probe == null) continue;

    int count = 0;
    foreach (var t in probe.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t == probe.transform) continue;
        if (t.GetComponent<FrostSpace.Features.Map.CellMesh>() != null) continue;
        count++;
    }

    if (count > 0)
    {
        authored += count;
        authoredCells.AppendLine(probeName + ": " + count + " авторских объектов");
    }
}

if (authored > 0)
{
    sb.AppendLine("СТОП: пересборка стёрла бы авторское содержимое префабов.");
    sb.AppendLine(authoredCells.ToString());
    sb.AppendLine("Для обновления геометрии используй Tools/Unity/sync_cell_prefabs.cs.");
    return sb.ToString();
}

for (int g = 0; g < guids.Length; g++)
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
    var name = System.IO.Path.GetFileNameWithoutExtension(path);
    var parts = name.Split('_');
    int ci, cj;
    if (parts.Length != 3 || !int.TryParse(parts[1], out ci) || !int.TryParse(parts[2], out cj))
    {
        bad++;
        sb.AppendLine(name + ": имя не разбирается как WorldCell_<x>_<z> — пропущен");
        continue;
    }

    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (model == null)
    {
        bad++;
        sb.AppendLine(name + ": FBX не загрузился — пропущен");
        continue;
    }

    var temp = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(model);
    temp.name = name;

    var cell = temp.GetComponent<FrostSpace.Features.Map.WorldCell>();
    if (cell == null) cell = temp.AddComponent<FrostSpace.Features.Map.WorldCell>();
    cell.Setup(new UnityEngine.Vector2Int(ci, cj));

    foreach (var mf in temp.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
    {
        if (mf.sharedMesh == null) continue;
        var go = mf.gameObject;
        var mc = go.GetComponent<UnityEngine.MeshCollider>();
        if (mc == null)
        {
            mc = go.AddComponent<UnityEngine.MeshCollider>();
            colliders++;
        }
        if (mc.sharedMesh == null) mc.sharedMesh = mf.sharedMesh;

        // Метка границы владения: этот объект собран из FBX и принадлежит
        // конвейеру. Без неё следующая синхронизация не отличит его от
        // авторского и создаст дубликат.
        if (go.GetComponent<FrostSpace.Features.Map.CellMesh>() == null)
            go.AddComponent<FrostSpace.Features.Map.CellMesh>();

        meshes++;
        tris += mf.sharedMesh.triangles.Length / 3;
    }

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(temp, prefabDir + "/" + name + ".prefab");
    UnityEngine.Object.DestroyImmediate(temp);
    made++;
}

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

sb.AppendLine(string.Format("пересобрано префабов={0} битых={1}", made, bad));
sb.AppendLine(string.Format("мешей={0} коллайдеров поставлено={1} трис={2}", meshes, colliders, tris));
return sb.ToString();
