// Наполнение поселения: метеовышка, четыре говорящих робота, восемь машин-животных,
// орёл, свет и пропы — всё внутри префаба клетки (0,−2).
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/place_settlement_content.cs
//
// Почему в префабе, а не на сцене: клетка — владелец содержимого (ADR 0037),
// и депо уже показывает образец — актёры, свет и слоты живут внутри
// `WorldCell_-2_-2.prefab`, а `sync_cell_prefabs.cs` их не трогает, потому что
// у них нет метки `CellMesh`. Поселение собрано так же, иначе выгрузка клетки
// оставляла бы в мире пустой двор.
//
// Heights: пол узла держит одну отметку −0,175 м (ADR 0041), поэтому каждая
// метка = пол + привычка. Привычки сняты с геометрии частей: стакан вышки
// кончается на +1,05, настил загона — на уровне пола, ступенька крыльца — +0,15.
//
// ВНИМАНИЕ: скрипт перезаписывает содержимое группы `Settlement` в префабе
// клетки. Перед запуском — копия сцены в `Temp/backup/`.
var sb = new System.Text.StringBuilder();
const string CELL_PREFAB = "Assets/_Project/Content/Prefabs/World/WorldCell_0_-2.prefab";
const string ROBOTS = "Assets/_Project/Content/Prefabs/Robots/";
const string ANIMALS = "Assets/_Project/Content/Prefabs/RobotAnimals/";
const string DIALOGUES = "Assets/_Project/Content/Data/Dialogues/";
const string MODELS = "Assets/_Project/Content/Models/Level/";
float FLOOR = -0.175f;                       // пол узла: PAD_LEVEL0["settlement"]

var tNpc = System.Type.GetType("FrostSpace.Features.Npc.NpcController, FrostSpace.Features");
var tAir = System.Type.GetType("FrostSpace.Features.Npc.AirPatrolController, FrostSpace.Features");
var tTalk = System.Type.GetType("FrostSpace.Features.Dialogue.DialogueSpeaker, FrostSpace.Features");
var tScan = System.Type.GetType("FrostSpace.Features.Scan.ScanTarget, FrostSpace.Features");
if (tNpc == null || tTalk == null || tScan == null) return "не найдены типы NPC/диалога/скана";

var root = UnityEditor.PrefabUtility.LoadPrefabContents(CELL_PREFAB);
if (root == null) return "нет префаба " + CELL_PREFAB;

// Старое содержимое группы уходит: скрипт — источник истины, и второй прогон
// обязан дать тот же набор, а не удвоенный.
var old = root.transform.Find("Settlement");
if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
var group = new UnityEngine.GameObject("Settlement");
group.transform.SetParent(root.transform, false);

System.Func<string, UnityEngine.Vector3, float, UnityEngine.Transform> Spawn =
    delegate (string prefabPath, UnityEngine.Vector3 at, float rotY)
{
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
    if (asset == null) { sb.AppendLine("НЕТ МОДЕЛИ " + prefabPath); return null; }
    var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset);
    go.transform.SetParent(group.transform, false);
    go.transform.position = at;
    go.transform.rotation = UnityEngine.Quaternion.Euler(0f, rotY, 0f);
    return go.transform;
};

// Маршрут: четыре точки внутри зоны. NpcController берёт путь с детей, поэтому
// узлы живут под отдельным `Route`, а не в корнях загона.
System.Func<UnityEngine.Transform, UnityEngine.Vector3[], UnityEngine.Transform[]> Route =
    delegate (UnityEngine.Transform owner, UnityEngine.Vector3[] pts)
{
    var route = new UnityEngine.GameObject("Route");
    route.transform.SetParent(owner, false);
    var list = new UnityEngine.Transform[pts.Length];
    for (int i = 0; i < pts.Length; i++)
    {
        var wp = new UnityEngine.GameObject("WP_" + i);
        wp.transform.SetParent(route.transform, false);
        wp.transform.position = pts[i];
        list[i] = wp.transform;
    }
    return list;
};

