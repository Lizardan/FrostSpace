// Кадры трёх узлов после пересадки: ортоплан клетки, общий план содержимого и
// кадр на уровне пяток. Для каждого узла снимается и вариант «до» — актёры
// поднимаются обратно на величину сдвига, чтобы зазор был виден глазами.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/shoot_node_frames.cs
//
// Вне Play намеренно: хук захвата в Play дважды ронял редактор
// (docs/UNITY_EDITOR.md §5). Сцену не сохраняет, позы и активность клеток
// возвращает. Кадры — в Temp/, не в отслеживаемый Assets/Screenshots.
// Правила docs/UNITY_EDITOR.md §2: без using, полные имена типов.

var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

var dir = "Temp/verify/2026-11-12";
System.IO.Directory.CreateDirectory(dir);

UnityEngine.Transform cellsRoot = null;
foreach (var t in scene.GetRootGameObjects())
{
    var found = t.transform.Find("WorldCells");
    if (found != null) { cellsRoot = found; break; }
    if (t.name == "WorldCells") { cellsRoot = t.transform; break; }
}
if (cellsRoot == null) return "нет корня WorldCells";

var cam = UnityEngine.Camera.main;
if (cam == null) return "нет Camera.main";

var sb = new System.Text.StringBuilder();
bool fogWas = UnityEngine.RenderSettings.fog;

