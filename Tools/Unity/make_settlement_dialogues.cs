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
    new object[] { "SettlementWeather", "n0", new object[][]
    {
        new object[] { "n0",
            "Дуй сегодня ровно с северо-запада, семь метров в секунду, минус сорок один. " +
            "Мы тут не за красивым стоим: метеопост передаёт ветер и лёд на станцию, " +
            "пока тишина не съела связь.", "n1", 0, new object[0] },
        new object[] { "n1",
            "Вышка считает всё: давление, влажность, направление аномалии. " +
            "Если её цифры расходятся с твоим датчиком — правду имеет та, что выше на двенадцать метров.",
            "", 0, new object[][]
            {
                new object[] { "А что бывает, когда расходятся?", "n2" },
                new object[] { "Зачем вам столько техники на одну вышку?", "n3" },
                new object[] { "Понял, не мешаю.", "" },
            } },
        new object[] { "n2",
            "Бывает холоднее, чем кажется. Один раз мы два дня грели перрон по приборам, " +
            "которые уже врали, и к утру на дороге встала техника. С тех пор сверяем дважды.",
            "", 0, new object[0] },
        new object[] { "n3",
            "Потому что поселение живёт ровно на том, что мы успеем сказать до бури. " +
            "Тепло, свет и корм для машин зависят от одного прогноза.", "", 0, new object[0] },
    } },
    new object[] { "SettlementShelter", "n0", new object[][]
    {
        new object[] { "n0",
            "Осторожно у загона. Этот козёл три года назад вёл погрузчик на депо " +
            "и помнил, где лежат аккумуляторы. Теперь помнит только, где вкусное.",
            "n1", 0, new object[0] },
        new object[] { "n1",
            "К нам привозят тех, у кого помутился разум: тишина доедает их раньше, чем ржавеют " +
            "шарниры. Мы не чиним голову — мы забираем её совсем и оставляем тело. " +
            "Вот и весь приют.", "", 0, new object[][]
            {
                new object[] { "Это же пытка — стирать память.", "n3" },
                new object[] { "Они хоть страдают?", "n4" },
                new object[] { "Сколько у вас таких?", "n5" },
                new object[] { "Чем их кормить?", "n2" },
            } },
        new object[] { "n2",
            "Корми вон у тех, что с ободранной краской: их привезли в прошлом месяце, " +
            "они ещё помнят, как держать инструмент.", "", 0, new object[0] },
        new object[] { "n3",
            "Согласен. Только без головы машин не выживает, а с головой доживает до того дня, " +
            "когда начнёт сам себе отрывать провода. Мы выбираем середину.", "", 0, new object[0] },
        new object[] { "n4",
            "Нет. Страдал тот, кто был внутри. Осталась привычка есть, ходить и греться " +
            "у печки — за неё никто не платит.", "", 0, new object[0] },
        new object[] { "n5",
            "Восемь голов, одна птица и два пса у избы. Немного, но депо отдаёт нам всех, " +
            "кого не смог починить.", "", 0, new object[0] },
    } },
    new object[] { "SettlementPower", "n0", new object[][]
    {
        new object[] { "n0",
            "Не лезь за ограду подстанции: там три бака под напряжением, и они не предупреждают. " +
            "Стой здесь, я покажу, откуда у вас тепло.", "n1", 0, new object[0] },
        new object[] { "n1",
            "Поселение держится на одном кабеле. Мы тянем его из-под снега, ставим на баки " +
            "и кормим им и перрон, и загоны, и вышку. Сядет кабель — к утру здесь кладбище техники.",
            "n2", 0, new object[][]
            {
                new object[] { "Откуда берёте ток?", "n3" },
                new object[] { "Что делать, если сядет?", "n4" },
                new object[] { "Понял, не сую руки.", "" },
            } },
        new object[] { "n2",
            "Смотри на крышку канавки: под ней кабель к северной ноге дороги. " +
            "Пока она цела, посёлок в тепле.", "", 0, new object[0] },
        new object[] { "n3",
            "Из разницы. Гряда за вышкой держит холод, двор у избы — тепло, и пока они " +
            "спорят, баки снимают с этого спора достаточно, чтобы варить воду и светить фонарями.",
            "n2", 0, new object[0] },
        new object[] { "n4",
            "Идти пешком в депо и просить аккумулятор. Мы не придумали ничего лучше, " +
            "и, кажется, никогда не придумаем.", "n2", 0, new object[0] },
    } },
    new object[] { "SettlementWorkshop", "n0", new object[][]
    {
        new object[] { "n0",
            "Изба для тех, кто ещё думает, мастерская для тех, кто уже только работает. " +
            "Между ними тридцать шагов и одна очень длинная ночь.", "n1", 0, new object[0] },
        new object[] { "n1",
            "У меня тут верстак, ящик обжимок и три банки смазки, которые депо считает списанными. " +
            "За это я держу на ходу и загон, и сани, и твою технику, если доедет.",
            "n2", 0, new object[][]
            {
                new object[] { "Что чаще всего ломается?", "n3" },
                new object[] { "Почему мастерская не в депо?", "n4" },
                new object[] { "Спасибо, загляну, если сяду.", "" },
            } },
        new object[] { "n2",
            "Бери с полки сухой паёк, он не мой и не твой — он того, кто не дойдёт.", "", 0, new object[0] },
        new object[] { "n3",
            "Пальцы. На морозе они трескаются первыми, а без них любой узел разваливается " +
            "на болты.", "n2", 0, new object[0] },
        new object[] { "n4",
            "Потому что в депо чинят то, что уедет. Здесь чинят то, что никуда не уедет, " +
            "но без него уедут остальные.", "n2", 0, new object[0] },
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