System.Action<UnityEngine.Component, string, object> Set = delegate (UnityEngine.Component c, string name, object val)
{
    var so = new UnityEditor.SerializedObject(c);
    var p = so.FindProperty(name);
    if (p == null) { sb.AppendLine("нет поля " + name + " у " + c.GetType().Name); return; }
    if (val is string s0) p.stringValue = s0;
    else if (val is float f0) p.floatValue = f0;
    else if (val is int i0) p.intValue = i0;
    else if (val is bool b0) p.boolValue = b0;
    else p.objectReferenceValue = (UnityEngine.Object)val;
    so.ApplyModifiedPropertiesWithoutUndo();
};

// (модель, имя, x, z, поворот, диалог, подпись скана, категория, маршрут)
var robots = new object[][]
{
    new object[] { "R3_Scout_Model", "R3_Weatherwatch", 46f, 40f, 200f, "SettlementWeather",
        "Пост метеонаблюдения", "ОБОРУДОВАНИЕ",
        new[] { 46f, -2f, 44f, -2f, 48f, -2f, 48f, 0f } },
    new object[] { "R17_Hermit_Model", "R17_Keeper", 17f, 20f, 90f, "SettlementShelter",
        "Смотритель приюта", "ЛЮДИ",
        new[] { 14f, 14f, 20f, 14f, 20f, 24f, 14f, 24f } },
    new object[] { "R14_Medic_Model", "R14_Powerman", 18f, 36f, 180f, "SettlementPower",
        "Машинист подстанции", "ЛЮДИ",
        new[] { 15f, 35f, 27f, 35f, 27f, 38f, 15f, 38f } },
    new object[] { "R18_Forge_Model", "R18_Forge", 40f, 8f, 300f, "SettlementWorkshop",
        "Мастерская приюта", "ЗДАНИЕ",
        new[] { 39f, 5f, 41f, 5f, 41f, 12f, 39f, 12f } },
};

int placed = 0;
foreach (var r in robots)
{
    float x = (float)r[2], z = (float)r[3];
    var tr = Spawn(ROBOTS + r[0] + ".prefab", new UnityEngine.Vector3(x, FLOOR + 0.005f, z), (float)r[4]);
    if (tr == null) continue;
    tr.gameObject.name = (string)r[1];
    var cc = tr.gameObject.AddComponent<UnityEngine.CharacterController>();
    cc.height = 1.7f; cc.radius = 0.34f; cc.center = new UnityEngine.Vector3(0f, 0.85f, 0f);
    cc.stepOffset = 0.2f; cc.skinWidth = 0.06f;
    var anim = tr.GetComponentInChildren<UnityEngine.Animator>();
    var npc = tr.gameObject.AddComponent(tNpc);
    var pts = (float[])r[8];
    var wps = new UnityEngine.Vector3[pts.Length / 2];
    for (int i = 0; i < wps.Length; i++)
        wps[i] = new UnityEngine.Vector3(pts[i * 2], FLOOR + 0.02f, pts[i * 2 + 1]);
    var soNpc = new UnityEditor.SerializedObject(npc);
    var arr = soNpc.FindProperty("_waypoints");
    arr.arraySize = wps.Length;
    var route = Route(tr, wps);
    for (int i = 0; i < wps.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = route[i];
    soNpc.FindProperty("_animator").objectReferenceValue = anim;
    soNpc.FindProperty("_walkSpeed").floatValue = 0.85f;
    soNpc.ApplyModifiedPropertiesWithoutUndo();
    var scan = tr.gameObject.AddComponent(tScan);
    Set(scan, "_displayName", (string)r[6]);
    Set(scan, "_category", (string)r[7]);
    var talk = tr.gameObject.AddComponent(tTalk);
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DIALOGUES + r[5] + ".asset");
    Set(talk, "_npc", npc);
    Set(talk, "_dialogue", asset);
    Set(talk, "_turnSpeed", 240f);
    placed++;
}

