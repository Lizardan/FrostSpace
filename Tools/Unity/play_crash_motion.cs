// Живая проба аварии Train: поезд не встаёт, камера доворачивается к кабине.
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/play_crash_motion.cs
// Камера жёстко ставится боком (+X), риг уходит в кинематографическую паузу, затем
// штатная crash.Play. Снимаются числа: курс до/после доворота и доля хода по фазам.
// Кадры в Play не снимаются (роняли редактор). Требует фокус окна.

var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
object Priv(object o, string n)
{
    if (o == null) return null;
    var f = o.GetType().GetField(n, flags);
    return f != null ? f.GetValue(o) : null;
}

if (UnityEditor.EditorApplication.isPlaying)
    return "редактор уже в Play — останови и запусти пробу заново";

UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Scenes/Train.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
UnityEditor.EditorApplication.isPlaying = true;

var report = new System.Text.StringBuilder();
void Line(string t) { report.AppendLine(t); }
void Emit(string t)
{
    foreach (var l in t.Split('\n'))
        UnityEngine.Debug.Log("CRASHMOTION|" + l.TrimEnd('\r'));
}

float playStart = 0f;
int state = 0;
float t0 = 0f;
float lastSample = -1f;
UnityEngine.Vector3 initialFwd = UnityEngine.Vector3.zero;
UnityEngine.Vector3 cabinDir = UnityEngine.Vector3.zero;
float minSpeed = 9f;
float speedAtBrakeEnd = -1f;
float speedAtQuiet = -1f;
float turnDotAtBrakeEnd = -1f;
float initialDot = -1f;
UnityEngine.Vector3 fwdAtBrakeEnd = UnityEngine.Vector3.zero;
FrostSpace.Features.Story.TrainCrashRig crash = null;
FrostSpace.Features.Story.TrainMotionRig motion = null;
UnityEngine.Transform cabinSmoke = null;
UnityEngine.Camera cam = null;

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
        if (playStart == 0f) playStart = UnityEngine.Time.realtimeSinceStartup;
        if (UnityEngine.Time.realtimeSinceStartup - playStart < 0.6f) return;
        var director = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.TrainSequenceDirector>();
        if (director == null) return;
        var rig = Priv(director, "_rig") as FrostSpace.Features.View.PlayerCameraRig;
        crash = Priv(director, "_crash") as FrostSpace.Features.Story.TrainCrashRig;
        motion = crash != null ? Priv(crash, "_motion") as FrostSpace.Features.Story.TrainMotionRig : null;
        var ps = crash != null ? Priv(crash, "_cabinSmoke") as UnityEngine.ParticleSystem : null;
        cabinSmoke = ps != null ? ps.transform : null;
        cam = rig != null ? rig.ViewCamera : UnityEngine.Camera.main;
        if (rig == null || crash == null || motion == null || cam == null || cabinSmoke == null)
        {
            Emit("не найдены ссылки: rig/crash/motion/cam/cabinSmoke");
            UnityEditor.EditorApplication.update -= act;
            UnityEditor.EditorApplication.isPlaying = false;
            return;
        }
        // Боковой курс: камера смотрит поперёк вагона (+X), риг заморожен, чтобы не
        // переписать позу. Это и есть случай «квест сдали боком».
        rig.SetCinematic(true);
        cam.transform.rotation = UnityEngine.Quaternion.Euler(0f, 90f, 0f);
        initialFwd = cam.transform.forward;
        cabinDir = (cabinSmoke.position - cam.transform.position).normalized;
        initialDot = UnityEngine.Vector3.Dot(initialFwd, cabinDir);
        var play = crash.GetType().GetMethod("Play",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var en = play.Invoke(crash, new object[] { cam }) as System.Collections.IEnumerator;
        crash.StartCoroutine(en);
        t0 = UnityEngine.Time.realtimeSinceStartup;
        Line(string.Format("старт боком: forward={0}, направление на кабину={1}, dot={2:0.000}",
            initialFwd.ToString("F2"), cabinDir.ToString("F2"), initialDot));
        state = 1;
        return;
    }

    float t = UnityEngine.Time.realtimeSinceStartup - t0;
    if (t - lastSample < 0.04f) return;
    lastSample = t;

    float speed = motion != null ? System.Convert.ToSingle(Priv(motion, "_speedScale")) : -1f;
    if (speed >= 0f && speed < minSpeed) minSpeed = speed;

    if (speedAtBrakeEnd < 0f && t >= 1.4f)
    {
        speedAtBrakeEnd = speed;
        fwdAtBrakeEnd = cam.transform.forward;
        turnDotAtBrakeEnd = UnityEngine.Vector3.Dot(fwdAtBrakeEnd, cabinDir);
    }
    if (speedAtQuiet < 0f && t >= 3.6f) speedAtQuiet = speed;

    if (t < 4.3f) return;

    UnityEditor.EditorApplication.update -= act;
    float finalDot = UnityEngine.Vector3.Dot(cam.transform.forward, cabinDir);
    Line(string.Format("ход: конец торможения={0:0.000}, тишина={1:0.000}, минимум за аварию={2:0.000}",
        speedAtBrakeEnd, speedAtQuiet, minSpeed));
    Line(string.Format("камера: боком forward={0} -> после доворота forward={1}",
        initialFwd.ToString("F2"), fwdAtBrakeEnd.ToString("F2")));
    Line(string.Format("совпадение с кабиной: dot после доворота={0:0.000}, в конце={1:0.000}",
        turnDotAtBrakeEnd, finalDot));
    bool keepsMoving = minSpeed > 0.2f;
    bool startedSideways = initialDot < 0.3f;
    bool turnedToCabin = turnDotAtBrakeEnd > 0.9f;
    Line("ИТОГ: поезд " + (keepsMoving ? "НЕ встаёт" : "ВСТАЛ — дефект") +
         "; старт боком " + (startedSideways ? "подтверждён" : "НЕ подтверждён") +
         "; камера " + (turnedToCabin ? "довёрнута к кабине" : "НЕ довёрнута — дефект"));
    Emit(report.ToString());
    UnityEditor.EditorApplication.isPlaying = false;
};
UnityEditor.EditorApplication.update += act;
return "проба поставлена: отчёт CRASHMOTION| через ~6 с, Play остановится сам";