// Живой прогон сюжета депо: DP-01…DP-04 и DP-07.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/play_depot_quest.cs
//
// Требует Play Mode на сцене `Frontier` и фокус окна (без фокуса Play не тикает).
// Ставит хук и сразу возвращает управление; отчёт печатается построчно с
// префиксом `DEPOT_Q|` через ~25–35 с.
//
// Прогон идёт штатной логикой, а не записью полей:
//   * разговор открывает сама игра — приватные `DialogueController.FindTarget`,
//     `Open`, `OnAdvancePressed`, `OnChoicePressed` вызываются как из ввода;
//   * снаряжение выдаёт настоящее событие `DialogueAction` → `PlayerEquipment`;
//   * миникарта и сканер проверяются по их же гейтам.
// Reflection нужен только потому, что сниппет живёт вне сборки и не может
// позвать приватные методы иначе.

var report = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

bool mapBefore = false;
bool scannerBefore = false;
int mapCellBefore = 0;
int scannerCellBefore = 0;

void Line(string text) { report.AppendLine(text); }
void Emit(string text)
{
    foreach (var line in text.Split('\n'))
        UnityEngine.Debug.Log("DEPOT_Q|" + line.TrimEnd('\r'));
}
object Priv(object owner, string name)
{
    if (owner == null) return null;
    var f = owner.GetType().GetField(name, flags);
    return f != null ? f.GetValue(owner) : null;
}
object Call(object owner, string name, object[] args)
{
    if (owner == null) return null;
    var m = owner.GetType().GetMethod(name, flags);
    return m != null ? m.Invoke(owner, args) : null;
}
UnityEngine.Transform FindDeep(UnityEngine.Transform root, string name)
{
    if (root == null) return null;
    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == name) return t;
    return null;
}
void Teleport(UnityEngine.Transform target, UnityEngine.Vector3 where)
{
    // CharacterController держит своё положение и при следующем Move вернул бы
    // объект назад; выключение на время перестановки синхронизирует его.
    var cc = target.GetComponent<UnityEngine.CharacterController>();
    if (cc != null) cc.enabled = false;
    target.position = where;
    UnityEngine.Physics.SyncTransforms();
    if (cc != null) cc.enabled = true;
}

UnityEngine.Transform player = null;
UnityEngine.Transform cell = null;
FrostSpace.Features.Dialogue.DialogueController dialogue = null;
FrostSpace.Features.View.PlayerCameraRig rig = null;
FrostSpace.Features.Menu.PauseMenuScreen pause = null;
FrostSpace.Features.Story.PlayerEquipment equipment = null;
FrostSpace.Features.Scan.ObjectScanner scanner = null;
FrostSpace.Features.Map.MiniMap miniMap = null;
FrostSpace.Features.Map.FarLayer farLayer = null;
UnityEngine.Camera viewCamera = null;

int phase = 0;
int wait = 0;
float t0 = 0f;
int speakerIndex = 0;
var speakers = new[] { "R16_Warden_A", "R16_Warden_B", "R8_Bulwark", "R9_Tinker", "R20_Cogsmith" };

