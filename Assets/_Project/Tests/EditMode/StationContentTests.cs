using System.Collections.Generic;
using System.Reflection;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Map;
using FrostSpace.Features.Npc;
using FrostSpace.Features.Scan;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Станция «Север-7» (клетка (0,0)) — узел, который владелец задал словом
    /// «комплекс»: отсеки с комнатами, гигантский ангар со сломанным
    /// космическим кораблём и столько разных роботов, сколько есть в проекте.
    ///
    /// Проверки держат четыре обещания:
    /// 1. каждая модель робота проекта стоит на станции — модель, которой нет
    ///    ни в одной сцене, собирается, весит в билде и никого не играет;
    /// 2. ангар настоящий: пролёт держит обломок, а обломок лежит внутри
    ///    ангара, а не рядом с ним и не сквозь стену;
    /// 3. платформа стоит вдоль пути, а не под путём: магистраль приходит в
    ///    двор по оси узла, и это число, а не надежда на генератор;
    /// 4. узел освещён, сканируется и говорит: четыре сюжета станции закрыты
    ///    валидными диалогами.
    ///
    /// Тест читает префаб клетки, а не сцену: содержимое узла живёт в префабе
    /// (ADR 0037), и обещание станции не должно зависеть от того, какая сцена
    /// открыта в редакторе в момент прогона.
    /// </summary>
    public sealed class StationContentTests
    {
        private const string CellPrefab =
            "Assets/_Project/Content/Prefabs/World/WorldCell_0_0.prefab";

        private const string RobotFolder =
            "Assets/_Project/Content/Prefabs/Robots";

        private const string LayoutAsset =
            "Assets/_Project/Content/Data/World/world_layout.json";

        /// <summary>Габарит ангара в локальных осях клетки (station_parts.HANGAR).</summary>
        private static readonly Rect HangarArea = new Rect(34f, -28f, 70f, 80f);

        /// <summary>Платформа (station_parts.PLATFORM): x 6..26, z −50..45.</summary>
        private const float PlatformWestEdge = 6f;
        private const float PlatformSouth = -50f;
        private const float PlatformNorth = 45f;

        private static readonly BindingFlags Fields =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static GameObject _contents;

        [OneTimeSetUp]
        public void LoadCell()
        {
            _contents = PrefabUtility.LoadPrefabContents(CellPrefab);
            Assert.That(_contents, Is.Not.Null,
                "не открылся префаб клетки станции");
        }

        [OneTimeTearDown]
        public void ReleaseCell()
        {
            if (_contents != null) PrefabUtility.UnloadPrefabContents(_contents);
        }

        private static Transform Group(string name)
        {
            var group = _contents.transform.Find(name);
            Assert.That(group, Is.Not.Null,
                "в префабе клетки нет группы " + name + " — содержимое станции потеряно");
            return group;
        }

        private static object Value(object target, string field)
        {
            return target.GetType().GetField(field, Fields).GetValue(target);
        }

        [Test]
        public void EveryRobotModelStandsOnStation()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { RobotFolder });
            var available = new List<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("_Model.prefab")) continue;
                available.Add(System.IO.Path.GetFileNameWithoutExtension(path));
            }
            Assert.That(available.Count, Is.GreaterThanOrEqualTo(20),
                "в проекте моделей роботов " + available.Count +
                ", а узел задуман под все двадцать");

            var used = new HashSet<string>();
            foreach (var anim in Group("Station").GetComponentsInChildren<Animator>(true))
            {
                var root = PrefabUtility.GetCorrespondingObjectFromSource(anim.gameObject);
                if (root == null) continue;
                var path = AssetDatabase.GetAssetPath(root);
                if (path.StartsWith(RobotFolder)) used.Add(
                    System.IO.Path.GetFileNameWithoutExtension(path));
            }

            var missing = new List<string>();
            foreach (var model in available)
            {
                if (!used.Contains(model)) missing.Add(model);
            }
            Assert.That(missing, Is.Empty,
                "на станции не задействованы модели: " + string.Join(", ", missing));
        }

        [Test]
        public void HangarIsGiantAndHoldsTheWreck()
        {
            var hangar = FindPart("Station_Hangar");
            Assert.That(hangar, Is.Not.Null, "в клетке нет части Station_Hangar");
            var box = hangar.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(box.size.x, Is.GreaterThanOrEqualTo(66f),
                "пролёт ангара по X " + box.size.x + " м — меньше обещанных 70");
            Assert.That(box.size.z, Is.GreaterThanOrEqualTo(76f),
                "пролёт ангара по Z " + box.size.z + " м — меньше обещанных 80");
            Assert.That(box.size.y, Is.GreaterThanOrEqualTo(15f),
                "высота ангара " + box.size.y + " м — под корабль не хватит");

            // Обломок — часть клетки: корпус 44 м генерирует `station_parts`,
            // и он обязан лежать внутри пролёта, а не торчать сквозь стену.
            var wreck = FindPart("Station_Wreck");
            Assert.That(wreck, Is.Not.Null, "в клетке нет части Station_Wreck");
            var wb = wreck.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(wb.size.x, Is.GreaterThanOrEqualTo(34f),
                "размах обломка по X " + wb.size.x + " м — корабль не гигантский");
            Assert.That(wb.size.z, Is.GreaterThanOrEqualTo(34f),
                "размах обломка по Z " + wb.size.z + " м — корабль не гигантский");
            Assert.That(HangarArea.xMin, Is.LessThanOrEqualTo(wb.min.x + 34f),
                "обломок вышел за западную стену ангара");
            Assert.That(wb.max.y, Is.LessThanOrEqualTo(box.max.y),
                "обломок выше кровли ангара");

            var engine = 0;
            foreach (Transform child in Group("Station"))
            {
                if (!child.name.StartsWith("MenuWreck")) continue;
                engine++;
                var p = child.position;
                Assert.That(HangarArea.Contains(new Vector2(p.x, p.z)), Is.True,
                    child.name + " стоит в (" + p.x + ", " + p.z + ") — вне ангара");
                Assert.That(child.GetComponent<ScanTarget>(), Is.Not.Null,
                    child.name + ": движок корабля не сканируется");
            }
            Assert.That(engine, Is.EqualTo(1),
                "меню-обломков в ангаре " + engine + ", а оставлен один движок: " +
                "корпус генерируется частью клетки, и масштаб у него честный");
        }

        /// <summary>
        /// Платформа стоит вдоль пути, а не под путём. Владелец выбрал станции
        /// роль «остановка», и до правки 2026-11-04 магистраль входила во двор
        /// по диагонали 52°: ось проходила сквозь настил (замер — 1,35 м от оси
        /// до западного края платформы) и срезала юго-восточный угол ремзоны.
        /// Числа трассы и чисел клетки здесь не хватало — нужен общий источник,
        /// и он есть: JSON раскладки.
        /// </summary>
        [Test]
        public void PlatformStandsBesideTheMainLine()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutAsset);
            Assert.That(text, Is.Not.Null, "нет JSON раскладки: " + LayoutAsset);
            Assert.That(WorldLayout.TryParse(text, out var layout), Is.True,
                "раскладка не разобралась");
            var node = layout.FindNode("station");
            Assert.That(node, Is.Not.Null, "в раскладке нет узла station");

            WorldLine rail = null;
            foreach (var line in layout.Lines)
            {
                if (line.Kind == WorldLineKind.Rail) rail = line;
            }
            Assert.That(rail, Is.Not.Null, "в раскладке нет рельсовой линии");

            var worst = 0f;
            var measured = 0;
            for (var i = 0; i + 1 < rail.Points.Length; i++)
            {
                var ax = rail.Points[i].x - node.Position.x;
                var az = rail.Points[i].y - node.Position.y;
                var bx = rail.Points[i + 1].x - node.Position.x;
                var bz = rail.Points[i + 1].y - node.Position.y;
                for (var z = PlatformSouth; z <= PlatformNorth + 0.01f; z += 5f)
                {
                    if (Mathf.Abs(bz - az) < 1e-4f) continue;
                    var t = (z - az) / (bz - az);
                    if (t < 0f || t > 1f) continue;
                    var x = Mathf.Lerp(ax, bx, t);
                    measured++;
                    worst = Mathf.Max(worst, Mathf.Abs(x));
                }
            }
            Assert.That(measured, Is.GreaterThanOrEqualTo(15),
                "ось пути пересекла платформу " + measured + " раз — трасса в " +
                "клетке станции читается огрызками");
            Assert.That(worst, Is.LessThanOrEqualTo(3f),
                "ось пути уходит от x = 0 на " + worst.ToString("0.00") +
                " м: путь во дворе должен идти по оси узла, иначе он режет настил");
            Assert.That(PlatformWestEdge - worst, Is.GreaterThanOrEqualTo(3f),
                "от оси пути до западного края настила " +
                (PlatformWestEdge - worst).ToString("0.00") +
                " м: платформа встала бы на путь");
        }

        [Test]
        public void ComplexHasRoomsAndOneIsOpen()
        {
            var complex = FindPart("Station_Complex");
            Assert.That(complex, Is.Not.Null, "в клетке нет части Station_Complex");
            var mesh = complex.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.size.x, Is.GreaterThanOrEqualTo(50f),
                "комплекс по X " + mesh.bounds.size.x + " м — отсеков не хватит");
            Assert.That(mesh.bounds.size.z, Is.GreaterThanOrEqualTo(100f),
                "комплекс по Z " + mesh.bounds.size.z + " м — отсеков не хватит");

            // Вскрытый отсек: без него наполнение комнат существует только для
            // того, кто умеет проходить сквозь стены. Меряется кровлей: над
            // ремзоной вершин выше потолка быть не должно, над остальными
            // отсеками — должны.
            var open = 0;
            var closed = 0;
            var verts = mesh.vertices;
            var roofTop = -4.440f + 0.20f + 4.40f + 0.30f;
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                if (v.y > 0f) continue;                      // Blender y = -z
                var z = -v.y;
                if (v.x > -42f || v.x < -94f) continue;
                if (z > -10f && z < 4f) continue;            // соседние отсеки
                if (v.z > roofTop)
                {
                    if (z < -54f || z > -10f) closed++;
                    else open++;
                }
            }
            Assert.That(open, Is.LessThan(24),
                "над ремзоной стоит кровли вершин " + open + " — отсек не вскрыт, " +
                "комнаты снова невидимы");
            Assert.That(closed, Is.GreaterThan(64),
                "над закрытыми отсеками кровли почти нет (" + closed + ") — " +
                "комплекс превратился в руины");
        }

        [Test]
        public void StationSpeakersTellFourStoryLines()
        {
            var speakers = Group("Station").GetComponentsInChildren<DialogueSpeaker>(true);
            Assert.That(speakers.Length, Is.GreaterThanOrEqualTo(4),
                "на станции говорящих " + speakers.Length +
                ", а сюжетов четыре: ворота, диспетчер, врач, ангар");

            var names = new List<string>();
            foreach (var speaker in speakers)
            {
                var dialogue = Value(speaker, "_dialogue") as DialogueAsset;
                Assert.That(dialogue, Is.Not.Null,
                    speaker.name + ": реплика есть, ассета диалога нет");
                Assert.That(dialogue.name, Does.StartWith("Station"),
                    speaker.name + ": диалог " + dialogue.name + " лежит не на станции");
                var errors = new List<string>();
                var warnings = new List<string>();
                dialogue.CollectProblems(errors, warnings);
                Assert.That(errors, Is.Empty, speaker.name + ": диалог " +
                    dialogue.name + " игра не откроет — " + string.Join("; ", errors));
                Assert.That(warnings, Is.Empty, speaker.name + ": диалог " +
                    dialogue.name + " имеет висячие узлы — " + string.Join("; ", warnings));
                names.Add(dialogue.name);
            }
            Assert.That(new HashSet<string>(names).Count, Is.EqualTo(names.Count),
                "два робота станции говорят одно и то же");
        }

        [Test]
        public void NodeIsLitAndCrewWalks()
        {
            var lights = Group("Station").GetComponentsInChildren<Light>(true);
            Assert.That(lights.Length, Is.GreaterThanOrEqualTo(6),
                "на станции источников " + lights.Length + ": ангар и платформа " +
                "не освещаются одним фонарём");

            var walking = 0;
            foreach (var npc in Group("Station").GetComponentsInChildren<NpcController>(true))
            {
                var wps = Value(npc, "_waypoints") as System.Array;
                if (wps != null && wps.Length >= 3) walking++;
            }
            Assert.That(walking, Is.GreaterThanOrEqualTo(6),
                "с маршрутом ходят " + walking + " корпусов — узел выглядит " +
                "стоячей толпой, а не сменой");
        }

        private static Transform FindPart(string start)
        {
            foreach (var t in _contents.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(start)) return t;
            }
            return null;
        }
    }
}
