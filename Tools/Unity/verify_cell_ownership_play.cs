// Живая проверка: клетка — владелец содержимого.
//
//   python Tools/Unity/run_snippet.py Tools/Unity/verify_cell_ownership_play.cs
//
// Требует Play Mode на сцене `Frontier` и фокус окна (без фокуса Play не тикает).
// Ставит хук и сразу возвращает управление; отчёт печатается построчно с
// префиксом `CELL_OWNER|` через ~30 с.
//
// Проверяет четыре обещания архитектуры:
//   1. на время проезда игрок снова ребёнок вагона (иначе состав уедет без него);
//   2. уход на две клетки гасит депо вместе с актёрами;
//   3. возврат восстанавливает позы и те же самые объекты (не копии);
//   4. реестры сканирования и состояние диалога переживают выгрузку.

var report = new System.Text.StringBuilder();
// Отметки — от ровного пола: точка возврата стоит на настиле перрона (головка
// пути −1,06 + 1,04), дальняя клетка — на площадке станции (её пол −5,20) с
// запасом в три метра, чтобы луч старта не оказался под землёй.
var far = new UnityEngine.Vector3(1200f, -2.2f, 1200f);
var depot = new UnityEngine.Vector3(0.7f, -0.02f, -1.0f);
int phase = 0;
int waitFrames = 0;

UnityEngine.Transform player = null;
UnityEngine.Transform cell = null;
FrostSpace.Features.Dialogue.DialogueSpeaker speaker = null;
int scanBefore = 0;
int saveBefore = 0;
var posesBefore = new System.Collections.Generic.Dictionary<string, UnityEngine.Vector3>();
var objectsBefore = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();

UnityEditor.EditorApplication.CallbackFunction act = null;
act = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying)
    {
        UnityEditor.EditorApplication.update -= act;
        return;
    }

    if (player == null) player = FindPlayer();
    if (player == null) return;

    if (phase == 0)
    {
        if (UnityEngine.Time.time < 1.0f) return;
        report.AppendLine("P0 в пути: parent=" + (player.parent != null ? player.parent.name : "<root>") +
                          " t=" + UnityEngine.Time.time.ToString("F1"));
        phase = 1;
        return;
    }

    if (phase == 1)
    {
        if (player.parent != null) return; // катсцена ещё идёт

        cell = FindCell();
        if (cell == null) { Emit("НЕТ клетки WorldCell_-2_-2"); UnityEditor.EditorApplication.update -= act; return; }

        speaker = FindDeep(cell, "R7_Drifter").GetComponent<FrostSpace.Features.Dialogue.DialogueSpeaker>();
        scanBefore = FrostSpace.Features.Scan.ScanTarget.All.Count;
        saveBefore = UnityEngine.PlayerPrefs.GetString("FrostSpace.Save", string.Empty).Length;

        report.AppendLine("P0 после выхода: scanTargets=" + scanBefore + " saveLen=" + saveBefore +
                          " speakerCanTalk=" + (speaker != null && speaker.CanTalk));
        Snapshot("P0");

        // Не переставляем сразу: в этот кадр директор ещё возвращает управление,
        // и CharacterController при следующем Move вернул бы игрока на место.
        phase = 11;
        waitFrames = 90;
        return;
    }

    if (phase == 11)
    {
        Teleport(far);
        phase = 2;
        waitFrames = 40;
        return;
    }

    if (waitFrames > 0) { waitFrames--; return; }

    if (phase == 2)
    {
        report.AppendLine("P1 ушёл: cellActive=" + cell.gameObject.activeSelf +
                          " scanTargets=" + FrostSpace.Features.Scan.ScanTarget.All.Count);
        Snapshot("P1");
        Teleport(depot);
        phase = 3;
        waitFrames = 40;
        return;
    }

    if (phase == 3)
    {
        report.AppendLine("P2 вернулся: cellActive=" + cell.gameObject.activeSelf +
                          " scanTargets=" + FrostSpace.Features.Scan.ScanTarget.All.Count +
                          " saveLen=" + UnityEngine.PlayerPrefs.GetString("FrostSpace.Save", string.Empty).Length);
        Snapshot("P2");

        var sameSpeaker = FindDeep(cell, "R7_Drifter").GetComponent<FrostSpace.Features.Dialogue.DialogueSpeaker>();
        report.AppendLine("ссылка на говорящего жива: " + (speaker != null) +
                          ", тот же объект: " + (sameSpeaker == speaker) +
                          ", canTalk=" + (sameSpeaker != null && sameSpeaker.CanTalk));

        UnityEditor.EditorApplication.update -= act;
        Emit(report.ToString());
    }
};

void Teleport(UnityEngine.Vector3 where)
{
    // CharacterController держит собственное положение и при следующем Move
    // возвращает объект на него. Выключение на время перестановки синхронизирует
    // контроллер с трансформом — иначе проверка мерила бы не тот мир.
    var cc = player.GetComponent<UnityEngine.CharacterController>();
    if (cc != null) cc.enabled = false;
    player.position = where;
    UnityEngine.Physics.SyncTransforms();
    if (cc != null) cc.enabled = true;
}

void Snapshot(string tag)
{
    foreach (var name in new[] { "DepotTrain", "R7_Drifter", "R16_Warden_A", "R16_Warden_B",
                                 "R8_Bulwark", "DepotLamp", "DepotDoorSpill", "DepotDoorFill" })
    {
        var t = FindDeep(cell, name);
        if (t == null) { report.AppendLine(tag + " " + name + ": НЕТ"); continue; }

        var p = t.position;
        if (tag == "P0")
        {
            posesBefore[name] = p;
            objectsBefore[name] = t;
        }

        var same = objectsBefore.ContainsKey(name) && objectsBefore[name] == t;
        var drift = posesBefore.ContainsKey(name) ? UnityEngine.Vector3.Distance(posesBefore[name], p) : -1f;
        report.AppendLine(tag + " " + name + ": active=" + t.gameObject.activeInHierarchy +
                          " тотЖеОбъект=" + same + " сдвиг=" + drift.ToString("F4"));
    }
}

void Emit(string text)
{
    foreach (var line in text.Split('\n'))
        UnityEngine.Debug.Log("CELL_OWNER|" + line.TrimEnd('\r'));
}

UnityEngine.Transform FindPlayer()
{
    var go = UnityEngine.GameObject.Find("Player");
    return go != null ? go.transform : null;
}

UnityEngine.Transform FindCell()
{
    var cells = UnityEngine.GameObject.Find("WorldCells");
    return cells != null ? cells.transform.Find("WorldCell_-2_-2") : null;
}

UnityEngine.Transform FindDeep(UnityEngine.Transform root, string name)
{
    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == name) return t;
    return null;
}

UnityEditor.EditorApplication.update += act;
return "хук поставлен: ждём прибытия и выхода игрока";
