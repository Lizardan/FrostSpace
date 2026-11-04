// Наполнение станции «Север-7»: обломок корабля в ангаре и все двадцать моделей
// роботов проекта — по логике узлов.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/place_station_content.cs
//
// Узел живёт в префабе клетки (0,0) (ADR 0037): `sync_cell_prefabs.cs` трогает
// только помеченные `CellMesh`, авторское содержимое переживает пересборку
// частей. Поселение уже сделано так же — два узла читаются одним приёмом.
//
// Почему все двадцать моделей здесь: владелец задал станции роль «здесь много
// разных роботов», и тест `StationContentTests` меряет это числом. Модель,
// которая не появилась ни в одной сцене, — потерянный ассет: он собирается,
// весит в билде и никого не играет.
//
// Четверо говорящих стоят на открытом месте — у ворот, на платформе, у створа
// ангара. Разговор игра открывает лучом из центра экрана (`DialogueController.
// FindTarget`, 12 м) и только если игрок в 4 м от робота: робот внутри закрытого
// отсека — это декорация, а не персонаж, и реплики его никто не услышит.
// Остальные шестнадцать по логике живут в комнатах и смотрят за станками.
//
// Посадка — по факту геометрии, а не по таблице высот: для каждого актёра
// берётся нижняя точка rest-позы меша и ставится на поверхность (настил
// платформы +1,04 над полом узла, пол ангара +0,14, пол комплекса +0,26).
// Таблица высот один раз уже подвела депо, когда перрон считали по плите, а
// ходили по шапке вала.
var sb = new System.Text.StringBuilder();
const string CELL_PREFAB = "Assets/_Project/Content/Prefabs/World/WorldCell_0_0.prefab";
const string ROBOTS = "Assets/_Project/Content/Prefabs/Robots/";
const string MODELS = "Assets/_Project/Content/Models/Level/";
const string DIALOGUES = "Assets/_Project/Content/Data/Dialogues/";

float FLOOR = -4.440f;                       // PAD_LEVEL0["station"]
float DECK = FLOOR + 1.04f;                  // настил платформы: плита + шапка
float HANGAR_FLOOR = FLOOR + 0.14f;          // пол ангара: плита над снежным валом
float COMPLEX_FLOOR = FLOOR + 0.26f;         // пол отсеков: бетон по общей плите

var tNpc = System.Type.GetType("FrostSpace.Features.Npc.NpcController, FrostSpace.Features");
var tTalk = System.Type.GetType("FrostSpace.Features.Dialogue.DialogueSpeaker, FrostSpace.Features");
var tScan = System.Type.GetType("FrostSpace.Features.Scan.ScanTarget, FrostSpace.Features");
if (tNpc == null || tTalk == null || tScan == null) return "не найдены типы NPC/диалога/скана";

var root = UnityEditor.PrefabUtility.LoadPrefabContents(CELL_PREFAB);
if (root == null) return "нет префаба " + CELL_PREFAB;
var old = root.transform.Find("Station");
if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
var group = new UnityEngine.GameObject("Station");
group.transform.SetParent(root.transform, false);

System.Func<string, UnityEngine.Vector3, float, UnityEngine.Transform> Spawn =
    delegate (string path, UnityEngine.Vector3 at, float rotY)
{
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (asset == null) { sb.AppendLine("НЕТ МОДЕЛИ " + path); return null; }
    var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset);
    go.transform.SetParent(group.transform, false);
    go.transform.position = at;
    go.transform.rotation = UnityEngine.Quaternion.Euler(0f, rotY, 0f);
    return go.transform;
};

// Нижайшая точка rest-позы: у скиннутых мешей `renderer.bounds` в редакторе
// отстаёт, поэтому считаем от меша и матрицы.
System.Func<UnityEngine.Transform, float> Low = delegate (UnityEngine.Transform t)
{
    float min = float.PositiveInfinity;
    foreach (var sk in t.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(false))
    {
        if (sk.sharedMesh == null) continue;
        var c = sk.transform.localToWorldMatrix.MultiplyPoint(sk.sharedMesh.bounds.min);
        if (c.y < min) min = c.y;
    }
    foreach (var mf in t.GetComponentsInChildren<UnityEngine.MeshFilter>(false))
    {
        if (mf.sharedMesh == null) continue;
        var c = mf.transform.localToWorldMatrix.MultiplyPoint(mf.sharedMesh.bounds.min);
        if (c.y < min) min = c.y;
    }
    return min;
};

