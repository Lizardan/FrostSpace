using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>Режим камеры игрока.</summary>
    public enum CameraMode
    {
        /// <summary>Сверху-сзади, как раньше: обзор уровня без управления камерой мышью.</summary>
        ThirdPerson = 0,

        /// <summary>Из-за плеча, мышь ведёт обзор, в перекрестии круговой вырез.</summary>
        Aim = 1,

        /// <summary>Из головы робота: включается внутри зданий.</summary>
        FirstPerson = 2
    }

    /// <summary>Поза камеры: позиция и поворот. Значимый тип — блендинг не создаёт мусора.</summary>
    public readonly struct CameraPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public CameraPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Чистая математика перехода между позами камеры.
    /// Вынесена из рига, чтобы проверяться EditMode-тестом без сцены.
    /// </summary>
    public static class ViewBlend
    {
        /// <summary>Доля перехода 0..1 по прошедшему времени.</summary>
        public static float Progress(float elapsed, float duration)
        {
            if (duration <= 1e-4f)
                return 1f;

            return Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>Плавное начало и конец: камера не дёргает на старте и не бьётся в конце.</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Промежуточная поза между двумя крайними.</summary>
        public static CameraPose Lerp(CameraPose from, CameraPose to, float t)
        {
            var s = Smooth(t);
            return new CameraPose(
                Vector3.Lerp(from.Position, to.Position, s),
                Quaternion.Slerp(from.Rotation, to.Rotation, s));
        }
    }
}
