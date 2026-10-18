using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Движение робота по земле от третьего лица.
    ///
    /// Скорость клипа и скорость персонажа связаны: Animator.speed
    /// пересчитывается как «желаемая скорость / базовая скорость клипа».
    /// Поэтому стопа не проскальзывает при разгоне и беге.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotLocomotion : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");

        [Header("Ссылки")]
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Transform _camera;

        [Header("Скорости, м/с")]
        [SerializeField] private float _walkSpeed = 1.2f;
        [SerializeField] private float _sprintSpeed = 6.5f;
        [SerializeField] private float _acceleration = 14f;
        [SerializeField] private float _turnSpeed = 720f;

        [Header("Базовые скорости клипов, м/с")]
        [Tooltip("Замерено по контакту стопы в Tools/Blender/measure_locomotion.py")]
        [SerializeField] private float _walkClipSpeed = 0.93f;
        [SerializeField] private float _sprintClipSpeed = 8.9f;

        private CharacterController _controller;
        private Animator _animator;
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _animatorSpeed = 1f;
        private bool _inputOverridden;
        private Vector2 _overrideMove;
        private bool _overrideSprint;

        public float PlanarSpeed => _planarVelocity.magnitude;
        public float AnimatorSpeed => _animatorSpeed;

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
            _animator = GetComponentInChildren<Animator>();

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

            var targetSpeed = direction.sqrMagnitude > 0.001f
                ? (wantsSprint ? _sprintSpeed : _walkSpeed)
                : 0f;
            var targetVelocity = direction * targetSpeed;
            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity, targetVelocity, _acceleration * Time.deltaTime);

            if (direction.sqrMagnitude > 0.001f)
            {
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
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
            var moving = speed > 0.05f;
            var sprinting = wantsSprint && moving;

            _animator.SetBool(MovingId, moving);
            _animator.SetBool(SprintingId, sprinting);

            if (!moving)
            {
                _animatorSpeed = 1f;
            }
            else
            {
                var clipSpeed = sprinting ? _sprintClipSpeed : _walkClipSpeed;
                _animatorSpeed = Mathf.Clamp(speed / clipSpeed, 0.35f, 2.2f);
            }

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
