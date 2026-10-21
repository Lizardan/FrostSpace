using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Оживляет стоящий корпус ховербайка: покачивание на подушке, крен в повороте,
    /// вздёрнутый нос на разгоне и пульс свечения под днищем.
    ///
    /// Качается только визуальная модель (<c>Visual</c>), а посадочное место — её
    /// дочерний объект, поэтому игрок покачивается вместе с байком. Позиция модели
    /// запоминается в Awake как база: зазор над землёй задан в префабе, а не в скрипте,
    /// так что парение видно и в редакторе без запуска игры.
    ///
    /// Сила подушки задаётся через <see cref="MaterialPropertyBlock"/>: общий материал
    /// шейдера не мутируется, поэтому два байка в сцене пульсируют независимо.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class HoverbikeGlow : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");

        [Header("Ссылки")]
        [Tooltip("Байк — источник скорости и поворота; пусто — компонент на этом же объекте")]
        [SerializeField] private Hoverbike _bike;
        [Tooltip("Качающаяся модель; пусто — дочерний объект Visual")]
        [SerializeField] private Transform _visual;
        [Tooltip("Плоскость подушки; пусто — дочерний объект Cushion")]
        [SerializeField] private Renderer _cushion;
        [Tooltip("Лампа под днищем, необязательно")]
        [SerializeField] private Light _engineLight;

        [Header("Покачивание")]
        [Tooltip("Амплитуда вертикального покачивания, м")]
        [SerializeField] private float _bobAmplitude = 0.035f;
        [Tooltip("Частота покачивания, Гц")]
        [SerializeField] private float _bobFrequency = 0.45f;
        [Tooltip("Крен в повороте, градусы")]
        [SerializeField] private float _rollAngle = 9f;
        [Tooltip("Вздёрнутый нос на скорости, градусы")]
        [SerializeField] private float _pitchAngle = 5f;
        [Tooltip("Сглаживание крена и тангажа, 1/с")]
        [SerializeField] private float _tiltSmoothing = 6f;

        [Header("Подушка")]
        [Tooltip("Яркость подушки на стоянке")]
        [SerializeField] private float _idleStrength = 0.5f;
        [Tooltip("Яркость подушки на полном ходу")]
        [SerializeField] private float _rideStrength = 1.15f;
        [Tooltip("Частота пульсации, Гц")]
        [SerializeField] private float _pulseFrequency = 1.3f;
        [Tooltip("Яркость лампы под днищем, люмены")]
        [SerializeField] private float _lightIdle = 1.4f;
        [SerializeField] private float _lightRide = 3.6f;

        private MaterialPropertyBlock _block;
        private Vector3 _basePosition;
        private float _roll;
        private float _pitch;

        private void Awake()
        {
            if (_bike == null)
                _bike = GetComponent<Hoverbike>();
            if (_visual == null)
                _visual = transform.Find("Visual");
            if (_cushion == null)
            {
                var cushionTransform = transform.Find("Cushion");
                if (cushionTransform != null)
                    _cushion = cushionTransform.GetComponent<Renderer>();
            }

            if (_visual != null)
                _basePosition = _visual.localPosition;

            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            var time = Time.time;
            var ride = _bike != null ? _bike.NormalizedSpeed : 0f;
            var steer = _bike != null ? _bike.Steer : 0f;

            if (_visual != null)
            {
                // Покачивание висит на синусе: подушка «дышит» даже на стоянке,
                // иначе байк читается как приклеенный к снегу.
                var bob = Mathf.Sin(time * _bobFrequency * Mathf.PI * 2f) * _bobAmplitude;
                _visual.localPosition = _basePosition + Vector3.up * bob;

                var rollTarget = -steer * _rollAngle * Mathf.Lerp(0.3f, 1f, ride);
                var pitchTarget = -ride * _pitchAngle;
                var blend = 1f - Mathf.Exp(-_tiltSmoothing * Time.deltaTime);
                _roll = Mathf.Lerp(_roll, rollTarget, blend);
                _pitch = Mathf.Lerp(_pitch, pitchTarget, blend);
                _visual.localRotation = Quaternion.Euler(_pitch, 0f, _roll);
            }

            // Пульс: кольца бегут по подушке, сила светится от оборотов.
            var pulse = 1f + Mathf.Sin(time * _pulseFrequency * Mathf.PI * 2f) * 0.08f;
            var strength = Mathf.Lerp(_idleStrength, _rideStrength, ride) * pulse;

            if (_cushion != null)
            {
                _block ??= new MaterialPropertyBlock();
                _cushion.GetPropertyBlock(_block);
                _block.SetFloat(StrengthId, strength);
                _cushion.SetPropertyBlock(_block);
            }

            if (_engineLight != null)
                _engineLight.intensity = Mathf.Lerp(_lightIdle, _lightRide, ride) * pulse;
        }
    }
}
