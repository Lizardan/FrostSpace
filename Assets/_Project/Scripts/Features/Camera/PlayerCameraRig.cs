using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Единственный владелец главной камеры: три режима и плавный переход между ними.
    ///
    /// <list type="bullet">
    /// <item><b>ThirdPerson</b> — прежний вид сверху-сзади с фиксированным смещением;
    /// мышь обзор не крутит, так что управление остаётся таким же, как было.</item>
    /// <item><b>Aim</b> — удержание правой кнопки: камера переезжает за плечо, мышь ведёт
    /// рыскание и тангаж, корпус доворачивается по WASD (как в третьем лице), а голова
    /// поворачивается к точке прицела (<c>PlayerAimPose</c>); фейдер перекрытий светит круг.</item>
    /// <item><b>FirstPerson</b> — внутри <see cref="InteriorZone"/>: камера встаёт в голову
    /// робота, мышь крутит обзор, WASD работает стрейфом.</item>
    /// </list>
    ///
    /// Поза считается аналитически каждый кадр, а смена режима запоминает текущую позу и
    /// перетекает к новой за <c>_enterSeconds</c> (вход) или <c>_exitSeconds</c> (возврат).
    /// Камера прицела дополнительно отводится от стены сферокастом.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Корень игрока — вокруг него строятся все позы")]
        [SerializeField] private Transform _target;
        [Tooltip("Камера, которой управляет риг (обычно эта же Main Camera)")]
        [SerializeField] private Camera _camera;
        [Tooltip("Кость головы: в неё встаёт камера от первого лица")]
        [SerializeField] private Transform _headBone;
        [Tooltip("Ассет ввода; если пусто — берётся InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Третье лицо")]
        [SerializeField] private Vector3 _thirdPersonOffset = new(0f, 11f, -9f);
        [SerializeField] private float _thirdPersonLookHeight = 1f;
        [Tooltip("Сглаживание позиции в третьем лице, 1/с")]
        [SerializeField] private float _thirdPersonSmoothing = 8f;

        [Header("Прицел")]
        [Tooltip("Смещение камеры от точки над плечом: назад и вбок, в локальных осях рыскания")]
        [SerializeField] private Vector3 _aimOffset = new(0.6f, 0.35f, -3.1f);
        [Tooltip("Высота точки, вокруг которой стоит камера прицела")]
        [SerializeField] private float _aimLookHeight = 1.45f;
        [Tooltip("Радиус сферы, которой камера отводится от стен")]
        [SerializeField] private float _aimCollisionRadius = 0.25f;
        [Tooltip("Максимальная дальность прицельного луча, м")]
        [SerializeField] private float _aimDistance = 60f;

        [Header("Первое лицо")]
        [Tooltip("Смещение камеры от кости головы, м")]
        [SerializeField] private Vector3 _headOffset = new(0f, 0.12f, 0.08f);

        [Header("Переходы")]
        [Tooltip("Сколько камера едет в прицел и в голову, с")]
        [SerializeField] private float _enterSeconds = 0.22f;
        [Tooltip("Сколько камера возвращается наверх, с")]
        [SerializeField] private float _exitSeconds = 0.38f;
        [Tooltip("Насколько за стену надо выйти, чтобы первое лицо отпустило, м. Без этого запаса режим мигает точно на плоскости стены, когда игрок выбегает из дома")]
        [SerializeField] private float _interiorExitMargin = 0.5f;

        [Header("Обзор")]
        [Tooltip("Градусов поворота на пиксель движения мыши")]
        [SerializeField] private float _sensitivity = 0.12f;
        [SerializeField] private float _minPitch = -80f;
        [SerializeField] private float _maxPitch = 80f;
        [Tooltip("Тангаж, с которого начинается прицел и первое лицо")]
        [SerializeField] private float _startPitch = 10f;
        [Tooltip("Что останавливает камеру прицела и прицельный луч (слой Player исключить)")]
        [SerializeField] private LayerMask _blockers = ~0;

        private InputAction _lookAction;
        private InputAction _aimAction;
        private CameraPose _pose;
        private CameraPose _blendFrom;
        private float _blendTime;
        private float _blendDuration = 1f;
        private float _yaw;
        private float _pitch;
        private bool _hasAim;
        private Vector3 _aimPoint;
        private float _aimHitDistance;
        private bool _snapPose = true;

        /// <summary>Текущий режим.</summary>
        public CameraMode Mode { get; private set; } = CameraMode.ThirdPerson;

        /// <summary>Смена режима — событие для локомоции, позы рук и локального вида.</summary>
        public event Action<CameraMode> ModeChanged;

        /// <summary>Корпус доворачивается по камере только в первом лице.
        /// В прицеле камера ведёт взгляд, а корпус идёт по WASD — как в обычном третьем лице.</summary>
        public bool FacesCamera => Mode == CameraMode.FirstPerson;

        /// <summary>Рыскание камеры, градусы.</summary>
        public float ViewYaw => _yaw;

        /// <summary>Направление взгляда камеры в мире.</summary>
        public Vector3 LookDirection
        {
            get
            {
                if (_camera != null)
                    return _camera.transform.forward;
                return _target != null ? _target.forward : Vector3.forward;
            }
        }

        /// <summary>Камера режимов — её же читает фейдер перекрытий.</summary>
        public Camera ViewCamera => _camera;

        /// <summary>Точка, в которую смотрит перекрестие, и дистанция до неё.</summary>
        public bool TryGetAim(out Vector3 point, out float distance)
        {
            point = _aimPoint;
            distance = _aimHitDistance;
            return _hasAim;
        }

        private void Awake()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = Camera.main;

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _lookAction = source.FindAction("Player/Look");
                _aimAction = source.FindAction("Player/Aim");
            }

            if (_target != null)
                _yaw = _target.eulerAngles.y;
            _pitch = _startPitch;

            if (_camera != null)
                _pose = new CameraPose(_camera.transform.position, _camera.transform.rotation);

            // Старт без перехода: блендинг считается завершённым, иначе первые
            // _blendDuration секунд камера летела бы из нуля (позиция по умолчанию
            // у _blendFrom) в верхнюю позу — старт игры выглядел как выезд камеры.
            _blendFrom = _pose;
            _blendTime = _blendDuration;
        }

        private void OnEnable()
        {
            _lookAction?.Enable();
            _aimAction?.Enable();
        }

        private void OnDisable()
        {
            _lookAction?.Disable();
            _aimAction?.Disable();
            UnlockCursor();
        }

        private void LateUpdate()
        {
            if (_target == null || _camera == null)
                return;

            UpdateLook();
            UpdateMode();

            var settled = _blendTime >= _blendDuration;
            var desired = ComputePose(Mode, settled);

            if (_snapPose)
            {
                // Первый кадр: камера встаёт в позу режима сразу. Иначе она «выезжает»
                // из позиции, сохранённой в сцене, и старт игры выглядит как переход.
                _pose = desired;
                _snapPose = false;
            }
            else if (settled)
            {
                _pose = desired;
            }
            else
            {
                _pose = ViewBlend.Lerp(
                    _blendFrom, desired, ViewBlend.Progress(_blendTime, _blendDuration));
            }

            _blendTime = Mathf.Min(_blendTime + Time.deltaTime, _blendDuration);

            _camera.transform.SetPositionAndRotation(_pose.Position, _pose.Rotation);
            UpdateAim();
        }

        /// <summary>Мышь ведёт обзор только в прицеле и в первом лице.</summary>
        private void UpdateLook()
        {
            if (Mode == CameraMode.ThirdPerson || _lookAction == null)
                return;

            var look = _lookAction.ReadValue<Vector2>();
            if (look.sqrMagnitude < 1e-6f)
                return;

            // Дельта указателя приходит за кадр, поэтому множитель без deltaTime.
            _yaw += look.x * _sensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * _sensitivity, _minPitch, _maxPitch);
        }

        private void UpdateMode()
        {
            // Гистерезис: пока игрок в доме, границы зоны считаются на _interiorExitMargin
            // шире. Иначе на выходе через дверь режим успевает мигнуть туда-обратно,
            // и камера дёргается ровно на пороге.
            var inside = InteriorZone.ContainsAny(
                _target.position,
                Mode == CameraMode.FirstPerson ? _interiorExitMargin : 0f);
            var wantsAim = _aimAction != null && _aimAction.IsPressed();
            var next = inside
                ? CameraMode.FirstPerson
                : wantsAim ? CameraMode.Aim : CameraMode.ThirdPerson;

            if (next == Mode)
                return;

            var previous = Mode;
            Mode = next;
            _blendFrom = _pose;
            _blendDuration = next == CameraMode.ThirdPerson ? _exitSeconds : _enterSeconds;
            _blendTime = 0f;

            // Выходя из третьего лица, начинаем обзор не с накопленного ранее рыскания.
            // В прицел смотрим в сторону курсора, в первое лицо — по развороту робота.
            if (previous == CameraMode.ThirdPerson)
            {
                if (next == CameraMode.Aim)
                    AimFromCursor();
                else
                {
                    _yaw = _target.eulerAngles.y;
                    _pitch = _startPitch;
                }
            }

            if (next == CameraMode.ThirdPerson)
                UnlockCursor();
            else
                LockCursor();

            Debug.Log($"[PlayerCameraRig] режим {previous} → {next}", this);
            ModeChanged?.Invoke(next);
        }

        /// <summary>
        /// Куда целиться в момент входа в прицел: в сторону курсора относительно робота.
        /// Иначе камера разворачивалась бы по последнему сохранённому рысканию и прицел
        /// начинался бы не там, где игрок держит мышь.
        /// </summary>
        private void AimFromCursor()
        {
            _pitch = _startPitch;

            var mouse = Mouse.current;
            if (mouse == null || _camera == null)
            {
                _yaw = _target.eulerAngles.y;
                return;
            }

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(Vector3.up, _target.position + Vector3.up * _aimLookHeight);
            if (!plane.Raycast(ray, out var enter))
            {
                _yaw = _target.eulerAngles.y;
                return;
            }

            var flat = ray.GetPoint(enter) - _target.position;
            flat.y = 0f;
            _yaw = flat.sqrMagnitude > 0.04f
                ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
                : _target.eulerAngles.y;
        }

        private CameraPose ComputePose(CameraMode mode, bool settled)
        {
            switch (mode)
            {
                case CameraMode.FirstPerson:
                    return FirstPersonPose();
                case CameraMode.Aim:
                    return AimPose();
                default:
                    return ThirdPersonPose(settled);
            }
        }

        private CameraPose ThirdPersonPose(bool settled)
        {
            var wanted = _target.position + _thirdPersonOffset;
            var position = wanted;
            if (settled)
            {
                // Сглаживание только в устоявшемся режиме: во время перехода позицию
                // ведёт блендинг, и две инерции спорили бы друг с другом.
                var t = 1f - Mathf.Exp(-_thirdPersonSmoothing * Time.deltaTime);
                position = Vector3.Lerp(_camera.transform.position, wanted, t);
            }

            var lookAt = _target.position + Vector3.up * _thirdPersonLookHeight;
            var rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
            return new CameraPose(position, rotation);
        }

        private CameraPose AimPose()
        {
            var pivot = _target.position + Vector3.up * _aimLookHeight;
            var flat = Quaternion.Euler(0f, _yaw, 0f);
            var offset = flat * _aimOffset;
            var position = pivot + offset * Correction(offset);
            return new CameraPose(position, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        /// <summary>
        /// Сфера от точки над плечом до желаемой позиции камеры. Возвращает долю пути,
        /// на которую можно отъехать, не залезая в стену (1 — не мешает ничего).
        /// </summary>
        private float Correction(Vector3 delta)
        {
            var distance = delta.magnitude;
            if (distance < 0.05f)
                return 1f;

            if (!Physics.SphereCast(
                    _target.position + Vector3.up * _aimLookHeight,
                    _aimCollisionRadius,
                    delta / distance,
                    out var hit,
                    distance,
                    _blockers,
                    QueryTriggerInteraction.Ignore))
            {
                return 1f;
            }

            return Mathf.Clamp01(Mathf.Max(0.15f, hit.distance - 0.02f) / distance);
        }

        private CameraPose FirstPersonPose()
        {
            var origin = _headBone != null
                ? _headBone.position
                : _target.position + Vector3.up * 1.65f;
            var flat = Quaternion.Euler(0f, _yaw, 0f);
            return new CameraPose(origin + flat * _headOffset, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        private void UpdateAim()
        {
            _hasAim = false;
            if (Mode != CameraMode.Aim)
                return;

            var ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, _aimDistance, _blockers, QueryTriggerInteraction.Ignore))
                return;

            _hasAim = true;
            _aimPoint = hit.point;
            _aimHitDistance = hit.distance;
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
