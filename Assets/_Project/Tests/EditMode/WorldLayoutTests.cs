using System.Collections.Generic;
using FrostSpace.Features.Map;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Разбор раскладки мира и согласие C# с Python.
    ///
    /// Тест читает **настоящий** `world_layout.json`, а не выдуманный: цифры
    /// раскладки живут в `Tools/Blender/world_layout.py`, и единственное, что
    /// держит их вместе с рантаймом, — этот прогон. Если Python изменит форму
    /// файла или правило границы клетки, тест упадёт здесь, а не в миникарте,
    /// которая врёт о направлении.
    /// </summary>
    public sealed class WorldLayoutTests
    {
        private const string JsonPath =
            "Assets/_Project/Content/Data/World/world_layout.json";

        private static WorldLayout ParseReal()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            Assert.That(asset, Is.Not.Null,
                JsonPath + " не импортирован как TextAsset — сборка мира не та");
            WorldLayout layout;
            Assert.That(WorldLayout.TryParse(asset, out layout), Is.True,
                "раскладка не разобралась");
            return layout;
        }

        [Test]
        public void ParsesGridAndKeyNumbers()
        {
            var layout = ParseReal();

            Assert.That(layout.Block, Is.EqualTo(600f), "сторона клетки");
            Assert.That(layout.CellMin, Is.EqualTo(-5));
            Assert.That(layout.CellMax, Is.EqualTo(5));
            Assert.That(layout.Grid.CellCount, Is.EqualTo(121));
            Assert.That(layout.Depot, Is.EqualTo(new Vector2(-1200f, -1200f)));
            Assert.That(layout.DepotToStation, Is.EqualTo(1697.0563f).Within(0.01f));

            // Те же числа, что держат сцену: туман из STYLE.md, запас из RD-09.
            Assert.That(layout.FogDensity, Is.EqualTo(0.006f).Within(1e-5f));
            Assert.That(layout.FarClip, Is.EqualTo(400f));
            Assert.That(layout.ColdBudgetSeconds, Is.EqualTo(90f));
            Assert.That(layout.ColdReach, Is.EqualTo(450f));
        }

        /// <summary>
        /// Сцена `Frontier` набрана в осях депо, файл — в осях раскладки. Пока
        /// это так, перевод обязан существовать и давать верное расстояние:
        /// без него миникарта центрируется на станции, пока игрок стоит во дворе.
        /// </summary>
        [Test]
        public void SceneOriginMapsToWorldDepot()
        {
            var layout = ParseReal();

            Assert.That(layout.ToWorld(Vector2.zero), Is.EqualTo(layout.Depot),
                "ноль сцены — это депо, а не центр мира");

            var distance = Vector2.Distance(layout.ToWorld(Vector2.zero),
                layout.Station);
            Assert.That(distance, Is.EqualTo(layout.DepotToStation).Within(0.01f),
                "депо → станция в переведённых осях равна числу из раскладки");
        }

        [Test]
        public void ReadsEveryPlaceTheMapNeeds()
        {
            var layout = ParseReal();

            Assert.That(layout.Nodes.Count, Is.EqualTo(13), "узлов раскладки");
            Assert.That(layout.Ruins.Count, Is.EqualTo(8), "руин");
            Assert.That(layout.Anchors.Count, Is.EqualTo(7), "якорей");

            var depot = layout.FindNode("depot");
            Assert.That(depot, Is.Not.Null, "в раскладке нет узла depot");
            Assert.That(depot.Position, Is.EqualTo(new Vector2(-1200f, -1200f)));
            Assert.That(depot.Warm, Is.True, "депо обязано быть тёплым узлом");
            Assert.That(depot.Area.HasValue, Is.True,
                "у депо нет площадки — миникарта не знает, что рисовать");

            Assert.That(layout.FindNode("nope"), Is.Null,
                "несуществующий ключ обязан возвращать null");

            // Посёлок — первый узел слоя E: его меши живут в чанке клетки
            // (0,-2), а не отдельным объектом сцены. Позиция и клетка обязаны
            // совпадать с раскладкой, иначе чанк соберётся не туда.
            var settlement = layout.FindNode("settlement");
            Assert.That(settlement, Is.Not.Null, "в раскладке нет узла settlement");
            Assert.That(settlement.Position, Is.EqualTo(new Vector2(0f, -1200f)));
            Assert.That(settlement.LayoutCell, Is.EqualTo(new Vector2Int(0, -2)),
                "посёлок стоит в клетке (0,-2) — его чанк называется WorldCell_0_-2");
            Assert.That(settlement.Warm, Is.True,
                "посёлок тёплый: в нём живут роботы, и миникарта рисует его как площадку");

            var slide = layout.Ruins[0];
            Assert.That(slide.Key, Is.EqualTo("slide"));
            Assert.That(slide.Height, Is.EqualTo(58f).Within(0.01f));
            Assert.That(slide.Climbable, Is.True, "на «Откос» поднимаются ногами");
            Assert.That(slide.Reachable, Is.True);

            var yard = layout.Ruins[layout.Ruins.Count - 1];
            Assert.That(yard.Key, Is.EqualTo("yard"));
            Assert.That(yard.Reachable, Is.False,
                "«Мёртвый причал» — landmark: видно, не дойти");
        }

        /// <summary>
        /// Главное: клетка, которую посчитал C#, обязана совпасть с клеткой,
        /// которую посчитал Python и записал в файл. Шесть точек раскладки лежат
        /// ровно на границе клетки — именно там два языка округляют по-разному,
        /// если правило не записать явно (`floor(v/block + 0.5)`).
        /// </summary>
        [Test]
        public void CellOfAgreesWithPythonForEveryNode()
        {
            var layout = ParseReal();
            var mismatches = new List<string>();
            var withCell = 0;

            for (var i = 0; i < layout.Nodes.Count; i++)
            {
                var node = layout.Nodes[i];
                if (!node.LayoutCell.HasValue)
                    continue;

                withCell++;
                var cell = layout.Grid.CellOf(node.Position);
                if (cell != node.LayoutCell.Value)
                {
                    mismatches.Add(node.Key + ": python " + node.LayoutCell.Value +
                        ", C# " + cell);
                }
            }

            Assert.That(withCell, Is.EqualTo(layout.Nodes.Count),
                "в файле есть узлы без клетки — сверять стало нечем");
            Assert.That(mismatches, Is.Empty,
                "клетки разошлись с раскладкой -> " + string.Join("; ", mismatches));
        }

        [Test]
        public void ReadsLinesAsPolylinesNotAsFlatNoise()
        {
            var layout = ParseReal();

            // 1 линия пути + 4 дороги + 5 троп, все с реальным числом точек.
            Assert.That(layout.Lines.Count, Is.EqualTo(10));
            var rail = layout.Lines[0];
            Assert.That(rail.Kind, Is.EqualTo(WorldLineKind.Rail));
            Assert.That(rail.Points.Length, Is.EqualTo(11));
            Assert.That(rail.Points[0], Is.EqualTo(new Vector2(-1208f, -3300f))
                .Within(0.01f), "путь приходит из-за края карты, а не изнутри");

            var roads = 0;
            var paths = 0;
            for (var i = 0; i < layout.Lines.Count; i++)
            {
                if (layout.Lines[i].Kind == WorldLineKind.Road)
                    roads++;
                if (layout.Lines[i].Kind == WorldLineKind.Path)
                    paths++;
                Assert.That(layout.Lines[i].Points.Length, Is.GreaterThan(1),
                    "линия из одной точки — не дорога");
            }

            Assert.That(roads, Is.EqualTo(4));
            Assert.That(paths, Is.EqualTo(5));
        }

        [Test]
        public void AnomalyDirectionMatchesLayout()
        {
            var layout = ParseReal();

            Assert.That(layout.AnomalyRadiusDegrees, Is.EqualTo(3.2f).Within(0.01f));
            var want = new Vector3(-0.739942f, 0.642788f, 0.198267f);
            Assert.That(Vector3.Angle(layout.AnomalyDirection, want),
                Is.LessThan(0.01f),
                "направление аномалии разошлось с ANOMALY в раскладке");
        }

        /// <summary>
        /// Дальний слой: карточка обязана сохранять не метры якоря, а его угловой
        /// размер, и право рисоваться имеет только тот силуэт, чья макушка вышла
        /// из полосы мглы. Проверка держит обе договорённости на стороне C#: если
        /// Python изменит форму записи или порог, разойдётся здесь, а не в кадре,
        /// где «что-то не то с небом» чинят правкой тумана вслепую.
        /// </summary>
        /// <summary>
        /// Цепочка, которой живёт резидентность: точка сцены -> мир -> клетка.
        ///
        /// Каждый её кусок проверен и сам по себе, но вместе они ломаются молча:
        /// если забыть перевод осей, игрок во дворе депо попадёт в клетку (0,0)
        /// вместо (-2,-2), и включатся не те девять клеток — посреди карты будет
        /// дыра, а в логи нечего читать. Поэтому здесь ровно то, что делает
        /// <c>WorldResidency</c>, и ровно на тех точках, где ошибка видна.
        /// </summary>
        [Test]
        public void ResidencyChainPutsTheDepotYardInItsOwnCell()
        {
            var layout = ParseReal();
            var grid = layout.Grid;

            var depotCell = grid.CellOf(layout.ToWorld(Vector2.zero));
            Assert.That(depotCell, Is.EqualTo(new Vector2Int(-2, -2)),
                "двор депо обязан лежать в клетке (-2,-2), а не в центре мира");

            var cells = new List<Vector2Int>();
            grid.AddResidentCells(depotCell, 1, cells);
            Assert.That(cells.Count, Is.EqualTo(9), "радиус 1 = девять клеток");

            var seen = new HashSet<Vector2Int>(cells);
            for (var i = -3; i <= -1; i++)
            {
                for (var j = -3; j <= -1; j++)
                {
                    Assert.That(seen, Contains.Item(new Vector2Int(i, j)),
                        "клетка " + i + ":" + j + " не попала в окрестность депо");
                }
            }

            // Три тысячи метров к северу от двора — уже совсем другая окрестность,
            // и она обязана сдвинуться вместе с игроком, а не остаться депо.
            var north = grid.CellOf(layout.ToWorld(new Vector2(0f, 3000f)));
            Assert.That(north, Is.EqualTo(new Vector2Int(-2, 3)),
                "смещение по осям сцены не переведено в мир");
            Assert.That(new HashSet<Vector2Int>(cells).Contains(north), Is.False,
                "окрестность депо не должна включать клетку в 3 км к северу");
        }

        [Test]
        public void FarLayerKeepsAngularSizeAndContrastGate()
        {
            var layout = ParseReal();

            Assert.That(layout.FarCards.Count, Is.EqualTo(6),
                "у дальнего слоя должно быть по записи на каждый якорь, кроме пенька");

            var drawn = 0;
            for (var i = 0; i < layout.FarCards.Count; i++)
            {
                var card = layout.FarCards[i];
                WorldAnchor anchor = null;
                for (var a = 0; a < layout.Anchors.Count; a++)
                {
                    if (layout.Anchors[a].Key == card.Key)
                        anchor = layout.Anchors[a];
                }

                Assert.That(anchor, Is.Not.Null,
                    "карточка " + card.Key + " ссылается на якоря, которого нет");

                // Угловой размер: пропорция силуэта равна пропорции тела.
                Assert.That(card.Height / card.Width,
                    Is.EqualTo(anchor.Height / anchor.Width).Within(0.01f),
                    card.Key + ": карточка не сохраняет угловой размер якоря");
                Assert.That(card.Position.magnitude, Is.EqualTo(380f).Within(0.05f),
                    card.Key + ": карточка не на дальности слоя");

                if (card.Drawn)
                {
                    drawn++;
                    Assert.That(card.Contrast, Is.GreaterThanOrEqualTo(0.30f),
                        card.Key + ": рисуется силуэт, чья макушка не вышла из мглы");
                }
                else
                {
                    Assert.That(card.Contrast, Is.LessThan(0.30f),
                        card.Key + ": читаемый якорь остался без карточки");
                }
            }

            Assert.That(drawn, Is.EqualTo(3),
                "из депо читаются три тела: станция, «Косой» и остов «Южный»");
        }

        [Test]
        public void RefusesMissingAndBrokenData()
        {
            WorldLayout layout;

            Assert.That(WorldLayout.TryParse(null, out layout), Is.False,
                "без файла раскладки карта не строится");
            Assert.That(WorldLayout.TryParse(MakeAsset(string.Empty), out layout),
                Is.False, "пустой файл — отказ, а не пустая миникарта");
            Assert.That(WorldLayout.TryParse(MakeAsset("{ это не json"), out layout),
                Is.False, "битый JSON обязан давать отказ, а не исключение");
            Assert.That(WorldLayout.TryParse(MakeAsset("{\"block\": 600.0}"),
                out layout), Is.False, "одного блока мало: узлов нет");

            // Обрезанный хвост линий: линии не хватило точек — карта всё равно
            // открывается, но на неполной линии не рисует.
            var truncated = "{\"block\":600.0,\"cells\":[-5,5],\"depot\":[-1200.0,-1200.0]," +
                "\"nodes\":[{\"k\":\"depot\",\"n\":\"Депо\",\"x\":-1200.0,\"z\":-1200.0," +
                "\"warm\":true,\"cell\":[-2,-2]}]," +
                "\"roads\":[1.0,2.0,3.0],\"roadCounts\":[3]}";
            WorldLayout partial;
            Assert.That(WorldLayout.TryParse(MakeAsset(truncated), out partial),
                Is.True, "неполная линия не должна ронять разбор");
            Assert.That(partial.Lines.Count, Is.EqualTo(0),
                "линия из трёх координат при счётчике 3 не влезла — её быть не должно");
        }

        private static TextAsset MakeAsset(string body)
        {
            return new TextAsset(body);
        }
    }
}
