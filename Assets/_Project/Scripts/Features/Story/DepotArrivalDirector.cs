using System.Collections;
using FrostSpace.Core;
using FrostSpace.Features.Carry;
using FrostSpace.Features.Hud;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Quest;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Прибытие в депо (DP-01): поезд выходит из метели и встаёт у платформы,
    /// раздвигаются боковые створки, из проёма идёт очередь — R13, тележка, затем
    /// игрок. Всё это покрывает один непрерывный проезд камеры по платформу.
    ///
    /// проезд параметризован по длине дуги (<see cref="CameraTrack"/>): путевая скорость
    /// постоянна, а весь разгон и торможение задаёт одна глобальная кривая, поэтому
    /// кадр не может «то ускориться, то встать». Камера не облетает неподвижного героя:
    /// пока стоит субъект — стоит и камера, движение появляется вместе с тем, кто идёт.
    ///
    /// Игрок, R13 и тележка — дети вагона: пока поезд едет, они внутри и движутся с ним.
    /// Перед выходом их отцепляют, и дальше они идут по мировым координатам. Проём один
    /// (2.6 м), тележка 2.4 м поперёк — две полосы не помещаются. Поэтому очередь идёт
    /// паровозиком по одной линии с равной скоростью и равными интервалами: у каждого
    /// своя длительность пропорционально своему пути, обгона нет, и никто не проезжает
    /// сквозь никого.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class DepotArrivalDirector : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");

        /// <summary>Длительность проезда: он же момент возврата управления.</summary>
        public const float PathSeconds = 16.6f;

        /// <summary>Последние секунды: поза пути плавно садится в позу рига.</summary>
        public const float SettleSeconds = 2.2f;

        /// <summary>Высота точки взгляда над корнем актёра — уровень головы.</summary>
        public const float AimHeight = 1.25f;

        /// <summary>
        /// Точка, на которую наведён кадр, пока очередь не вышла: горловина проёма
        /// на уровне головы выходящих. Отметка держит головку пути напротив проёма
        /// (−16,29 м) плюс прежние 1,85 м над ней: раньше это было 1,25 м над
        /// перроном 0,60 м, теперь перрон грузовой (0,90 м), и цель едет вместе с ним.
        /// </summary>
        /// <remarks>
        /// Отсчёт здесь не от нуля сцены: депо — чанк клетки (−2,−2), и головка
        /// главного пути у перрона лежит на −16,29 м (числа держит
        /// `Tools/Blender/world_layout.py`). Все высоты кадра — отметка головки
        /// напротив этой точки плюс прежнее смещение над ней, поэтому кадр
        /// пережил и перенос депо в мир, и смену высоты перрона.
        /// </remarks>
        public static readonly Vector3 DefaultDoorAim = new(-3.20f, -14.193804f, -1.0f);

        /// <summary>Момент начала передачи цели от проёма к игроку, с от старта кадра.</summary>
        /// <remarks>
        /// 8,5 с: игрок выходит из проёма на 11,6-й, и к этому моменту цель уже его.
        /// Дольше держать взгляд на проёме нельзя: финальная поза рига стоит в 1,0 м
        /// от горловины, поэтому камера, которая всё ещё смотрит на проём, приезжает
        /// туда и видит стену вместо сцены.
        /// </remarks>
        public const float AimHandoffSeconds = 8.5f;

        /// <summary>
        /// Степень времени кадра: &gt;1 зажимает начало и переносит ход в конец.
        /// С 1,1 к четвёртой секунде камера проходит 9% дуги (0,7 м) — поезд стоит
        /// в кадре почти на месте, и к моменту выхода очереди камера всё ещё в воротах.
        /// </summary>
        public const float PathTimeExponent = 1.1f;

        /// <summary>Доля длины дуги на доле кадра: медленный старт, разворот к концу.</summary>
        public static float PathTime(float t01)
        {
            return CameraTrack.Ease(Mathf.Pow(Mathf.Clamp01(t01), PathTimeExponent));
        }

        /// <summary>
        /// Длительность передачи цели проём → игрок, с: кончается на 13,5 с, за
        /// три секунды до конца проезда.
        ///
        /// Раньше окно совпадало с концом проезда, и это было ловушкой: финальная
        /// поза рига стоит в 1,0 м от горловины, поэтому камера, которая до последней
        /// секунды смотрит на проём, приезжает в свою же точку прицела. Замер давал
        /// 1,3 м до цели и наклон 64° — на записи это два тёмных кадра подряд.
        /// Теперь поворот заканчивается, пока камера ещё в воротах, и последние
        /// секунды она едет за уже идущим игроком.
        /// </summary>
        public const float AimBlendSeconds = 5.0f;

        /// <summary>Предел, с какой скоростью за кадр может уйти целевая поза рига.</summary>
        private const float MaxSettleSpeed = 3.0f;

        /// <summary>То же для поворота: пределы щедрые, но мгновенный щелчок режут.</summary>
        private const float MaxSettleTurn = 60f;

        [Header("Ссылки")]
        [SerializeField] private RobotLocomotion _locomotion;
        [Tooltip("Корень игрока: после выхода обзор возвращается за спину")]
        [SerializeField] private Transform _player;
        [Tooltip("Риг камеры: на время прибытия ведёт сценовый кадр")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Вагон: R13, тележка и игрок — его дети")]
        [SerializeField] private Transform _train;
        [SerializeField] private Transform _doorLeft;
        [SerializeField] private Transform _doorRight;
        [Tooltip("R13 — сценовый актёр с Animator")]
        [SerializeField] private Transform _r13;
        [SerializeField] private Animator _r13Animator;
        [Tooltip("Тележка с четырьмя гнёздами")]
        [SerializeField] private Transform _cart;
        [SerializeField] private CarrySlot[] _slots = new CarrySlot[0];
        [SerializeField] private Carryable[] _cargo = new Carryable[0];
        [Tooltip("Полоса опыта: на время катсцены интерфейс не висит поверх кадра")]
        [SerializeField] private ExperienceBar _xpBar;

        [Header("Прибытие")]
        [Tooltip("Пауза перед началом, с")]
        [SerializeField] private float _startDelay = 0.1f;
        [Tooltip("Откуда выезжает вагон относительно места остановки, м")]
        [SerializeField] private Vector3 _trainStartOffset = new(0f, 0f, -44f);
        [Tooltip("Сколько вагон идёт до платформы, с")]
        [SerializeField] private float _trainSeconds = 3.6f;
        [Tooltip("Пауза после остановки, с")]
        [SerializeField] private float _doorSettle = 0.3f;
        [Tooltip("Сколько раздвигаются створки, с")]
        [SerializeField] private float _doorSeconds = 0.7f;
        [Tooltip("Насколько створка уезжает в сторону, м")]
        [SerializeField] private float _doorSlide = 1.35f;

        [Header("Очередь выхода")]
        [Tooltip("Точка внутри проёма в локальных координатах вагона")]
        [SerializeField] private Vector3 _doorInside = new(3.0f, 0f, 1.0f);
        [Tooltip("Точка снаружи проёма в локальных координатах вагона; y — уровень " +
                 "перрона: пол вагона лежит в 1,00 м над головкой рельса, грузовой " +
                 "перрон — в 0,90 м, значит со ступеньки вниз 0,10 м")]
        [SerializeField] private Vector3 _doorOutside = new(5.4f, -0.1f, 1.0f);
        [Tooltip("Куда уходит R13: из двери вперёд и налево (по ходу выхода), затем " +
                 "разворачивается вдоль состава — туда, откуда проезд начинался")]
        [SerializeField] private Vector3 _r13Exit = new(-0.8f, -15.573889f, 1.6f);
        [Tooltip("Туда же разворачивается корпус R13 после остановки")]
        [SerializeField] private Vector3 _r13Face = new(0.6f, -14.699259f, -8.6f);
        [Tooltip("Сколько R13 идёт, с. Скорость = путь/время, она согласована у всей очереди")]
        [SerializeField] private float _r13Seconds = 6.1f;
        [Tooltip("Скорость шага клипа Walk, м/с: Animator.speed подгоняется под путь")]
        [SerializeField] private float _r13ClipSpeed = 0.912f;
        [Tooltip("Скорость доворота корпуса по ходу, град/с")]
        [SerializeField] private float _r13TurnSpeed = 240f;
        [Tooltip("Задержка тележки после старта R13, с: она выезжает следом и не задевает " +
                 "его разворот у проёма")]
        [SerializeField] private float _cartDelay = 1.2f;
        [Tooltip("Куда выезжает тележка: строго по прямой из проёма, дальше R13 от борта")]
        [SerializeField] private Vector3 _cartExit = new(3.4f, -15.443804f, -1.0f);
        [Tooltip("Сколько тележка идёт, с")]
        [SerializeField] private float _cartSeconds = 8.2f;
        [Tooltip("Точка тележки перед проёмом в локальных координатах вагона")]
        [SerializeField] private Vector3 _cartApproach = new(1.0f, 0f, 1.0f);
        [Tooltip("Задержка игрока после старта тележки, с: он входит в проём, когда " +
                 "тележка уже его освободила")]
        [SerializeField] private float _playerDelay = 0.9f;
        [Tooltip("Куда выходит игрок: прямо из проёма, пару шагов, без поворота в бок. " +
                 "От этой точки считается поза рига")]
        [SerializeField] private Vector3 _playerExit = new(0.7f, -15.443804f, -1.0f);
        [Tooltip("Сколько игрок идёт до точки, с")]
        [SerializeField] private float _playerSeconds = 9.8f;
        [Tooltip("Скорость шага клипа игрока, м/с")]
        [SerializeField] private float _playerClipSpeed = 0.93f;
        [Tooltip("Скорость доворота игрока по ходу, град/с")]
        [SerializeField] private float _playerTurnSpeed = 180f;

        [Header("Камера прибытия")]
        [Tooltip("Один непрерывный проезд по длине дуги: точки опоры пути")]
        [SerializeField] private CameraKey[] _path = DefaultPath();
        [Tooltip("Длительность проезда, с")]
        [SerializeField] private float _seconds = PathSeconds;
        [Tooltip("Последние секунды: посадка в геймплейную позу рига, с")]
        [SerializeField] private float _settleSeconds = SettleSeconds;
        [Tooltip("Куда наведён кадр, пока очередь не вышла: горловина проёма")]
        [SerializeField] private Vector3 _doorAim = DefaultDoorAim;
        [Tooltip("С какого момента кадра цель переходит к игроку, с")]
        [SerializeField] private float _aimHandoffSeconds = AimHandoffSeconds;
        [Tooltip("Длительность перехода цели от проёма к игроку, с")]
        [SerializeField] private float _aimBlendSeconds = AimBlendSeconds;

        [Header("Звук")]
        [Tooltip("Ветер в депо: стартует под чёрным занавесом и остаётся после сцены")]
        [SerializeField] private AudioClip _wind;
        [SerializeField] private AudioClip _railRumble;
        [SerializeField] private AudioClip _brakes;
        [SerializeField] private AudioClip _doors;
        [SerializeField] private AudioClip _stepMetal;
        [SerializeField] private AudioClip _stepSnow;
        [SerializeField] private AudioClip _stopThud;
        [SerializeField] private AudioClip _cartHum;
        [SerializeField, Range(0f, 1f)] private float _windVolume = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _railVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] private float _cartVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float _oneShotVolume = 0.45f;

        private Animator _playerAnimator;
        private PathCurve _curve;
        private CameraPose _settlePose;
        private bool _settleHas;
        private float _baseFov = 50f;
        private AudioSource _windSource;
        private AudioSource _railSource;
        private AudioSource _cartSource;
        private AudioSource _oneShotSource;

        private void Awake()
        {
            if (_locomotion == null)
                _locomotion = FindAnyObjectByType<RobotLocomotion>();
            if (_player == null && _locomotion != null)
                _player = _locomotion.transform;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_r13Animator == null && _r13 != null)
                _r13Animator = _r13.GetComponentInChildren<Animator>(true);
            if (_playerAnimator == null && _player != null)
                _playerAnimator = _player.GetComponentInChildren<Animator>(true);
            if (_xpBar == null)
                _xpBar = FindAnyObjectByType<ExperienceBar>();

            if (_path == null || _path.Length == 0)
                _path = DefaultPath();
            _curve = new PathCurve(_path);

            _windSource = NewSource(true);
            _railSource = NewSource(true);
            _cartSource = NewSource(true);
            _oneShotSource = NewSource(false);

            if (_wind == null || _railRumble == null || _brakes == null ||
                _doors == null || _stepMetal == null || _stepSnow == null ||
                _stopThud == null || _cartHum == null)
            {
                Debug.LogWarning(
                    "[DepotArrivalDirector] не все клипы прибытия назначены: часть звука будет молчать",
                    this);
            }
        }

        private void Start()
        {
            // Сохранение переезжает в депо (ADR 0031): «Продолжить» должно вести
            // сюда, а не в уже пройденный вагон.
            ProgressSave.MarkScene(ProgressSave.FrontierScene);

            // Ветер стартует под чёрным занавесом: к проявлению депо он уже звучит,
            // иначе первый кадр метели снова «немой».
            PlayWind();
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            // Вагон живёт в префабе клетки депо, игрок — сценовый объект: префаб
            // не может ссылаться на сцену, поэтому на время проезда игрок снова
            // становится ребёнком вагона. Иначе состав уехал бы без него, а
            // CharacterController остался бы стоять на платформе.
            if (_player != null && _train != null && !_player.IsChildOf(_train))
                _player.SetParent(_train, true);

            _locomotion?.SetControlLock(true);

            // Игрок едет внутри вагона. Пока локомоция жива, она каждый кадр
            // двигает CharacterController гравитацией, и на ходу вагона его
            // выбивает из кузова. Отключаем её до конца заставки.
            if (_locomotion != null)
                _locomotion.enabled = false;

            BeginShot();
            LoadCargo();

            var trainRest = _train != null ? _train.position : Vector3.zero;
            if (_train != null)
                _train.position = trainRest + _trainStartOffset;

            var camera = _rig != null ? _rig.ViewCamera : Camera.main;

            // Проезд стартует вместе с проявлением из чёрного: первые секунды
            // поезд идёт в метели за кадром, и занавес открывается уже на движении.
            WarnIfActorsOutrunPath();
            var cameraRoutine = StartCoroutine(CameraPath(camera));

            yield return Wait(_startDelay);

            if (_train != null)
            {
                StartCoroutine(RailRoutine());
                StartCoroutine(BrakesRoutine());
                yield return MoveTrain(_train, trainRest + _trainStartOffset, trainRest, _trainSeconds);
                PlayOneShot(_stopThud);
            }

            yield return Wait(_doorSettle);
            PlayOneShot(_doors);
            yield return OpenDoors();

            DetachCouriers();

            var doorInside = DoorPoint(_doorInside);
            var doorOutside = DoorPoint(_doorOutside);

            // R13 идёт первым и встаёт в стороне от дорожки тележки.
            if (_r13 != null)
                StartCoroutine(ExitR13(doorInside, doorOutside));

            yield return Wait(_cartDelay);

            if (_cart != null)
            {
                StartCoroutine(CutsceneMover.Walk(_cart,
                    new[] { _cart.position, DoorPoint(_cartApproach), doorOutside, _cartExit },
                    _cartSeconds));
                StartCoroutine(CartHumRoutine());
            }
            yield return Wait(_playerDelay);

            // Игрок выходит последним: проём один, тележка шире полосы. Мировая
            // позиция сохраняется, когда его отцепляют от остановившегося вагона.
            var playerWalk = default(Coroutine);
            if (_player != null)
            {
                DetachPlayer();
                var route = BuildRoute(_player.position, doorInside, doorOutside, _playerExit);
                playerWalk = StartCoroutine(CutsceneMover.Walk(_player, route,
                    _playerSeconds, _playerAnimator, _playerClipSpeed, _playerTurnSpeed));
            }

            if (cameraRoutine != null)
                yield return cameraRoutine;
            if (playerWalk != null)
                yield return playerWalk;

            // Страховка: если корутина R13 была прервана, Idle всё равно включится.
            _r13Animator?.SetBool(MovingId, false);

            if (_locomotion != null)
                _locomotion.enabled = true;

            ReleaseShot(camera);

            // Тема возвращается ровно в момент передачи управления — как «можно идти».
            MusicManager.Instance?.SetDuck(1f, 1f);
            _locomotion?.SetControlLock(false);
        }

        /// <summary>
        /// Актёры длиннее проезда — кто-то ещё в проёме к возврату управления.
        /// Игрок обязан закончить ровно на конце проезда: на этом держится
        /// бесшовная посадка камеры в геймплейную позу.
        /// </summary>
        private void WarnIfActorsOutrunPath()
        {
            var total = _seconds;
            var doorsAt = _startDelay + _trainSeconds + _doorSettle + _doorSeconds;
            var r13At = doorsAt + _r13Seconds;
            var cartAt = doorsAt + _cartDelay + _cartSeconds;
            var playerAt = doorsAt + _cartDelay + _playerDelay + _playerSeconds;

            if (r13At > total)
                Debug.LogWarning(string.Format(
                    "[DepotArrivalDirector] R13 заканчивает на {0:0.00} с, а проезд — {1:0.00} с",
                    r13At, total), this);

            if (cartAt > total)
                Debug.LogWarning(string.Format(
                    "[DepotArrivalDirector] тележка заканчивает на {0:0.00} с, а проезд — {1:0.00} с",
                    cartAt, total), this);

            if (!Mathf.Approximately(playerAt, total))
                Debug.LogWarning(string.Format(
                    "[DepotArrivalDirector] игрок заканчивает на {0:0.00} с, проезд — {1:0.00} с: " +
                    "поза рига на финальном ключе разойдётся с живой", playerAt, total), this);
        }

        /// <summary>
        /// Маршрут выхода: место в вагоне → горловина проёма → точка на платформе.
        /// Всё по одной прямой, без поворота в бок: направление кадра наследует движение
        /// идущего, а угол в маршруте дал бы мгновенную смену скорости рыскания.
        /// Здесь же — источник истины для теста плавности.
        /// </summary>
        public static Vector3[] BuildRoute(
            Vector3 start, Vector3 doorInside, Vector3 doorOutside, Vector3 exit)
        {
            return new[] { start, doorInside, doorOutside, exit };
        }

        /// <summary>Точка в локальных координатах вагона — в мировые.</summary>
        private Vector3 DoorPoint(Vector3 local)
        {
            return _train != null ? _train.TransformPoint(local) : local;
        }

        /// <summary>Отцепляет игрока от вагона, сохраняя мировую позицию.</summary>
        private void DetachPlayer()
        {
            if (_player == null || _train == null || !_player.IsChildOf(_train))
                return;

            _player.SetParent(null, true);
        }

        private void LoadCargo()
        {
            var count = Mathf.Min(_slots.Length, _cargo.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = _slots[i];
                var item = _cargo[i];
                if (slot == null || item == null || !slot.IsFree)
                    continue;

                item.Take(slot.transform);
                slot.TryPlace(item);
            }
        }

        /// <summary>Створки разъезжаются в стороны от центра проёма одновременно.</summary>
        private IEnumerator OpenDoors()
        {
            if (_doorLeft == null || _doorRight == null)
                yield break;

            var leftFrom = _doorLeft.localPosition;
            var rightFrom = _doorRight.localPosition;
            var leftTo = leftFrom + Vector3.forward * _doorSlide;
            var rightTo = rightFrom - Vector3.forward * _doorSlide;

            // Обе створки стартуют в одном кадре и идут с одной длительностью:
            // последовательный запуск давал рывок правой.
            var left = StartCoroutine(MoveDoor(_doorLeft, leftFrom, leftTo, _doorSeconds));
            yield return MoveDoor(_doorRight, rightFrom, rightTo, _doorSeconds);
            yield return left;
        }

        /// <summary>
        /// Вернуть спутников в клетку депо. R13 и тележка созданы для депо и
        /// остаются его содержимым: корень клетки — родитель вагона (вагон лежит
        /// в префабе). Отцепка в корень сцены вынула бы их из чанка, и при
        /// выгрузке депо они остались бы висеть в пустоте.
        /// </summary>
        private void DetachCouriers()
        {
            var cell = _train != null ? _train.parent : null;
            if (_cart != null)
                _cart.SetParent(cell, true);
            if (_r13 != null)
                _r13.SetParent(cell, true);
        }

        /// <summary>
        /// Сценовый кадр на время прибытия: риг не переписывает позу, поэтому
        /// случайное движение мыши не сбивает обзор. Камера сразу встаёт в начало
        /// пути — под занавесом это не видно, зато нет выезда из позы, сохранённой
        /// в сцене.
        /// </summary>
        private void BeginShot()
        {
            if (_rig == null)
                return;

            _rig.SetCinematic(true);
            var camera = _rig.ViewCamera;
            if (camera == null)
                return;

            _baseFov = camera.fieldOfView;
            ApplyPose(camera, FramePose(0f), CameraTrack.FovAlong(_path, 0f));
            _xpBar?.SetVisible(false, 0.4f);
        }

        /// <summary>
        /// Заканчивает проезд ровно в позе, которую посчитал риг, и передаёт
        /// управление без скачка: последний кадр проезда и есть геймплейная поза.
        /// </summary>
        private void ReleaseShot(Camera camera)
        {
            if (camera != null)
                camera.fieldOfView = _baseFov;

            _xpBar?.SetVisible(true, 0.6f);

            if (_rig == null)
                return;

            var yaw = _player != null ? _player.eulerAngles.y : 0f;
            var settle = _rig.GetThirdPersonPose(yaw, _rig.StartPitch);

            if (camera != null)
            {
                // Один кадр расхождения — норма; больше — рывок, который видно.
                Debug.Log(string.Format(
                    "[DepotArrivalDirector] передача управления: {0:0.000} м, {1:0.00}°",
                    Vector3.Distance(settle.Position, camera.transform.position),
                    Quaternion.Angle(settle.Rotation, camera.transform.rotation)), this);
            }

            _rig.ReleaseCinematic(yaw, _rig.StartPitch, 0.01f);
        }

        /// <summary>
        /// Один непрерывный проезд: метры берутся из одной глобальной кривой времени,
        /// поэтому путевая скорость постоянна по построению. Последние
        /// <see cref="_settleSeconds"/> поза пути садится в живую позу рига; игрок к
        /// этому моменту тоже стоит, и обе стороны заканчивают со скоростью ноль.
        /// </summary>
        private IEnumerator CameraPath(Camera camera)
        {
            if (camera == null || _path == null || _path.Length == 0)
                yield break;

            var settleFrom = Mathf.Max(0f, _seconds - _settleSeconds);
            var elapsed = 0f;

            while (elapsed < _seconds)
            {
                elapsed += Time.deltaTime;
                ApplyPose(camera, PoseAlong(elapsed, settleFrom),
                    CameraTrack.FovAlong(_path, elapsed / _seconds));
                yield return null;
            }

            ApplyPose(camera, PoseAlong(_seconds, settleFrom),
                CameraTrack.FovAlong(_path, 1f));
        }

        private CameraPose PoseAlong(float elapsed, float settleFrom)
        {
            var pose = FramePose(elapsed);

            if (_rig == null || elapsed <= settleFrom)
                return pose;

            var k = Mathf.Clamp01((elapsed - settleFrom) / Mathf.Max(0.01f, _settleSeconds));
            return ViewBlend.Lerp(pose, SettleTarget(), k);
        }

        /// <summary>
        /// Живая геймплейная поза камеры с ограничителем скорости.
        ///
        /// <see cref="PlayerCameraRig.GetThirdPersonPose"/> включает Correction(): сфера
        /// может упереться в борт вагона и отпустить его за один кадр, и тогда целевая
        /// поза меняется на несколько сантиметров мгновенно. Без ограничитель передача
        /// управления наследовала этот щелчок — замер в Play Mode давал 21 м/с и 400°/с
        /// ровно на одном кадре. Реальному проезду лимит не мешает: designed пик 1,2 м/с
        /// и 26°/с, лимиты в разы щедрее.
        /// </summary>
        private CameraPose SettleTarget()
        {
            var yaw = _player != null ? _player.eulerAngles.y : 0f;
            var wanted = _rig.GetThirdPersonPose(yaw, _rig.StartPitch);

            if (!_settleHas)
            {
                _settleHas = true;
                _settlePose = wanted;
                return wanted;
            }

            var dt = Mathf.Clamp(Time.deltaTime, 0.0005f, 0.1f);
            var step = wanted.Position - _settlePose.Position;
            var limit = MaxSettleSpeed * dt;

            if (step.sqrMagnitude > limit * limit)
                step = step.normalized * limit;

            var position = _settlePose.Position + step;
            var rotation = Quaternion.RotateTowards(
                _settlePose.Rotation, wanted.Rotation, MaxSettleTurn * dt);

            _settlePose = new CameraPose(position, rotation);
            return _settlePose;
        }

        /// <summary>
        /// Кадр проезда на заданном времени: позиция с пути, а направление — за
        /// субъектом. Пока очередь не вышла, кадр наведён на горловину проёма; игрок
        /// идёт через неё, поэтому передача цели непрерывна и по положению, и по
        /// скорости. Так рыскание не получает пинка на стыках ключей, каким бы ни было
        /// их распределение по пути.
        ///
        /// Окно передачи намеренно длинное: весь геометрический разброс направлений
        /// кадра (~100°) никуда не денешь, поэтому он размазан по половине проезда,
        /// а не сжат в несколько секунд.
        /// </summary>
        public static CameraPose PoseAt(
            PathCurve curve, float seconds, float elapsed,
            Vector3 doorwayAim, Vector3 playerPosition,
            float aimHandoffSeconds, float aimBlendSeconds)
        {
            var t01 = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds));
            var position = curve.PointAtMeters(PathTime(t01) * curve.TotalMeters);
            var head = playerPosition + Vector3.up * AimHeight;
            var k = CameraTrack.Ease(
                (elapsed - aimHandoffSeconds) / Mathf.Max(0.01f, aimBlendSeconds));

            return new CameraPose(
                position, CameraTrack.Aim(position, Vector3.Lerp(doorwayAim, head, k)));
        }

        /// <summary>Кадр на текущем времени из сериализованных настроек сцены.</summary>
        public CameraPose FramePose(float elapsed)
        {
            return PoseAt(_curve, _seconds, elapsed, _doorAim,
                _player != null ? _player.position : _doorAim,
                _aimHandoffSeconds, _aimBlendSeconds);
        }

        private static void ApplyPose(Camera camera, CameraPose pose, float fov)
        {
            if (camera == null)
                return;

            camera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            camera.fieldOfView = fov;
        }

        private AudioSource NewSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        private void PlayWind()
        {
            if (_wind == null || _windSource == null)
                return;

            _windSource.clip = _wind;
            _windSource.volume = 0f;
            _windSource.Play();
            StartCoroutine(FadeSource(_windSource, 0f, _windVolume, 1.5f));
        }

        /// <summary>Гул рельсов нарастает вместе с ходом и глохнет к остановке.</summary>
        private IEnumerator RailRoutine()
        {
            if (_railRumble == null || _railSource == null)
                yield break;

            _railSource.clip = _railRumble;
            _railSource.volume = 0f;
            _railSource.Play();

            yield return FadeSource(_railSource, 0f, _railVolume, 0.8f);
            yield return Wait(Mathf.Max(0f, _trainSeconds - 2.0f));
            yield return FadeSource(_railSource, _railVolume, 0f, 1.2f);
            _railSource.Stop();
        }

        private IEnumerator BrakesRoutine()
        {
            yield return Wait(Mathf.Max(0f, _trainSeconds - 1.6f));
            PlayOneShot(_brakes, 1f);
        }

        private IEnumerator CartHumRoutine()
        {
            if (_cartHum == null || _cartSource == null)
                yield break;

            _cartSource.clip = _cartHum;
            _cartSource.volume = 0f;
            _cartSource.Play();

            yield return FadeSource(_cartSource, 0f, _cartVolume, 0.6f);
            yield return Wait(Mathf.Max(0f, _cartSeconds - 1.4f));
            yield return FadeSource(_cartSource, _cartVolume, 0f, 0.8f);
            _cartSource.Stop();
        }

        /// <summary>
        /// Шаги идут по времени маршрута, а не по фактическому пути: клип Walk
        /// уже подогнан под скорость, и шаг не скользит. Первую треть пути —
        /// металл проёма, дальше — снег платформы.
        /// </summary>
        private IEnumerator StepRoutine(float seconds)
        {
            const float interval = 0.52f;
            var metalUntil = seconds * 0.45f;
            var elapsed = 0f;

            while (elapsed + interval <= seconds)
            {
                yield return Wait(interval);
                elapsed += interval;

                var clip = elapsed <= metalUntil ? _stepMetal : _stepSnow;
                PlayOneShot(clip, 0.75f, Random.Range(0.94f, 1.06f));
            }
        }

        private void PlayOneShot(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
        {
            if (clip == null || _oneShotSource == null)
                return;

            _oneShotSource.pitch = pitch;
            _oneShotSource.PlayOneShot(clip, _oneShotVolume * volumeScale);
        }

        private static IEnumerator FadeSource(
            AudioSource source, float from, float to, float seconds)
        {
            if (source == null)
                yield break;

            if (seconds <= 0f)
            {
                source.volume = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            source.volume = to;
        }

        /// <summary>
        /// Кадр прибытия. Старт — (0,5; 3,0; −10,0): высоко и издалека, за рядом
        /// охраны, по слову владельца. Прежний старт в 1,5 м над платформой начинался
        /// «в ногах» у стражников. Дуга входит в щель между их корпусами (x от −1,95
        /// до 0,15 при z ≈ −4,9) на высоте 2,9 м — выше их голов, поэтому пролёт
        /// читается как между роботами, а не сквозь одного.
        ///
        /// Форма и время подобраны моделью Temp/shot_model.py: она держит те же восемь
        /// метрик, что <c>CameraTrackTests</c>, и добавляет три проверки кадра, которых
        /// в числах движения не видно, — расстояние до точки прицела, наклон взгляда и
        /// зазор до идущей очереди. Дуга 7,4 м: к 4 с камера проходит 0,7 м (9%), пока
        /// приходит поезд; к 7 с — 2,7 м (36%), пока выходит очередь; дальше разворот
        /// цели закончен и камера едет за игроком. Пик хода 0,86 м/с при пределе
        /// 1,10, рыскание 32°/с при пределе 40, наклон кадра не круче 25°, до цели не
        /// ближе 2,4 м.
        ///
        /// Восточная сторона дуги выбрана не для красоты: правая створка уезжает в
        /// сторону (−3,84; −2,98) и остаётся стоять ровно на подходе к проёму. Прежний
        /// финал в (−2,10; −3,20) проходил от неё в 1,7 м, и на 12-й секунде половина
        /// кадра была закрыта серым щитом. Здесь до створки 2,4 м.
        ///
        /// Последний ключ стоит в 1,4 м ОТ живой позы рига, а не в ней: игрок проходит
        /// точку, где камера встанет ему за спину, на 13,6 с — ровно за три секунды до
        /// конца. Если приехать туда раньше, он идёт сквозь кадр; поэтому последние
        /// секунды дистанцию сводит штатный <see cref="SettleSeconds"/>, у которого и
        /// скорость, и нулевой подход на финише уже ограничены.
        ///
        /// Отметки — абсолютные, в осях сцены: депо стало чанком клетки (−2,−2), и
        /// головка главного пути у платформы лежит на −16,3 м, а не на нуле. Каждый
        /// ключ — это головка напротив своей точки плюс та же высота над платформой,
        /// что была в принятом кадре, поэтому перенос депо в мир не тронул ни
        /// композицию, ни метры дуги.
        /// </summary>
        /// <summary>
        /// Ключи проезда. Высота всех точек поднята на 0,30 м вместе с грузовым
        /// перроном, а первые две опущены ещё на 0,25 м и сдвинуты к составу:
        /// начало кадра идёт низко вдоль борта, закрытый проём занимает центр
        /// кадра, и двор открывается в момент, когда створки разъезжаются.
        /// К западу дальше -2,15 м камера не уходит: `CameraTrackTests` бережёт
        /// полтора метра до борта вагона, и сжимать их ради кадра нельзя.
        /// </summary>
        public static CameraKey[] DefaultPath()
        {
            return new[]
            {
                new CameraKey
                {
                    Position = new Vector3(0.05f, -12.888041f, -10.00f), Fov = 44f
                },
                new CameraKey
                {
                    Position = new Vector3(-0.55f, -12.986750f, -8.20f), Fov = 45f
                },
                new CameraKey
                {
                    Position = new Vector3(-1.15f, -12.855459f, -6.40f), Fov = 46f
                },
                new CameraKey
                {
                    Position = new Vector3(-1.55f, -12.956677f, -5.00f), Fov = 47f
                },
                new CameraKey
                {
                    Position = new Vector3(-1.90f, -13.054778f, -3.90f), Fov = 49f
                },
                new CameraKey
                {
                    Position = new Vector3(-2.15f, -13.148741f, -2.90f), Fov = 50f
                }
            };
        }

        /// <summary>Вагон тормозит: скорость падает к концу пути.</summary>
        private static IEnumerator MoveTrain(
            Transform target, Vector3 from, Vector3 to, float seconds)
        {
            if (seconds <= 0f)
            {
                target.position = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = 1f - (1f - t) * (1f - t);
                target.position = Vector3.LerpUnclamped(from, to, eased);
                yield return null;
            }

            target.position = to;
        }

        /// <summary>
        /// Ведёт R13 по маршруту и снимает Moving сразу по прибытии, не дожидаясь
        /// тележки: иначе Walk остаётся включённым, пока тележка едет следом.
        /// </summary>
        private IEnumerator ExitR13(Vector3 doorInside, Vector3 doorOutside)
        {
            // Параметр ходьбы ведёт CutsceneMover: он же поднимает его игроку.
            StartCoroutine(StepRoutine(_r13Seconds));
            yield return CutsceneMover.Walk(_r13,
                BuildRoute(_r13.position, doorInside, doorOutside, _r13Exit),
                _r13Seconds, _r13Animator, _r13ClipSpeed, _r13TurnSpeed);

            // Встал и разворачивается вдоль состава — туда, откуда начинался проезд.
            yield return FaceTowards(_r13, _r13Face);
        }

        /// <summary>Медленный доворот корпуса на точку после остановки.</summary>
        private static IEnumerator FaceTowards(Transform target, Vector3 point)
        {
            if (target == null)
                yield break;

            var flat = point - target.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                yield break;

            var wanted = Quaternion.LookRotation(flat.normalized, Vector3.up);

            while (Quaternion.Angle(target.rotation, wanted) > 0.5f)
            {
                target.rotation = Quaternion.RotateTowards(
                    target.rotation, wanted, 40f * Time.deltaTime);
                yield return null;
            }

            target.rotation = wanted;
        }

        private static IEnumerator MoveDoor(
            Transform target, Vector3 from, Vector3 to, float seconds)
        {
            if (seconds <= 0f)
            {
                target.localPosition = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                target.localPosition = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.localPosition = to;
        }

        private static IEnumerator Wait(float seconds)
        {
            if (seconds <= 0f)
                yield break;

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
