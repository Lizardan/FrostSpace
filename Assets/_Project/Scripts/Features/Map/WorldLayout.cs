using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Раскладка мира Stage 3 в рантайме: только то, что нужно карте и HUD.
    ///
    /// Числа приходят из <c>world_layout.json</c>, а их единственный источник —
    /// <c>Tools/Blender/world_layout.py</c> (ADR 0036). Здесь нет ни одной
    /// константы, которую можно было бы «подправить на месте»: расхождение карты
    /// и реальности обходится тем, что миникарта врёт о направлении.
    ///
    /// Осознанное ограничение: <c>JsonUtility</c> не читает словари и массивы
    /// массивов, поэтому линии приходят плоским списком с числом точек на линию,
    /// а площадка узла — массивом из четырёх границ.
    /// </summary>
    public sealed class WorldLayout
    {
        /// <summary>Сторона клетки (и блока раскладки), метры.</summary>
        public float Block { get; private set; }

        /// <summary>Нижний индекс клетки по каждой оси, включая саму клетку.</summary>
        public int CellMin { get; private set; }

        /// <summary>Верхний индекс клетки по каждой оси, включая саму клетку.</summary>
        public int CellMax { get; private set; }

        /// <summary>Депо в осях сцены: (x, z).</summary>
        public Vector2 Depot { get; private set; }

        /// <summary>Станция «Север-7» — начало отсчёта раскладки, (0, 0).</summary>
        public Vector2 Station { get; private set; }

        /// <summary>Расстояние депо → станция по прямой, метры.</summary>
        public float DepotToStation { get; private set; }

        /// <summary>Плотность тумана Exp² — та же, что в сцене (STYLE.md §«Туман дали»).</summary>
        public float FogDensity { get; private set; }

        /// <summary>Дальность отсечения камеры, метры.</summary>
        public float FarClip { get; private set; }

        /// <summary>Запас остывания вне тепла, секунды (бит RD-09).</summary>
        public float ColdBudgetSeconds { get; private set; }

        /// <summary>Радиус достижимых руин от тёплого каркаса, метры.</summary>
        public float ColdReach { get; private set; }

        /// <summary>Цель подъёма — верх «Откоса» (x, z) и его отметка.</summary>
        public Vector2 Goal { get; private set; }

        /// <summary>Точка обзора «открытого неба» и азимут взгляда, градусы.</summary>
        public Vector2 SkyView { get; private set; }

        public float SkyViewBearing { get; private set; }

        /// <summary>Направление аномалии — единичный вектор в осях сцены.</summary>
        public Vector3 AnomalyDirection { get; private set; }

        /// <summary>Видимый радиус диска аномалии, градусы.</summary>
        public float AnomalyRadiusDegrees { get; private set; }

        public IReadOnlyList<WorldNode> Nodes { get; private set; }
        public IReadOnlyList<WorldRuin> Ruins { get; private set; }
        public IReadOnlyList<WorldAnchor> Anchors { get; private set; }

        /// <summary>
        /// Карточки дальнего слоя — все якоря, кроме пенька, с флагом «читается».
        /// Нужны не рантайму (слой стоит в сцене), а проверке, что сцена и
        /// раскладка не разошлись.
        /// </summary>
        public IReadOnlyList<WorldFarCard> FarCards { get; private set; }

        public IReadOnlyList<WorldLine> Lines { get; private set; }

        /// <summary>Сетка клеток этой раскладки.</summary>
        public WorldGrid Grid { get; private set; }

        /// <summary>
        /// Разбирает <c>TextAsset</c> раскладки. Ложь — если файла нет, он пуст или
        /// в нём не читается то, без чего карта невозможна; молча работать на
        /// неполных данных миникарта не должна.
        /// </summary>
        public static bool TryParse(TextAsset data, out WorldLayout layout)
        {
            layout = null;
            if (data == null || string.IsNullOrWhiteSpace(data.text))
                return false;

            WorldLayoutData raw;
            try
            {
                raw = JsonUtility.FromJson<WorldLayoutData>(data.text);
            }
            catch (System.Exception)
            {
                return false;
            }

            if (raw == null || raw.block <= 0f || raw.cells == null ||
                raw.cells.Length < 2 || raw.depot == null || raw.depot.Length < 2 ||
                raw.nodes == null || raw.nodes.Length == 0)
            {
                return false;
            }

            var parsed = new WorldLayout
            {
                Block = raw.block,
                CellMin = raw.cells[0],
                CellMax = raw.cells[1],
                Depot = new Vector2(raw.depot[0], raw.depot[1]),
                Station = Vector2.zero,
                DepotToStation = raw.depotToStation,
                FogDensity = raw.fog != null ? raw.fog.density : 0f,
                FarClip = raw.fog != null ? raw.fog.farClip : 0f,
                ColdBudgetSeconds = raw.cold != null ? raw.cold.budgetSeconds : 0f,
                ColdReach = raw.cold != null ? raw.cold.reach : 0f,
            };

            if (raw.goal != null)
                parsed.Goal = new Vector2(raw.goal.x, raw.goal.z);
            if (raw.skyView != null)
            {
                parsed.SkyView = new Vector2(raw.skyView.x, raw.skyView.z);
                parsed.SkyViewBearing = raw.skyView.head;
            }
            if (raw.anomaly != null && raw.anomaly.dir != null &&
                raw.anomaly.dir.Length >= 3)
            {
                parsed.AnomalyDirection = new Vector3(raw.anomaly.dir[0],
                    raw.anomaly.dir[1], raw.anomaly.dir[2]).normalized;
                parsed.AnomalyRadiusDegrees = raw.anomaly.radius;
            }

            parsed.Nodes = ReadNodes(raw.nodes);
            parsed.Ruins = ReadRuins(raw.ruins);
            parsed.Anchors = ReadAnchors(raw.anchors);
            parsed.FarCards = ReadFarCards(raw.farLayer);
            parsed.Lines = ReadLines(raw);
            if (parsed.Nodes.Count == 0)
                return false;

            parsed.Grid = new WorldGrid(parsed.Block, parsed.CellMin, parsed.CellMax);
            layout = parsed;
            return true;
        }

        /// <summary>Узел раскладки по ключу; null — если такого ключа нет.</summary>
        public WorldNode FindNode(string key)
        {
            for (var i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Key == key)
                    return Nodes[i];
            }

            return null;
        }

        /// <summary>
        /// Точка сцены в осях раскладки. Сцена `Frontier` набрана в осях депо:
        /// её ноль — двор депо, а в мире депо стоит в <see cref="Depot"/>, то
        /// есть (-800, -800). Без этого перевода миникарта центрируется там,
        /// где игрок думает, что стоит, — проверка кадром 2026-11-02 поймала
        /// ровно это: игрок во дворе, а карта показывает окрестности станции.
        /// Когда террейн переедет в мировы оси, переводом станет ноль — и только
        /// это одно место.
        /// </summary>
        public Vector2 ToWorld(Vector2 scenePoint)
        {
            return scenePoint + Depot;
        }

        private static List<WorldNode> ReadNodes(NodeData[] source)
        {
            var list = new List<WorldNode>(source != null ? source.Length : 0);
            if (source == null)
                return list;

            for (var i = 0; i < source.Length; i++)
            {
                var node = source[i];
                if (node == null || string.IsNullOrEmpty(node.k))
                    continue;

                list.Add(new WorldNode
                {
                    Key = node.k,
                    Title = node.n,
                    Position = new Vector2(node.x, node.z),
                    Warm = node.warm,
                    Area = node.rect != null && node.rect.Length >= 4
                        ? new Rect(node.rect[0], node.rect[2],
                            node.rect[1] - node.rect[0], node.rect[3] - node.rect[2])
                        : (Rect?)null,
                    LayoutCell = node.cell != null && node.cell.Length >= 2
                        ? new Vector2Int(node.cell[0], node.cell[1])
                        : (Vector2Int?)null,
                });
            }

            return list;
        }

        private static List<WorldRuin> ReadRuins(RuinData[] source)
        {
            var list = new List<WorldRuin>(source != null ? source.Length : 0);
            if (source == null)
                return list;

            for (var i = 0; i < source.Length; i++)
            {
                var ruin = source[i];
                if (ruin == null || string.IsNullOrEmpty(ruin.k))
                    continue;

                list.Add(new WorldRuin
                {
                    Key = ruin.k,
                    Title = ruin.n,
                    Position = new Vector2(ruin.x, ruin.z),
                    Height = ruin.h,
                    Climbable = ruin.climb,
                    Reachable = ruin.kind == "reach",
                });
            }

            return list;
        }

        private static List<WorldAnchor> ReadAnchors(AnchorData[] source)
        {
            var list = new List<WorldAnchor>(source != null ? source.Length : 0);
            if (source == null)
                return list;

            for (var i = 0; i < source.Length; i++)
            {
                var anchor = source[i];
                if (anchor == null || string.IsNullOrEmpty(anchor.k))
                    continue;

                list.Add(new WorldAnchor
                {
                    Key = anchor.k,
                    Title = anchor.n,
                    Position = new Vector2(anchor.x, anchor.z),
                    Width = anchor.w,
                    Height = anchor.h,
                    Kind = anchor.kind,
                });
            }

            return list;
        }

        private static List<WorldFarCard> ReadFarCards(FarCardData[] source)
        {
            var list = new List<WorldFarCard>(source != null ? source.Length : 0);
            if (source == null)
                return list;

            for (var i = 0; i < source.Length; i++)
            {
                var card = source[i];
                if (card == null || string.IsNullOrEmpty(card.k))
                    continue;

                list.Add(new WorldFarCard
                {
                    Key = card.k,
                    Title = card.n,
                    Position = new Vector2(card.x, card.z),
                    Width = card.w,
                    Height = card.h,
                    Bearing = card.bearing,
                    RealDistance = card.real,
                    TopDegrees = card.deg,
                    Contrast = card.dark,
                    Drawn = card.draw,
                });
            }

            return list;
        }

        private static List<WorldLine> ReadLines(WorldLayoutData raw)
        {
            var list = new List<WorldLine>();
            AddLines(list, raw.rail, raw.railCounts, WorldLineKind.Rail);
            AddLines(list, raw.roads, raw.roadCounts, WorldLineKind.Road);
            AddLines(list, raw.paths, raw.pathCounts, WorldLineKind.Path);
            return list;
        }

        /// <summary>
        /// Нарезает плоский список <c>[x, z, …]</c> на ломаные по счётчику точек.
        /// Неполный хвост или несовпавший счётчик отбрасывает линию, а не рвёт
        /// сборку: карта обязана открыться и на урезанных данных.
        /// </summary>
        private static void AddLines(List<WorldLine> into, float[] points,
            int[] counts, WorldLineKind kind)
        {
            if (points == null || counts == null)
                return;

            var offset = 0;
            for (var i = 0; i < counts.Length; i++)
            {
                var count = counts[i];
                if (count < 2 || offset + count * 2 > points.Length)
                    continue;

                var line = new Vector2[count];
                for (var p = 0; p < count; p++)
                {
                    line[p] = new Vector2(points[offset + p * 2],
                        points[offset + p * 2 + 1]);
                }

                offset += count * 2;
                into.Add(new WorldLine { Kind = kind, Points = line });
            }
        }
    }

    /// <summary>Узел раскладки: двор, станция, вышка, порт — точка на карте.</summary>
    public sealed class WorldNode
    {
        public string Key { get; internal set; }
        public string Title { get; internal set; }
        public Vector2 Position { get; internal set; }

        /// <summary>Внутри тёплого каркаса (RD-09): сюда можно вернуться живым.</summary>
        public bool Warm { get; internal set; }

        /// <summary>Площадка в осях сцены; null — если узел точка, а не площадь.</summary>
        public Rect? Area { get; internal set; }

        /// <summary>
        /// Клетка, которую посчитал Python и записал в файл. Нужна только
        /// сверкой: <see cref="WorldGrid.CellOf"/> считает её заново на C#, и
        /// совпадение двух независимых вычислений — единственное, что держит
        /// карту и раскладку вместе.
        /// </summary>
        public Vector2Int? LayoutCell { get; internal set; }
    }

    /// <summary>Руина Пояса: то, к чему можно подойти (15–60 м).</summary>
    public sealed class WorldRuin
    {
        public string Key { get; internal set; }
        public string Title { get; internal set; }
        public Vector2 Position { get; internal set; }
        public float Height { get; internal set; }

        /// <summary>На руину можно подняться ногами («Откос»).</summary>
        public bool Climbable { get; internal set; }

        /// <summary>Достижима за запас остывания; иначе только видна.</summary>
        public bool Reachable { get; internal set; }
    }

    /// <summary>Якорь Пояса: тело наследия, от обжитой станции до силуэта за краем.</summary>
    public sealed class WorldAnchor
    {
        public string Key { get; internal set; }
        public string Title { get; internal set; }
        public Vector2 Position { get; internal set; }
        public float Width { get; internal set; }
        public float Height { get; internal set; }

        /// <summary><c>main</c>, <c>lean</c>, <c>stump</c> или <c>far</c>.</summary>
        public string Kind { get; internal set; }
    }

    /// <summary>
    /// Карточка дальнего слоя: силуэт якоря на 380 м от наблюдателя. Позиция —
    /// в осях сцены <c>Frontier</c> (её ноль — двор депо), размеры уже пересчитаны
    /// из углового размера настоящего тела. <see cref="Drawn"/> — читается ли
    /// силуэт вообще: у якоря, чья макушка не выходит из полосы мглы, карточки
    /// нет, и это не пропуск, а решение раскладки (ADR 0036 п.6).
    /// </summary>
    public sealed class WorldFarCard
    {
        public string Key { get; internal set; }
        public string Title { get; internal set; }

        /// <summary>Ось сцены: куда ставить карточку.</summary>
        public Vector2 Position { get; internal set; }

        /// <summary>Габарит силуэта, метры: ширина и высота.</summary>
        public float Width { get; internal set; }
        public float Height { get; internal set; }

        /// <summary>Азимут от двора депо, градусы от +Z на восток.</summary>
        public float Bearing { get; internal set; }

        /// <summary>Дальность настоящего якоря, метры.</summary>
        public float RealDistance { get; internal set; }

        /// <summary>Угол макушки над горизонтом, градусы.</summary>
        public float TopDegrees { get; internal set; }

        /// <summary>Насколько небо над макушкой уже потемнело, доля.</summary>
        public float Contrast { get; internal set; }

        public bool Drawn { get; internal set; }
    }

    public enum WorldLineKind
    {
        Rail = 0,
        Road = 1,
        Path = 2,
    }

    /// <summary>Ломаная: путь, дорога или тропка тем же порядком точек, что в раскладке.</summary>
    public sealed class WorldLine
    {
        public WorldLineKind Kind { get; internal set; }
        public Vector2[] Points { get; internal set; }
    }
}
