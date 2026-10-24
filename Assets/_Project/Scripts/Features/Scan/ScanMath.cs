using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Scan
{
    /// <summary>
    /// Чистая математика сканера: выбор ближайшей цели, точки сетки прицела, вес по
    /// дистанции и сглаживание. Вынесена из <see cref="ObjectScanner"/> и HUD, чтобы
    /// проверяться EditMode-тестами без сцены и без кадров.
    /// </summary>
    public static class ScanMath
    {
        /// <summary>Минимальная дистанция: на нуле деление и нормали дают мусор.</summary>
        public const float MinDistance = 0.01f;

        /// <summary>
        /// Индекс ближайшей точки в радиусе или <c>-1</c>, если в радиус не попала ни одна.
        /// Сравнение идёт по квадрату дистанции — корень на каждой цели не нужен.
        /// </summary>
        public static int NearestIndex(Vector3 origin, IReadOnlyList<Vector3> positions, float radius)
        {
            if (positions == null || positions.Count == 0)
                return -1;

            var limit = Mathf.Max(0f, radius);
            var best = limit * limit;
            var index = -1;

            for (var i = 0; i < positions.Count; i++)
            {
                var delta = positions[i] - origin;
                var square = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                if (square > best)
                    continue;

                best = square;
                index = i;
            }

            return index;
        }

        /// <summary>
        /// Вес цели по дистанции: 1 внутри <paramref name="fadeStart"/> и плавно падает
        /// до 0 на <paramref name="radius"/>. Нужен, чтобы цель не гасла ступенькой на
        /// границе радиуса сонара.
        /// </summary>
        public static float DistanceWeight(float distance, float radius, float fadeStart)
        {
            var limit = Mathf.Max(MinDistance, radius);
            if (distance <= fadeStart)
                return 1f;
            if (distance >= limit)
                return 0f;

            var span = Mathf.Max(MinDistance, limit - fadeStart);
            return 1f - (distance - fadeStart) / span;
        }

        /// <summary>
        /// Точка сетки прицела в координатах viewport. Крайние точки лежат на границе
        /// квадрата, центральная — в перекрестии. <paramref name="half"/> — половина
        /// стороны квадрата в долях экрана.
        /// </summary>
        public static Vector2 ViewportSample(int x, int y, int samples, float half)
        {
            var count = Mathf.Clamp(samples, 1, 9);
            if (count == 1)
                return new Vector2(0.5f, 0.5f);

            var span = Mathf.Clamp(half, 0.01f, 0.5f);
            var u = 0.5f + span * (Mathf.Clamp(x, 0, count - 1) / (count - 1f) * 2f - 1f);
            var v = 0.5f + span * (Mathf.Clamp(y, 0, count - 1) / (count - 1f) * 2f - 1f);
            return new Vector2(u, v);
        }

        /// <summary>
        /// Экспоненциальное сглаживание к цели: за <paramref name="seconds"/> остаток пути
        /// уменьшается в e раз. Кадронезависимо, поэтому WebGL и редактор ведут себя
        /// одинаково. Нулевое время — мгновенный переход.
        /// </summary>
        public static float Approach(float current, float target, float seconds, float deltaTime)
        {
            if (seconds <= 0f)
                return target;

            var t = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds);
            return Mathf.Lerp(current, target, t);
        }

        /// <summary>
        /// Прогресс полосы скана: доходит до 1 за <paramref name="seconds"/> и не
        /// перескакивает границу.
        /// </summary>
        public static float Advance(float progress, float seconds, float deltaTime)
        {
            if (seconds <= 0f)
                return 1f;

            return Mathf.Clamp01(progress + Mathf.Max(0f, deltaTime) / seconds);
        }
    }
}
