using UnityEngine;
using UnityEngine.InputSystem;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Ховербайк: посадка по <c>Player/Interact</c> (E), вождение WASD и вид сверху-сзади над байком.
    /// На сиденье игрок садится верхом и держится за руль (<see cref="RiderPose"/>).
    ///
    /// Едет на <see cref="CharacterController"/>, а не на Rigidbody: рельеф уровня —
    /// фасетный лоуполе с крутыми склонами, и кинематический контроллер даёт
    /// предсказуемое следование по снегу без застреваний в стыках меша.
    ///
    /// Корпус висит над землёй: контроллер стоит на поверхности, а визуальная модель
    /// поднята на <c>_hoverClearance</c>, под ней светится подушка
    /// (<see cref="HoverbikeGlow"/>). Так парение читается как штатный режим, при этом
    /// ничего не висит в воздухе без крепления — все детали прикручены к корпусу.
    ///
    /// Управление как в виде сверху: W/A/S/D задают направление в экранных осях
    /// (вверх/влево/вниз/вправо), корпус байка сам доворачивается к этому направлению,
    /// а камера сверху стоит на месте и не крутится (<see cref="PlayerCameraRig.RideViewYaw"/>).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class Hoverbike : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Ассет ввода; если пусто — берётся InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;
        [Tooltip("Риг камеры: включает вид сверху, пока игрок на байке")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Точка посадки; пусто — дочерний объект Seat")]
        [SerializeField] private Transform _seat;
        [Tooltip("Точка схода; пусто — дочерний объект ExitPoint")]
        [SerializeField] private Transform _exit;

        [Header("Езда")]
        [Tooltip("Максимальная скорость, м/с")]
        [SerializeField] private float _maxSpeed = 12f;
        [Tooltip("Разгон, м/с²")]
        [SerializeField] private float _acceleration = 11f;
        [Tooltip("Торможение при отпущенном вводе и развороте, м/с²")]
        [SerializeField] private float _brake = 16f;
        [Tooltip("Скорость доворота корпуса к направлению движения, градусов/с")]
        [SerializeField] private float _turnSpeed = 420f;
        [Tooltip("Скорость поворота вектора движения, градусов/с: меньше — больше занос, больше — резче повороты")]
        [SerializeField] private float _velocityTurnSpeed = 160f;

        [Header("Посадка")]
        [Tooltip("С какого расстояния E сажает игрока, м")]
        [SerializeField] private float _mountRadius = 3.5f;

        private CharacterController _controller;
        private RobotLocomotion _locomotion;
        private RiderPose _riderPose;
        private Transform _rider;
        private InputAction _moveAction;
        private InputAction _interactAction;
        private Vector3 _velocity;
        private float _speed;
        private float _steer;
        private float _nextSpeedLog;
        private bool _mounted;

        // На сцене два байка, и оба в радиусе посадки от спавна: без этой защёлки
        // одно нажатие E сажало игрока на оба сразу, и они спорили за родителя.
        private static Hoverbike s_mountedBike;

        /// <summary>Игрок на байке.</summary>
        public bool IsMounted => _mounted;

        /// <summary>На любом ховербайке сцены сейчас сидит игрок.</summary>
        public static bool SeatTaken => s_mountedBike != null;

        /// <summary>
        /// Domain reload выключен: без сброса защёлка прошлой сессии не давала
        /// сесть на байк во втором запуске Play Mode.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_mountedBike = null;
        }

        /// <summary>Текущая скорость, м/с (модуль фактической скорости).</summary>
        public float Speed => _speed;

        /// <summary>Скорость, приведённая к максимуму: 0 — стоит, 1 — полный ход.</summary>
        public float NormalizedSpeed => Mathf.Clamp01(_speed / Mathf.Max(0.01f, _maxSpeed));

        /// <summary>
        /// Крен −1..1: насколько корпус ещё не совпал с направлением движения.
        /// По нему наклоняется подушка в сторону доворота.
        /// </summary>
        public float Steer => _steer;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
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
            // Действия не выключаются в OnDisable: у сцены два байка, и один
            // выключенный объект иначе отобрал бы ввод у второго.
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

            if (!_mounted)
            {
                _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, _brake * Time.deltaTime);
                _speed = _velocity.magnitude;
                _steer = 0f;

                // Прижатие к рельефу и на стоянке: байк садится на снег после
                // расстановки в сцене и не остаётся висеть там, где его поставили.
                _controller.Move(Vector3.down * 2f * Time.deltaTime);
                return;
            }

            var input = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;

            // Базис — фиксированный курс вида сверху: W всегда ведёт вверх по экрану,
            // D — вправо. Корпус доворачивается к направлению сам, поэтому A/D больше
            // не «рулят» байком, а задают сторону движения, как в старом виде сверху.
            var basis = Quaternion.Euler(0f, _rig != null ? _rig.RideViewYaw : 0f, 0f);
            var wish = Vector3.ClampMagnitude(basis * new Vector3(input.x, 0f, input.y), 1f);

            // Скорость и направление разведены: разгон меняет длину вектора, а курс
            // поворачивается со своей скоростью — именно она задаёт величину заноса.
            var currentSpeed = _velocity.magnitude;
            var targetSpeed = _maxSpeed * wish.magnitude;
            var rate = targetSpeed > currentSpeed ? _acceleration : _brake;
            _speed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

            var currentDirection = currentSpeed > 0.001f
                ? _velocity / currentSpeed
                : (wish.sqrMagnitude > 0.0001f ? wish.normalized : transform.forward);
            var newDirection = wish.sqrMagnitude > 0.0001f
                ? Vector3.RotateTowards(
                    currentDirection, wish.normalized,
                    _velocityTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f)
                : currentDirection;
            _velocity = newDirection * _speed;

            // Корпус всегда смотрит туда, куда байк едет: доворот корпуса идёт заметно
            // быстрее поворота самого вектора движения, поэтому боком он не едет.
            if (_velocity.sqrMagnitude > 0.01f)
            {
                var facing = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, facing, _turnSpeed * Time.deltaTime);

                _steer = Mathf.Clamp(
                    Vector3.SignedAngle(transform.forward, _velocity.normalized, Vector3.up) / 45f,
                    -1f, 1f);
            }
            else
            {
                _steer = 0f;
            }

            // Небольшое прижатие к земле: контроллер держится за рельеф и не улетает
            // с гребня, а визуально байк всё равно висит на подушке.
            var motion = _velocity + Vector3.down * 2f;
            _controller.Move(motion * Time.deltaTime);

            if (Time.time >= _nextSpeedLog && _speed > 0.5f)
            {
                _nextSpeedLog = Time.time + 1f;
                Debug.Log($"[Hoverbike] скорость {_speed:0.0} м/с", this);
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

            // Игрок уже сидит на другом байке: второй не перехватывает управление.
            if (s_mountedBike != null && s_mountedBike != this)
                return;
            if (Motorcycle.SeatTaken)
                return;

            if (Vector3.Distance(_rider.position, transform.position) > _mountRadius)
                return;

            Mount();
        }

        /// <summary>
        /// Поиск дочернего объекта по имени на любой глубине: посадочное место лежит
        /// внутри качающейся модели, поэтому Transform.Find (только прямые дети) его
        /// не находит.
        /// </summary>
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

        /// <summary>Сажает игрока: ходьба выключается, камера уходит в вид сверху-сзади.</summary>
        public void Mount()
        {
            if (_rider == null || _mounted)
                return;

            if (s_mountedBike != null && s_mountedBike != this)
                return;
            if (Motorcycle.SeatTaken)
                return;

            _mounted = true;
            s_mountedBike = this;
            _speed = 0f;
            _velocity = Vector3.zero;
            _locomotion?.SetRideOverride(true);
            _rig?.SetRideTarget(transform);

            _rider.SetParent(_seat != null ? _seat : transform, false);
            _rider.localPosition = Vector3.zero;
            _rider.localRotation = Quaternion.identity;

            // Поза — после пересадки на сиденье: она ставит таз в посадочную точку.
            _riderPose?.Sit(_seat != null ? _seat : transform);

            Debug.Log("[Hoverbike] посадка", this);
        }

        /// <summary>Высаживает игрока сбоку от байка и возвращает третий вид.</summary>
        public void Dismount()
        {
            if (_rider == null || !_mounted)
                return;

            _mounted = false;
            if (s_mountedBike == this)
                s_mountedBike = null;
            _speed = 0f;
            _velocity = Vector3.zero;

            var spot = _exit != null
                ? _exit.position
                : transform.position + transform.right * 1.6f + Vector3.up * 0.1f;
            var facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            _riderPose?.Stand();
            _rider.SetParent(null, true);
            _rider.SetPositionAndRotation(spot, facing);

            _locomotion?.SetRideOverride(false);
            _rig?.ClearRideTarget();

            Debug.Log("[Hoverbike] сход", this);
        }
    }
}
