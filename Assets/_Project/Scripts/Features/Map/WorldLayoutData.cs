using System;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Зеркало <c>world_layout.json</c>: ровно те поля формата, которые читает
    /// рантайм. Названия совпадают с JSON дословно, потому что разбирает их
    /// <see cref="UnityEngine.JsonUtility"/> — он не умеет ни массивы массивов,
    /// ни переименование полей.
    ///
    /// Формат задаёт <c>Tools/Blender/world_layout.py</c> (ADR 0036): линии лежат
    /// плоским списком <c>[x, z, …]</c> с счётчиками точек, а не списком списков.
    /// Лишние поля файла этот класс не трогает — <c>JsonUtility</c> их молча
    /// пропускает, и добавление числа в раскладку не ломает сборку.
    /// </summary>
    [Serializable]
    internal sealed class WorldLayoutData
    {
        public float block;
        public int[] cells;
        public float[] bounds;
        public int[] depotCell;
        public float[] depot;
        public float depotToStation;
        public float fov;

        public FogData fog;
        public SpeedsData speeds;
        public TerrainData terrain;
        public ColdData cold;
        public AnomalyData anomaly;
        public PointData goal;
        public SkyViewData skyView;

        public NodeData[] nodes;
        public RuinData[] ruins;
        public AnchorData[] anchors;
        public FarCardData[] farLayer;

        public float[] rail;
        public int[] railCounts;
        public float[] roads;
        public int[] roadCounts;
        public float[] paths;
        public int[] pathCounts;

        public string[] cellsContent;
    }

    [Serializable]
    internal sealed class FogData
    {
        public float density;
        public float farClip;
        public float t99;
    }

    [Serializable]
    internal sealed class SpeedsData
    {
        public float walk;
        public float sprint;
        public float ride;
    }

    [Serializable]
    internal sealed class TerrainData
    {
        public float step;
        public int trisPerCell;
    }

    [Serializable]
    internal sealed class ColdData
    {
        public float budgetSeconds;
        public float reach;
    }

    [Serializable]
    internal sealed class AnomalyData
    {
        public float bearing;
        public float elev;
        public float radius;
        public float[] dir;
    }

    [Serializable]
    internal sealed class PointData
    {
        public string n;
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    internal sealed class SkyViewData
    {
        public float x;
        public float y;
        public float z;
        public float head;
    }

    [Serializable]
    internal sealed class NodeData
    {
        public string k;
        public string n;
        public float x;
        public float z;
        public bool warm;
        public float[] rect;
        public int[] cell;
    }

    [Serializable]
    internal sealed class RuinData
    {
        public string k;
        public string n;
        public float x;
        public float z;
        public float h;
        public bool climb;
        public string kind;
    }

    [Serializable]
    internal sealed class AnchorData
    {
        public string k;
        public string n;
        public float x;
        public float z;
        public float w;
        public float h;
        public string kind;
    }

    /// <summary>
    /// Карточка дальнего слоя: силуэт якоря на 380 м. `x` и `z` — оси сцены
    /// `Frontier` (её ноль — двор депо), а не мир: слой ставится в сцене.
    /// `draw` — читается ли тело вообще; `w` и `h` уже пересчитаны из углового
    /// размера настоящего якоря, поэтому по ним и проверяется расстановка.
    /// </summary>
    [Serializable]
    internal sealed class FarCardData
    {
        public string k;
        public string n;
        public string kind;
        public float bearing;
        public float real;
        public float w;
        public float h;
        public float deg;
        public float dark;
        public bool draw;
        public float x;
        public float z;
    }
}
