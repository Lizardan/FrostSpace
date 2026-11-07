// Живая проверка аварии в `Train`: запускает TrainCrashRig.Play штатно и снимает
// кадры ИЗ ИГРОВОЙ КАМЕРЫ по моментам фаз.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/check_crash_look.cs
//
// Требует Play Mode на сцене `Train` и фокус окна (без фокуса Play не тикает).
// Сниппет ставит хук и возвращается сразу; отчёт — строками CRASH_LOOK|, кадры —
// в Logs/verify/<дата>/. Снимает именно Camera.main: размер искр на пробной камере
// в 40 см от точки эмиссии выглядит шаром, а кадром игрока — нет, и решать должно
// то, что видит игрок.
if (!UnityEditor.EditorApplication.isPlaying)
    return "редактор не в Play — войди в Play на сцене Train";

var rig = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.TrainCrashRig>();
if (rig == null) return "TrainCrashRig не найден (открой Train)";

var dir = "Logs/verify/2026-11-05";
System.IO.Directory.CreateDirectory(dir);

var marks = new[] { 0.6f, 1.3f, 1.6f, 1.9f, 2.3f, 2.8f, 3.4f, 4.2f };
var index = 0;
var started = float.NaN;
var peaks = new int[marks.Length];
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var buffer = new UnityEngine.ParticleSystem.Particle[600];
var systems = new System.Collections.Generic.List<UnityEngine.ParticleSystem>();
foreach (var ps in UnityEngine.Object.FindObjectsByType<UnityEngine.ParticleSystem>(
             UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
    if (ps.name.StartsWith("Crash")) systems.Add(ps);

void Shoot(string tag)
{
    var camera = UnityEngine.Camera.main;
    if (camera == null) return;

    var rt = new UnityEngine.RenderTexture(1024, 576, 24);
    var prevTarget = camera.targetTexture;
    camera.targetTexture = rt;
    camera.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(1024, 576, UnityEngine.TextureFormat.RGBA32, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, 1024, 576), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    camera.targetTexture = prevTarget;
    System.IO.File.WriteAllBytes($"{dir}/{tag}.png", tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    UnityEngine.Object.DestroyImmediate(rt);
}

string Live()
{
    var sb = new System.Text.StringBuilder();
    foreach (var ps in systems)
        sb.Append($"{ps.name.Replace("Crash", "")}:{ps.GetParticles(buffer)} ");
    return sb.ToString().TrimEnd();
}

UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying)
    {
        UnityEditor.EditorApplication.update -= tick;
        var report = new System.Text.StringBuilder("CRASH_LOOK|пики частиц: ");
        for (var i = 0; i < index; i++)
            report.Append($"{(int)(marks[i] * 100)}:{peaks[i]} ");
        var body = typeof(FrostSpace.Features.Story.TrainCrashRig)
            .GetField("_body", flags).GetValue(rig) as UnityEngine.Transform;
        report.Append($"| корпус {body.localRotation.eulerAngles} поз {body.localPosition:N3} " +
                      $"| камера {UnityEngine.Camera.main.transform.position:N2}");
        UnityEngine.Debug.Log(report.ToString());
        return;
    }

    var t = UnityEngine.Time.time;
    if (float.IsNaN(started))
    {
        started = t;
        rig.StartCoroutine(rig.Play(UnityEngine.Camera.main));
        return;
    }

    var local = t - started;
    var peakNow = 0;
    foreach (var ps in systems)
    {
        var n = ps != null ? ps.GetParticles(buffer) : 0;
        if (n > peakNow) peakNow = n;
    }

    if (index < marks.Length && local >= marks[index])
    {
        if (peakNow > peaks[index]) peaks[index] = peakNow;
        Shoot($"crash_{(int)(marks[index] * 100):D3}");
        UnityEngine.Debug.Log(string.Format(
            "CRASH_LOOK|t={0:N2} пик сейчас {1} | {2}", local, peakNow, Live()));
        index++;
    }
};

UnityEditor.EditorApplication.update += tick;
return $"авария ставится на живые счётчики; систем {systems.Count}, кадры — {dir}/crash_*.png";
