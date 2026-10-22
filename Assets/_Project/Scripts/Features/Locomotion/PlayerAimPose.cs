using UnityEngine;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Прицеливание головой: в третьем лице (основной вид) робот поворачивает шею и голову
    /// в сторону взгляда из перекрестия и «сканирует» местность.
    ///
    /// Рука и корпус не трогаются вовсе — их ведёт Animator, поэтому поза не ломается,
    /// когда камеру проводят вокруг робота. Доворот идёт в мировых осях (рыскание — вокруг
    /// вертикали, наклон — вокруг правого вектора корпуса), поэтому неважно, как
    /// ориентированы оси в риге. Поза каждый кадр берётся из клипа заново, поэтому угол
    /// не накапливается: сглаживается только применяемая величина.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class PlayerAimPose : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Риг камеры: голова поворачивается по взгляду в третьем лице")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Кость шеи — берёт на себя часть доворота")]
        [SerializeField] private Transform _neck;
        [Tooltip("Кость головы")]
        [SerializeField] private Transform _head;

        [Header("Сила позы")]
        [Tooltip("Скорость ввода веса, 1/с (0.2 с до полного доворота)")]
        [SerializeField] private float _weightSpeed = 5f;
        [Tooltip("Скорость доворота головы, градусы в секунду")]
        [SerializeField] private float _turnSpeed = 420f;

        [Header("Пределы")]
        [Tooltip("Предел доворота головы в сторону, градусы")]
        [SerializeField] private float _maxYaw = 75f;
        [Tooltip("Предел наклона головы вверх/вниз, градусы")]
        [SerializeField] private float _maxPitch = 30f;
        [Tooltip("Какая часть доворота уходит в шею")]
        [SerializeField, Range(0f, 1f)] private float _neckShare = 0.45f;

        private float _weight;
        private float _yawApplied;
        private float _pitchApplied;
        private bool _logged;

        private void Awake()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_neck == null)
                _neck = FindBone("neck_01");
            if (_head == null)
                _head = FindBone("Head");
        }

        /// <summary>Кость ищется по имени рига: путь в FBX длинный и меняется при реимпорте.</summary>
        private Transform FindBone(string boneName)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == boneName)
                    return transforms[i];
            }

            return null;
        }

        private void LateUpdate()
        {
            var aiming = _rig != null && _rig.Mode == CameraMode.ThirdPerson;

            if (!aiming)
            {
                // Смена режима (посадка на байк, первое лицо): доворот снимается сразу.
                // Плавное затухание здесь нельзя — позу наездника (RiderPose) считает
                // другая система в том же кадре, и на переходе они складывали поворот
                // в одной шее, из-за чего голова дёргалась.
                _weight = 0f;
                _yawApplied = 0f;
                _pitchApplied = 0f;

                if (_logged)
                {
                    _logged = false;
                    Debug.Log("[PlayerAimPose] голова вернулась в клип", this);
                }

                return;
            }

            _weight = Mathf.MoveTowards(_weight, 1f, _weightSpeed * Time.deltaTime);

            if (_neck == null && _head == null)
                return;

            if (!_logged && _weight > 0.99f)
            {
                _logged = true;
                Debug.Log("[PlayerAimPose] голова повёрнута по взгляду", this);
            }

            var direction = AimDirection();
            if (direction.sqrMagnitude < 1e-4f)
                return;

            TurnHead(direction);
        }

        /// <summary>
        /// Куда смотреть: в точку перекрестия, а не по оси камеры. Камера стоит за плечом
        /// со смещением, поэтому взгляд по её forward уходил бы мимо перекрестия.
        /// Если попадания нет или цель ближе метра (направление неустойчиво) — берём взгляд.
        /// </summary>
        private Vector3 AimDirection()
        {
            var fallback = _rig != null ? _rig.LookDirection : Vector3.forward;
            if (_rig == null || _head == null)
                return fallback;
            if (!_rig.TryGetAim(out var point, out _))
                return fallback;

            var delta = point - _head.position;
            return delta.sqrMagnitude > 1f ? delta.normalized : fallback;
        }

        /// <summary>
        /// Доворот головы в сторону взгляда: рыскание ограничено <c>_maxYaw</c>, наклон —
        /// <c>_maxPitch</c>, и оба сглажены, чтобы голова не щёлкала на резком движении мыши.
        /// Доворот делится между шеей и головой, иначе поворот читается как вывих.
        /// </summary>
        private void TurnHead(Vector3 direction)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            var yaw = flat.sqrMagnitude > 1e-4f
                ? Vector3.SignedAngle(BodyForward(), flat.normalized, Vector3.up)
                : 0f;
            var pitch = Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

            yaw = Mathf.Clamp(yaw, -_maxYaw, _maxYaw) * _weight;
            pitch = Mathf.Clamp(pitch, -_maxPitch, _maxPitch) * _weight;

            if (Time.deltaTime > 0f)
            {
                var step = _turnSpeed * Time.deltaTime;
                yaw = Mathf.MoveTowards(_yawApplied, yaw, step);
                pitch = Mathf.MoveTowards(_pitchApplied, pitch, step);
            }

            _yawApplied = yaw;
            _pitchApplied = pitch;

            if (Mathf.Abs(yaw) < 0.01f && Mathf.Abs(pitch) < 0.01f)
                return;

            // Положительный угол вокруг правого вектора опускает взгляд, поэтому
            // наклон вверх (direction.y > 0) применяется с минусом.
            var rotation = Quaternion.AngleAxis(yaw, Vector3.up) *
                           Quaternion.AngleAxis(-pitch, RightAxis());

            if (_neck != null)
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, rotation, _neckShare) * _neck.rotation;

            if (_head != null)
                _head.rotation = Quaternion.Slerp(Quaternion.identity, rotation, 1f - _neckShare) * _head.rotation;
        }

        /// <summary>
        /// Плоское «вперёд» корпуса. Модель поворачивается целиком, поэтому корень
        /// и есть та ось, относительно которой голова доворачивается.
        /// </summary>
        private Vector3 BodyForward()
        {
            var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }

        /// <summary>Горизонтальный правый вектор корпуса — ось наклона головы.</summary>
        private Vector3 RightAxis()
        {
            var flat = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.right;
        }
    }
}
