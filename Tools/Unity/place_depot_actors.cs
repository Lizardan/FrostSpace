// Расстановка актёров депо по новым отметкам двора.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/place_depot_actors.cs
//
// Переиспользуемый скрипт: запускается всякий раз, когда двигается двор депо.
// Головка рельса в депо больше не ноль сцены: двор наклонён вдоль магистрали, и
// её отметка задана таблицей из `world_layout` (шаг 5 м по локальному z). Все
// высоты актёров считаются от неё, поэтому «порог двери вровень с платформой»
// держится в любой точке двора, а не только там, где кадр был принят.
//
// ВНИМАНИЕ: скрипт перезаписывает позиции актёров и ключи кадра прибытия и
// сохраняет сцену. Перед запуском убедиться, что сцена `Frontier` активна и
// редактор не в Play.
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

float[] HZ = new float[] { -200f, -195f, -190f, -185f, -180f, -175f, -170f, -165f, -160f, -155f,
    -150f, -145f, -140f, -135f, -130f, -125f, -120f, -115f, -110f, -105f, -100f, -95f, -90f, -85f,
    -80f, -75f, -70f, -65f, -60f, -55f, -50f, -45f, -40f, -35f, -30f, -25f, -20f, -15f, -10f, -5f,
    0f, 5f, 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f,
    95f, 100f };
float[] HH = new float[] { -8.454f, -8.654f, -8.854f, -9.054f, -9.254f, -9.454f, -9.654f, -9.854f,
    -10.054f, -10.254f, -10.454f, -10.654f, -10.854f, -11.054f, -11.254f, -11.454f, -11.654f,
    -11.854f, -12.054f, -12.254f, -12.454f, -12.654f, -12.854f, -13.054f, -13.254f, -13.429f,
    -13.604f, -13.778f, -13.953f, -14.127f, -14.308f, -14.502f, -14.696f, -14.890f, -15.084f,
    -15.282f, -15.501f, -15.719f, -15.938f, -16.157f, -16.394f, -16.644f, -16.894f, -17.144f,
    -17.420f, -17.712f, -18.004f, -18.308f, -18.659f, -19.011f, -19.394f, -19.835f, -20.289f,
    -20.880f, -21.270f, -21.360f, -21.449f, -21.539f, -21.628f, -21.718f, -21.807f };

System.Func<float, float> head = delegate (float z)
{
    if (z <= HZ[0]) return HH[0];
    if (z >= HZ[HZ.Length - 1]) return HH[HH.Length - 1];
    for (int k = 0; k < HZ.Length - 1; k++)
    {
        if (z < HZ[k] || z > HZ[k + 1]) continue;
        float t = (z - HZ[k]) / (HZ[k + 1] - HZ[k]);
        return HH[k] + (HH[k + 1] - HH[k]) * t;
    }
    return HH[HH.Length - 1];
};

// (имя, x, z, прибавка к головке рельса) — прибавка есть разность прежней высоты
// актёра и прежней головки (0,0): кто стоял на платформе, тот и стоит на ней.
string[] names = { "DepotTrain", "DepotDoorSpill", "DepotDoorFill", "R16_Warden_A", "R16_Warden_B",
                   "R7_Drifter", "R8_Bulwark", "FrontierSnowfall", "Main Camera" };
float[] xs = { -8f, -3.3f, -5.6f, -2.4f, 0.6f, 9.59f, 11.51f, 0f, -2.3f };
float[] zs = { -2f, -1f, -1f, -4.8f, -5f, 0.13f, 1.74f, 0f, -1.6f };
float[] add = { 1f, 2.8f, 2f, 0.7f, 0.7f, 0.68f, 0.58f, 5.98f, 2.7f };
for (int k = 0; k < names.Length; k++)
{
    var go = UnityEngine.GameObject.Find(names[k]);
    if (go == null) { sb.AppendLine("нет " + names[k]); continue; }
    var p = go.transform.position;
    float y = head(zs[k]) + add[k];
    go.transform.position = new UnityEngine.Vector3(xs[k], y, zs[k]);
    sb.AppendLine(string.Format("{0,-17} ({1:0.00}, {2:0.00}, {3:0.00})  было ({4:0.00}, {5:0.00}, {6:0.00})",
        names[k], xs[k], y, zs[k], p.x, p.y, p.z));
}

// Лампа переезжает под новый навес: прежняя висела над снятым навесом из кубов.
var lamp = UnityEngine.GameObject.Find("DepotLamp");
if (lamp != null)
{
    float ly = head(-16f) - 0.6f + 3.35f;
    lamp.transform.position = new UnityEngine.Vector3(9.2f, ly, -16f);
    sb.AppendLine(string.Format("{0,-17} ({1:0.00}, {2:0.00}, {3:0.00})", "DepotLamp", 9.2f, ly, -16f));
}

// Кадр прибытия: ключи пути и мировые точки цели едут вместе с путём.
var dir = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
if (dir == null) return sb + "\nнет директора";
var so = new UnityEditor.SerializedObject(dir);
var path = so.FindProperty("_path");
for (int k = 0; k < path.arraySize; k++)
{
    var pos = path.GetArrayElementAtIndex(k).FindPropertyRelative("Position");
    float y = head(pos.vector3Value.z) + pos.vector3Value.y;
    pos.vector3Value = new UnityEngine.Vector3(pos.vector3Value.x, y, pos.vector3Value.z);
}
string[] vecs = { "_doorAim", "_r13Exit", "_r13Face", "_cartExit", "_playerExit" };
for (int v = 0; v < vecs.Length; v++)
{
    var p = so.FindProperty(vecs[v]);
    if (p == null) { sb.AppendLine("нет поля " + vecs[v]); continue; }
    var val = p.vector3Value;
    p.vector3Value = new UnityEngine.Vector3(val.x, head(val.z) + val.y, val.z);
    sb.AppendLine(string.Format("{0,-17} ({1:0.00}, {2:0.00}, {3:0.00})", vecs[v],
        p.vector3Value.x, p.vector3Value.y, p.vector3Value.z));
}
so.ApplyModifiedPropertiesWithoutUndo();

// Проверка ногами: луч вниз от каждого актёра по чанкам. Висячий остаток —
// это то, что числами двора не видно: отметка посчитана верно, а коллайдер
// клетки под ним другой формы.
sb.AppendLine("--- луч вниз (дистанция до земли от ступней) ---");
string[] probe = { "Player", "DepotTrain", "R16_Warden_A", "R16_Warden_B", "R7_Drifter",
                   "R8_Bulwark", "DepotGate?", "DepotLamp" };
foreach (var n in probe)
{
    var go = UnityEngine.GameObject.Find(n);
    if (go == null) continue;
    var o = go.transform.position;
    var ray = new UnityEngine.Ray(new UnityEngine.Vector3(o.x, o.y + 4f, o.z), UnityEngine.Vector3.down);
    UnityEngine.RaycastHit hit;
    if (UnityEngine.Physics.Raycast(ray, out hit, 20f))
        sb.AppendLine(string.Format("  {0,-15} feet→{1:0.000} м, слой {2}", n, hit.distance - 4f,
            UnityEngine.LayerMask.LayerToName(hit.collider.gameObject.layer)));
    else
        sb.AppendLine(string.Format("  {0,-15} НИЧЕГО под ногами в 20 м", n));
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
sb.AppendLine("сохранено=" + saved);
return sb.ToString();
