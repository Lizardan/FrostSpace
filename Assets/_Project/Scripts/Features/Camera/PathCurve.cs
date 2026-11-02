using System;
using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Гладкая кривая через набор точек: центрипетальный Catmull-Rom, проходимый по длине
    /// дуги. Один и тот же primitive ведёт и длинный проезд камеры, и маршруты сценовых
    /// актёров — ломаная с углами даёт мгновенный скачок направления, который камера
    /// наследует как щелчок рыскания, а актёр — как поворот корпуса на месте.
    ///
    /// Кривая принадлежит тому, кто её идёт: общий статический кэш на все маршруты
    /// заставлял бы пересобирать таблицу длины дуги каждый кадр, когда по сценам
    /// одновременно едут камера, тележка и робот.
    /// </summary>
    public sealed class PathCurve
    {
        private const int SamplesPerSegment = 128;
        private const float Epsilon = 1e-4f;

        private readonly Vector3[] _points;
        private readonly float[] _cumulative;

        public PathCurve(Vector3[] points)
        {
            _points = points == null || points.Length == 0
                ? Array.Empty<Vector3>()
                : points;

            if (_points.Length < 2)
            {
                _cumulative = new[] { 0f };
                return;
            }

            var samples = (_points.Length - 1) * SamplesPerSegment + 1;
            _cumulative = new float[samples];

            var previous = _points[0];
            var walked = 0f;
            for (var i = 1; i < samples; i++)
            {
                var position = Sample((float)i / (samples - 1));
                walked += Vector3.Distance(previous, position);
                _cumulative[i] = walked;
                previous = position;
            }

            TotalMeters = walked;
        }

        /// <summary>Кривая через позиции ключей проезда камеры.</summary>
        public PathCurve(CameraKey[] keys)
            : this(Positions(keys))
        {
        }

        /// <summary>Полная длина дуги в метрах.</summary>
        public float TotalMeters { get; }

        /// <summary>
        /// Точка на пройденном расстоянии. Равномерность хода вдоль кривой даёт именно
        /// эта метрика, а не времена ключей.
        /// </summary>
        public Vector3 PointAtMeters(float meters)
        {
            if (_points.Length < 2 || TotalMeters <= Epsilon)
                return _points.Length == 0 ? Vector3.zero : _points[0];

            var parameter = ParameterAt(Mathf.Clamp(meters, 0f, TotalMeters));
            return Sample(parameter);
        }

        /// <summary>Точка по доле длины дуги 0..1.</summary>
        public Vector3 PointAtFraction(float t01)
        {
            return PointAtMeters(Mathf.Clamp01(t01) * TotalMeters);
        }

        private static Vector3[] Positions(CameraKey[] keys)
        {
            if (keys == null)
                return Array.Empty<Vector3>();

            var positions = new Vector3[keys.Length];
            for (var i = 0; i < keys.Length; i++)
                positions[i] = keys[i] != null ? keys[i].Position : Vector3.zero;

            return positions;
        }

        private float ParameterAt(float meters)
        {
            var samples = _cumulative.Length;
            if (meters <= 0f)
                return 0f;
            if (meters >= TotalMeters)
                return 1f;

            var low = 0;
            var high = samples - 1;
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (_cumulative[middle] <= meters)
                    low = middle;
                else
                    high = middle;
            }

            var span = _cumulative[high] - _cumulative[low];
            var k = span <= Epsilon ? 0f : (meters - _cumulative[low]) / span;
            return Mathf.Clamp01(((float)low + k) / (samples - 1));
        }

        /// <summary>
        /// Точка на ломаной Catmull-Rom по параметру 0..1. Края отражаются ближайшим
        /// отрезком: кривая входит в первую точку и выходит из последней без лишнего узла.
        /// </summary>
        private Vector3 Sample(float parameter)
        {
            var segments = _points.Length - 1;
            var scaled = Mathf.Clamp01(parameter) * segments;
            var index = Mathf.Min((int)scaled, segments - 1);
            var u = scaled - index;

            var from = _points[index];
            var to = _points[index + 1];
            var p0 = index > 0 ? _points[index - 1] : from * 2f - to;
            var p3 = index + 1 < segments ? _points[index + 2] : to * 2f - from;

            return CatmullRom(p0, from, to, p3, u);
        }

        /// <summary>
        /// Центрипетальный Catmull-Rom: узлы расставляются по корню из расстояния,
        /// поэтому на крутых поворотах кривая не вылетает за точки. Равномерность хода
        /// вдоль кривой даёт таблица длины дуги, а не эти времена узлов.
        /// </summary>
        private static Vector3 CatmullRom(
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            var d01 = Mathf.Sqrt(Mathf.Max(Vector3.Distance(p0, p1), Epsilon));
            var d12 = Mathf.Sqrt(Mathf.Max(Vector3.Distance(p1, p2), Epsilon));
            var d23 = Mathf.Sqrt(Mathf.Max(Vector3.Distance(p2, p3), Epsilon));

            var t1 = d01;
            var t2 = t1 + d12;
            var t3 = t2 + d23;
            var t = Mathf.Lerp(t1, t2, u);

            var a1 = Vector3.LerpUnclamped(p0, p1, t / t1);
            var a2 = Vector3.LerpUnclamped(p1, p2, (t - t1) / (t2 - t1));
            var a3 = Vector3.LerpUnclamped(p2, p3, (t - t2) / (t3 - t2));

            var b1 = Vector3.LerpUnclamped(a1, a2, t / t2);
            var b2 = Vector3.LerpUnclamped(a2, a3, (t - t1) / (t3 - t1));

            return Vector3.LerpUnclamped(b1, b2, (t - t1) / (t2 - t1));
        }
    }
}
