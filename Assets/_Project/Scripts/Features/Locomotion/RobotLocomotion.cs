using UnityEngine;
using UnityEngine.InputSystem;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Движение робота по земле.
    ///
    /// Вектор скорости поворачивается к вводу отдельно от корпуса: на резкой смене
    /// направления робот не «заносится» боком, а доворачивает траекторию. Animator
    /// получает Moving/Sprinting и скорость «фактическая скорость / базовая скорость
    /// клипа», поэтому стопа не проскальзывает при разгоне и торможении.
    ///
    /// Базис WASD — всегда рыскание камеры. Раньше он «замораживался» при выходе из дома,
    /// чтобы зажатый W не разворачивал игрока; теперь третий вид сам управляется мышью,
    /// поэтому базис одинаков во всех режимах и заморозка не нужна.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotLocomotion : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int AirborneId = Animator.StringToHash("Airborne");

        [Header("Ссылки")]
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Transform _camera;
        [Tooltip("Animator визуальной модели — дочерний объект, например R1_PolarMechanic_Model")]
        [SerializeField] private Animator _animator;
        [Tooltip("Риг камеры: базис WASD — рыскание камеры, в первом лице корпус идёт по ней")]
        [SerializeField] private PlayerCameraRig _rig;

        [Header("Скорости, м/с")]
        [SerializeField] private float _walkSpeed = 1.2f;
        [SerializeField] private float _sprintSpeed = 6.5f;
        [Tooltip("Разгон до целевой скорости, м/с^2")]
        [SerializeField] private float _acceleration = 16f;
        [Tooltip("Торможение при отпущенном вводе, м/с^2")]
        [SerializeField] private float _brake = 24f;
        [Tooltip("Скорость доворота корпуса, градусов/с")]
        [SerializeField] private float _turnSpeed = 720f;
        [Tooltip("Скорость доворота вектора скорости, градусов/с")]
        [SerializeField] private float _velocityTurnSpeed = 800f;

        [Header("Прыжок")]
        [Tooltip("Высота прыжка по вершине траектории, м")]
        [SerializeField] private float _jumpHeight = 1.2f;
        [Tooltip("Пауза приседа перед импульсом, с: клип JumpStart успевает начаться")]
        [SerializeField] private float _jumpAnticipation = 0.08f;
        [Tooltip("Сколько нажатие живёт в буфере до приземления, с")]
        [SerializeField] private float _jumpBuffer = 0.12f;
        [Tooltip("Сколько после схода с края ещё можно прыгнуть, с")]
        [SerializeField] private float _coyoteTime = 0.1f;
        [Tooltip("Доля управления в воздухе: 1 — как на земле")]
        [SerializeField, Range(0f, 1f)] private float _airControl = 0.65f;

        // Базовые скорости клипов, м/с: Animator.speed подгоняется так, чтобы
        // стопа не проскальзывала. В сцене зафиксирован разовый замер в Unity;
        // дефолты ниже — ориентир из Tools/Blender/measure_locomotion.py.
        // Вручную не правятся.
        [SerializeField, HideInInspector] private float _walkClipSpeed = 0.93f;
        [SerializeField, HideInInspector] private float _sprintClipSpeed = 8.9f;

        private CharacterController _controller;
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _jumpAction;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _animatorSpeed = 1f;
        private bool _sprinting;
        private bool _inputOverridden;
        private Vector2 _overrideMove;
        private bool _overrideSprint;
        private bool _rideOverride;
        private float _loggedBasisYaw = float.NaN;
        private float _lastGroundedAt = -999f;
        private float _jumpBufferedAt = -999f;
        private float _jumpQueuedAt = -999f;
        private bool _jumpPending;
        private bool _airborne;

        public float PlanarSpeed => _planarVelocity.magnitude;
        public float AnimatorSpeed => _animatorSpeed;
        public bool IsAirborne => _airborne;

        /// <summary>
        /// Поставить прыжок в очередь так же, как это делает Space. Нужен
        /// проверочному стенду: он меряет высоту и время полёта без клавиатуры.
        /// </summary>
        public void RequestJump()
        {
            _jumpBufferedAt = Time.time;
        }

        /// <summary>Фактический вектор движения по земле — для проверок, не для геймплея.</summary>
        public Vector3 PlanarVelocity => _planarVelocity;

        /// <summary>Подмена ввода для автоматической проверки движения и скольжения.</summary>
        public void SetInputOverride(Vector2 move, bool sprint)
        {
            _inputOverridden = true;
            _overrideMove = move;
            _overrideSprint = sprint;
        }

        public void ClearInputOverride()
        {
            _inputOverridden = false;
            _overrideMove = Vector2.zero;
            _overrideSprint = false;
        }

        /// <summary>
        /// Пока игрок на транспорте, ходьба выключена: коллайдер отключается, чтобы
        /// персонаж жёстко ехал вместе с байком, а не спорил с ним за позицию.
        /// </summary>
        public void SetRideOverride(bool riding)
        {
            if (_rideOverride == riding)
                return;

            _rideOverride = riding;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _jumpPending = false;
            _airborne = false;
            _jumpBufferedAt = -999f;
            _controller.enabled = !riding;

            if (_animator != null)
            {
                _animator.SetBool(MovingId, false);
                _animator.SetBool(SprintingId, false);
                _animator.SetBool(AirborneId, false);
                _animator.speed = 1f;
            }

            Debug.Log(riding ? "[RobotLocomotion] посадка" : "[RobotLocomotion] сход", this);
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogError(
                    "RobotLocomotion: не назначен Animator визуальной модели", this);
                enabled = false;
                return;
            }

            // Игрок в третьем лице часто уходит к краю кадра: без этого анимация
            // замирает, а при возврате взгляда прыгает в текущую позу.
            FrostSpace.Features.Npc.AnimationCulling.Apply(_animator);

            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _moveAction = source.FindAction("Player/Move");
                _sprintAction = source.FindAction("Player/Sprint");
                _jumpAction = source.FindAction("Player/Jump");
            }
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _sprintAction?.Enable();
            _jumpAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _sprintAction?.Disable();
            _jumpAction?.Disable();
        }

        private void Update()
        {
            if (_rideOverride)
                return;

            var input = _inputOverridden
                ? _overrideMove
                : (_moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero);
            var wantsSprint = _inputOverridden
                ? _overrideSprint
                : (_sprintAction != null && _sprintAction.IsPressed());
            var direction = CameraRelative(input);
            UpdateJump();

            // Скорость и направление разведены: разгон/торможение меняют длину
            // вектора, steering — только его направление. Это убирает «занос»
            // при резкой смене курса на бегу.
            var currentSpeed = _planarVelocity.magnitude;
            var targetSpeed = direction.sqrMagnitude > 0.001f
                ? (wantsSprint ? _sprintSpeed : _walkSpeed)
                : 0f;
            var control = _airborne ? _airControl : 1f;
            var rate = (targetSpeed > currentSpeed ? _acceleration : _brake) * control;
            var newSpeed = Mathf.MoveTowards(
                currentSpeed, targetSpeed, rate * Time.deltaTime);

            var currentDirection = currentSpeed > 0.001f
                ? _planarVelocity / currentSpeed
                : direction;
            var newDirection = direction.sqrMagnitude > 0.001f
                ? Vector3.RotateTowards(
                    currentDirection, direction,
                    _velocityTurnSpeed * control * Mathf.Deg2Rad * Time.deltaTime, 0f)
                : currentDirection;
            _planarVelocity = newDirection * newSpeed;

            if (_rig != null && _rig.FacesCamera)
            {
                // Первое лицо: корпус доворачивается по рысканию камеры,
                // поэтому WASD читается как стрейф, как в шутере.
                var facing = Quaternion.Euler(0f, _rig.ViewYaw, 0f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, facing, _turnSpeed * Time.deltaTime);
            }
            else if (_planarVelocity.sqrMagnitude > 0.0025f)
            {
                var rotation = Quaternion.LookRotation(
                    _planarVelocity.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, rotation, _turnSpeed * Time.deltaTime);
            }

            if (_controller.isGrounded)
            {
                _lastGroundedAt = Time.time;
                if (_verticalVelocity < 0f)
                    _verticalVelocity = -2f;
            }
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;

            var motion = _planarVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            // Приземление засчитываем только после реального импульса: в кадре
            // приседа персонаж ещё стоит на земле, и без этого флага прыжок
            // отменялся бы до старта.
            if (_airborne && _controller.isGrounded && _verticalVelocity <= 0f &&
                Time.time - _jumpQueuedAt > _jumpAnticipation + 0.05f)
            {
                _airborne = false;
                _animator.SetBool(AirborneId, false);
            }

            UpdateAnimator(wantsSprint);
        }

        /// <summary>
        /// Прыжок с буфером и coyote time: нажатие в воздухе запоминается на
        /// короткое время, а сход с края не отбирает прыжок мгновенно.
        /// Импульс выдаётся не сразу, а через присед — так JumpStart успевает
        /// начаться и вес прыжка читается.
        /// </summary>
        private void UpdateJump()
        {
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpBufferedAt = Time.time;

            if (_jumpPending)
            {
                if (Time.time - _jumpQueuedAt < _jumpAnticipation)
                    return;

                if (_controller.isGrounded ||
                    Time.time - _lastGroundedAt <= _coyoteTime)
                {
                    _jumpPending = false;
                    _airborne = true;
                    _verticalVelocity = Mathf.Sqrt(
                        2f * -Physics.gravity.y * Mathf.Max(0.05f, _jumpHeight));
                    _animator.SetBool(AirborneId, true);
                }
                else
                {
                    _jumpPending = false;
                    _animator.SetBool(AirborneId, false);
                }

                return;
            }

            if (_airborne || Time.time - _jumpBufferedAt > _jumpBuffer)
                return;

            if (!_controller.isGrounded && Time.time - _lastGroundedAt > _coyoteTime)
                return;

            _jumpBufferedAt = -999f;
            _jumpPending = true;
            _jumpQueuedAt = Time.time;
            _animator.SetTrigger(JumpId);
        }

        private void UpdateAnimator(bool wantsSprint)
        {
            // В прыжке клип ведёт Animator, а не подгонка шага: иначе скорость
            // клипа прыжка зависела бы от горизонтальной скорости.
            if (_airborne || _jumpPending)
            {
                _animatorSpeed = 1f;
                _animator.speed = 1f;
                return;
            }

            var speed = _planarVelocity.magnitude;
            var moving = speed > 0.08f;

            // Гистерезис по фактической скорости: Sprint включается, когда робот
            // уже разогнался, и выключается с запасом, чтобы состояния не дребезжали.
            if (moving)
            {
                if (wantsSprint && speed > _sprintSpeed * 0.5f)
                    _sprinting = true;
                else if (!wantsSprint && speed < _sprintSpeed * 0.45f)
                    _sprinting = false;
            }
            else
            {
                _sprinting = false;
            }

            _animator.SetBool(MovingId, moving);
            _animator.SetBool(SprintingId, _sprinting);

            // Animator.speed = фактическая скорость / базовая скорость клипа.
            // Сглаживание убирает рывок в момент кроссфейда.
            var target = 1f;
            if (moving)
            {
                var clipSpeed = _sprinting ? _sprintClipSpeed : _walkClipSpeed;
                if (clipSpeed > 0.01f)
                    target = Mathf.Clamp(speed / clipSpeed, 0.2f, 4f);
            }

            _animatorSpeed = Mathf.MoveTowards(_animatorSpeed, target, 8f * Time.deltaTime);
            _animator.speed = _animatorSpeed;
        }

        private Vector3 CameraRelative(Vector2 input)
        {
            var yaw = BasisYaw();
            if (input.sqrMagnitude < 0.001f)
                return Vector3.zero;

            var basis = Quaternion.Euler(0f, yaw, 0f);
            return (basis * new Vector3(input.x, 0f, input.y)).normalized;
        }

        /// <summary>
        /// Рыскание, по которому читается WASD. В игре это всегда рыскание рига:
        /// камера третьего лица смотрит туда же, куда ведёт мышь, поэтому базис
        /// не меняется при переходе между третьим и первым лицом и заморозка не нужна.
        /// </summary>
        private float BasisYaw()
        {
            float yaw;
            if (_rig != null)
            {
                yaw = _rig.ViewYaw;
            }
            else
            {
                var cameraForward = _camera != null ? _camera.forward : Vector3.forward;
                var flat = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
                yaw = flat.sqrMagnitude > 1e-4f
                    ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
                    : 0f;
            }

            if (!float.IsNaN(_loggedBasisYaw) && Mathf.Abs(Mathf.DeltaAngle(_loggedBasisYaw, yaw)) > 15f)
            {
                _loggedBasisYaw = yaw;
                Debug.Log($"[RobotLocomotion] базис={yaw:0.#}°", this);
            }
            else if (float.IsNaN(_loggedBasisYaw))
            {
                _loggedBasisYaw = yaw;
            }

            return yaw;
        }
    }
}
