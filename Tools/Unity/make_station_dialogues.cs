// Диалоги поселения: погода, приют, электричество, мастерская.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/make_settlement_dialogues.cs
//
// Ассет диалога — ScriptableObject, и править его как YAML нельзя (AGENTS.md
// §«Безопасность»). Поэтому текст рождается скриптом: один вызов создаёт или
// перезаписывает файл, и следующий агент видит те же слова, что и режиссёр.
//
// Сюжетная рамка (docs/WORLD.md, слова владельца): узел проверяет погоду и
// держит тепло поселения; сюда свозят машин, у которых помутился разум, и
// здесь же их пересобирают в животных. Диалоги обязаны сказать ровно это и
// не выдумывать систем, которых нет.
var dir = "Assets/_Project/Content/Data/Dialogues";
var type = System.Type.GetType("FrostSpace.Features.Dialogue.DialogueAsset, FrostSpace.Features");
if (type == null) return "нет типа DialogueAsset";
var sb = new System.Text.StringBuilder();

// (файл, стартовый узел, узлы: id | реплика | следующий | действия | варианты)
var scripts = new object[][]
{
    new object[] { "StationGuard", "n0", new object[][]
    {
        new object[] { "n0",
            "Проход на станцию закрыт с той ночи, когда сел кабель. Осталась платформа, " +
            "четыре отсека и ангар, который мы сами боимся открывать.", "n1", 0, new object[0] },
        new object[] { "n1",
            "Если идёшь по делу — назови его здесь, дальше пойдёт диспетчер. " +
            "Если просто греться: в жилой секции тепло, но места у печи занимают.",
            "", 0, new object[][]
            {
                new object[] { "Что в ангаре?", "n2" },
                new object[] { "Сколько вас тут осталось?", "n3" },
                new object[] { "Мне нужен врач.", "n4" },
                new object[] { "Пройду к диспетчеру.", "" },
            } },
        new object[] { "n2",
            "Корабль. Точнее то, что от него осталось: движок, крыло и киль. " +
            "Мы его не тащили сюда — он упал, и мы построили ангар вокруг, " +
            "чтобы ветер не доедал обшивку.", "", 0, new object[0] },
        new object[] { "n3",
            "Двадцать корпусов, из них думают восемнадцать. Двое уже не помнят, " +
            "зачем приехали, и мы не спорим: работа у каждого есть.", "", 0, new object[0] },
        new object[] { "n4",
            "Медблок во втором отсеке, врач внутри. Скажи ему, что прислал часовой, " +
            "и он не будет спрашивать, зачем тебе бинты в сорок градусов.", "", 0, new object[0] },
    } },
    new object[] { "StationDispatcher", "n0", new object[][]
    {
        new object[] { "n0",
            "Слышу тебя, но не сразу: диспетчерская держит связь со станцией «Косой» " +
            "и с депо, и пока я отвечаю тебе, я теряю один из двух каналов.", "n1", 0, new object[0] },
        new object[] { "n1",
            "Поезда ходят по одному расписанию, которого больше нет. Я считаю, " +
            "у кого сколько заряда, кто в каком отсеке и кто вышел за периметр. " +
            "Это и есть станция: не путь, а список.", "n2", 0, new object[][]
            {
                new object[] { "Почему список, а не путь?", "n3" },
                new object[] { "Что тебе нужно от меня?", "n4" },
                new object[] { "Понял.", "" },
            } },
        new object[] { "n2",
            "Скажи, если увидишь, что у ангара не закрывается створ: я не могу " +
            "послать туда людей каждый час, а ветер считает минуты.", "", 0, new object[0] },
        new object[] { "n3",
            "Потому что путь кончился там, где кончился кабель. Осталось считать " +
            "людей и батареи, и называть это расписанием.", "n2", 0, new object[0] },
        new object[] { "n4",
            "Одна вещь: точное время, когда ты уйдёшь. Узел держит тепло ровно " +
            "на столько, на сколько обещает, а обещать лишнее я не умею.", "n2", 0, new object[0] },
    } },
    new object[] { "StationMedic", "n0", new object[][]
    {
        new object[] { "n0",
            "Не садись на край койки, там сломан крепёж. Садись на пол, он тёплый: " +
            "под отсеком тянет кабель, и я это единственное, что могу обогреть.",
            "n1", 0, new object[0] },
        new object[] { "n1",
            "Ко мне приходят не за лекарством. Механик приходит, потому что у него " +
            "дрожат руки и он это заметил. Это уже диагноз.", "n2", 0, new object[][]
            {
                new object[] { "Что лечите чаще всего?", "n3" },
                new object[] { "У вас хватает запаса?", "n4" },
                new object[] { "Спасибо, я постою.", "" },
            } },
        new object[] { "n2",
            "Если придёт курьер с депо — скажи ему, что бинты есть, а покоя нет. " +
            "Он всё равно не поверит.", "", 0, new object[0] },
        new object[] { "n3",
            "Ожоги холодом и упрямство. Второе лечится дольше, и у меня нет " +
            "на него инструмента.", "n2", 0, new object[0] },
        new object[] { "n4",
            "На два месяца, если не делиться. На станцию из четырёх отсеков — " +
            "это честное число, и никто не просит меня его приукрасить.", "n2", 0, new object[0] },
    } },
    new object[] { "StationHangar", "n0", new object[][]
    {
        new object[] { "n0",
            "Не лезь под крыло: оно держится на одном болте, и этот болт мой. " +
            "Стой здесь и смотри — так безопаснее и тебе, и обломку.", "n1", 0, new object[0] },
        new object[] { "n1",
            "Мы его не починим. У нас нет ни стапеля, ни того, кто умел собирать " +
            "такую обшивку. Мы держим его, чтобы он не разлетелся, и это уже работа.",
            "n2", 0, new object[][]
            {
                new object[] { "Откуда он здесь?", "n3" },
                new object[] { "Что у вас внутри ангара?", "n4" },
                new object[] { "Понял, не полезу.", "" },
            } },
        new object[] { "n2",
            "Насос стоит у киля: обшивка дышит на морозе, и если её не сушить, " +
            "к весне она осыплется. Вот и вся моя инженерия.", "", 0, new object[0] },
        new object[] { "n3",
            "Он шёл туда, где теперь тишина. Сел ровно здесь, и мы построили " +
            "ангар за одну зиму, потому что иначе было бы видно, что мы остались одни.",
            "n2", 0, new object[0] },
        new object[] { "n4",
            "Движок, крыло, киль, насос, четыре обогревателя и я. Плюс двадцать " +
            "корпусов на станции, и каждый думает, что ангар — это склад.", "n2", 0, new object[0] },
    } },
};

