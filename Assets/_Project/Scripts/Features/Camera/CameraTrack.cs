using System;
using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Точка опоры длинного проезда камеры: позиция и поле зрения. Времени у ключа нет
    /// намеренно: порядок задаёт форму пути, а не скорость. Скорость считает
    /// <see cref="PathCurve"/> по длине дуги — иначе соседние участки ехали бы в разы
    /// быстрее друг друга.
    ///
    /// Куда смотреть, здесь не задаётся: направление кадра даёт режиссёр, который ведёт
    /// взгляд за субъектом. Сплайн по точкам взгляда на пересечении ключей даёт мгновенный
    /// пинок рыскания — именно то «то быстрее, то медленнее», что видно даже при ровном
    /// ходе камеры.
    /// </summary>
    [Serializable]
    public sealed class CameraKey
    {
        public Vector3 Position = Vector3.zero;
        [Range(20f, 90f)] public float Fov = 50f;
    }

    /// <summary>
    /// Чистая математика одного непрерывного проезда: глобальная кривая времени,
    /// монотонный зум и наведение на точку. Проход по длине дуги делает
    /// <see cref="PathCurve"/>, здесь — только то, что зависит от времени кадра.
    /// </summary>
    public static class CameraTrack
    {
        /// <summary>
        /// Глобальная кривая времени: тот же smootherstep, что у занавеса
        /// <see cref="Core.SceneController"/>, поэтому разгон кадра и смена сцены идут
        /// по одной интонации. Скорость на обоих концах равна нулю.
        /// </summary>
        public static float Ease(float t01)
        {
            var t = Mathf.Clamp01(t01);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        /// Поле зрения на доле кадра: монотонно от первого ключа к последнему. Никакой
        /// интерполяции по сегментам — она давала «дыхание» зума на каждом стыке.
        /// </summary>
        public static float FovAlong(CameraKey[] keys, float t01)
        {
            if (keys == null || keys.Length == 0)
                return 50f;

            var from = keys[0].Fov;
            var to = keys[keys.Length - 1].Fov;
            return Mathf.Clamp(Mathf.Lerp(from, to, Ease(t01)), 1f, 179f);
        }

        /// <summary>Поворот на точку. Вертикаль остаётся вертикалью.</summary>
        public static Quaternion Aim(Vector3 position, Vector3 target)
        {
            var direction = target - position;
            if (direction.sqrMagnitude < 1e-6f)
                return Quaternion.identity;

            var normal = direction.normalized;
            var up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.999f
                ? Vector3.forward
                : Vector3.up;
            return Quaternion.LookRotation(normal, up);
        }
    }
}
