using UnityEngine;

namespace FrostSpace.Features.Npc
{
    /// <summary>
    /// Автоматический воздушный NPC (орёл, пиранья): летит к точке маршрута,
    /// стоит паузу, летит к следующей; после последней снова к первой.
    /// Без точек маршрута висит на месте.
    ///
    /// Движение кинематическое, без гравитации и CharacterController: маршруты
    /// выбираются так, чтобы не пересекать препятствия. Поворот — только по
    /// рысканью (высота берётся из 3D-разницы точек), поэтому птица и рыба
    /// сохраняют горизонт на всём маршруте.
    /// </summary>
    public sealed class AirPatrolController : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");

        [Header("Маршрут")]
        [Tooltip("Точки по порядку, включая высоту. Пусто — висит на месте.")]
        [SerializeField] private Transform[] _waypoints;

        [Header("Ссылки")]
        [Tooltip("Animator визуальной модели — дочерний объект")]
        [SerializeField] private Animator _animator;

        [Header("Движение, м/с")]
        [SerializeField] private float _flySpeed = 4f;
        [Tooltip("Разгон, м/с^2")]
        [SerializeField] private float _acceleration = 4f;
        [Tooltip("Скорость доворота корпуса, градусов/с")]
        [SerializeField] private float _turnSpeed = 150f;
        [Tooltip("С какого расстояния точка считается достигнутой, м")]
        [SerializeField] private float _arriveRadius = 1f;

        [Header("Пауза на точке, с")]
        [SerializeField] private float _idleMin = 1f;
        [SerializeField] private float _idleMax = 3f;

        private Vector3[] _targets;
        private float _speed;
        private float _idleTimer;
        private int _index;

        private void Awake()
        {
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogError("AirPatrolController: не назначен Animator визуальной модели", this);
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
            var moving = false;

            // Маршрута нет — висит на месте; иначе летит к текущей точке.
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
                    var distance = offset.magnitude;

                    if (distance <= _arriveRadius)
                    {
                        _idleTimer = Random.Range(_idleMin, _idleMax);
                        _speed = Mathf.MoveTowards(
                            _speed, 0f, _acceleration * 2f * deltaTime);
                    }
                    else
                    {
                        var direction = offset / Mathf.Max(distance, 0.001f);
                        _speed = Mathf.MoveTowards(
                            _speed, _flySpeed, _acceleration * deltaTime);
                        moving = true;

                        // Поворот только по рысканью: набор высоты и снижение
                        // не задирают нос.
                        var planar = new Vector3(direction.x, 0f, direction.z);
                        if (planar.sqrMagnitude > 1e-4f)
                        {
                            var rotation = Quaternion.LookRotation(
                                planar.normalized, Vector3.up);
                            transform.rotation = Quaternion.RotateTowards(
                                transform.rotation, rotation, _turnSpeed * deltaTime);
                        }

                        transform.position += direction * (_speed * deltaTime);
                    }
                }
            }
            else
            {
                _speed = Mathf.MoveTowards(_speed, 0f, _acceleration * deltaTime);
            }

            _animator.SetBool(MovingId, moving && _speed > 0.2f);
        }

        private void GoToNextPoint()
        {
            _index = (_index + 1) % _targets.Length;
        }
    }
}