System.Action<UnityEngine.Transform, float> Seat = delegate (UnityEngine.Transform t, float surface)
{
    float low = Low(t);
    if (float.IsPositiveInfinity(low)) return;
    var p = t.position;
    t.position = new UnityEngine.Vector3(p.x, p.y + (surface - low), p.z);
};

System.Action<UnityEngine.Component, string, object> Set = delegate (UnityEngine.Component c, string name, object val)
{
    var so = new UnityEditor.SerializedObject(c);
    var p = so.FindProperty(name);
    if (p == null) { sb.AppendLine("нет поля " + name + " у " + c.GetType().Name); return; }
    if (val is string s0) p.stringValue = s0;
    else if (val is float f0) p.floatValue = f0;
    else if (val is bool b0) p.boolValue = b0;
    else p.objectReferenceValue = (UnityEngine.Object)val;
    so.ApplyModifiedPropertiesWithoutUndo();
};

// (модель, имя, x, z, поворот, пол, подпись скана, категория, диалог, маршрут)
// Маршрут — четыре точки; пустой массив значит «стоит в позе», таких на узле
// большинство: двадцать аниматоров в WebGL дороже, чем кажется.
var crew = new object[][]
{
    new object[] { "R1_PolarMechanic", "ST_R1_Mech", -60f, -30f, 20f, COMPLEX_FLOOR,
        "Механик ремзоны", "ЛЮДИ", "", new[] { -62f, -32f, -58f, -30f, -60f, -26f, -64f, -28f } },
    new object[] { "R2_Furnace", "ST_R2_Stoker", -88f, 46f, 300f, COMPLEX_FLOOR,
        "Кочегар котельной", "ЛЮДИ", "", new float[0] },
    new object[] { "R3_Scout", "ST_R3_Dispatcher", 14f, 26f, 250f, DECK,
        "Диспетчер станции", "ЛЮДИ", "StationDispatcher",
        new[] { 12f, 14f, 18f, 22f, 12f, 30f, 18f, 26f } },
    new object[] { "R4_Hauler", "ST_R4_Hauler", 12f, -40f, 90f, DECK,
        "Грузовик платформы", "ЛЮДИ", "", new float[0] },
    new object[] { "R5_Ranger", "ST_R5_Ranger", -60f, 66f, 0f, FLOOR,
        "Патрульный периметра", "ЛЮДИ", "",
        new[] { -80f, 66f, -40f, 66f, -20f, 60f, -60f, 70f } },
    new object[] { "R6_Smelter", "ST_R6_Smelter", -76f, -36f, 60f, COMPLEX_FLOOR,
        "Плавильщик ремзоны", "ЛЮДИ", "", new float[0] },
    new object[] { "R7_Drifter", "ST_R7_Waiter", 10f, 20f, 250f, DECK,
        "Пассажир у навеса", "ЛЮДИ", "", new float[0] },
    new object[] { "R8_Bulwark", "ST_R8_Guard", -98f, 20f, 90f, FLOOR,
        "Часовой у ворот", "ЛЮДИ", "StationGuard",
        new[] { -98f, 24f, -98f, 16f, -94f, 14f, -94f, 26f } },
    new object[] { "R9_Tinker", "ST_R9_Tinker", 40f, 20f, 200f, HANGAR_FLOOR,
        "Мастер у ангара", "ЛЮДИ", "", new float[0] },
    new object[] { "R10_Pipewright", "ST_R10_Pipes", -88f, 10f, 0f, COMPLEX_FLOOR,
        "Трубник склада", "ЛЮДИ", "", new float[0] },
    new object[] { "R11_Snowplow", "ST_R11_Plow", 38f, -10f, 120f, HANGAR_FLOOR,
        "Снегоочиститель ворот", "ОБОРУДОВАНИЕ", "", new float[0] },
    new object[] { "R12_Beacon", "ST_R12_Beacon", 10f, 44f, 180f, DECK,
        "Маяк платформы", "ОБОРУДОВАНИЕ", "", new float[0] },
    new object[] { "R13_Prospector", "ST_R13_Prospector", 44f, -24f, 30f, HANGAR_FLOOR,
        "Разведчик у обломков", "ЛЮДИ", "", new float[0] },
    new object[] { "R14_Medic", "ST_R14_Medic", -36f, 40f, 90f, FLOOR,
        "Врач медблока", "ЛЮДИ", "StationMedic",
        new[] { -38f, 36f, -34f, 44f, -40f, 44f, -42f, 36f } },
    new object[] { "R15_Courier", "ST_R15_Courier", 12f, 4f, 270f, DECK,
        "Курьер на платформе", "ЛЮДИ", "", new float[0] },
    new object[] { "R16_Warden", "ST_R16_Warden", -70f, 58f, 180f, FLOOR,
        "Начальник смены", "ЛЮДИ", "", new float[0] },
    new object[] { "R17_Hermit", "ST_R17_Store", -80f, 4f, 90f, COMPLEX_FLOOR,
        "Кладовщик склада", "ЛЮДИ", "", new float[0] },
    new object[] { "R18_Forge", "ST_R18_Forge", -50f, -26f, 330f, COMPLEX_FLOOR,
        "Кузнец ремзоны", "ЛЮДИ", "", new float[0] },
    new object[] { "R19_Tide", "ST_R19_Tide", 60f, 30f, 200f, HANGAR_FLOOR,
        "Насосный двор ангара", "ЛЮДИ", "", new float[0] },
    new object[] { "R20_Cogsmith", "ST_R20_Hangar", 38f, 12f, 270f, HANGAR_FLOOR,
        "Механик ангара", "ЛЮДИ", "StationHangar",
        new[] { 38f, 6f, 40f, 18f, 44f, 14f, 38f, 2f } },
};