// (модель, имя, x, z, поворот, подпись, категория, маршрут)
var beasts = new object[][]
{
    new object[] { "Sheep", "AN_Sheep_A", 15f, 2f, 20f, "Овечка «Погрузчик-4»", "ЖИВОТНЫЕ",
        new[] { 14f, 0f, 20f, 1f, 19f, 7f, 13f, 6f } },
    new object[] { "Sheep", "AN_Sheep_B", 19f, 5f, 140f, "Овечка «Погрузчик-9»", "ЖИВОТНЫЕ",
        new[] { 20f, 6f, 14f, 5f, 15f, 1f, 19f, 2f } },
    new object[] { "Cow", "AN_Cow", 17f, 18f, 300f, "Корова «Тягач»", "ЖИВОТНЫЕ",
        new[] { 15f, 15f, 20f, 17f, 18f, 23f, 14f, 21f } },
    new object[] { "Llama", "AN_Llama", 20f, 22f, 60f, "Лама «Снегоход»", "ЖИВОТНЫЕ",
        new[] { 20f, 14f, 16f, 20f, 20f, 25f, 14f, 24f } },
    new object[] { "Horse", "AN_Horse", 30f, 18f, 250f, "Конь «Караван-12»", "ЖИВОТНЫЕ",
        new[] { 28f, 14f, 35f, 16f, 34f, 24f, 29f, 22f } },
    new object[] { "Pig", "AN_Pig", 33f, 20f, 110f, "Кабан «Буровая»", "ЖИВОТНЫЕ",
        new[] { 30f, 13f, 36f, 20f, 31f, 25f, 27f, 18f } },
    new object[] { "Dog", "AN_Dog", 38f, 34f, 180f, "Пёс «Часовой»", "ЖИВОТНЫЕ",
        new[] { 37f, 33f, 42f, 33f, 43f, 37f, 38f, 37f } },
    new object[] { "Cat", "AN_Cat", 36f, 36f, 40f, "Кошка «Диспетчер»", "ЖИВОТНЫЕ",
        new[] { 36f, 35f, 39f, 35f, 39f, 37f, 36f, 37f } },
};

