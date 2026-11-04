// Расстановка актёров, света и кадра прибытия депо по отметке пола двора.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/place_depot_actors.cs
//
// Переиспользуемый скрипт: запускается всякий раз, когда двигается двор депо.
// Метки заданы ПРИВЫШКОЙ над головкой главного пути (таблица `HH`), а не
// абсолютной высотой: кто стоит на перроне, тот и остаётся на перроне, когда
// двор едет. Таблица головки — вывод `Temp/reseat_marks.py` по
// `Tools/Blender/world_layout.py`; она обязана соответствовать сборке клеток.
//
// Правит ДВА уровня. Актёры депо живут внутри префаба клетки
// (`WorldCell_-2_-2.prefab`, ADR 0037/0039), поэтому сначала пересаживаются
// корни инстансов в префабе, затем те же числа применяются к сцене, а
// внутренние корни моделей (одно имя с инстансом!) возвращаются к позам
// источника. `GameObject.Find` возвращает первый совпавший трансформ, и без
// этого условия скрипт двигал бы модель внутри префаба, оставляя корень на
// месте: робот улетал бы на 15 м вверх местным смещением.
//
// Привычки сняты с принятой сцены 2026-11-04, когда двор стал ровным: пол
// площадки −1,060, плита перрона +0,90 над головкой, плюс 0,14 снеговой шапки
// вала → ходить по перрону нужно на +1,04, а тот, у кого пятка на 5 мм ниже
// корня, стоит на +1,045. Терраса двора — на 7 см ниже настила, то есть +0,97.
// Пол кузова +1,00. План (x, z) скрипт не трогает — правит только высоты.
//
// ВНИМАНИЕ: скрипт перезаписывает позиции, сериализованные числа прибытия,
// сохраняет префаб клетки и сцену. Перед запуском убедиться, что сцена
// `Frontier` активна, редактор не в Play и копия сцены лежит в `Temp/backup/`.
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.name != "Frontier") return "активна не Frontier: " + scene.name;
if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

const string CELL_PREFAB = "Assets/_Project/Content/Prefabs/World/WorldCell_-2_-2.prefab";

// Головка рельса по оси сцены z, шаг 5 м: ровный двор и начало волны стола у
// северного края (там линия выходит из площадки).
float[] HZ = new float[] { -80f, -75f, -70f, -65f, -60f, -55f, -50f, -45f, -40f, -35f, -30f,
    -25f, -20f, -15f, -10f, -5f, 0f, 5f, 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f,
    60f, 65f, 70f, 75f, 80f };
float[] HH = new float[] { -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f,
    -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f,
    -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.060f,
    -1.060f, -1.060f, -1.060f, -1.060f, -1.060f, -1.058f, -1.055f, -1.051f };

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

// (имя, привычка над головкой). Свет и объём снегопада меряются от той же
// головки: стол линии и пол узла прирезаны друг к другу, поэтому одна база
// держит и настил перрона, и мачты, и снег над двором.
string[] names = { "DepotTrain", "R16_Warden_A", "R16_Warden_B", "R7_Drifter", "R8_Bulwark",
    "R9_Tinker", "R20_Cogsmith", "DepotDoorSpill", "DepotDoorFill", "DepotLamp",
    "YardLampDocks", "WorkLight", "SidingLight", "BoothLamp", "FrontierSnowfall",
    "Main Camera", "Player" };
float[] add = { 1.000f, 1.045f, 1.045f, 0.971f, 0.970f, 0.970f, 0.970f, 3.850f, 3.050f,
    4.344f, 4.763f, 7.514f, 5.594f, 2.344f, 5.980f, 2.696f, 1.200f };

// Корень инстанса — тот, что висит на корне клетки (в префабе) или на объекте
// клетки (в сцене). Одноимённый ребёнок внутри префаба модели — не он.
System.Func<UnityEngine.Transform, string, UnityEngine.Transform> rootOf =
    delegate (UnityEngine.Transform scope, string name)
{
    for (int i = 0; i < scope.childCount; i++)
    {
        var c = scope.GetChild(i);
        if (c.name == name) return c;
    }
    return null;
};

