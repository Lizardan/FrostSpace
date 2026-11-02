using System.Collections.Generic;
using FrostSpace.Features.Map;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Сетка клеток: границы, серединный случай и residency.
    ///
    /// Середина клетки важнее, чем кажется: шесть точек раскладки лежат ровно на
    /// границе, и пока правило не записано как `floor(v/block + 0.5)`, Python и C#
    /// считали их разными клетками. Ниже эти точки стоят прямыми числами.
    /// </summary>
    public sealed class WorldGridTests
    {
        private static WorldGrid Map()
        {
            return new WorldGrid(600f, -5, 5);
        }

        [Test]
        public void CellOfPutsTiesIntoTheHigherCell()
        {
            var grid = Map();

            Assert.That(grid.CellOf(new Vector2(0f, 0f)), Is.EqualTo(new Vector2Int(0, 0)),
                "станция — начало отсчёта");
            Assert.That(grid.CellOf(new Vector2(-1200f, -1200f)),
                Is.EqualTo(new Vector2Int(-2, -2)), "депо клетки (-2,-2)");

            // Ровно на границе: -900 = граница клеток -2 и -1, +300 = граница 0 и 1.
            Assert.That(grid.CellOf(new Vector2(-900f, 250f)),
                Is.EqualTo(new Vector2Int(-1, 0)), "«Откос» на границе");
            Assert.That(grid.CellOf(new Vector2(300f, 420f)),
                Is.EqualTo(new Vector2Int(1, 1)), "«Пролёт» на границе");
            Assert.That(grid.CellOf(new Vector2(-300f, 800f)),
                Is.EqualTo(new Vector2Int(0, 1)), "«Плита ангара» на границе");
        }

        [Test]
        public void CellsCoverTheWholeMapAndNoMore()
        {
            var grid = Map();

            Assert.That(grid.CellCount, Is.EqualTo(121));
            Assert.That(grid.Contains(new Vector2Int(-5, -5)), Is.True);
            Assert.That(grid.Contains(new Vector2Int(5, 5)), Is.True);
            Assert.That(grid.Contains(new Vector2Int(6, 0)), Is.False,
                "за краем карты считать нечего");

            // Полуклетки за краем индекса всё ещё принадлежат крайней клетке.
            Assert.That(grid.CellOf(new Vector2(3299f, 0f)), Is.EqualTo(new Vector2Int(5, 0)));
            Assert.That(grid.CellOf(new Vector2(3600f, 0f)), Is.EqualTo(new Vector2Int(6, 0)),
                "вне карты — тоже клетка, но её `Contains` не пропустит");
        }

        [Test]
        public void CenterOfIsTheGridNodeItself()
        {
            var grid = Map();

            Assert.That(grid.CenterOf(new Vector2Int(0, 0)), Is.EqualTo(Vector2.zero));
            Assert.That(grid.CenterOf(new Vector2Int(-2, -2)),
                Is.EqualTo(new Vector2(-1200f, -1200f)));
            Assert.That(grid.CenterOf(new Vector2Int(3, -4)),
                Is.EqualTo(new Vector2(1800f, -2400f)));
        }

        [Test]
        public void ResidencyIsSquareClampedByMapEdge()
        {
            var grid = Map();
            var cells = new List<Vector2Int>();

            Assert.That(grid.ResidentCount(new Vector2Int(0, 0), 0), Is.EqualTo(1),
                "радиус 0 — только своя клетка");
            Assert.That(grid.ResidentCount(new Vector2Int(0, 0), 1), Is.EqualTo(9),
                "радиус 1 — 9 клеток, то есть 7200 трис террейна в памяти");
            Assert.That(grid.ResidentCount(new Vector2Int(-5, -5), 1), Is.EqualTo(4),
                "угол карты: квадрат обрезается, а не висит за краем");
            Assert.That(grid.ResidentCount(new Vector2Int(0, 0), 5), Is.EqualTo(121),
                "радиус, равный половине карты, накрывает весь мир");

            cells.Clear();
            grid.AddResidentCells(new Vector2Int(-5, -5), 1, cells);
            Assert.That(cells.Count, Is.EqualTo(grid.ResidentCount(new Vector2Int(-5, -5), 1)));
            Assert.That(cells.Contains(new Vector2Int(-6, -6)), Is.False,
                "обход не выходит за границу");

            cells.Clear();
            grid.AddResidentCells(new Vector2Int(0, 0), 1, cells);
            Assert.That(cells, Has.Member(new Vector2Int(-1, 1)),
                "сосед по диагонали обязан быть в residency");
        }

        [Test]
        public void DegenerateGridStillWorks()
        {
            var grid = new WorldGrid(0f, 3, 1);

            Assert.That(grid.Block, Is.EqualTo(1f), "нулевой блок заменён, деления ноль нет");
            Assert.That(grid.CellMax, Is.EqualTo(3), "перевёрнутый диапазон починен");
            Assert.That(grid.CellCount, Is.EqualTo(1));
        }
    }
}
