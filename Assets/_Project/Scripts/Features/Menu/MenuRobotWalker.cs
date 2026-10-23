using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Ходящий робот в главном меню: идёт по точкам туда-обратно и включает
    /// клип Walk. Для носильщика вместо Walk играет CarryWalk — клип переноски
    /// ящика из того же ActionForge-пака.
    ///
    /// Компонент живёт на сценовом актёре, а не на визуальном префабе
    /// (ADR 0004). <see cref="CharacterController"/> не нужен: меню — декорация,
    /// столкновения не моделируются, а маршрут проходит по свободному проходу.
    ///
    /// Скорость клипа — измеренная скорость стопы в контакте, м/с. Animator.speed
    /// подгоняется так, чтобы стопа не скользила, а не подбирается на глаз.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(215)]
    public sealed class MenuRobotWalker : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int CarryStateId = Animator.StringToHash("CarryWalk");

        [Header("Маршрут")]
        [Tooltip("Точки маршрута в мире; робот идёт по ним туда-обратно")]
        [SerializeField] private Transform[] _waypoints;

        [Tooltip("Скорость ходьбы, м/с")]
        [SerializeField] private float _walkSpeed = 0.93f;

        [Tooltip("Скорость стопы в клипе, м/с (замер measure_locomotion)")]
        [SerializeField] private float _clipSpeed = 0.927f;

        [Tooltip("Скорость доворота корпуса, градусов/с")]
        [SerializeField] private float _turnSpeed = 220f;

        [Tooltip("Насколько близко к точке считать её достигнутой, м")]
        [SerializeField] private float _arriveRadius = 0.08f;

        [Header("Клип")]
        [Tooltip("Играть CarryWalk вместо Walk")]
        [SerializeField] private bool _carry;

        [Header("Ссылки")]
        [Tooltip("Animator визуальной модели; пусто — ищется у детей")]
        [SerializeField] private Animator _animator;

        [Header("Ящик")]
        [Tooltip("Проп ящика, который покачивается в такт шагам; необязателен")]
        [SerializeField] private Transform _crate;

        [Tooltip("Амплитуда покачивания ящика, м")]
        [SerializeField] private float _crateBob = 0.025f;

        [Tooltip("Период покачивания ящика, с")]
        [SerializeField] private float _crateBobSeconds = 1.4f;

        private Vector3[] _targets;
        private int _targetIndex;
        private int _direction = 1;
        private float _speed;
        private float _animatorSpeed = 1f;
        private Vector3 _crateBase;
        private bool _ready;

        private void Awake()
        {
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);

            if (_crate != null)
                _crateBase = _crate.localPosition;
        }

        private void OnEnable()
        {
            _ready = BuildRoute();
            _speed = 0f;
            _animatorSpeed = 1f;
            _targetIndex = _targets != null && _targets.Length > 1 ? 1 : 0;
            _direction = 1;

            if (_animator == null)
            {
                Debug.LogError(
                    "[MenuRobotWalker] не найден Animator — робот останется на месте", this);
                _ready = false;
                return;
            }

            if (_carry)
                _animator.Play(CarryStateId, 0, 0f);
            else
                _animator.SetBool(MovingId, true);
        }

        private void OnDisable()
        {
            if (_animator != null)
            {
                _animator.speed = 1f;
                if (!_carry)
                    _animator.SetBool(MovingId, false);
            }
        }

        private bool BuildRoute()
        {
            if (_waypoints == null || _waypoints.Length < 2)
            {
                Debug.LogError(
                    "[MenuRobotWalker] нужно минимум две точки маршрута", this);
                return false;
            }

            _targets = new Vector3[_waypoints.Length];
            for (var i = 0; i < _waypoints.Length; i++)
            {
                if (_waypoints[i] == null)
                {
                    Debug.LogError(
                        $"[MenuRobotWalker] точка {i} не назначена", this);
                    return false;
                }

                _targets[i] = _waypoints[i].position;
            }

            return true;
        }

        private void Update()
        {
            if (!_ready)
                return;

            var deltaTime = Time.unscaledDeltaTime;
            var offset = _targets[_targetIndex] - transform.position;
            offset.y = 0f;
            var distance = offset.magnitude;

            if (distance <= _arriveRadius)
            {
                AdvanceTarget();
                offset = _targets[_targetIndex] - transform.position;
                offset.y = 0f;
                distance = offset.magnitude;
            }

            if (distance > 0.001f)
            {
                var direction = offset / distance;
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, rotation, _turnSpeed * deltaTime);

                // Идём только когда корпус уже довернулся: иначе шаги читаются
                // боком, особенно на развороте в конце маршрута.
                var alignment = Vector3.Dot(transform.forward, direction);
                _speed = Mathf.MoveTowards(
                    _speed,
                    alignment > 0.75f ? _walkSpeed : 0f,
                    2.2f * deltaTime);

                if (_speed > 0.01f)
                {
                    var step = Mathf.Min(_speed * deltaTime, distance);
                    transform.position += transform.forward * step;
                }
            }
            else
            {
                _speed = Mathf.MoveTowards(_speed, 0f, 2.2f * deltaTime);
            }

            UpdateAnimator(deltaTime);
            BobCrate();
        }

        private void AdvanceTarget()
        {
            if (_targets.Length == 2)
            {
                _targetIndex = _targetIndex == 0 ? 1 : 0;
                return;
            }

            // Для длинного маршрута — маятник без телепорта с конца в начало.
            _targetIndex += _direction;
            if (_targetIndex >= _targets.Length)
            {
                _targetIndex = _targets.Length - 2;
                _direction = -1;
            }
            else if (_targetIndex < 0)
            {
                _targetIndex = 1;
                _direction = 1;
            }
        }

        private void UpdateAnimator(float deltaTime)
        {
            if (_animator == null)
                return;

            var walking = _speed > 0.05f;
            if (!_carry)
                _animator.SetBool(MovingId, walking);

            var target = walking
                ? Mathf.Clamp(_walkSpeed / Mathf.Max(_clipSpeed, 0.01f), 0.2f, 2f)
                : 1f;
            _animatorSpeed = Mathf.MoveTowards(_animatorSpeed, target, 4f * deltaTime);
            _animator.speed = _animatorSpeed;
        }

        private void BobCrate()
        {
            if (_crate == null)
                return;

            var phase = Time.unscaledTime * Mathf.PI * 2f /
                        Mathf.Max(_crateBobSeconds, 0.1f);
            var position = _crateBase;
            position.y += Mathf.Sin(phase) * _crateBob * Mathf.Clamp01(_speed);
            _crate.localPosition = position;
        }
    }
}