// 1. Префаб клетки: актёры депо живут там, а не в сцене.
var fw = UnityEngine.GameObject.Find("FrontierWorld");
var cells = fw != null ? rootOf(fw.transform, "WorldCells") : null;
int moved = 0;
if (cells != null && rootOf(cells, "WorldCell_-2_-2") != null)
{
    var prefabRoot = UnityEditor.PrefabUtility.LoadPrefabContents(CELL_PREFAB);
    for (int k = 0; k < names.Length; k++)
    {
        var t = rootOf(prefabRoot.transform, names[k]);
        if (t == null) continue;                       // сценные объекты не в префабе
        var lp = t.localPosition;
        lp.y = head(lp.z) + add[k];
        t.localPosition = lp;
        moved++;
    }
    // Состав: крен вагона (2,705°) и местная высота −0,022 были подгонкой под
    // уклон старого стола. Двор ровный, стол на всём подходе −1,060 → подгонка
    // не нужна: низ колеса на 1,00 ниже пола вагона, то есть в головку.
    var carP = rootOf(prefabRoot.transform, "DepotTrain");
    if (carP != null)
    {
        var tc = rootOf(carP, "TrainCar");
        if (tc != null)
        {
            var lp = tc.localPosition;
            tc.localPosition = new UnityEngine.Vector3(lp.x, 0f, lp.z);
            tc.localRotation = UnityEngine.Quaternion.identity;
        }
        var gc = rootOf(carP, "Prop_GravCart");
        if (gc != null)
        {
            var lp = gc.localPosition;
            gc.localPosition = new UnityEngine.Vector3(lp.x, 0f, lp.z);
            var le = gc.localEulerAngles;
            gc.localEulerAngles = new UnityEngine.Vector3(0f, le.y, 0f);
        }
    }
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefabRoot, CELL_PREFAB);
    UnityEditor.PrefabUtility.UnloadPrefabContents(prefabRoot);
    sb.AppendLine("префаб: пересажено корней " + moved);
}
else sb.AppendLine("нет WorldCells — префаб не тронут");

// 2. Сцена: те же высоты по корням инстансов и возврат внутренних корней.
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(FindObjectsSortMode.None))
{
    int idx = -1;
    for (int k = 0; k < names.Length; k++) if (t.name == names[k]) idx = k;
    if (idx < 0) continue;
    bool onCell = t.parent != null && (t.parent.name == "WorldCell_-2_-2" ||
                                        t.parent.name == "FrontierWorld");
    if (onCell || t.parent == null)
    {
        var p = t.position;
        t.position = new UnityEngine.Vector3(p.x, head(p.z) + add[idx], p.z);
    }
    else
    {
        // внутренний корень модели: вернуть позу источника, если её сдвинули
        var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (src != null && (t.localPosition != src.transform.localPosition ||
                            t.localRotation != src.transform.localRotation))
        {
            t.localPosition = src.transform.localPosition;
            t.localRotation = src.transform.localRotation;
            sb.AppendLine("  возвращена поза модели " + t.name + " (parent=" + t.parent.name + ")");
        }
    }
}
// Состав в сцене — те же нули, что в префабе.
var trainScene = UnityEngine.GameObject.Find("DepotTrain");
if (trainScene != null)
{
    var tc = rootOf(trainScene.transform, "TrainCar");
    if (tc != null)
    {
        var lp = tc.localPosition;
        tc.localPosition = new UnityEngine.Vector3(lp.x, 0f, lp.z);
        tc.localRotation = UnityEngine.Quaternion.identity;
    }
    var gc = rootOf(trainScene.transform, "Prop_GravCart");
    if (gc != null)
    {
        var lp = gc.localPosition;
        gc.localPosition = new UnityEngine.Vector3(lp.x, 0f, lp.z);
        var le = gc.localEulerAngles;
        gc.localEulerAngles = new UnityEngine.Vector3(0f, le.y, 0f);
    }
}

