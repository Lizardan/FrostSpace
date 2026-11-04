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
    /// Поселение (клетка (0,−2)) обязано быть обжитым узлом, а не площадкой с
    /// забором. Владелец задал узлу три сюжета: здесь проверяют погоду, здесь
    /// содержат машин, у которых помутился разум (их переделывают в животных),
    /// и здесь добывают электричество. Каждый пункт проверяется числом, потому
    /// что «выглядит обжитым» не ловит ни пустой двор, ни робота без маршрута,
    /// ни диалог, который игра не откроет.
    ///
    /// Актёры живут внутри префаба клетки (ADR 0043): `sync_cell_prefabs.cs`
    /// трогает только помеченные `CellMesh`, поэтому авторское содержимое
    /// переживает пересборку частей. Тест читает именно префаб — сцена может
    /// быть открыта в другой момент, а обещание узла не должно от этого зависеть.
    /// </summary>
    public sealed class SettlementContentTests
    {
        private const string CellPrefab =
            "Assets/_Project/Content/Prefabs/World/WorldCell_0_-2.prefab";

        private const string LayoutAsset =
            "Assets/_Project/Content/Data/World/world_layout.json";

        /// <summary>Смещение клетки (0,−2) в осях сцены: её центр — (1200, 0).</summary>
        private const float CellSceneX = 800f;

        /// <summary>Пол узла: `PAD_LEVEL0["settlement"]`, ADR 0041.</summary>
        private const float FloorLevel = -0.175f;

        private static readonly BindingFlags Fields =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private static GameObject _contents;

        [OneTimeSetUp]
        public void LoadCell()
        {
            _contents = PrefabUtility.LoadPrefabContents(CellPrefab);
            Assert.That(_contents, Is.Not.Null,
                "не открылся префаб клетки поселения");
        }

        [OneTimeTearDown]
        public void ReleaseCell()
        {
            if (_contents != null) PrefabUtility.UnloadPrefabContents(_contents);
        }

        private static Transform Group()
        {
            var group = _contents.transform.Find("Settlement");
            Assert.That(group, Is.Not.Null,
                "в префабе клетки нет группы Settlement — наполнение поселения потеряно");
            return group;
        }

        private static T[] InGroup<T>() where T : Component
        {
            return Group().GetComponentsInChildren<T>(true);
        }

        private static object Value(object target, string field)
        {
            return target.GetType().GetField(field, Fields).GetValue(target);
        }

        private static int Waypoints(NpcController npc)
        {
            var wps = Value(npc, "_waypoints") as System.Array;
            return wps == null ? 0 : wps.Length;
        }

        [Test]
        public void NodeHasTalkingRobotsForEveryStoryLine()
        {
            var speakers = InGroup<DialogueSpeaker>();
            Assert.That(speakers.Length, Is.GreaterThanOrEqualTo(4),
                "у поселения говорящих " + speakers.Length +
                ", а сюжетов четыре: погода, приют, электричество, мастерская");

            var names = new List<string>();
            foreach (var speaker in speakers)
            {
                var dialogue = Value(speaker, "_dialogue") as DialogueAsset;
                Assert.That(dialogue, Is.Not.Null,
                    speaker.name + ": реплика есть, ассета диалога нет");
                Assert.That(dialogue.name, Does.Contain("Settlement"),
                    speaker.name + ": диалог " + dialogue.name + " лежит не в поселении");

                var errors = new List<string>();
                var warnings = new List<string>();
                dialogue.CollectProblems(errors, warnings);
                Assert.That(errors, Is.Empty, speaker.name + ": диалог " +
                    dialogue.name + " игра не откроет — " + string.Join("; ", errors));

                var npc = Value(speaker, "_npc") as NpcController;
                Assert.That(npc, Is.Not.Null,
                    speaker.name + ": говорить некому, контроллера ходьбы нет");
                Assert.That(Waypoints(npc), Is.GreaterThanOrEqualTo(2),
                    speaker.name + ": маршрут короче двух точек, робот стоит на месте");
                names.Add(dialogue.name);
            }
            Assert.That(HasDuplicates(names), Is.False,
                "два робота говорят один и тот же текст: " + string.Join(", ", names));
        }

        [Test]
        public void ShelterKeepsReworkedMachinesWithRoutes()
        {
            var animals = 0;
            foreach (var npc in InGroup<NpcController>())
            {
                if (npc.gameObject.name.StartsWith("AN_")) animals++;
            }
            Assert.That(animals, Is.GreaterThanOrEqualTo(8),
                "в приюте машин-животных " + animals + ", а узел задуман приютом");

            foreach (var npc in InGroup<NpcController>())
            {
                Assert.That(Waypoints(npc), Is.GreaterThanOrEqualTo(3),
                    npc.name + ": маршрут из " + Waypoints(npc) +
                    " точек — это стойка, а не жизнь");
            }
        }

        [Test]
        public void EverythingStandsInsideWarmNode()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutAsset);
            Assert.That(text, Is.Not.Null, "нет JSON раскладки: " + LayoutAsset);
            Assert.That(WorldLayout.TryParse(text, out var layout), Is.True,
                "раскладка не разобралась");
            var node = layout.FindNode("settlement");
            Assert.That(node, Is.Not.Null, "в раскладке нет узла settlement");
            Assert.That(node.Warm, Is.True, "поселение должно быть тёплым узлом");
            Assert.That(node.Area.HasValue, Is.True, "у поселения нет прямоугольника");

            var rect = node.Area.Value;
            foreach (Transform actor in Group())
            {
                var world = layout.ToWorld(new Vector2(
                    CellSceneX + actor.position.x, actor.position.z));
                Assert.That(rect.Contains(world), Is.True,
                    actor.name + " стоит в " + world + " — вне тёплой площадки " + rect);
                Assert.That(actor.position.y, Is.GreaterThanOrEqualTo(FloorLevel - 0.06f),
                    actor.name + ": отметка " + actor.position.y +
                    " ушла ниже пола узла " + FloorLevel);
            }
        }

        [Test]
        public void MastAndPostsAreScannable()
        {
            var labels = new List<string>();
            foreach (var target in InGroup<ScanTarget>())
            {
                labels.Add((string)Value(target, "_displayName"));
            }
            Assert.That(labels.Exists(n => n.Contains("Метеовышка")), Is.True,
                "метеовышка поселения не сканируется: " + string.Join(", ", labels));
            Assert.That(labels.Exists(n => n.Contains("Мастерская") ||
                                           n.Contains("мастерская")), Is.True,
                "мастерская приюта не сканируется");
            Assert.That(labels.Exists(n => n.Contains("подстанции") ||
                                           n.Contains("метеонаблюдения")), Is.True,
                "посты узла (подстанция, метеопост) не сканируются");
        }

        [Test]
        public void NodeIsLitForNight()
        {
            var lights = InGroup<Light>();
            Assert.That(lights.Length, Is.GreaterThanOrEqualTo(5),
                "в поселении источников " + lights.Length + ": узел, который держит " +
                "тепло и погоду, не может освещаться одним фонарём");
            var warm = 0;
            foreach (var light in lights)
            {
                if (light.color.b < light.color.r) warm++;
                Assert.That(light.range, Is.GreaterThan(0f),
                    light.name + ": дальность нулевая");
            }
            Assert.That(warm, Is.GreaterThanOrEqualTo(4),
                "тёплого света " + warm + " — поселение читается холодным узлом");
        }

        private static bool HasDuplicates(List<string> values)
        {
            var seen = new HashSet<string>();
            foreach (var value in values)
            {
                if (!seen.Add(value)) return true;
            }
            return false;
        }
    }
}