// Прогон одного разговора: подвести игрока с четырёх сторон, спросить игру
// (FindTarget), открыть штатным Open и пролистать до закрытия.
bool TalkTo(UnityEngine.Transform actor)
{
    if (actor == null) return false;
    var speaker = actor.GetComponent<FrostSpace.Features.Dialogue.DialogueSpeaker>();
    if (speaker == null || !speaker.CanTalk) return false;

    var dirs = new[]
    {
        UnityEngine.Vector3.forward, -UnityEngine.Vector3.forward,
        UnityEngine.Vector3.right, -UnityEngine.Vector3.right
    };

    FrostSpace.Features.Dialogue.DialogueSpeaker got = null;
    foreach (var d in dirs)
    {
        Teleport(player, actor.position + d * 2.4f);
        player.rotation = UnityEngine.Quaternion.LookRotation(-d);

        // Камеру ставит так, как её через кадр положил бы риг: после переноса
        // она висит на старом месте, а луч идёт из камеры и ограничен 12 м.
        viewCamera.transform.position = player.position - player.forward * 1.2f
                                        + UnityEngine.Vector3.up * 1.5f;
        var aim = actor.position + UnityEngine.Vector3.up * 1.1f - viewCamera.transform.position;
        viewCamera.transform.rotation = UnityEngine.Quaternion.LookRotation(aim);

        got = Call(dialogue, "FindTarget", null) as FrostSpace.Features.Dialogue.DialogueSpeaker;
        if (got == speaker) break;
    }

    if (got != speaker)
    {
        var nearest = float.PositiveInfinity;
        foreach (var s in UnityEngine.Object.FindObjectsByType<FrostSpace.Features.Dialogue.DialogueSpeaker>(
                     UnityEngine.FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None))
        {
            var d = UnityEngine.Vector3.Distance(player.position, s.transform.position);
            if (d < nearest) nearest = d;
        }
        Line("  " + actor.name + ": нет цели (CanTalk=" + speaker.CanTalk +
             ", rigPaused=" + rig.IsPaused + ", ближайшийГоворящий=" + nearest.ToString("F1") + " м)");
        return false;
    }

    Call(dialogue, "Open", new object[] { speaker });
    if (!dialogue.IsOpen) return false;

    var seen = new System.Collections.Generic.List<object>();
    var guard = 0;
    while (dialogue.IsOpen && guard++ < 60)
    {
        var session = Priv(dialogue, "_session")
            as FrostSpace.Features.Dialogue.DialogueSession;
        if (session == null) break;

        var node = session.Current;
        if (node == null) break;
        if (!seen.Contains(node)) seen.Add(node);

        if (session.HasChoices)
            Call(dialogue, "OnChoicePressed", new object[] { 0 });
        else
            Call(dialogue, "OnAdvancePressed", null);
    }

    if (dialogue.IsOpen) Call(dialogue, "Close", null);

    var acts = new System.Text.StringBuilder();
    foreach (var node in seen)
    {
        var type = node.GetType();
        if (!(bool)type.GetProperty("HasAction").GetValue(node)) continue;
        if (acts.Length > 0) acts.Append(',');
        acts.Append(type.GetProperty("Action").GetValue(node).ToString());
    }

    Line("  " + actor.name + ": узлов " + seen.Count + ", действия [" +
         (acts.Length > 0 ? acts.ToString() : "нет") + "]");
    return true;
}

