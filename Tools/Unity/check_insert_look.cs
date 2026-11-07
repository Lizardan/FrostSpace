// Кадры внешнего плана прибытия без Play: состав ставится на точки заезда, дым
// симулируется, кадр рендерится с ключей вставки.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/check_insert_look.cs
//
// Сделано вне Play намеренно: хук захвата в Play дважды ронял редактор
// (UNITY_EDITOR.md §5), а ракурс надо видеть глазами. Позицию состава и состояние
// дыма возвращает, сцену не сохраняет. Правила docs/UNITY_EDITOR.md §2: без using,
// полные имена типов.
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Scenes/Frontier.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);

var dir = "Logs/verify/2026-11-05";
System.IO.Directory.CreateDirectory(dir);

var trainGo = UnityEngine.GameObject.Find("DepotTrain");
if (trainGo == null) return "DepotTrain не найден — открой Frontier";
var train = trainGo.transform;
var rest = train.position;

var director = UnityEngine.Object.FindFirstObjectByType<FrostSpace.Features.Story.DepotArrivalDirector>();
if (director == null) return "режиссёр не найден";

var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var dt = director.GetType();
var keys = (FrostSpace.Features.View.CameraKey[])dt.GetField("_insertPath", flags).GetValue(director);
var insertSeconds = (float)dt.GetField("_insertSeconds", flags).GetValue(director);
var aimLocal = (UnityEngine.Vector3)dt.GetField("_insertAim", flags).GetValue(director);
var offset = (UnityEngine.Vector3)dt.GetField("_trainStartOffset", flags).GetValue(director);
var runSeconds = insertSeconds + (float)dt.GetField("_trainSeconds", flags).GetValue(director);

var smoke = new System.Collections.Generic.List<UnityEngine.ParticleSystem>();
foreach (var ps in train.GetComponentsInChildren<UnityEngine.ParticleSystem>(true))
    if (ps.name.StartsWith("TrainSmoke")) smoke.Add(ps);
if (smoke.Count == 0) return "дымовых систем на составе нет";

var cam = UnityEngine.Camera.main;
var sb = new System.Text.StringBuilder();
sb.Append($"вставка {insertSeconds:N1} с, ключей {keys.Length}, старт состава z {rest.z + offset.z:N1}, " +
          $"заезд {runSeconds:N1} с, систем дыма {smoke.Count}\n");

void Shoot(string tag, UnityEngine.Vector3 pos, UnityEngine.Vector3 aim, float fov)
{
    var go = new UnityEngine.GameObject("TEMP_InsertLookCamera");
    var c = go.AddComponent<UnityEngine.Camera>();
    c.clearFlags = cam.clearFlags;
    c.backgroundColor = cam.backgroundColor;
    c.cullingMask = cam.cullingMask;
    c.nearClipPlane = 0.3f;
    c.farClipPlane = cam.farClipPlane;
    c.transform.position = pos;
    c.transform.LookAt(aim);
    c.fieldOfView = fov;
    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
    var src = cam.gameObject.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
    if (src != null) { data.renderType = src.renderType; data.renderShadows = src.renderShadows; }

    c.targetTexture = new UnityEngine.RenderTexture(1280, 720, 24);
    c.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = c.targetTexture;
    var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGBA32, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0);
    tex.Apply();
    UnityEngine.RenderTexture.active = prev;
    var rt = c.targetTexture;
    c.targetTexture = null;
    System.IO.File.WriteAllBytes($"{dir}/{tag}.png", tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    UnityEngine.Object.DestroyImmediate(rt);
    UnityEngine.Object.DestroyImmediate(go);
}

// Моменты внутри вставки: появление из-за стены, середина подхода, канун склейки.
var marks = new[] { 0.4f, 2.4f, 4.6f, 6.0f };
var buffer = new UnityEngine.ParticleSystem.Particle[600];

foreach (var local in marks)
{
    var pathT = FrostSpace.Features.View.CameraTrack.Ease(
        UnityEngine.Mathf.Pow(UnityEngine.Mathf.Clamp01(local / insertSeconds), 1.1f));
    var keyIndex = UnityEngine.Mathf.Min(keys.Length - 1,
        (int)(pathT * (keys.Length - 1) + 0.5f));
    var pos = keys[keyIndex].Position;
    var fov = UnityEngine.Mathf.Lerp(keys[0].Fov, keys[keys.Length - 1].Fov,
        FrostSpace.Features.View.CameraTrack.Ease(local / insertSeconds));

    // Позиция состава по тому же пути, что и в игре: равная скорость по длине.
    var z = rest.z + offset.z + (-offset.z) * UnityEngine.Mathf.Clamp01(local / runSeconds);
    train.position = new UnityEngine.Vector3(rest.x, rest.y, z);

    foreach (var ps in smoke)
    {
        ps.Simulate(0f, true, true);
        for (var i = 0; i < 40; i++) ps.Simulate(0.1f, true, false);
    }

    var aim = train.TransformPoint(aimLocal);
    var tag = $"insert_{(int)(local * 10):D2}";
    Shoot(tag, pos, aim, fov);

    var total = 0;
    foreach (var ps in smoke) total += ps.GetParticles(buffer);
    sb.Append($"{dir}/{tag}: t={local:N1} с, z состава {z:N1}, цель {aim:N1}, " +
              $"камера {pos} fov {fov:N0}, частиц дыма {total}\n");
}

train.position = rest;
foreach (var ps in smoke)
{
    ps.Stop();
    ps.Clear();
}
sb.Append($"состав возвращён в {rest}, дым выключен; сцена не сохранялась");
return sb.ToString();
