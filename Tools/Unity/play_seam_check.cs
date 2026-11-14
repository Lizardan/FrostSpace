// Живой прогон стыка Train → Frontier: таймлайн занавеса, титра, гула, ветра и
// вставки снимается числами, а не кадрами (захват в Play дважды ронял редактор,
// docs/UNITY_EDITOR.md §5).
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/play_seam_check.cs
//
// Ставит хук и сразу возвращает управление; отчёт печатается построчно с
// префиксом `SEAM|` через ~15–20 с. Требует фокус окна: без фокуса Play не тикает.
// Финал запускается штатной корутиной PlayFinale (reflection только чтобы позвать
// приватный метод из сниппета), дальше всё идёт как в игре.
//
// Что проверяется:
//   * титр не делит кадр: пока его альфа > 0,02, занавес обязан быть >= 0,98,
//     и в новой сцене титра уже нет;
//   * немой дыры нет: на окне вокруг склейки max(гул, ветер) > 0,02;
//   * вставка видна с открытия занавеса: старт вставки не позже старта проявления.

var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
object Priv(object o, string n)
{
    if (o == null) return null;
    var f = o.GetType().GetField(n, flags);
    return f != null ? f.GetValue(o) : null;
}
object Call(object o, string n, object[] a)
{
    if (o == null) return null;
    var m = o.GetType().GetMethod(n, flags);
    return m != null ? m.Invoke(o, a) : null;
}
float AlphaOf(object o)
{
    var g = Priv(o, "_group") as UnityEngine.CanvasGroup;
    return g != null ? g.alpha : -1f;
}
float VolOf(object o, string field)
{
    var s = Priv(o, field) as UnityEngine.AudioSource;
    return s != null ? s.volume : -1f;
}

if (UnityEditor.EditorApplication.isPlaying)
    return "редактор уже в Play — останови и запусти пробу заново";

var trainScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Scenes/Train.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
UnityEditor.EditorApplication.isPlaying = true;

var report = new System.Text.StringBuilder();
void Line(string t) { report.AppendLine(t); }
void Emit(string t)
{
    foreach (var l in t.Split('\n'))
        UnityEngine.Debug.Log("SEAM|" + l.TrimEnd('\r'));
}

float t0 = 0f;
int state = 0;
float lastSample = -1f;
string prevScene = "";
float tClosed = -1, tTitleIn = -1, tTitleOut = -1, tSwitch = -1, tReveal = -1,
      tRevealDone = -1, tInsert = -1;
float audioMin = 9f;
int violations = 0;
float lastT = 0f;
UnityEngine.Vector3 insertStart = UnityEngine.Vector3.zero;
bool haveInsertStart = false;

UnityEditor.EditorApplication.CallbackFunction act = null;
act = delegate
{
    if (!UnityEditor.EditorApplication.isPlaying)
    {
        UnityEditor.EditorApplication.update -= act;
        Emit("Play не стартовал или остановлен раньше отчёта");
        return;
    }

    if (state == 0)
    {
        var dir0 = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.TrainSequenceDirector>();
        if (dir0 == null) return;
        t0 = UnityEngine.Time.realtimeSinceStartup;
        var en = Call(dir0, "PlayFinale", null) as System.Collections.IEnumerator;
        if (en != null) dir0.StartCoroutine(en);
        Line("финал запущен штатной PlayFinale, t0 отсчёта");
        state = 1;
        return;
    }

    float t = UnityEngine.Time.realtimeSinceStartup - t0;
    lastT = t;
    if (t - lastSample < 0.04f && state == 1) return;
    lastSample = t;

    string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    var sc = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Core.SceneController>();
    var title = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.TitleCardScreen>();
    float curtain = AlphaOf(sc);
    float titleA = AlphaOf(title);
    float drone = VolOf(title, "_audio");

    var depot = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
    float wind = VolOf(depot, "_windSource");

    if (!haveInsertStart && depot != null)
    {
        var pose = Call(depot, "InsertPose", new object[] { 0f });
        if (pose != null)
        {
            var pf = pose.GetType().GetField("Position");
            if (pf != null) { insertStart = (UnityEngine.Vector3)pf.GetValue(pose); haveInsertStart = true; }
        }
    }
    if (tInsert < 0 && haveInsertStart && scene == "Frontier" && UnityEngine.Camera.main != null)
    {
        if ((UnityEngine.Camera.main.transform.position - insertStart).magnitude < 0.15f)
            tInsert = t;
    }

    if (curtain >= 0.98f && tClosed < 0 && scene == "Train") tClosed = t;
    if (titleA > 0.02f && tTitleIn < 0) tTitleIn = t;
    if (tTitleIn >= 0 && titleA <= 0.02f && tTitleOut < 0) tTitleOut = t;
    if (scene != prevScene && prevScene == "Train" && scene == "Frontier") tSwitch = t;
    if (scene == "Frontier" && curtain < 0.98f && tReveal < 0) tReveal = t;
    if (scene == "Frontier" && curtain <= 0.01f && tRevealDone < 0) tRevealDone = t;
    prevScene = scene;

    // Титр делит кадр: виден при неполном занавесе или уже в новой сцене.
    if (titleA > 0.02f && (curtain < 0.98f || scene == "Frontier"))
    {
        violations++;
        if (violations <= 12)
            Line(string.Format("НАРУШЕНИЕ t={0:0.00} сцена={1} титр={2:0.00} занавес={3:0.00}",
                t, scene, titleA, curtain));
    }

    // Немая дыра вокруг склейки.
    if (tSwitch >= 0 && t >= tSwitch - 0.3f && (tRevealDone < 0 || t <= tRevealDone + 0.5f))
    {
        float loud = UnityEngine.Mathf.Max(drone, wind);
        if (loud < audioMin) audioMin = loud;
    }

    bool done = (tRevealDone >= 0 && t > tRevealDone + 7f) || t > 25f;
    if (!done) return;

    UnityEditor.EditorApplication.update -= act;
    Line(string.Format("занавес закрыт t={0:0.00}, титр вошёл t={1:0.00}, титр ушёл t={2:0.00}",
        tClosed, tTitleIn, tTitleOut));
    Line(string.Format("склейка t={0:0.00}, проявление t={1:0.00}, проявление конец t={2:0.00}, вставка старт t={3:0.00}",
        tSwitch, tReveal, tRevealDone, tInsert));
    if (tInsert >= 0 && tReveal >= 0)
        Line(string.Format("вставка минус проявление = {0:0.000} с (<=0 — вставка видна с открытия)", tInsert - tReveal));
    Line(string.Format("минимум max(гул,ветер) на склейке = {0:0.000} (<=0.02 — немая дыра)", audioMin));
    Line("нарушений «титр делит кадр»: " + violations);
    Line("последний t=" + lastT.ToString("0.00"));
    Emit(report.ToString());
};

UnityEditor.EditorApplication.update += act;
return "хук поставлен: Play на Train, финал стартует сам; отчёт SEAM| через ~15-20 с";