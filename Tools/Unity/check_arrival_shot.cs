// Живая проверка прибытия в депо: снимает кадры штатной камеры по моментам
// заставки и печатает расписание, позу камеры и плотность дыма.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/check_arrival_shot.cs
//
// Требует Play Mode на сцене `Frontier` и фокус окна. Сниппет ставит хук и
// возвращается сразу; отчёт — строками ARR_LOOK|, кадры — Logs/verify/<дата>/.
// Хук нужен потому, что заставка идёт 22,6 с и ни один вызов моста не может
// столько держать соединение: редактор отвечает только когда окно в фокусе.
// Отсчёт — от `Time.timeSinceLevelLoad`, а не от первого тика хука: между входом
// в Play и установкой хука проходит несколько секунд, и по тикам метки уезжают
// вперёд ровно на эту задержку (проверено 2026-11-05).
if (!UnityEditor.EditorApplication.isPlaying)
    return "редактор не в Play — войди в Play на сцене Frontier";

var dir = "Logs/verify/2026-11-05";
System.IO.Directory.CreateDirectory(dir);

var director = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
if (director == null) return "DepotArrivalDirector не найден (открой Frontier)";

var marks = new[] { 1.0f, 3.0f, 5.5f, 6.2f, 8.0f, 10.5f, 13.0f, 17.0f, 21.0f, 23.0f };
var index = 0;
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var buffer = new UnityEngine.ParticleSystem.Particle[600];
var directorType = director.GetType();

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

string Smoke()
{
    var sb = new System.Text.StringBuilder();
    foreach (var ps in UnityEngine.Object.FindObjectsByType<UnityEngine.ParticleSystem>(
                 UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
    {
        if (!ps.name.StartsWith("TrainSmoke")) continue;
        sb.Append($"{ps.name.Replace("TrainSmoke", "")}:{ps.GetParticles(buffer)} ");
    }
    return sb.Length == 0 ? "дыма нет" : sb.ToString().TrimEnd();
}

string State(float local)
{
    var train = (UnityEngine.Transform)directorType.GetField("_train", flags).GetValue(director);
    var s = director.Schedule();
    var cam = UnityEngine.Camera.main;
    return string.Format(
        "t={0:N1} | поезд {1:N1} | кадр {2:N1} fov {3:N0} | конец кадра {4:N1}, игрок {5:N1} | дым {6}",
        local,
        train != null ? train.position.z : 0f,
        cam != null ? cam.transform.position.z : 0f,
        cam != null ? cam.fieldOfView : 0f,
        s.CameraEndsAt, s.PlayerDoneAt, Smoke());
}

UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying)
    {
        UnityEditor.EditorApplication.update -= tick;
        UnityEngine.Debug.Log($"ARR_LOOK|прогон снят, кадров {index}");
        return;
    }

    var local = UnityEngine.Time.timeSinceLevelLoad;
    if (index >= marks.Length || local < marks[index]) return;

    var mark = marks[index];
    Shoot($"arrival_{(int)(mark * 10):D3}");
    UnityEngine.Debug.Log("ARR_LOOK|" + State(local));
    index++;
};

UnityEditor.EditorApplication.update += tick;
return $"прибытие ставится на счётчики, {marks.Length} кадров в {dir}";
