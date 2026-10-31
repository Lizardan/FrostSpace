using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Interior
{
    /// <summary>
    /// Автоматическая дверь кабины: створки разъезжаются, когда игрок подходит,
    /// и съезжаются, когда отходит.
    ///
    /// Движение — прогресс 0..1 в <see cref="Update"/>, а не корутина: дверь
    /// должна уметь поехать обратно на полпути, если игрок развернулся.
    /// Расстояние считается в локальных координатах вагона, поэтому в
    /// `Frontier` дверь работает и на ходу поезда. Игрок ищется через риг
    /// камеры — в проекте не принято искать по тегу.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CabinDoor : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private Transform _left;
        [SerializeField] private Transform _right;
        [Tooltip("Игрок; если пусто — берётся из рига камеры")]
        [SerializeField] private Transform _player;

        [Header("Дистанции")]
        [Tooltip("С какого расстояния дверь открывается, м")]
        [SerializeField] private float _openRadius = 3.2f;
        [Tooltip("С какого расстояния закрывается, м: больше открытия — гистерезис")]
        [SerializeField] private float _closeRadius = 4.2f;
        [Tooltip("Полуширина проёма, м: стоящего в нём дверь не закрывает")]
        [SerializeField] private float _apertureHalfWidth = 2.4f;
        [Tooltip("Глубина проёма вдоль вагона, м")]
        [SerializeField] private float _apertureDepth = 0.6f;

        [Header("Движение")]
        [Tooltip("Полный ход створки, с")]
        [SerializeField] private float _seconds = 0.8f;
        [Tooltip("Насколько створка уезжает в сторону, м")]
        [SerializeField] private float _slide = 1.55f;

        private Vector3 _leftClosed;
        private Vector3 _rightClosed;
        private float _progress;
        private PlayerCameraRig _rig;

        /// <summary>Дверь открыта больше чем наполовину.</summary>
        public bool IsOpen => _progress > 0.5f;

        /// <summary>Прогресс хода: 0 — закрыто, 1 — открыто.</summary>
        public float Progress => _progress;

        private void Awake()
        {
            if (_left != null)
                _leftClosed = _left.localPosition;
            if (_right != null)
                _rightClosed = _right.localPosition;
        }

        private void Update()
        {
            if (_left == null || _right == null)
                return;

            var player = ResolvePlayer();
            if (player == null)
                return;

            var local = transform.InverseTransformPoint(player.position);
            var door = (_leftClosed + _rightClosed) * 0.5f;
            var distance = Vector2.Distance(new Vector2(local.x, local.z),
                                            new Vector2(door.x, door.z));

            var wantOpen = WantsOpen(distance, _progress > 0.5f,
                                     _openRadius, _closeRadius);

            // Стоящего в проёме не закрываем: створка поехала бы ему в бок.
            if (!wantOpen && IsInAperture(local, _apertureHalfWidth,
                                          _apertureDepth, door.z))
                wantOpen = true;

            _progress = Step(_progress, wantOpen ? 1f : 0f,
                             Time.deltaTime, _seconds);

            _left.localPosition = _leftClosed + Vector3.left * (_slide * _progress);
            _right.localPosition = _rightClosed + Vector3.right * (_slide * _progress);
        }

        private Transform ResolvePlayer()
        {
            if (_player != null)
                return _player;

            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_rig != null)
                _player = _rig.Target;

            return _player;
        }

        /// <summary>Гистерезис: открытие и закрытие на разных дистанциях.</summary>
        public static bool WantsOpen(float distance, bool isOpen,
                                     float openRadius, float closeRadius)
        {
            return isOpen ? distance <= closeRadius : distance <= openRadius;
        }

        /// <summary>Шаг прогресса 0..1: полный ход за <paramref name="seconds"/>.</summary>
        public static float Step(float progress, float target, float dt, float seconds)
        {
            return Mathf.MoveTowards(progress, target,
                                     dt / Mathf.Max(seconds, 0.01f));
        }

        /// <summary>Точка внутри проёма: закрывать нельзя.</summary>
        public static bool IsInAperture(Vector3 local, float halfWidth,
                                        float depth, float doorZ)
        {
            return Mathf.Abs(local.x) <= halfWidth &&
                   Mathf.Abs(local.z - doorZ) <= depth;
        }
    }
}
