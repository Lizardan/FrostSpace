using UnityEngine;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Пересчёт мира в точку миникарты. Вынесен из UI специально: критерий
    /// <c>RD-05</c> («маркер не теряется за краем и не врёт о направлении»)
    /// проверяется числами, а не взглядом на картинку, и без этого класс был бы
    /// нечем проверить до Play Mode.
    ///
    /// Карта ориентирована на север: +Z мира смотрит вверх экрана, +X — вправо.
    /// Поворот не применяется, поэтому направление маркера игрока читается
    /// так же, как читает его камера, а не «как-то повёрнуто».
    /// </summary>
    public static class MiniMapProjection
    {
        /// <summary>
        /// Точка мира в осях карты вокруг <paramref name="center"/>:
        /// <paramref name="radiusMeters"/> дальности соответствует
        /// <paramref name="radiusPixels"/> радиуса круга.
        /// </summary>
        public static Vector2 Project(Vector2 world, Vector2 center,
            float radiusMeters, float radiusPixels)
        {
            if (radiusMeters <= 0f)
                return Vector2.zero;

            var k = radiusPixels / radiusMeters;
            return new Vector2((world.x - center.x) * k, (world.y - center.y) * k);
        }

        /// <summary>
        /// То же для дальности и азимута (градусы от +Z на восток) — нужно
        /// маркерам цели, которые считаются от игрока, а не из файла.
        /// </summary>
        public static Vector2 ProjectPolar(float distanceMeters, float bearingDegrees,
            float radiusMeters, float radiusPixels)
        {
            var radians = bearingDegrees * Mathf.Deg2Rad;
            var world = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            return Project(world * distanceMeters, Vector2.zero, radiusMeters, radiusPixels);
        }

        /// <summary>
        /// Точка внутри круга возвращается как есть; вышедшая за край прижимается
        /// к окружности. Возвращает <c>true</c>, если прижать пришлось: по этому
        /// флагу маркер цели меняет вид на «стрелку на ободе».
        /// </summary>
        public static bool ClampToRim(ref Vector2 point, float radiusPixels)
        {
            if (radiusPixels <= 0f)
            {
                point = Vector2.zero;
                return false;
            }

            var length = point.magnitude;
            if (length <= radiusPixels)
                return false;

            point = point * (radiusPixels / length);
            return true;
        }

        /// <summary>
        /// Отсекает отрезок от круга карты. Возвращает <c>false</c>, если рисовать
        /// нечего: отрезок мимо круга или вырожден.
        ///
        /// Без этого линия, у одного конца ушедшая за дальность, дорисовывалась
        /// целиком — и на первом же кадре в Play Mode рельс вылезал за обод в
        /// угол экрана, превращая прибор в царапину.
        /// </summary>
        public static bool TryClipToDisc(Vector2 from, Vector2 to, float radius,
            out Vector2 clippedFrom, out Vector2 clippedTo)
        {
            clippedFrom = from;
            clippedTo = to;

            if (radius <= 0f)
                return false;

            var d = to - from;
            var a = Vector2.Dot(d, d);
            if (a <= 1e-9f)
                return from.sqrMagnitude <= radius * radius;

            var b = 2f * Vector2.Dot(from, d);
            var c = Vector2.Dot(from, from) - radius * radius;
            var discriminant = b * b - 4f * a * c;

            if (discriminant < 0f)
                return false;

            var root = Mathf.Sqrt(discriminant);
            var t0 = Mathf.Clamp01((-b - root) / (2f * a));
            var t1 = Mathf.Clamp01((-b + root) / (2f * a));

            if (t1 < t0)
                return false;

            clippedFrom = from + d * t0;
            clippedTo = from + d * t1;
            return true;
        }

        /// <summary>
        /// Пиксельный размер отрезка между двумя точками карты: минимальная
        /// длина, ниже которой линия не рисуется, иначе на дальних дистанциях
        /// набегает несколько сотен невидимых объектов.
        /// </summary>
        public static float SegmentLength(Vector2 from, Vector2 to)
        {
            return (to - from).magnitude;
        }
    }
}
