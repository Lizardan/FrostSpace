using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Оживляет мотоцикл: вращает колёса по пройденному пути, наклоняет корпус
    /// в повороте и на склоне, качает подвеску, зажигает фару и стоп-сигнал,
    /// сыплет снег из-под заднего колеса.
    ///
    /// Вращение колёс считается от скорости, а не от времени: колесо проходит
    /// ровно столько, сколько проехал байк, и не «буксует» при разгоне. У колёс
    /// в FBX собственный pivot в оси, а ось X после импорта совпадает с осью X
    /// модели, поэтому достаточно довернуть их локально.
    ///
    /// Качается только визуальная модель, а не корень: капсула контроллера
    /// остаётся вертикальной, иначе байк на склоне застревал бы в рельефе.
    /// Наклон рельефа берётся из нормали, которую нашёл сам контроллер, — здесь
    /// нет второго луча и второй правды о том, где земля.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class MotorcycleRideVisual : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Байк — источник скорости и руля; пусто — компонент на этом же объекте")]
        [SerializeField] private Motorcycle _motorcycle;
        [Tooltip("Качающаяся модель; пусто — дочерний объект Visual")]
        [SerializeField] private Transform _visual;
        [Tooltip("Переднее колесо; пусто — ищется MotorcycleWheelF")]
        [SerializeField] private Transform _wheelFront;
        [Tooltip("Заднее колесо; пусто — ищется MotorcycleWheelR")]
        [SerializeField] private Transform _wheelRear;

        [Header("Колёса")]
        [Tooltip("Радиус колеса, м: по нему скорость переводится в угол поворота")]
        [SerializeField] private float _wheelRadius = 0.38f;
        [Tooltip("Ось вращения колеса в локальных осях модели")]
        [SerializeField] private Vector3 _wheelAxis = Vector3.right;
        [Tooltip("Знак вращения: зависит от того, как bake-трансформ FBX развернул узел")]
        [SerializeField] private float _wheelSpinSign = 1f;

        [Header("Крен и подвеска")]
        [Tooltip("Максимальный крен в повороте, градусы")]
        [SerializeField] private float _leanAngle = 22f;
        [Tooltip("Наклон корпуса от разгона и торможения, градусы на 1 м/с²")]
        [SerializeField] private float _pitchPerAcceleration = 0.35f;
        [Tooltip("Предел наклона от разгона и торможения, градусы")]
        [SerializeField] private float _pitchLimit = 5f;
        [Tooltip("Амплитуда качания подвески, м")]
        [SerializeField] private float _bobAmplitude = 0.012f;
        [Tooltip("Частота качания, Гц")]
        [SerializeField] private float _bobFrequency = 2.1f;
        [Tooltip("Сглаживание наклона, 1/с")]
        [SerializeField] private float _tiltSmoothing = 7f;

        [Header("Свет")]
        [Tooltip("Фара; пусто — дочерний объект Headlight")]
        [SerializeField] private Light _headlight;
        [Tooltip("Стоп-сигнал; пусто — дочерний объект BrakeLight")]
        [SerializeField] private Light _brakeLight;
        [SerializeField] private float _headlightIdle = 2.2f;
        [SerializeField] private float _headlightRide = 3.4f;
        [SerializeField] private float _brakeOff = 0.25f;
        [SerializeField] private float _brakeOn = 2.6f;

        [Header("Эффекты")]
        [Tooltip("Снежная пыль из-под заднего колеса; можно несколько систем")]
        [SerializeField] private ParticleSystem[] _snowSpray;
        [Tooltip("Выхлоп; пусто — дочерний объект Exhaust")]
        [SerializeField] private ParticleSystem _exhaust;
        [SerializeField] private float _snowMaxRate = 70f;

        private Vector3 _basePosition;
        private float _roll;
        private float _pitch;
        private float _lastSpeed;

        private void Awake()
        {
            if (_motorcycle == null)
                _motorcycle = GetComponent<Motorcycle>();
            if (_visual == null)
                _visual = transform.Find("Visual");
            if (_wheelFront == null)
                _wheelFront = FindDeep(transform, "MotorcycleWheelF");
            if (_wheelRear == null)
                _wheelRear = FindDeep(transform, "MotorcycleWheelR");
            if (_headlight == null)
                _headlight = FindLight("Headlight");
            if (_brakeLight == null)
                _brakeLight = FindLight("BrakeLight");
            if (_exhaust == null)
                _exhaust = FindParticles("Exhaust");

            if (_visual != null)
                _basePosition = _visual.localPosition;
        }

        private void Update()
        {
            if (_motorcycle == null || _visual == null)
                return;

            var deltaTime = Mathf.Max(Time.deltaTime, 1e-4f);
            var speed = _motorcycle.Speed;
            var ride = _motorcycle.NormalizedSpeed;

            SpinWheels(speed, deltaTime);
            Tilt(speed, ride, deltaTime);
            UpdateLights(ride);
            UpdateParticles(ride);

            _lastSpeed = speed;
        }

        /// <summary>Колесо проходит ровно путь байка: угол = путь / радиус.</summary>
        private void SpinWheels(float speed, float deltaTime)
        {
            var degrees = speed * deltaTime / Mathf.Max(0.05f, _wheelRadius) *
                          Mathf.Rad2Deg * _wheelSpinSign;
            if (Mathf.Abs(degrees) < 1e-4f)
                return;

            if (_wheelFront != null)
                _wheelFront.Rotate(_wheelAxis, degrees, Space.Self);
            if (_wheelRear != null)
                _wheelRear.Rotate(_wheelAxis, degrees, Space.Self);
        }

        private void Tilt(float speed, float ride, float deltaTime)
        {
            var steer = _motorcycle.Steer;
            var acceleration = (speed - _lastSpeed) / deltaTime;

            // Крен в повороте и наклон от разгона складываются в одну позу модели.
            var rollTarget = -steer * _leanAngle * Mathf.Lerp(0.3f, 1f, ride);
            var pitchTarget = Mathf.Clamp(
                -acceleration * _pitchPerAcceleration, -_pitchLimit, _pitchLimit);

            var blend = 1f - Mathf.Exp(-_tiltSmoothing * deltaTime);
            _roll = Mathf.Lerp(_roll, rollTarget, blend);
            _pitch = Mathf.Lerp(_pitch, pitchTarget, blend);

            // Нормаль рельефа уже посчитана контроллером: второй луч здесь
            // разошёлся бы с ней на стыках фасетного меша.
            var localNormal = transform.InverseTransformDirection(_motorcycle.GroundNormal);
            var groundTilt = Quaternion.FromToRotation(Vector3.up, localNormal);
            _visual.localRotation = groundTilt * Quaternion.Euler(_pitch, 0f, _roll);

            // Покачивание висит на синусе: на стоянке это холостой ход, на ходу —
            // подвеска, которая не даёт байку выглядеть приклеенным к снегу.
            var bob = Mathf.Sin(Time.time * _bobFrequency * Mathf.PI * 2f) *
                      _bobAmplitude * Mathf.Lerp(0.6f, 1.6f, ride);
            _visual.localPosition = _basePosition + Vector3.up * bob;
        }

        private void UpdateLights(float ride)
        {
            if (_headlight != null)
                _headlight.intensity = Mathf.Lerp(_headlightIdle, _headlightRide, ride);

            if (_brakeLight != null)
                _brakeLight.intensity = _motorcycle.IsBraking ? _brakeOn : _brakeOff;
        }

        private void UpdateParticles(float ride)
        {
            var spraying = _motorcycle.IsGrounded && ride > 0.02f;
            SetRate(_snowSpray, spraying ? Mathf.Lerp(6f, _snowMaxRate, ride) : 0f);
            if (_exhaust != null)
                SetRate(_exhaust, Mathf.Lerp(4f, 16f, ride));
        }

        private static void SetRate(ParticleSystem[] systems, float rate)
        {
            if (systems == null)
                return;

            for (var i = 0; i < systems.Length; i++)
                SetRate(systems[i], rate);
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
                return;

            var emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
        }

        private Light FindLight(string name)
        {
            var child = FindDeep(transform, name);
            return child != null ? child.GetComponent<Light>() : null;
        }

        private ParticleSystem FindParticles(string name)
        {
            var child = FindDeep(transform, name);
            return child != null ? child.GetComponent<ParticleSystem>() : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
