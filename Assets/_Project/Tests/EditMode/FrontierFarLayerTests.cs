using System.Reflection;
using FrostSpace.Features.Map;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Страховка на расстановку дальнего слоя в сцене `Frontier`.
    ///
    /// Слой ставится руками (ADR 0035), а его координаты живут в
    /// `world_layout.py`. Расхождение выглядит так: силуэт стоит не на своём
    /// азимуте, «станция» уезжает с горловины дороги, и никто это не замечает,
    /// пока кто-нибудь не решит, что туман надо усилить. Поэтому здесь сверяются
    /// именно позиции детей, их число и то, что карточка нечитаемого якоря в
    /// сцене отсутствует: рисовать то, что не видно, — не «запас на будущее», а
    /// молча оплаченные меши.
    ///
    /// Тест открывает сцену только если терять нечего — по образцу
    /// <see cref="EquipmentSceneTests"/>.
    /// </summary>
    public sealed class FrontierFarLayerTests
    {
        private const string ScenePath = "Assets/Scenes/Frontier.unity";

        private const string JsonPath =
            "Assets/_Project/Content/Data/World/world_layout.json";

        /// <summary>Допуск расстановки: карточка ставится по числу из раскладки,
        /// сантиметр — это уже «кто-то подвинул руками».</summary>
        private const float PlaceTolerance = 0.02f;

        private static readonly BindingFlags Fields =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void Frontier_HasFarLayer_AtLayoutPositions()
        {
            var original = EditorSceneManager.GetActiveScene();
            if (original.isDirty && original.path != ScenePath)
                Assert.Ignore(
                    "активная сцена " + original.name + " не сохранена — "
                    + "открывать Frontier значит затерять правки");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                Assert.That(scene.IsValid(), Is.True, "сцена не открылась");

                var root = GameObject.Find("FarLayer");
                Assert.That(root, Is.Not.Null,
                    "в Frontier нет FarLayer — дальнего слоя нет вовсе");

                var layer = root.GetComponent<FarLayer>();
                Assert.That(layer, Is.Not.Null,
                    "на FarLayer нет компонента: слой стоял бы на месте и полз бы "
                    + "по небу вместе с каждым шагом игрока");
                var player = Read(typeof(FarLayer), layer, "_player") as Transform;
                Assert.That(player, Is.Not.Null, "FarLayer не знает игрока");

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
                WorldLayout layout;
                Assert.That(WorldLayout.TryParse(asset, out layout), Is.True,
                    "раскладка мира не разобралась — сверять расстановку не с чем");

                var want = 0;
                for (var i = 0; i < layout.FarCards.Count; i++)
                {
                    var card = layout.FarCards[i];
                    if (!card.Drawn)
                        continue;

                    want++;
                    var child = ChildAt(root.transform, card.Position);
                    Assert.That(child, Is.Not.Null,
                        "карточки " + card.Key + " нет в (" + card.Position.x +
                        ", " + card.Position.y + ") — силуэт уехал с азимута " +
                        card.Bearing + "°");

                    var view = child.GetComponentInChildren<MeshRenderer>(true);
                    Assert.That(view, Is.Not.Null,
                        card.Key + ": у карточки нет рендера, то есть её не видно");
                    Assert.That(view.sharedMaterial, Is.Not.Null,
                        card.Key + ": материал не подменён LevelAssetPostprocessor");
                    Assert.That(view.shadowCastingMode,
                        Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off),
                        card.Key + ": силуэт в 380 м отбрасывает тень — это расход "
                        + "теней ради пикселей, которые съел туман");
                }

                Assert.That(root.transform.childCount, Is.EqualTo(want),
                    "у слоя " + root.transform.childCount + " детей, раскладка "
                    + "обещает " + want + " читаемых карточек");
            }
            finally
            {
                if (original.IsValid() && original.path != ScenePath)
                    EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
            }
        }

        /// <summary>Первый ребёнок на этой точке слоя; null — если такого нет.</summary>
        private static Transform ChildAt(Transform root, Vector2 position)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var dx = child.localPosition.x - position.x;
                var dz = child.localPosition.z - position.y;
                if (dx * dx + dz * dz < PlaceTolerance * PlaceTolerance
                    && Mathf.Abs(child.localPosition.y) < PlaceTolerance)
                {
                    return child;
                }
            }

            return null;
        }

        private static object Read(System.Type type, Object target, string name)
        {
            var field = type.GetField(name, Fields);
            Assert.That(field, Is.Not.Null, "нет сериализованного поля " + name);
            return field.GetValue(target);
        }
    }
}