// Съёмка одной камеры: perspective или orthographic, без сохранения сцены.
System.Action<string, UnityEngine.Vector3, UnityEngine.Quaternion, float, float, bool> Shoot =
    delegate (string tag, UnityEngine.Vector3 pos, UnityEngine.Quaternion rot,
              float fov, float orthoSize, bool ortho)
{
    var go = new UnityEngine.GameObject("TEMP_NodeFrameCamera");
    var c = go.AddComponent<UnityEngine.Camera>();
    c.clearFlags = cam.clearFlags;
    c.backgroundColor = cam.backgroundColor;
    c.cullingMask = cam.cullingMask;
    c.nearClipPlane = 0.3f;
    c.farClipPlane = 6000f;
    c.transform.position = pos;
    c.transform.rotation = rot;
    c.orthographic = ortho;
    if (ortho) c.orthographicSize = orthoSize; else c.fieldOfView = fov;
    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
    var src = cam.gameObject.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
    if (src != null) { data.renderType = src.renderType; data.renderShadows = src.renderShadows; }

    int w = ortho ? 1400 : 1280;
    int h = ortho ? 1400 : 720;
    c.targetTexture = new UnityEngine.RenderTexture(w, h, 24);
    c.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = c.targetTexture;
    var tex = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBA32, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    var rt = c.targetTexture;
    c.targetTexture = null;
    System.IO.File.WriteAllBytes(dir + "/" + tag + ".png", tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    UnityEngine.Object.DestroyImmediate(rt);
    UnityEngine.Object.DestroyImmediate(go);
};

// Границы по рендерерам группы; includeInactive, чтобы погашенное тоже мерилось.
System.Func<UnityEngine.Transform, bool, UnityEngine.Bounds> BoundsOf =
    delegate (UnityEngine.Transform scope, bool onlyActors)
{
    var b = new UnityEngine.Bounds(scope.position, UnityEngine.Vector3.zero);
    bool any = false;
    foreach (var r in scope.GetComponentsInChildren<UnityEngine.Renderer>(true))
    {
        if (onlyActors && r.GetComponentInParent<FrostSpace.Features.Map.CellMesh>() != null) continue;
        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
    }
    return any ? b : new UnityEngine.Bounds(scope.position, new UnityEngine.Vector3(40f, 10f, 40f));
};

System.Action<string, string, string, float> shootNode =
    delegate (string cellName, string groupName, string title, float shift)
{
    var cell = cellsRoot.Find(cellName);
    if (cell == null) { sb.AppendLine(title + ": нет клетки " + cellName); return; }
    bool wasActive = cell.gameObject.activeSelf;
    if (!wasActive) cell.gameObject.SetActive(true);

    UnityEngine.Transform scope = cell;
    if (!string.IsNullOrEmpty(groupName))
    {
        var g = cell.Find(groupName);
        if (g == null) { sb.AppendLine(title + ": нет группы " + groupName); return; }
        scope = g;
    }

    // Актёры узла: всё, что не помечено CellMesh. Именно их сажали скрипты.
    var actors = new System.Collections.Generic.List<UnityEngine.Transform>();
    for (int i = 0; i < scope.childCount; i++)
    {
        var ch = scope.GetChild(i);
        if (ch.GetComponent<FrostSpace.Features.Map.CellMesh>() != null) continue;
        actors.Add(ch);
    }
    var rest = new System.Collections.Generic.List<UnityEngine.Vector3>();
    foreach (var a in actors) rest.Add(a.position);

    var cellBounds = BoundsOf(cell, false);
    var actorBounds = BoundsOf(scope, true);

    // Позы камер считаются ОДИН раз по состоянию «после» и не меняются в
    // проходе «до»: иначе камера сама поднимается на величину сдвига и
    // сравнивать кадры нельзя — весь кадр едет, а не актёры.
    var b0 = actorBounds;
    var c0 = b0.center;
    float reach = UnityEngine.Mathf.Max(b0.extents.x, b0.extents.z);
    var widePos = new UnityEngine.Vector3(c0.x - reach * 1.5f,
        b0.max.y + reach * 0.85f, c0.z - reach * 1.5f);
    var wideRot = UnityEngine.Quaternion.LookRotation(c0 - widePos);
    float eye = b0.min.y + 0.9f;
    var feetPos = new UnityEngine.Vector3(c0.x - reach * 0.9f, eye, c0.z - reach * 0.9f);
    var feetAim = new UnityEngine.Vector3(c0.x, b0.min.y + 0.25f, c0.z);
    var feetRot = UnityEngine.Quaternion.LookRotation(feetAim - feetPos);

    sb.AppendLine("=== " + title + " (" + cellName + ") ===");
    sb.AppendLine(string.Format("  актёров {0}, границы содержимого {1} ± {2}",
        actors.Count, c0.ToString("0.0"), b0.extents.ToString("0.0")));

    // Ортоплан всей клетки: туман снят, иначе сверху читается молоко.
    UnityEngine.RenderSettings.fog = false;
    float half = UnityEngine.Mathf.Max(cellBounds.extents.x, cellBounds.extents.z) * 0.52f;
    var topPos = new UnityEngine.Vector3(cellBounds.center.x,
        cellBounds.max.y + half * 2.2f, cellBounds.center.z);
    Shoot(cellName + "_ortho", topPos, UnityEngine.Quaternion.Euler(90f, 0f, 0f), 60f, half, true);
    UnityEngine.RenderSettings.fog = fogWas;
    sb.AppendLine(string.Format("  {0}/{1}_ortho: ортоплан, полуразмер {2:0} м, туман снят", dir, cellName, half));

    for (int pass = 0; pass < 2; pass++)
    {
        // pass 0 — «после» (как стоит сейчас), pass 1 — «до» (обратный сдвиг).
        string suffix = pass == 0 ? "after" : "before";
        if (pass == 1)
            for (int i = 0; i < actors.Count; i++)
                actors[i].position = rest[i] + new UnityEngine.Vector3(0f, -shift, 0f);

        Shoot(cellName + "_wide_" + suffix, widePos, wideRot, 50f, 0f, false);
        Shoot(cellName + "_feet_" + suffix, feetPos, feetRot, 55f, 0f, false);

        sb.AppendLine(string.Format("  {0}/{1}_wide_{2} и _feet_{2}: актёров {3}, низ «после» {4:0.000}",
            dir, cellName, suffix, actors.Count, b0.min.y));

        if (pass == 1)
            for (int i = 0; i < actors.Count; i++) actors[i].position = rest[i];
    }

    if (!wasActive) cell.gameObject.SetActive(false);
};

// shift — величина, на которую узел опущен 2026-11-12 (ADR 0036, поправка).
shootNode("WorldCell_-2_-2", null, "ДЕПО", -0.6006f);
shootNode("WorldCell_0_-2", "Settlement", "ПОСЕЛЕНИЕ", -0.4444f);
shootNode("WorldCell_0_0", "Station", "СТАНЦИЯ", -1.6482f);

UnityEngine.RenderSettings.fog = fogWas;
sb.AppendLine("позы актёров и активность клеток возвращены; сцена не сохранялась; туман " + fogWas);
return sb.ToString();