// 3. Кадр прибытия: те же привычки. Значения обязаны совпасть с дефолтами
// `DepotArrivalDirector` — это меряет тест `FrontierSceneSyncTests`.
var dir = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
if (dir == null) return sb + "\nнет директора";
var so = new UnityEditor.SerializedObject(dir);
float[] pathAdd = { 3.050f, 3.030f, 3.241f, 3.200f, 3.150f, 3.100f };
var path = so.FindProperty("_path");
if (path != null)
{
    for (int k = 0; k < path.arraySize && k < pathAdd.Length; k++)
    {
        var pos = path.GetArrayElementAtIndex(k).FindPropertyRelative("Position");
        var v = pos.vector3Value;
        v.y = head(v.z) + pathAdd[k];
        pos.vector3Value = v;
        sb.AppendLine(string.Format("_path[{0}]     ({1:0.00}, {2:0.00}, {3:0.00})", k, v.x, v.y, v.z));
    }
}
string[] vecs = { "_doorAim", "_r13Exit", "_r13Face", "_cartExit", "_playerExit" };
// Цель в проёме: пол кузова (+1,00) плюс 1,10 м до точки прицела. Точки выхода
// встают на настил перрона (+1,04), R13 — на +1,045 из-за пятки под корнем.
float[] vecAdd = { 2.100f, 1.045f, 1.301f, 1.040f, 1.040f };
for (int v = 0; v < vecs.Length; v++)
{
    var p = so.FindProperty(vecs[v]);
    if (p == null) { sb.AppendLine("нет поля " + vecs[v]); continue; }
    var val = p.vector3Value;
    val.y = head(val.z) + vecAdd[v];
    p.vector3Value = val;
    sb.AppendLine(string.Format("{0,-17} ({1:0.00}, {2:0.00}, {3:0.00})", vecs[v], val.x, val.y, val.z));
}
so.ApplyModifiedPropertiesWithoutUndo();

// 4. Проверка ногами: самая верхняя опора ПОД низом геометрии, не считая
// самого объекта. Уровень и роботы лежат на слое Default, поэтому mask не
// помогает — перебираем все попадания. Это ловит то, чего числами двора не
// видно: отметка посчитана верно, а коллайдер клетки под меткой другой формы.
sb.AppendLine("--- ступни против опоры ---");
var found = new UnityEngine.RaycastHit[32];
foreach (var n in names)
{
    var go = UnityEngine.GameObject.Find(n);
    if (go == null) continue;
    float feet = float.PositiveInfinity;
    foreach (var sk in go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(false))
    {
        if (sk.sharedMesh == null) continue;
        var b = sk.sharedMesh.bounds;
        var c = sk.transform.localToWorldMatrix.MultiplyPoint(new UnityEngine.Vector3(0, b.min.y, 0));
        if (c.y < feet) feet = c.y;
    }
    foreach (var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(false))
    {
        if (mf.sharedMesh == null) continue;
        var b = mf.sharedMesh.bounds;
        var c = mf.transform.localToWorldMatrix.MultiplyPoint(new UnityEngine.Vector3(0, b.min.y, 0));
        if (c.y < feet) feet = c.y;
    }
    if (float.IsPositiveInfinity(feet)) continue;      // свет и объёмы мерить нечем
    var origin = new UnityEngine.Vector3(go.transform.position.x, feet + 0.6f, go.transform.position.z);
    int count = UnityEngine.Physics.RaycastNonAlloc(new UnityEngine.Ray(origin, UnityEngine.Vector3.down),
        found, 8f);
    float best = float.NegativeInfinity; string who = "";
    for (int i = 0; i < count; i++)
    {
        var t = found[i].collider.transform;
        if (t == go.transform || t.IsChildOf(go.transform)) continue;
        float y = origin.y - found[i].distance;
        if (y > best) { best = y; who = found[i].collider.gameObject.name; }
    }
    if (float.IsNegativeInfinity(best))
    {
        sb.AppendLine(string.Format("  {0,-17} низ {1:0.000} — опоры нет", n, feet));
        continue;
    }
    float gap = feet - best;
    sb.AppendLine(string.Format("  {0,-17} низ {1:0.000}  опора {2:0.000} ({3})  {4}",
        n, feet, best, who, gap > 0.06f ? "ВИСИТ " + gap.ToString("0.000")
        : (gap < -0.06f ? "ТОНЕТ " + (-gap).ToString("0.000") : "вровень")));
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
sb.AppendLine("сохранено сцены=" + saved);
return sb.ToString();
