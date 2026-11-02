using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Сетка клеток мира: те же границы и тот же закон середины, что в
    /// <c>world_layout.py</c> (ADR 0036).
    ///
    /// Клетка центрирована на узлах сетки, то есть центр клетки (i, j) — это
    /// точка (i·block, j·block), а граница — на ±block/2. Ровно на границе
    /// точка достаётся клетке с бо́льшим индексом: правило записано как
    /// <c>floor(v/block + 0.5)</c> специально, потому что округление половины
    /// в Python идёт к чётному, а в C# — от нуля, и шесть точек раскладки
    /// лежат ровно на границе. Разойдись языки на них — миникарта молчала бы
    /// не о той клетке.
    /// </summary>
    public readonly struct WorldGrid
    {
        public WorldGrid(float block, int cellMin, int cellMax)
        {
            Block = block > 0f ? block : 1f;
            CellMin = cellMin;
            CellMax = cellMax >= cellMin ? cellMax : cellMin;
        }

        /// <summary>Сторона клетки, метры.</summary>
        public float Block { get; }

        /// <summary>Нижний индекс клетки по обеим осям, включительно.</summary>
        public int CellMin { get; }

        /// <summary>Верхний индекс клетки по обеим осям, включительно.</summary>
        public int CellMax { get; }

        /// <summary>Число клеток во всём мире: 121 при индексах −5…+5.</summary>
        public int CellCount
        {
            get
            {
                var side = CellMax - CellMin + 1;
                return side * side;
            }
        }

        /// <summary>Клетка точки в осях сцены.</summary>
        public Vector2Int CellOf(Vector2 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / Block + 0.5f),
                Mathf.FloorToInt(position.y / Block + 0.5f));
        }

        /// <summary>Центр клетки — узел сетки, вокруг которого набран террейн.</summary>
        public Vector2 CenterOf(Vector2Int cell)
        {
            return new Vector2(cell.x * Block, cell.y * Block);
        }

        /// <summary>Внутри ли клетка карты: за краем мира считать нечего.</summary>
        public bool Contains(Vector2Int cell)
        {
            return cell.x >= CellMin && cell.x <= CellMax &&
                   cell.y >= CellMin && cell.y <= CellMax;
        }

        /// <summary>
        /// Сколько клеток живёт вокруг точки при радиусе <paramref name="radius"/>:
        /// квадрат (2r+1)², обрезанный по краю карты. Угол карты при радиусе 1
        /// даёт 4, а не 9.
        /// </summary>
        public int ResidentCount(Vector2Int center, int radius)
        {
            var span = ClampSpan(center.x, radius);
            var spanZ = ClampSpan(center.y, radius);
            return span * spanZ;
        }

        /// <summary>Клетки residency в осях (x, z); порядок обхода — строками.</summary>
        public void AddResidentCells(Vector2Int center, int radius,
            List<Vector2Int> into)
        {
            var x0 = Mathf.Max(center.x - radius, CellMin);
            var x1 = Mathf.Min(center.x + radius, CellMax);
            var z0 = Mathf.Max(center.y - radius, CellMin);
            var z1 = Mathf.Min(center.y + radius, CellMax);

            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    into.Add(new Vector2Int(x, z));
                }
            }
        }

        private int ClampSpan(int cell, int radius)
        {
            var lo = Mathf.Max(cell - radius, CellMin);
            var hi = Mathf.Min(cell + radius, CellMax);
            return Mathf.Max(0, hi - lo + 1);
        }
    }
}