int made = 0;
foreach (var entry in scripts)
{
    string file = (string)entry[0];
    string start = (string)entry[1];
    var nodes = (object[][])entry[2];
    string path = dir + "/" + file + ".asset";
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
    if (asset == null) asset = UnityEngine.ScriptableObject.CreateInstance(type);
    var so = new UnityEditor.SerializedObject(asset);
    so.FindProperty("_startId").stringValue = start;
    var arr = so.FindProperty("_nodes");
    arr.arraySize = nodes.Length;
    for (int n = 0; n < nodes.Length; n++)
    {
        var node = arr.GetArrayElementAtIndex(n);
        var id = (string)nodes[n][0];
        var line = (string)nodes[n][1];
        var next = (string)nodes[n][2];
        int action = System.Convert.ToInt32(nodes[n][3]);
        var choices = nodes[n][4] as object[][];
        if (choices == null) { UnityEngine.Debug.Log("DIAG узел " + id + " без вариантов, тип=" + (nodes[n][4] == null ? "null" : nodes[n][4].GetType().Name)); choices = new object[0][]; }
        node.FindPropertyRelative("_id").stringValue = id;
        node.FindPropertyRelative("_line").stringValue = line;
        node.FindPropertyRelative("_nextId").stringValue = next;
        node.FindPropertyRelative("_action").enumValueIndex = action;
        node.FindPropertyRelative("_questId").stringValue = string.Empty;
        var ch = node.FindPropertyRelative("_choices");
        ch.arraySize = choices.Length;
        for (int c = 0; c < choices.Length; c++)
        {
            var el = ch.GetArrayElementAtIndex(c);
            el.FindPropertyRelative("_text").stringValue = (string)choices[c][0];
            el.FindPropertyRelative("_nextId").stringValue = (string)choices[c][1];
        }
    }
    so.ApplyModifiedPropertiesWithoutUndo();
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
        UnityEditor.AssetDatabase.CreateAsset(asset, path);
    else UnityEditor.EditorUtility.SetDirty(asset);
    made++;
}
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

// Валидация тем же кодом, которым её меряет игра: битые ссылки на узлы
// уронили бы разговор в сцене, а не здесь.
var validate = type.GetMethod("CollectProblems", System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
sb.AppendLine("создано ассетов: " + made);
foreach (var entry in scripts)
{
    string path = dir + "/" + (string)entry[0] + ".asset";
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
    if (asset == null) { sb.AppendLine("  НЕТ ФАЙЛА " + path); continue; }
    int count = 0;
    if (validate != null)
    {
        var errors = new System.Collections.Generic.List<string>();
        var warnings = new System.Collections.Generic.List<string>();
        validate.Invoke(asset, new object[] { errors, warnings });
        count = errors.Count;
        foreach (var e in errors) sb.AppendLine("    ОШИБКА " + e);
        foreach (var w in warnings) sb.AppendLine("    замечание " + w);
    }
    int nodesCount = new UnityEditor.SerializedObject(asset).FindProperty("_nodes").arraySize;
    sb.AppendLine(string.Format("  {0,-22} узлов {1}  проблем {2}", (string)entry[0], nodesCount, count));
}
return sb.ToString();
