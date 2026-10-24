using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Настройка посадки конкретного байка: насколько корпус наклоняется к рулю,
    /// какую долю наклона забирает шея, как расставлены колени и куда ставится таз.
    ///
    /// Поза наездника одна на все байки (<see cref="RiderPose"/>), а различия —
    /// в данных префаба: на спортивном ховербайке корпус ложится на бак, а таз
    /// уходит назад по седлу; на прогулочном мотоцикле посадка прямее. Компонент
    /// живёт на корне байка рядом с сиденьем, поэтому веток «если ховербайк»
    /// в коде позы нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RiderCockpit : MonoBehaviour
    {
        [Tooltip("Суммарный наклон корпуса к рулю, градусы")]
        [SerializeField] private float _leanAngle = 30f;

        [Tooltip("Доля наклона, которую забирает шея — чтобы робот смотрел вперёд")]
        [SerializeField, Range(0f, 1f)] private float _neckShare = 0.45f;

        [Tooltip("Боковой вынос колена: больше — колени шире, меньше/отрицательно — уже")]
        [SerializeField] private float _kneeOut = 0.25f;

        [Tooltip("Точка таза. Пусто — считается от сиденья по настройкам RiderPose")]
        [SerializeField] private Transform _seatPoint;

        /// <summary>Суммарный наклон корпуса к рулю, градусы.</summary>
        public float LeanAngle => _leanAngle;

        /// <summary>Доля наклона, которую забирает шея.</summary>
        public float NeckShare => _neckShare;

        /// <summary>Боковой вынос колена в плоскости байка.</summary>
        public float KneeOut => _kneeOut;

        /// <summary>Якорь таза наездника; может быть пустым.</summary>
        public Transform SeatPoint => _seatPoint;
    }
}