UnityEditor.EditorApplication.CallbackFunction act = null;
act = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying)
    {
        UnityEditor.EditorApplication.update -= act;
        return;
    }

    if (player == null)
    {
        var go = UnityEngine.GameObject.Find("Player");
        if (go == null) return;
        player = go.transform;
        rig = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.View.PlayerCameraRig>();
        dialogue = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Dialogue.DialogueController>();
        pause = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Menu.PauseMenuScreen>();
        equipment = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Story.PlayerEquipment>();
        scanner = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Scan.ObjectScanner>();
        miniMap = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Map.MiniMap>();
        farLayer = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Map.FarLayer>();
        viewCamera = rig != null ? rig.ViewCamera : null;
        var cells = UnityEngine.GameObject.Find("WorldCells");
        cell = cells != null ? cells.transform.Find("WorldCell_-2_-2") : null;
        t0 = UnityEngine.Time.time;
    }

    if (rig == null || dialogue == null || equipment == null || scanner == null || cell == null)
    {
        if (UnityEngine.Time.time > 5f)
        {
            UnityEditor.EditorApplication.update -= act;
            Emit("НЕ НАЙДЕНЫ ссылки: rig/dialogue/equipment/scanner/клетка депо");
        }
        return;
    }

    if (phase == 0)
    {
        if (UnityEngine.Time.time < 1f) return;
        Line("t=" + UnityEngine.Time.time.ToString("F1") + " в пути, parent=" +
             (player.parent != null ? player.parent.name : "<root>"));
        phase = 1;
        return;
    }

    if (phase == 1) // DP-01: ждём возврат управления
    {
        // Признак возврата — снятая блокировка, а не отцепление от вагона:
        // игрока отцепляют раньше, чем кончается проезд камеры (проверено:
        // на 7,3 с parent уже <root>, а _controlLock ещё true).
        var lockProbe = typeof(FrostSpace.Features.Locomotion.RobotLocomotion)
            .GetField("_controlLock", flags);
        var bodyProbe = player.GetComponent<FrostSpace.Features.Locomotion.RobotLocomotion>();
        var stillLocked = lockProbe != null && bodyProbe != null &&
                          (bool)lockProbe.GetValue(bodyProbe);

        if (stillLocked || rig.IsPaused)
        {
            if (UnityEngine.Time.time - t0 > 60f)
            {
                Line("DP-01 ПРОВАЛ: управление не вернулось за 60 с (locked=" +
                     stillLocked + ", rigPaused=" + rig.IsPaused + ")");
                phase = 9;
            }
            return;
        }

        var train = FindDeep(cell, "DepotTrain");
        var r13 = FindDeep(cell, "R13_Prospector");
        var cart = FindDeep(cell, "Prop_GravCart");

        var bounds = new UnityEngine.Bounds();
        var first = true;
        foreach (var r in train.GetComponentsInChildren<UnityEngine.Renderer>(true))
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }

        var lockField = typeof(FrostSpace.Features.Locomotion.RobotLocomotion)
            .GetField("_controlLock", flags);
        var body = player.GetComponent<FrostSpace.Features.Locomotion.RobotLocomotion>();
        var locked = lockField != null && body != null && (bool)lockField.GetValue(body);

        Line("DP-01 t=" + UnityEngine.Time.time.ToString("F1") + " управление=" +
             (locked ? "ЗАБЛОКИРОВАНО" : "свободно") +
             " ригPaused=" + rig.IsPaused + " пауза=" + (pause != null && pause.IsPaused));
        Line("  R13=" + (r13 != null ? r13.position.ToString("F2") : "НЕТ") +
             " внеВагона=" + (r13 != null && !bounds.Contains(r13.position)) +
             " ребёнокВагона=" + (r13 != null && r13.IsChildOf(train)));
        Line("  тележка=" + (cart != null ? cart.position.ToString("F2") : "НЕТ") +
             " внеВагона=" + (cart != null && !bounds.Contains(cart.position)) +
             " ребёнокВагона=" + (cart != null && cart.IsChildOf(train)));
        var r13speaker = r13 != null
            ? r13.GetComponent<FrostSpace.Features.Dialogue.DialogueSpeaker>() : null;
        Line("  R13 говорит=" + (r13speaker != null && r13speaker.CanTalk) +
             " сканируется=" + (r13 != null &&
             r13.GetComponent<FrostSpace.Features.Scan.ScanTarget>() != null));

        phase = 2;
        wait = 5;
        return;
    }

    if (wait > 0) { wait--; return; }

    if (phase == 2) // DP-02: по одному говорящему за кадр
    {
        if (speakerIndex >= speakers.Length)
        {
            phase = 3;
            wait = 3;
            return;
        }

        var who = speakers[speakerIndex];
        var actor = FindDeep(cell, who);
        var target = actor != null
            ? actor.GetComponent<FrostSpace.Features.Scan.ScanTarget>() : null;
        var ok = TalkTo(actor);
        if (!ok) Line("  " + who + ": разговор НЕ ОТКРЫЛСЯ");
        Line("  " + who + ": скан=" + (target != null));
        speakerIndex++;
        wait = 3;
        return;
    }

    if (phase == 3) // DP-03: первый разговор с R7
    {
        var r7 = FindDeep(cell, "R7_Drifter");
        var ok = TalkTo(r7);
        mapBefore = equipment.HasMap;
        scannerBefore = equipment.HasScanner;
        mapCellBefore = equipment.Board.CellOf(FrostSpace.Features.Inventory.EquipmentKind.Map);
        scannerCellBefore = equipment.Board.CellOf(FrostSpace.Features.Inventory.EquipmentKind.Scanner);

        Line("DP-03 первый разговор=" + ok +
             " карта=" + mapBefore + " сканер=" + scannerBefore +
             " (ячейкаКарты=" + mapCellBefore + ", ячейкаСканера=" + scannerCellBefore + ")");
        Line("  сканер.available=" + scanner.Available + " enabled=" + scanner.enabled +
             " естьМиникарта=" + (miniMap != null));
        var mapRoot = Priv(miniMap, "_root") as UnityEngine.GameObject;
        Line("  кореньМиникарты активен=" + (mapRoot != null && mapRoot.activeSelf));

        phase = 4;
        wait = 5;
        return;
    }

    if (phase == 4) // DP-03: повторный разговор не выдаёт второй раз
    {
        var r7 = FindDeep(cell, "R7_Drifter");
        var ok = TalkTo(r7);
        var same = mapBefore == equipment.HasMap && scannerBefore == equipment.HasScanner
                   && mapCellBefore == equipment.Board.CellOf(FrostSpace.Features.Inventory.EquipmentKind.Map)
                   && scannerCellBefore == equipment.Board.CellOf(FrostSpace.Features.Inventory.EquipmentKind.Scanner);
        Line("DP-03 повторный разговор=" + ok + " состояниеСнаряженияНеИзменилось=" + same +
             " карта=" + equipment.HasMap + " сканер=" + equipment.HasScanner);

        scanner.StartScan();
        Line("  Q: сканЗапущен=" + scanner.IsScanning + " available=" + scanner.Available);

        phase = 5;
        wait = 5;
        return;
    }

    if (phase == 5) // DP-04: управление и финальный экран
    {
        var lockField = typeof(FrostSpace.Features.Locomotion.RobotLocomotion)
            .GetField("_controlLock", flags);
        var body = player.GetComponent<FrostSpace.Features.Locomotion.RobotLocomotion>();
        var locked = lockField != null && body != null && (bool)lockField.GetValue(body);
        var title = UnityEngine.Object.FindAnyObjectByType<FrostSpace.Features.Story.TitleCardScreen>();
        var map = UnityEngine.InputSystem.InputSystem.actions != null
            ? UnityEngine.InputSystem.InputSystem.actions.FindActionMap("Player", false) : null;

        Line("DP-04 управление=" + (locked ? "ЗАБЛОКИРОВАНО" : "свободно") +
             " timeScale=" + UnityEngine.Time.timeScale +
             " титулИдёт=" + (title != null && title.IsPlaying) +
             " курсор=" + UnityEngine.Cursor.lockState +
             " пауза=" + (pause != null && pause.IsPaused) +
             " картаВвода=" + (map != null && map.enabled));

        phase = 6;
        wait = 2;
        return;
    }

    if (phase == 6) // DP-07: дальний слой едет за игроком
    {
        if (farLayer == null)
        {
            Line("DP-07 ПРОВАЛ: в сцене нет FarLayer");
            phase = 9;
            return;
        }

        var p = player.position;
        var f = farLayer.transform.position;
        var flat = new UnityEngine.Vector2(f.x - p.x, f.z - p.z).magnitude;
        var cards = farLayer.GetComponentsInChildren<UnityEngine.Renderer>(true);
        var names = new System.Text.StringBuilder();
        foreach (var c in cards)
        {
            if (names.Length > 0) names.Append(',');
            names.Append(c.name);
        }

        Line("DP-07 слой=" + f.ToString("F1") + " игрок=" + p.ToString("F1") +
             " сносПоXZ=" + flat.ToString("F3") + " карточек=" + cards.Length +
             " [" + names + "]");

        phase = 9;
        return;
    }

    if (phase == 9)
    {
        UnityEditor.EditorApplication.update -= act;
        Emit(report.ToString());
    }
};

UnityEditor.EditorApplication.update += act;
return "хук поставлен: ждём прибытия поезда и выхода игрока";
