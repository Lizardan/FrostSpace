using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Единственный владелец главной камеры: три режима и плавный переход между ними.
    ///
    /// <list type="bullet">
    /// <item><b>ThirdPerson</b> — основной вид из-за плеча: мышь ведёт рыскание и тангаж,
    /// WASD читается от камеры, голова робота доворачивается к перекрестию
    /// (<c>PlayerAimPose</c>). Правая кнопка мыши больше не меняет позу — она только
    /// включает эффект выбранного модуля (просвет или сканер).</item>
    /// <item><b>FirstPerson</b> — внутри <see cref="InteriorZone"/>: камера встаёт в голову
    /// робота, WASD работает стрейфом.</item>
    /// <item><b>Ride</b> — пока игрок на ховербайке: камера сверху-сзади над байком,
    /// мышь обзор не крутит, направление задаёт сам байк.</item>
    /// </list>
    ///
    /// Поза считается аналитически каждый кадр, а смена режима запоминает текущую позу и
    /// перетекает к новой за <c>_enterSeconds</c> (вход) или <c>_exitSeconds</c> (возврат).
    /// Курсор захвачен во всех игровых режимах: перекрестие всегда в центре экрана.
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

        [Header("Третье лицо (основной вид)")]
        [Tooltip("Смещение камеры от точки над плечом: назад и вбок, в локальных осях рыскания")]
        [SerializeField] private Vector3 _aimOffset = new(0.6f, 0.35f, -3.1f);
        [Tooltip("Высота центра орбиты в персонаже: камера облетает именно эту точку")]
        [SerializeField] private float _aimLookHeight = 1.25f;
        [Tooltip("Радиус сферы, которой камера отводится от стен")]
        [SerializeField] private float _aimCollisionRadius = 0.25f;
        [Tooltip("Максимальная дальность луча из перекрестия, м")]
        [SerializeField] private float _aimDistance = 60f;

        [Header("Первое лицо")]
        [Tooltip("Смещение камеры от кости головы, м")]
        [SerializeField] private Vector3 _headOffset = new(0f, 0.12f, 0.08f);

        [Header("Езда")]
        [Tooltip("Высота камеры над байком, м")]
        [SerializeField] private float _rideHeight = 7.5f;
        [Tooltip("Насколько камера отстаёт назад от байка, м")]
        [SerializeField] private float _rideDistance = 5.5f;
        [Tooltip("Наклон камеры вниз при езде, градусы")]
        [SerializeField] private float _ridePitch = 52f;
        [Tooltip("Фиксированный курс вида сверху, градусы: камера не крутится за байком, верх экрана всегда смотрит сюда")]
        [SerializeField] private float _rideViewYaw;

        [Header("Переходы")]
        [Tooltip("Сколько камера едет в первый/третий режим по требованию, с")]
        [SerializeField] private float _enterSeconds = 0.22f;
        [Tooltip("Сколько камера возвращается в третий вид, с")]
        [SerializeField] private float _exitSeconds = 0.38f;
        [Tooltip("Насколько за стену надо выйти, чтобы первое лицо отпустило, м. Без этого запаса режим мигает точно на плоскости стены, когда игрок выбегает из дома")]
        [SerializeField] private float _interiorExitMargin = 0.5f;

        [Header("Обзор")]
        [Tooltip("Градусов поворота на пиксель движения мыши")]
        [SerializeField] private float _sensitivity = 0.12f;
        [Tooltip("Предел взгляда вверх: с орбитой камера физически опускается, и ниже -35° она уходит в землю")]
        [SerializeField] private float _minPitch = -35f;
        [Tooltip("Предел взгляда вниз: с орбитой камера поднимается над персонажем")]
        [SerializeField] private float _maxPitch = 70f;
        [Tooltip("Тангаж по умолчанию: камера смотрит чуть вниз, на робота")]
        [SerializeField] private float _startPitch = 10f;
        [Tooltip("Что останавливает камеру и луч из перекрестия (слой Player исключить)")]
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
        private Transform _rideTarget;

        /// <summary>Текущий режим.</summary>
        public CameraMode Mode { get; private set; } = CameraMode.ThirdPerson;

        /// <summary>Смена режима — событие для локомоции, позы рук и локального вида.</summary>
        public event Action<CameraMode> ModeChanged;

        /// <summary>Корпус доворачивается по камере только в первом лице.
        /// В третьем лице камера ведёт взгляд, а корпус идёт по WASD.</summary>
        public bool FacesCamera => Mode == CameraMode.FirstPerson;

        /// <summary>Правая кнопка мыши удерживается: включаются эффекты выбранного модуля.</summary>
        public bool IsAiming => Mode == CameraMode.ThirdPerson &&
                                _aimAction != null && _aimAction.IsPressed();

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
        /// <summary>Кость головы: от неё сканер выпускает конус сканирования.</summary>
        public Transform HeadBone => _headBone;


        /// <summary>Объект, вокруг которого строится вид сверху (ховербайк).</summary>
        public Transform RideTarget => _rideTarget;

        /// <summary>
        /// Курс, в котором смотрит вид сверху. По нему байк читает WASD: W — вверх
        /// по экрану, A — влево, S — вниз, D — вправо.
        /// </summary>
        public float RideViewYaw => _rideViewYaw;

        /// <summary>Точка, в которую смотрит перекрестие, и дистанция до неё.</summary>
        public bool TryGetAim(out Vector3 point, out float distance)
        {
            point = _aimPoint;
            distance = _aimHitDistance;
            return _hasAim;
        }

        /// <summary>Посадка на транспорт: камера уходит в вид сверху над ним.</summary>
        public void SetRideTarget(Transform rideTarget)
        {
            _rideTarget = rideTarget;
        }

        /// <summary>Сход с транспорта: камера возвращается в третий вид.</summary>
        public void ClearRideTarget()
        {
            _rideTarget = null;
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

            LockCursor();
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

        /// <summary>Мышь ведёт обзор в третьем и первом лице; вид сверху ведёт сам байк.</summary>
        private void UpdateLook()
        {
            if (Mode == CameraMode.Ride || _lookAction == null)
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
            var inside = _rideTarget == null && InteriorZone.ContainsAny(
                _target.position,
                Mode == CameraMode.FirstPerson ? _interiorExitMargin : 0f);

            CameraMode next;
            if (_rideTarget != null)
                next = CameraMode.Ride;
            else if (inside)
                next = CameraMode.FirstPerson;
            else
                next = CameraMode.ThirdPerson;

            if (next == Mode)
                return;

            var previous = Mode;
            Mode = next;
            _blendFrom = _pose;
            _blendDuration = next == CameraMode.ThirdPerson ? _exitSeconds : _enterSeconds;
            _blendTime = 0f;

            // Выходя из вида сверху или из первого лица, начинаем обзор по развороту
            // корпуса: камера третьего лица смотрит туда же, куда стоит робот.
            if (previous != CameraMode.ThirdPerson && next == CameraMode.ThirdPerson)
            {
                _yaw = _target.eulerAngles.y;
                _pitch = _startPitch;
            }

            LockCursor();

            Debug.Log($"[PlayerCameraRig] режим {previous} → {next}", this);
            ModeChanged?.Invoke(next);
        }

        private CameraPose ComputePose(CameraMode mode, bool settled)
        {
            switch (mode)
            {
                case CameraMode.FirstPerson:
                    return FirstPersonPose();
                case CameraMode.Ride:
                    return RidePose();
                default:
                    return ThirdPersonPose();
            }
        }

        /// <summary>
        /// Основной вид: камера висит за плечом и смотрит по мыши. Сферокаст от точки
        /// над плечом не даёт ей заехать в стену — иначе у стены камера оказывалась бы
        /// внутри геометрии и перекрытие шумело бы на весь экран.
        ///
        /// Тангаж поворачивает не только поворот камеры, но и всё смещение от точки
        /// орбиты: камера облетает центр в персонаже, а не крутится на месте. Иначе
        /// при взгляде вверх/вниз точка вращения оказывалась за спиной робота.
        /// </summary>
        private CameraPose ThirdPersonPose()
        {
            var pivot = _target.position + Vector3.up * _aimLookHeight;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var offset = rotation * _aimOffset;
            var position = pivot + offset * Correction(offset, pivot);
            return new CameraPose(position, rotation);
        }

        /// <summary>
        /// Вид сверху над байком: камера едет за ним по позиции, но стоит на месте
        /// по повороту — курс фиксирован (<c>_rideViewYaw</c>), поэтому корпус байка
        /// разворачивается под неподвижным верхом экрана.
        /// </summary>
        private CameraPose RidePose()
        {
            var pivot = _rideTarget.position + Vector3.up * 0.6f;
            var flat = Quaternion.Euler(0f, _rideViewYaw, 0f);
            var offset = flat * new Vector3(0f, _rideHeight, -_rideDistance);
            var position = pivot + offset * Correction(offset, pivot);
            return new CameraPose(position, Quaternion.Euler(_ridePitch, _rideViewYaw, 0f));
        }

        /// <summary>
        /// Сфера от точки над плечом до желаемой позиции камеры. Возвращает долю пути,
        /// на которую можно отъехать, не залезая в стену (1 — не мешает ничего).
        /// </summary>
        private float Correction(Vector3 delta, Vector3 pivot)
        {
            var distance = delta.magnitude;
            if (distance < 0.05f)
                return 1f;

            if (!Physics.SphereCast(
                    pivot,
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

        /// <summary>
        /// Точка в перекрестии. Считается в третьем лице каждый кадр: по ней доворачивается
        /// голова робота, по ней же работают просвет и сканер.
        /// </summary>
        private void UpdateAim()
        {
            _hasAim = false;
            if (Mode != CameraMode.ThirdPerson)
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