int placed = 0, speaking = 0;
foreach (var r in crew)
{
    float x = (float)r[2], z = (float)r[3];
    float floor = (float)r[5];
    var tr = Spawn(ROBOTS + r[0] + "_Model.prefab", new UnityEngine.Vector3(x, floor + 0.5f, z), (float)r[4]);
    if (tr == null) continue;
    tr.gameObject.name = (string)r[1];
    Seat(tr, floor + 0.005f);
    var cc = tr.gameObject.AddComponent<UnityEngine.CharacterController>();
    cc.height = 1.7f; cc.radius = 0.34f; cc.center = new UnityEngine.Vector3(0f, 0.85f, 0f);
    cc.stepOffset = 0.2f; cc.skinWidth = 0.06f;
    var anim = tr.GetComponentInChildren<UnityEngine.Animator>();
    var npc = tr.gameObject.AddComponent(tNpc);
    var pts = (float[])r[9];
    var soNpc = new UnityEditor.SerializedObject(npc);
    var arr = soNpc.FindProperty("_waypoints");
    var route = new UnityEngine.GameObject("Route");
    route.transform.SetParent(tr, false);
    arr.arraySize = pts.Length / 2;
    for (int i = 0; i < pts.Length / 2; i++)
    {
        var wp = new UnityEngine.GameObject("WP_" + i);
        wp.transform.SetParent(route.transform, false);
        wp.transform.position = new UnityEngine.Vector3(pts[i * 2], floor + 0.02f, pts[i * 2 + 1]);
        arr.GetArrayElementAtIndex(i).objectReferenceValue = wp.transform;
    }
    soNpc.FindProperty("_animator").objectReferenceValue = anim;
    soNpc.FindProperty("_walkSpeed").floatValue = 0.8f;
    soNpc.ApplyModifiedPropertiesWithoutUndo();
    var scan = tr.gameObject.AddComponent(tScan);
    Set(scan, "_displayName", (string)r[6]);
    Set(scan, "_category", (string)r[7]);
    string dialogue = (string)r[8];
    if (dialogue.Length > 0)
    {
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            DIALOGUES + dialogue + ".asset");
        if (asset == null) { sb.AppendLine("НЕТ ДИАЛОГА " + dialogue); }
        else
        {
            var talk = tr.gameObject.AddComponent(tTalk);
            Set(talk, "_npc", npc);
            Set(talk, "_dialogue", asset);
            Set(talk, "_turnSpeed", 240f);
            speaking++;
        }
    }
    placed++;
}