foreach (var b in beasts)
{
    float x = (float)b[2], z = (float)b[3];
    var tr = Spawn(ANIMALS + b[0] + "/RA_" + b[0] + "_Model.prefab",
                   new UnityEngine.Vector3(x, FLOOR + 0.01f, z), (float)b[4]);
    if (tr == null) continue;
    tr.gameObject.name = (string)b[1];
    var cc = tr.gameObject.AddComponent<UnityEngine.CharacterController>();
    cc.height = 0.9f; cc.radius = 0.32f; cc.center = new UnityEngine.Vector3(0f, 0.45f, 0f);
    // Шаг у мелкой модели обязан быть меньше половины её роста: модели животных
    // приезжают со своим масштабом, и штатные 0,3 м Unity ругает прямо в
    // `LoadPrefabContents` — тест на этом падает, а не игра.
    cc.stepOffset = 0.08f; cc.skinWidth = 0.03f;
    var anim = tr.GetComponentInChildren<UnityEngine.Animator>();
    var npc = tr.gameObject.AddComponent(tNpc);
    var pts = (float[])b[7];
    var wps = new UnityEngine.Vector3[pts.Length / 2];
    for (int i = 0; i < wps.Length; i++)
        wps[i] = new UnityEngine.Vector3(pts[i * 2], FLOOR + 0.02f, pts[i * 2 + 1]);
    var soNpc = new UnityEditor.SerializedObject(npc);
    var arr = soNpc.FindProperty("_waypoints");
    arr.arraySize = wps.Length;
    var route = Route(tr, wps);
    for (int i = 0; i < wps.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = route[i];
    soNpc.FindProperty("_animator").objectReferenceValue = anim;
    soNpc.FindProperty("_walkSpeed").floatValue = 0.45f;
    soNpc.FindProperty("_idleMin").floatValue = 4f;
    soNpc.FindProperty("_idleMax").floatValue = 14f;
    soNpc.ApplyModifiedPropertiesWithoutUndo();
    var scan = tr.gameObject.AddComponent(tScan);
    Set(scan, "_displayName", (string)b[5]);
    Set(scan, "_category", (string)b[6]);
    placed++;
}

// Орёл кружит над двором: AirPatrolController без контроллера и без коллайдера,
// иначе птица цепляла бы заборы.
var eagle = Spawn(ANIMALS + "Eagle/RA_Eagle_Model.prefab",
                  new UnityEngine.Vector3(24f, FLOOR + 14f, 20f), 0f);
if (eagle != null)
{
    eagle.gameObject.name = "AN_Eagle";
    var air = eagle.gameObject.AddComponent(tAir);
    Set(air, "_animator", eagle.GetComponentInChildren<UnityEngine.Animator>());
    placed++;
}

// Вышка: та же модель, что в меню и над депо, посажена на стакан метеопоста.
var mast = Spawn(MODELS + "MenuStationMast.fbx", new UnityEngine.Vector3(48f, FLOOR + 1.288f, 46f), 0f);
if (mast != null)
{
    mast.gameObject.name = "SettlementMast";
    mast.localScale = new UnityEngine.Vector3(0.7f, 0.7f, 0.7f);
    var scan = mast.gameObject.AddComponent(tScan);
    Set(scan, "_displayName", "Метеовышка поселения");
    Set(scan, "_category", "ОБОРУДОВАНИЕ");
    var col = mast.gameObject.AddComponent<UnityEngine.BoxCollider>();
    col.center = new UnityEngine.Vector3(0f, 5.6f, 0f);
    col.size = new UnityEngine.Vector3(4f, 11.6f, 4f);
}

// Ящики и бочки: узел живёт грузом, пустые углы читаются музеем.
foreach (var pr in new object[][]
{
    new object[] { "Prop_Crate", 43f, 14f }, new object[] { "Prop_Crate", 44.6f, 15.2f },
    new object[] { "Prop_Barrel", 42f, 16f }, new object[] { "Prop_Barrel", 13f, 29f },
    new object[] { "Prop_Crate", 37f, 32f },
})
{
    var tr = Spawn(MODELS + pr[0] + ".fbx", new UnityEngine.Vector3((float)pr[1], FLOOR, (float)pr[2]), 30f);
    if (tr != null) tr.gameObject.name = (string)pr[0];
}

// Свет: крыльцо, четыре фонаря на створах ворот, подстанция и маяк вышки.
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
Lamp("PorchLight", new UnityEngine.Vector3(40f, FLOOR + 3.0f, 36f), warm, 16f, 420f);
Lamp("GateWestLight", new UnityEngine.Vector3(-6f, FLOOR + 2.7f, 0f), warm, 22f, 520f);
Lamp("GateNorthLight", new UnityEngine.Vector3(0f, FLOOR + 2.7f, 52f), warm, 22f, 520f);
Lamp("PowerLight", new UnityEngine.Vector3(17f, FLOOR + 3.6f, 38f), warm, 20f, 480f);
Lamp("ShopLight", new UnityEngine.Vector3(46f, FLOOR + 2.6f, 12f), warm, 14f, 360f);
Lamp("MastBeacon", new UnityEngine.Vector3(48f, FLOOR + 12.0f, 46f), new UnityEngine.Color(0.45f, 0.9f, 1f), 60f, 900f);

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, CELL_PREFAB);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
sb.AppendLine("актёров поставлено: " + placed + " (роботов 4, машин-животных 8, птица 1)");
return sb.ToString();
