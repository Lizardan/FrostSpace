using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Движение робота по земле от третьего лица.
    ///
    /// Вектор скорости поворачивается к вводу отдельно от корпуса: на резкой смене
    /// направления робот не «заносится» боком, а доворачивает траекторию. Animator
    /// получает Moving/Sprinting и скорость «фактическая скорость / базовая скорость
    /// клипа», поэтому стопа не проскальзывает при разгоне и торможении.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotLocomotion : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");

        [Header("Ссылки")]
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Transform _camera;
        [Tooltip("Animator визуальной модели — дочерний объект, например R1_PolarMechanic_Model")]
        [SerializeField] private Animator _animator;

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

        // Базовые скорости клипов, м/с: Animator.speed подгоняется так, чтобы
        // стопа не проскальзывала. В сцене зафиксирован разовый замер в Unity;
        // дефолты ниже — ориентир из Tools/Blender/measure_locomotion.py.
        // Вручную не правятся.
        [SerializeField, HideInInspector] private float _walkClipSpeed = 0.93f;
        [SerializeField, HideInInspector] private float _sprintClipSpeed = 8.9f;

        private CharacterController _controller;
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _animatorSpeed = 1f;
        private bool _sprinting;
        private bool _inputOverridden;
        private Vector2 _overrideMove;
        private bool _overrideSprint;

        public float PlanarSpeed => _planarVelocity.magnitude;
        public float AnimatorSpeed => _animatorSpeed;

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

            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _moveAction = source.FindAction("Player/Move");
                _sprintAction = source.FindAction("Player/Sprint");
            }
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _sprintAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _sprintAction?.Disable();
        }

        private void Update()
        {
            var input = _inputOverridden
                ? _overrideMove
                : (_moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero);
            var wantsSprint = _inputOverridden
                ? _overrideSprint
                : (_sprintAction != null && _sprintAction.IsPressed());
            var direction = CameraRelative(input);

            // Скорость и направление разведены: разгон/торможение меняют длину
            // вектора, steering — только его направление. Это убирает «занос»
            // при резкой смене курса на бегу.
            var currentSpeed = _planarVelocity.magnitude;
            var targetSpeed = direction.sqrMagnitude > 0.001f
                ? (wantsSprint ? _sprintSpeed : _walkSpeed)
                : 0f;
            var rate = targetSpeed > currentSpeed ? _acceleration : _brake;
            var newSpeed = Mathf.MoveTowards(
                currentSpeed, targetSpeed, rate * Time.deltaTime);

            var currentDirection = currentSpeed > 0.001f
                ? _planarVelocity / currentSpeed
                : direction;
            var newDirection = direction.sqrMagnitude > 0.001f
                ? Vector3.RotateTowards(
                    currentDirection, direction,
                    _velocityTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f)
                : currentDirection;
            _planarVelocity = newDirection * newSpeed;

            if (_planarVelocity.sqrMagnitude > 0.0025f)
            {
                var rotation = Quaternion.LookRotation(
                    _planarVelocity.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, rotation, _turnSpeed * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;

            var motion = _planarVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            UpdateAnimator(wantsSprint);
        }

        private void UpdateAnimator(bool wantsSprint)
        {
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
            if (input.sqrMagnitude < 0.001f)
                return Vector3.zero;

            var forward = _camera != null ? _camera.forward : Vector3.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            var right = Vector3.Cross(Vector3.up, forward);
            return (forward * input.y + right * input.x).normalized;
        }
    }
}
