using FrostSpace.Features.Locomotion;
using FrostSpace.Features.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Снежный мотоцикл: посадка по <c>Player/Interact</c> (E), движение WASD в экранных
    /// осях, камера сверху над корпусом.
    ///
    /// Управление как у <see cref="Hoverbike"/> и как в виде сверху: W/A/S/D задают
    /// направление на экране (вверх/влево/вниз/вправо), а корпус сам доворачивается к
    /// выбранному направлению. Разделены только скорости: вектор движения поворачивается
    /// медленнее корпуса, поэтому в повороте читается занос, но байк не едет боком.
    /// Курс вида фиксирован (<see cref="PlayerCameraRig.SetRideTarget"/>), так что верх
    /// экрана всегда «север» и клавиши не меняют смысл при развороте.
    ///
    /// Едет на <see cref="CharacterController"/> по той же причине, что и ховербайк:
    /// рельеф — фасетный лоуполе с крутыми склонами, и кинематический контроллер
    /// проходит стыки меша без застреваний. Корпус прижимается вниз каждым кадром,
    /// поэтому байк не отрывается от гребня на спуске.
    ///
    /// Наклон по склону, крен в повороте и вращение колёс — не здесь, а в
    /// <see cref="MotorcycleRideVisual"/>: контроллер держит корень вертикально,
    /// чтобы капсула не заваливалась, а модель наклоняется отдельно. Посадочное место
    /// лежит внутри качающейся модели, поэтому наездник качается и кренится вместе с ней.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class Motorcycle : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Ассет ввода; если пусто — берётся InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;
        [Tooltip("Риг камеры: включает вид сверху, пока игрок за рулём")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Точка посадки; пусто — дочерний объект Seat")]
        [SerializeField] private Transform _seat;
        [Tooltip("Точка схода; пусто — дочерний объект ExitPoint")]
        [SerializeField] private Transform _exit;

        [Header("Езда")]
        [Tooltip("Максимальная скорость, м/с")]
        [SerializeField] private float _maxSpeed = 17f;
        [Tooltip("Разгон, м/с²")]
        [SerializeField] private float _acceleration = 9f;
        [Tooltip("Торможение и замедление без ввода, м/с²")]
        [SerializeField] private float _brake = 14f;
        [Tooltip("Скорость доворота корпуса к направлению движения, градусов/с")]
        [SerializeField] private float _turnSpeed = 540f;
        [Tooltip("Скорость поворота самого вектора движения, градусов/с: меньше — больше занос")]
        [SerializeField] private float _velocityTurnSpeed = 170f;
        [Tooltip("Скорость сглаживания руля для крена, 1/с")]
        [SerializeField] private float _steerResponse = 7f;
        [Tooltip("Насколько сильно байк прижимается к земле, м/с")]
        [SerializeField] private float _groundStick = 4f;

        [Header("Посадка")]
        [Tooltip("С какого расстояния E сажает игрока, м")]
        [SerializeField] private float _mountRadius = 3.5f;

        [Header("Земля")]
        [Tooltip("С какого расстояния искать под байком рельеф для наклона модели, м")]
        [SerializeField] private float _groundProbe = 1.8f;
        [Tooltip("Что считается землёй (слой Player исключить)")]
        [SerializeField] private LayerMask _groundMask = ~0;

        private CharacterController _controller;
        private RobotLocomotion _locomotion;
        private RiderPose _riderPose;
        private Transform _rider;
        private InputAction _moveAction;
        private InputAction _interactAction;
        private float _speed;
        private float _steer;
        private Vector3 _direction = Vector3.forward;
        private bool _mounted;

        // Игрок один: второй транспорт не должен перехватывать его управление.
        // Защёлка статическая, потому что посадка выполняется до того, как
        // соседний байк вообще узнает о событии.
        private static Motorcycle s_mounted;

        /// <summary>Игрок за рулём.</summary>
        public bool IsMounted => _mounted;

        /// <summary>За рулём сейчас кто-то есть — на любой мотоцикл сцены.</summary>
        public static bool SeatTaken => s_mounted != null;

        /// <summary>
        /// Domain reload выключен: защёлка прошлой сессии иначе осталась бы
        /// занятой во втором запуске Play Mode.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_mounted = null;
        }

        /// <summary>Скорость со знаком: минус — задний ход.</summary>
        public float Speed => _speed;

        /// <summary>Скорость, приведённая к максимуму, 0..1.</summary>
        public float NormalizedSpeed => Mathf.Clamp01(Mathf.Abs(_speed) / Mathf.Max(0.01f, _maxSpeed));

        /// <summary>Сглаженный руль −1..1: по нему наклоняется корпус.</summary>
        public float Steer => _steer;

        /// <summary>Газ отпущен или нажат тормоз при движении — для стоп-сигнала.</summary>
        public bool IsBraking { get; private set; }

        /// <summary>Нормаль рельефа под байком; в воздухе остаётся прошлой.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>Колёса стоят на земле.</summary>
        public bool IsGrounded => _controller != null && _controller.isGrounded;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _direction = transform.forward;
            if (_seat == null)
                _seat = FindDeep(transform, "Seat");
            if (_exit == null)
                _exit = FindDeep(transform, "ExitPoint");

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _moveAction = source.FindAction("Player/Move");
                _interactAction = source.FindAction("Player/Interact");
            }
        }

        private void Start()
        {
            ResolveRider();
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _interactAction?.Enable();
            if (_interactAction != null)
                _interactAction.performed += OnInteract;
        }

        private void OnDisable()
        {
            if (_interactAction != null)
                _interactAction.performed -= OnInteract;
        }

        private void Update()
        {
            if (_rider == null)
                ResolveRider();

            ProbeGround();

            if (!_mounted)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, _brake * Time.deltaTime);
                _steer = Mathf.MoveTowards(_steer, 0f, _steerResponse * Time.deltaTime);
                IsBraking = false;

                // Прижатие к рельефу и на стоянке: байк садится на снег после
                // расстановки в сцене, а не остаётся висеть там, где его поставили.
                _controller.Move(Vector3.down * 2f * Time.deltaTime);
                return;
            }

            Drive();
        }

        private void Drive()
        {
            var input = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;

            // Базис — фиксированный курс вида сверху: W всегда ведёт вверх по экрану,
            // D — вправо. Корпус доворачивается к выбранному направлению сам, поэтому
            // A/D больше не «рулят» байком, а задают сторону движения.
            var basis = Quaternion.Euler(0f, _rig != null ? _rig.RideViewYaw : 0f, 0f);
            var wish = Vector3.ClampMagnitude(basis * new Vector3(input.x, 0f, input.y), 1f);

            var target = _maxSpeed * wish.magnitude;
            var rate = target > _speed ? _acceleration : _brake;
            _speed = Mathf.MoveTowards(_speed, target, rate * Time.deltaTime);
            IsBraking = _speed > 0.5f && target < _speed - 0.01f;

            // Скорость и направление разведены: разгон меняет длину, поворот — только
            // курс вектора. Он поворачивается медленнее корпуса, поэтому в повороте
            // видно дугу, а не мгновенный разворот машины вокруг своей оси.
            if (wish.sqrMagnitude > 0.0001f)
            {
                _direction = Vector3.RotateTowards(
                    _direction, wish.normalized,
                    _velocityTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }

            if (_speed > 0.05f && _direction.sqrMagnitude > 0.001f)
            {
                var facing = Quaternion.LookRotation(_direction.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, facing, _turnSpeed * Time.deltaTime);
            }

            // Руль для крена — расхождение между носом и фактическим движением.
            var slip = Vector3.SignedAngle(transform.forward, _direction, Vector3.up);
            _steer = Mathf.MoveTowards(
                _steer, Mathf.Clamp(slip / 45f, -1f, 1f), _steerResponse * Time.deltaTime);

            var motion = _direction * _speed + Vector3.down * _groundStick;
            _controller.Move(motion * Time.deltaTime);
        }

        /// <summary>
        /// Нормаль рельефа под байком: по ней модель наклоняется на склоне.
        /// Луч начинается выше корпуса, чтобы не зацепиться за собственные коллайдеры.
        /// </summary>
        private void ProbeGround()
        {
            var origin = transform.position + Vector3.up * 0.6f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, _groundProbe,
                    _groundMask, QueryTriggerInteraction.Ignore))
            {
                GroundNormal = hit.normal;
            }
            else
            {
                GroundNormal = Vector3.up;
            }
        }

        private void ResolveRider()
        {
            if (_locomotion == null)
                _locomotion = FindAnyObjectByType<RobotLocomotion>();
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_locomotion != null)
            {
                _rider = _locomotion.transform;
                if (_riderPose == null)
                    _riderPose = _rider.GetComponent<RiderPose>();
            }
        }

        private void OnInteract(InputAction.CallbackContext context)
        {
            if (_rider == null)
                return;

            if (_mounted)
            {
                Dismount();
                return;
            }

            // Игрок уже за рулём другого транспорта: второй его не перехватывает.
            if (s_mounted != null || Hoverbike.SeatTaken)
                return;

            if (Vector3.Distance(_rider.position, transform.position) > _mountRadius)
                return;

            Mount();
        }

        /// <summary>Поиск дочернего объекта по имени на любой глубине.</summary>
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

        /// <summary>Сажает игрока: ходьба выключается, камера уходит в вид сверху.</summary>
        public void Mount()
        {
            if (_rider == null || _mounted || s_mounted != null || Hoverbike.SeatTaken)
                return;

            _mounted = true;
            s_mounted = this;
            _speed = 0f;
            _steer = 0f;
            _direction = transform.forward;
            _locomotion?.SetRideOverride(true);
            _rig?.SetRideTarget(transform);

            _rider.SetParent(_seat != null ? _seat : transform, false);
            _rider.localPosition = Vector3.zero;
            _rider.localRotation = Quaternion.identity;

            _riderPose?.Sit(_seat != null ? _seat : transform);

            Debug.Log("[Motorcycle] посадка", this);
        }

        /// <summary>Высаживает игрока сбоку и возвращает третий вид.</summary>
        public void Dismount()
        {
            if (_rider == null || !_mounted)
                return;

            _mounted = false;
            if (s_mounted == this)
                s_mounted = null;
            _speed = 0f;
            _steer = 0f;
            _direction = transform.forward;
            IsBraking = false;

            var spot = _exit != null
                ? _exit.position
                : transform.position + transform.right * 1.6f + Vector3.up * 0.1f;
            var facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            _riderPose?.Stand();
            _rider.SetParent(null, true);
            _rider.SetPositionAndRotation(spot, facing);

            _locomotion?.SetRideOverride(false);
            _rig?.ClearRideTarget();

            Debug.Log("[Motorcycle] сход", this);
        }
    }
}
