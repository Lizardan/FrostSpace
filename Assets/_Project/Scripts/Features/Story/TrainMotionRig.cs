using System;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Движение мира за окнами вагона: параллакс из трёх слоёв и снег.
    ///
    /// Поезд стоит на месте — двигаются слои. Каждый слой смещается по Z со своей
    /// скоростью и зацикливается на <see cref="_period"/>: рисунок внутри слоя
    /// повторяется ровно через период, поэтому сброс незаметен. Ближний слой
    /// (столбы) идёт быстро, дальний (скалы) — медленно; разница скоростей и даёт
    /// глубину.
    ///
    /// После сдачи квеста вызывается <see cref="Stop"/>: скорость плавно уходит в
    /// ноль за <see cref="_stopSeconds"/>, снег перестаёт сносить назад. Это конец
    /// рейса по GDD, а не пауза.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class TrainMotionRig : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            [Tooltip("Корень слоя: его localPosition.z двигается и зацикливается")]
            public Transform Root;

            [Tooltip("Множитель скорости относительно номинала: 1 — ближний план")]
            [Range(0f, 2f)] public float Speed = 1f;
        }

        [Header("Слои")]
        [Tooltip("Ближний, средний и дальний планы — порядок не важен")]
        [SerializeField] private Layer[] _layers = Array.Empty<Layer>();

        [Tooltip("Длина полосы слоя, м: предметы стоят от −24 до +18 и повторяются через 48")]
        [SerializeField] private float _stripLength = 48f;

        [Tooltip("Скорость ближнего слоя, м/с: 16 — примерно 58 км/ч")]
        [SerializeField] private float _nominalSpeed = 16f;

        [Header("Снег")]
        [Tooltip("Системы снега по бортам; при остановке снос назад гаснет")]
        [SerializeField] private ParticleSystem[] _snow = Array.Empty<ParticleSystem>();

        [Tooltip("Сколько секунд длится остановка после конца рейса")]
        [SerializeField] private float _stopSeconds = 3f;

        private float _speedScale = 1f;
        private bool _stopping;

        /// <summary>Начать торможение: слои и снег плавно замирают.</summary>
        public void Stop()
        {
            _stopping = true;
        }

        /// <summary>Снова ехать на полной скорости (для отладки и повтора сцены).</summary>
        public void Resume()
        {
            _stopping = false;
            _speedScale = 1f;
            ApplySnow(1f);
        }

        private void Update()
        {
            var target = _stopping ? 0f : 1f;
            var step = Time.deltaTime / Mathf.Max(0.01f, _stopSeconds);
            _speedScale = Mathf.MoveTowards(_speedScale, target, step);

            var speed = _nominalSpeed * _speedScale;
            var half = _stripLength * 0.5f;
            for (var i = 0; i < _layers.Length; i++)
            {
                var layer = _layers[i];
                if (layer == null || layer.Root == null)
                {
                    continue;
                }

                // Двигаем каждый предмет отдельно: корень сдвигать нельзя,
                // иначе на сбросе весь слой прыгает через окно.
                var count = layer.Root.childCount;
                for (var c = 0; c < count; c++)
                {
                    var child = layer.Root.GetChild(c);
                    var position = child.localPosition;
                    position.z -= speed * layer.Speed * Time.deltaTime;
                    if (position.z < -half)
                    {
                        position.z += _stripLength;
                    }

                    child.localPosition = position;
                }
            }

            ApplySnow(_speedScale);
        }

        private void ApplySnow(float scale)
        {
            // Снос назад — это скорость поезда в системе отсчёта вагона.
            var drift = -_nominalSpeed * 0.8f * scale;
            for (var i = 0; i < _snow.Length; i++)
            {
                var snow = _snow[i];
                if (snow == null)
                {
                    continue;
                }

                var velocity = snow.velocityOverLifetime;
                velocity.z = drift;
            }
        }

        private void OnValidate()
        {
            _stripLength = Mathf.Max(2f, _stripLength);
            _nominalSpeed = Mathf.Max(0f, _nominalSpeed);
            _stopSeconds = Mathf.Max(0.01f, _stopSeconds);
        }
    }
}
