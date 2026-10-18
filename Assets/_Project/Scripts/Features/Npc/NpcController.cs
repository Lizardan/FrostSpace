using UnityEngine;

namespace FrostSpace.Features.Npc
{
    /// <summary>
    /// Автоматический NPC: идёт к точке маршрута, стоит паузу 2–10 с, идёт к
    /// следующей; после последней снова к первой. Без точек маршрута стоит на
    /// месте. Ввод не читается — NPC игроком не управляется.
    ///
    /// Скорость анимации подгоняется под фактическую (как у RobotLocomotion),
    /// а если NPC упирается в препятствие и дольше _stuckSeconds не
    /// приближается к цели, точка пропускается: маршрут не встаёт колом.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class NpcController : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");

        [Header("Маршрут")]
        [Tooltip("Точки по порядку. Пусто — NPC стоит на месте.")]
        [SerializeField] private Transform[] _waypoints;

        [Header("Ссылки")]
        [Tooltip("Animator визуальной модели — дочерний объект")]
        [SerializeField] private Animator _animator;

        [Header("Движение, м/с")]
        [SerializeField] private float _walkSpeed = 1.05f;
        [Tooltip("Разгон, м/с^2")]
        [SerializeField] private float _acceleration = 3.5f;
        [Tooltip("Скорость доворота корпуса, градусов/с")]
        [SerializeField] private float _turnSpeed = 240f;
        [Tooltip("С какого расстояния точка считается достигнутой, м")]
        [SerializeField] private float _arriveRadius = 0.5f;

        [Header("Пауза на точке, с")]
        [SerializeField] private float _idleMin = 2f;
        [SerializeField] private float _idleMax = 10f;

        [Header("Антизастревание")]
        [Tooltip("Сколько секунд без прогресса считать, что NPC упёрся")]
        [SerializeField] private float _stuckSeconds = 2f;
        [Tooltip("Прогресс к цели ниже этого (м/с) считается стоянием на месте")]
        [SerializeField] private float _stuckProgress = 0.05f;

        // Базовая скорость клипа Walk, м/с: Animator.speed = фактическая /
        // базовая, чтобы стопа не проскальзывала. Замер в Unity; вручную не
        // правится (как у RobotLocomotion).
        [SerializeField, HideInInspector] private float _walkClipSpeed = 0.912f;

        private CharacterController _controller;
        // Мировые позиции точек, снятые один раз при включении: вейпоинты —
        // дочерние объекты NPC, и при движении они едут вместе с ним. Маршрут
        // хранится в абсолютных координатах, поэтому агенту всё равно, куда
        // сместились сами маркеры.
        private Vector3[] _targets;
        private float _speed;
        private float _verticalVelocity;
        private float _animatorSpeed = 1f;
        private float _idleTimer;
        private float _stuckTimer;
        private float _previousDistance = float.MaxValue;
        private int _index;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogError("NpcController: не назначен Animator визуальной модели", this);
                enabled = false;
            }

            if (_waypoints != null && _waypoints.Length > 0)
            {
                _targets = new Vector3[_waypoints.Length];
                for (var i = 0; i < _waypoints.Length; i++)
                {
                    _targets[i] = _waypoints[i] != null
                        ? _waypoints[i].position
                        : transform.position;
                }
            }
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            var planar = Vector3.zero;

            // Маршрута нет — стоит на месте; иначе идёт к текущей точке.
            if (_targets != null && _targets.Length > 0)
            {
                if (_idleTimer > 0f)
                {
                    _idleTimer -= deltaTime;
                    if (_idleTimer <= 0f)
                        GoToNextPoint();
                    _speed = Mathf.MoveTowards(
                        _speed, 0f, _acceleration * 2f * deltaTime);
                }
                else
                {
                    var offset = _targets[_index] - transform.position;
                    offset.y = 0f;
                    var distance = offset.magnitude;

                    if (distance <= _arriveRadius)
                    {
                        // Точка достигнута: пауза и затем переход к следующей.
                        _idleTimer = Random.Range(_idleMin, _idleMax);
                        _previousDistance = float.MaxValue;
                        _speed = Mathf.MoveTowards(
                            _speed, 0f, _acceleration * 2f * deltaTime);
                    }
                    else
                    {
                        var direction = offset / Mathf.Max(distance, 0.001f);
                        _speed = Mathf.MoveTowards(
                            _speed, _walkSpeed, _acceleration * deltaTime);

                        var rotation = Quaternion.LookRotation(direction, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(
                            transform.rotation, rotation, _turnSpeed * deltaTime);

                        // Антизастревание: сколько времени нет прогресса к цели.
                        if (_previousDistance - distance < _stuckProgress * deltaTime)
                            _stuckTimer += deltaTime;
                        else
                            _stuckTimer = 0f;

                        if (_stuckTimer >= _stuckSeconds)
                        {
                            _stuckTimer = 0f;
                            GoToNextPoint();
                        }

                        _previousDistance = distance;
                        planar = direction * (_speed * deltaTime);
                    }
                }
            }
            else
            {
                _speed = Mathf.MoveTowards(_speed, 0f, _acceleration * deltaTime);
            }

            // Гравитация применяется один раз за кадр, поверх шага по земле.
            planar.y = Vertical(deltaTime);
            _controller.Move(planar);
            UpdateAnimator(deltaTime);
        }

        private void GoToNextPoint()
        {
            _index = (_index + 1) % _targets.Length;
            _previousDistance = float.MaxValue;
            _stuckTimer = 0f;
        }

        private float Vertical(float deltaTime)
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            _verticalVelocity += Physics.gravity.y * deltaTime;
            return _verticalVelocity * deltaTime;
        }

        private void UpdateAnimator(float deltaTime)
        {
            var moving = _speed > 0.08f;
            _animator.SetBool(MovingId, moving);

            var target = moving
                ? Mathf.Clamp(_speed / Mathf.Max(_walkClipSpeed, 0.01f), 0.2f, 2f)
                : 1f;
            _animatorSpeed = Mathf.MoveTowards(_animatorSpeed, target, 4f * deltaTime);
            _animator.speed = _animatorSpeed;
        }
    }
}