// Движок обломка: единственная меню-модель, которая осталась в ангаре.
// Корпус, крылья и киль генерирует `station_parts._wreck` — их размеры держат
// пролёт, а модель из диорамы меню в 2,2 раза даёт узнаваемый агрегат с
// человеческим масштабом. Стоит на носовой половине: ось корабля идёт от
// (54, −6) к (78, 30), и с ворот движок видно первым.
foreach (var wr in new object[][]
{
    new object[] { "MenuWreckEngine", 55.0f, -5.0f, 214f, 2.2f, "Двигатель корабля", "ЗДАНИЕ" },
})
{
    var tr = Spawn(MODELS + wr[0] + ".fbx",
                   new UnityEngine.Vector3((float)wr[1], HANGAR_FLOOR, (float)wr[2]), (float)wr[3]);
    if (tr == null) continue;
    tr.localScale = UnityEngine.Vector3.one * (float)wr[4];
    Seat(tr, HANGAR_FLOOR - 0.20f);
    var scan = tr.gameObject.AddComponent(tScan);
    Set(scan, "_displayName", (string)wr[5]);
    Set(scan, "_category", (string)wr[6]);
    var mf = tr.GetComponentInChildren<UnityEngine.MeshFilter>();
    if (mf != null)
    {
        var col = tr.gameObject.AddComponent<UnityEngine.MeshCollider>();
        col.sharedMesh = mf.sharedMesh;
    }
}

// Свет узла: ангар, платформа, ворота и холодное пятно под обшивкой — то, что
// делает станцию ночной, а не просто освещённой.
System.Action<string, UnityEngine.Vector3, UnityEngine.Color, float, float> Lamp =
    delegate (string name, UnityEngine.Vector3 at, UnityEngine.Color color, float range, float power)
{
    var go = new UnityEngine.GameObject(name);
    go.transform.SetParent(group.transform, false);
    go.transform.position = at;
    var l = go.AddComponent<UnityEngine.Light>();
    l.type = UnityEngine.LightType.Point;
    l.color = color; l.range = range; l.intensity = power;
    l.shadows = UnityEngine.LightShadows.None;
};
var warm = new UnityEngine.Color(1f, 0.78f, 0.45f);
var cold = new UnityEngine.Color(0.45f, 0.86f, 1f);
Lamp("HangarLightA", new UnityEngine.Vector3(50f, FLOOR + 7.4f, 30f), warm, 46f, 900f);
Lamp("HangarLightB", new UnityEngine.Vector3(80f, FLOOR + 7.4f, -6f), warm, 46f, 900f);
Lamp("WreckGlow", new UnityEngine.Vector3(66f, HANGAR_FLOOR + 1.6f, 12f), cold, 18f, 320f);
Lamp("PlatformLightA", new UnityEngine.Vector3(10f, DECK + 4.4f, -34f), warm, 26f, 520f);
Lamp("PlatformLightB", new UnityEngine.Vector3(10f, DECK + 4.4f, 34f), warm, 26f, 520f);
Lamp("GateLight", new UnityEngine.Vector3(-98f, FLOOR + 3.2f, 20f), warm, 24f, 480f);
Lamp("ComplexLight", new UnityEngine.Vector3(-68f, FLOOR + 5.2f, 58f), warm, 30f, 560f);

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, CELL_PREFAB);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
sb.AppendLine("роботов поставлено: " + placed + " из 20, говорящих " + speaking);
return sb.ToString();
