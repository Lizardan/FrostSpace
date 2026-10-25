using FrostSpace.Features.Locomotion;
using FrostSpace.Features.View;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Ездовой двуногий ходун: игрок по <c>Player/Interact</c> (E) садится в открытую
    /// кабину в груди, ходун идёт на клипах пака, камера облетает его мышью.
    ///
    /// Управление отличается от байков: WASD задаёт направление **относительно
    /// камеры**, а корпус доворачивается к нему. У байка курс вида зафиксирован и
    /// верх экрана всегда «север» — на ходуне так нельзя, потому что игрок крутит
    /// обзор вокруг корпуса и клавиши должны идти за ним. Поэтому базис движения
    /// берётся по <see cref="PlayerCameraRig.ViewYaw"/>, а не по фиксированному курсу.
    ///
    /// Скорость две: шаг и бег (`Player/Sprint`). Обе ниже байков — ходун тяжёлый,
    /// зато проходит там, где байк цепляется за рельеф. Клип играет со скоростью,
    /// пропорциональной фактической: без этого на шаге стопа проскальзывала
    /// по снегу. Отношение ограничено сверху и снизу, чтобы ходун не превращался
    /// в мультяшную мельницу, — поэтому на беге походка читается тяжёлой, а не быстрой.
    ///
    /// Корпус ходуна качается на шаге, поэтому посадочная точка не статична:
    /// <see cref="MechSeat"/> каждый кадр ставит её в точку кости `Chest`.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class Mech : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Ассет ввода; если пусто — берётся InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;
        [Tooltip("Риг камеры: включает облёт, пока игрок за рулём")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Профиль облёта; пусто — ищется на этом объекте")]
        [SerializeField] private RideCameraProfile _cameraProfile;
        [Tooltip("Точка посадки; пусто — дочерний объект Seat")]
        [SerializeField] private Transform _seat;
        [Tooltip("Точка схода; пусто — дочерний объект ExitPoint")]
        [SerializeField] private Transform _exit;
        [Tooltip("Аниматор модели; пусто — ищется в детях")]
        [SerializeField] private Animator _animator;

        [Header("Ходьба")]
        [Tooltip("Скорость шага, м/с")]
        [SerializeField] private float _walkSpeed = 2.2f;
        [Tooltip("Скорость бега по Player/Sprint, м/с")]
        [SerializeField] private float _runSpeed = 5f;
        [Tooltip("Разгон, м/с²")]
        [SerializeField] private float _acceleration = 5f;
        [Tooltip("Торможение и замедление без ввода, м/с²")]
        [SerializeField] private float _brake = 9f;
        [Tooltip("Скорость доворота корпуса к направлению движения, градусов/с")]
        [SerializeField] private float _turnSpeed = 160f;
        [Tooltip("Насколько сильно ходун прижимается к земле, м/с")]
        [SerializeField] private float _groundStick = 3f;

        [Header("Анимация")]
        [Tooltip("Скорость земли в клипе шага, м/с: по ней масштабируется проигрывание")]
        [SerializeField] private float _walkClipSpeed = 2.2f;
        [Tooltip("Скорость земли в клипе бега, м/с")]
        [SerializeField] private float _runClipSpeed = 4.842f;
        [Tooltip("Нижний предел скорости клипа: медленнее — походка перестаёт читаться")]
        [SerializeField] private float _minClipRate = 0.55f;
        [Tooltip("Верхний предел скорости клипа")]
        [SerializeField] private float _maxClipRate = 1.35f;

        [Header("Посадка")]
        [Tooltip("С какого расстояния E сажает игрока, м")]
        [SerializeField] private float _mountRadius = 4.5f;

        private CharacterController _controller;
        private RobotLocomotion _locomotion;
        private RiderPose _riderPose;
        private Transform _rider;
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _interactAction;
        private float _speed;
        private bool _mounted;

        // Последний отлогированный режим клипа и коэффициент: SetAnimatorSpeed
        // считается каждый кадр, а лог нужен один на переход и на выход
        // скорости на полку, иначе консоль забивается и сверять нечего.
        private const float LogRateStep = 0.1f;
        private int _loggedClipMode = -1;
        private float _loggedRate = float.NaN;
        private float _lastLoggedSpeed;

        // Игрок один: второй транспорт не должен перехватывать его управление.
        // Защёлка статическая, потому что посадка выполняется до того, как
        // соседний ходун вообще узнает о событии.
        private static Mech s_mounted;

        /// <summary>Игрок за рулём.</summary>
        public bool IsMounted => _mounted;

        /// <summary>За рулём сейчас кто-то есть — на любой ходун сцены.</summary>
        public static bool SeatTaken => s_mounted != null;

        /// <summary>Текущая скорость, м/с.</summary>
        public float Speed => _speed;

        /// <summary>
        /// Domain reload выключен: защёлка прошлой сессии иначе осталась бы
        /// занятой во втором запуске Play Mode.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_mounted = null;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_cameraProfile == null)
                _cameraProfile = GetComponent<RideCameraProfile>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);
            if (_seat == null)
                _seat = FindDeep(transform, "Seat");
            if (_exit == null)
                _exit = FindDeep(transform, "ExitPoint");

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _moveAction = source.FindAction("Player/Move");
                _sprintAction = source.FindAction("Player/Sprint");
                _interactAction = source.FindAction("Player/Interact");
            }

            SetAnimatorSpeed(0f);
        }

        private void Start()
        {
            ResolveRider();
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _sprintAction?.Enable();
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
                _speed = Mathf.MoveTowards(_speed, 0f, _brake * Time.deltaTime);
                SetAnimatorSpeed(_speed);
                // Прижатие к рельефу и на стоянке: ходун садится на снег после
                // расстановки в сцене, а не остаётся висеть там, где его поставили.
                _controller.Move(Vector3.down * 2f * Time.deltaTime);
                return;
            }

            Walk();
        }

        private void Walk()
        {
            var input = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;

            // Базис — курс камеры: W ведёт туда, куда смотрит игрок. Курс берётся
            // у рига, а не у корпуса, поэтому после разворота мышью клавиши
            // сохраняют смысл.
            var basis = Quaternion.Euler(0f, _rig != null ? _rig.ViewYaw : transform.eulerAngles.y, 0f);
            var wish = Vector3.ClampMagnitude(basis * new Vector3(input.x, 0f, input.y), 1f);

            var sprint = _sprintAction != null && _sprintAction.IsPressed();
            var top = sprint ? _runSpeed : _walkSpeed;
            var target = top * wish.magnitude;
            var rate = target > _speed ? _acceleration : _brake;
            _speed = Mathf.MoveTowards(_speed, target, rate * Time.deltaTime);

            // Корпус доворачивается к направлению движения, а идёт всегда вперёд:
            // ходун не стрейфит, у него шаг по одной оси.
            if (wish.sqrMagnitude > 0.0001f)
            {
                var facing = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, facing, _turnSpeed * Time.deltaTime);
            }

            var motion = transform.forward * _speed + Vector3.down * _groundStick;
            _controller.Move(motion * Time.deltaTime);

            SetAnimatorSpeed(_speed);
        }

        /// <summary>
        /// Анимация: булевы параметры состояний и скорость проигрывания по
        /// фактической скорости. Клип шага рассчитан на свою скорость земли,
        /// поэтому отношение «фактическая / клип» и есть коэффициент без
        /// проскальзывания стопы.
        /// </summary>
        private void SetAnimatorSpeed(float speed)
        {
            if (_animator == null)
                return;

            // Режим клипа: 0 — стойка, 1 — бег, 2 — шаг. Порог бега чуть выше
            // скорости шага, иначе на разгоне в бег уходил бы сам шаг.
            var mode = speed <= 0.15f ? 0 : speed > _walkSpeed * 1.05f ? 1 : 2;
            _animator.SetBool("Moving", mode != 0);
            _animator.SetBool("Running", mode == 1);

            if (mode == 0)
            {
                _animator.speed = 1f;
                LogClipMode(mode, speed, 0f);
                return;
            }

            var reference = mode == 1 ? _runClipSpeed : _walkClipSpeed;
            _animator.speed = Mathf.Clamp(
                speed / Mathf.Max(0.01f, reference), _minClipRate, _maxClipRate);
            LogClipMode(mode, speed, reference);
        }

        /// <summary>
        /// Строка лога фактической скорости клипа. Пишется при смене режима, на
        /// ступеньке разгона (шаг <see cref="LogRateStep"/>) и один раз на полке —
        /// когда скорость перестала меняться: именно эту строку сверяют с
        /// клиповой скоростью, а разгонный шаг в 0.04 её бы иначе проглотил.
        /// Числа — в инвариантной культуре, иначе в WebGL-сборке разделитель
        /// десятичных зависит от локали браузера и строки не сравнить.
        /// </summary>
        private void LogClipMode(int mode, float speed, float clipSpeed)
        {
            var rate = _animator.speed;
            var switched = mode != _loggedClipMode;
            var stepped = Mathf.Abs(rate - _loggedRate) >= LogRateStep;
            var settled = !switched
                && Mathf.Abs(speed - _lastLoggedSpeed) < 0.005f
                && Mathf.Abs(rate - _loggedRate) > 0.0005f;
            _lastLoggedSpeed = speed;

            if (!switched && !stepped && !settled)
                return;

            _loggedClipMode = mode;
            _loggedRate = rate;
            var clip = mode == 0 ? "Idle" : mode == 1 ? "Run" : "Walk";
            Debug.Log(
                "[Mech] speed=" + speed.ToString("0.00", CultureInfo.InvariantCulture) +
                " clip=" + clip +
                " clipSpeed=" + clipSpeed.ToString("0.000", CultureInfo.InvariantCulture) +
                " rate=" + rate.ToString("0.00", CultureInfo.InvariantCulture),
                this);
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
            if (s_mounted != null || Hoverbike.SeatTaken || Motorcycle.SeatTaken)
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

        /// <summary>Сажает игрока в кабину: ходьба выключается, камера уходит в облёт.</summary>
        public void Mount()
        {
            if (_rider == null || _mounted || s_mounted != null ||
                Hoverbike.SeatTaken || Motorcycle.SeatTaken)
            {
                return;
            }

            _mounted = true;
            s_mounted = this;
            _speed = 0f;
            _locomotion?.SetRideOverride(true);
            if (_cameraProfile != null)
                _rig?.SetRideTarget(transform, _cameraProfile);
            else
                _rig?.SetRideTarget(transform);

            _rider.SetParent(_seat != null ? _seat : transform, false);
            _rider.localPosition = Vector3.zero;
            _rider.localRotation = Quaternion.identity;

            _riderPose?.Sit(_seat != null ? _seat : transform);
            SetAnimatorSpeed(0f);

            Debug.Log("[Mech] посадка", this);
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

            var spot = _exit != null
                ? _exit.position
                : transform.position + transform.right * 2.2f + Vector3.up * 0.2f;
            var facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            _riderPose?.Stand();
            _rider.SetParent(null, true);
            _rider.SetPositionAndRotation(spot, facing);

            _locomotion?.SetRideOverride(false);
            _rig?.ClearRideTarget();
            SetAnimatorSpeed(0f);

            Debug.Log("[Mech] сход", this);
        }
    }
}
