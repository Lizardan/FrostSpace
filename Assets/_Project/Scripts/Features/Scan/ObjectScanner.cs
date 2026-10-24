using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using FrostSpace.Core;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Scan
{
    /// <summary>Что сканер прочитал с цели: имя, категория, точка подписи и дистанция.</summary>
    public readonly struct ScanView
    {
        public readonly ScanTarget Target;
        public readonly string Name;
        public readonly string Category;
        public readonly Vector3 Anchor;
        public readonly Bounds Bounds;
        public readonly float Distance;

        /// <summary>Яркость контура 0..1 — HUD гасит подпись вместе с ним.</summary>
        public readonly float Strength;

        /// <summary>Прогресс полосы скана 0..1.</summary>
        public readonly float Progress;

        /// <summary>Цель попала в область прицела.</summary>
        public readonly bool Aim;

        /// <summary>Цель ближайшая к игроку.</summary>
        public readonly bool Nearest;

        /// <summary>Цель попала в круговой обзор по Q.</summary>
        public readonly bool Sonar;

        public ScanView(
            ScanTarget target, string name, string category, Vector3 anchor, Bounds bounds,
            float distance, float strength, float progress, bool aim, bool nearest, bool sonar)
        {
            Target = target;
            Name = name;
            Category = category;
            Anchor = anchor;
            Bounds = bounds;
            Distance = distance;
            Strength = strength;
            Progress = progress;
            Aim = aim;
            Nearest = nearest;
            Sonar = sonar;
        }
    }

    /// <summary>
    /// Сканер — переключаемая способность на Q плюс два постоянных канала детекции.
    ///
    /// <list type="bullet">
    /// <item><b>Q</b> включает режим сонара: все цели в радиусе получают контур, а от
    /// игрока по земле расходится кольцо (<see cref="ScanPulse"/>).</item>
    /// <item><b>Ближайшая цель</b> подписывается сама, без Q, в радиусе
    /// <see cref="_nearestRadius"/>.</item>
    /// <item><b>Цель в области прицела</b> подписывается сама: не лучом, а сеткой лучей
    /// по квадрату <see cref="_scanAreaHalf"/> — объект читается, даже когда перекрестие
    /// смотрит рядом с ним.</item>
    /// </list>
    ///
    /// Цели берутся только из реестра <see cref="ScanTarget.All"/>: произвольная геометрия
    /// (снег, стены, рельеф) больше не подписывается, потому что маркера у неё нет.
    ///
    /// Подсветка — дубль геометрии цели с материалом <c>FrostSpace/VFX/ScanHighlight</c>.
    /// Материал у каждой цели свой: сила и полоса скана лежат в нём, а не в глобальных
    /// параметрах шейдера, иначе одновременно светилась бы только одна цель. Исходные
    /// материалы объектов не трогаются, поэтому уровень остаётся в своей палитре.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(190)]
    public sealed class ObjectScanner : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int ScanBandId = Shader.PropertyToID("_ScanBand");

        /// <summary>Флаги каналов, которыми цель попала в кадр.</summary>
        [System.Flags]
        private enum Channel
        {
            None = 0,
            Aim = 1,
            Nearest = 2,
            Sonar = 4
        }

        [Header("Ссылки")]
        [Tooltip("Камера, из которой смотрим")]
        [SerializeField] private Camera _view;
        [Tooltip("Риг камеры: режим, пауза и точка прицела")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Игрок — от него считаются дистанции и растёт кольцо сонара; пусто — берётся цель рига")]
        [SerializeField] private Transform _player;
        [Tooltip("Шейдер FrostSpace/VFX/ScanHighlight")]
        [SerializeField] private Shader _shader;
        [Tooltip("Кольцо сонара: создаётся само, если поле пустое")]
        [SerializeField] private ScanPulse _pulse;

        [Header("Сканирование")]
        [Tooltip("Слои, по которым идёт прицельный луч (слой Player исключить)")]
        [SerializeField] private LayerMask _targets = ~0;
        [Tooltip("Дальность прицельного луча, м")]
        [SerializeField] private float _maxDistance = 40f;
        [Tooltip("Сколько секунд идёт полоса скана")]
        [SerializeField] private float _scanSeconds = 0.6f;
        [Tooltip("За сколько секунд контур гаснет, когда цель вышла из канала")]
        [SerializeField] private float _fadeSeconds = 0.16f;
        [Tooltip("Сколько рендереров максимум красить у одной цели: защита от сканирования целого террейна")]
        [SerializeField] private int _maxRenderers = 48;
        [Tooltip("Полуразмер квадратной области прицела в долях экрана: 0.14 ≈ 9° при поле зрения 60°")]
        [SerializeField, Range(0.02f, 0.5f)] private float _scanAreaHalf = 0.14f;
        [Tooltip("Сколько лучей по стороне квадрата области: 5 — это 25 лучей за кадр")]
        [SerializeField, Range(1, 9)] private int _scanSamples = 5;

        [Header("Каналы")]
        [Tooltip("Радиус автодетекции ближайшего объекта, м")]
        [SerializeField] private float _nearestRadius = 20f;
        [Tooltip("Радиус кругового обзора по Q, м")]
        [SerializeField] private float _sonarRadius = 25f;
        [Tooltip("Сколько целей в сонаре получают контур: остальные только в счётчике")]
        [SerializeField] private int _maxSonarTargets = 24;
        [Tooltip("Сколько подписей показывает HUD одновременно")]
        [SerializeField] private int _maxLabels = 12;
        [Tooltip("Насколько новая цель должна быть ближе к перекрестию, чтобы перебить текущую: 0.25 — четверть квадрата прицела")]
        [SerializeField, Range(0f, 1f)] private float _aimSwitchMargin = 0.25f;
        [Tooltip("Сколько секунд новая цель должна продержаться в кадре, прежде чем забрать выбор: отсекает мигание на тонких объектах")]
        [SerializeField] private float _aimSwitchSeconds = 0.12f;
        [Tooltip("Сколько секунд держать прицельную цель, если лучи её больше не находят, с")]
        [SerializeField] private float _aimHoldSeconds = 0.2f;

        [Header("Сонар")]
        [Tooltip("Сколько секунд идёт волна от игрока до границы радиуса")]
        [SerializeField] private float _pulseSeconds = 1.1f;
        [Tooltip("Сколько секунд подписи держатся после прохода волны, прежде чем погаснуть")]
        [SerializeField] private float _scanHoldSeconds = 2.5f;
        [Tooltip("Сколько дублей геометрии строить за кадр: всё сразу даёт рывок на старте волны")]
        [SerializeField] private int _buildsPerFrame = 3;
        [Tooltip("Цвет кольца — циан цифрового слоя #4DE6FF")]
        [SerializeField] private Color _scanColor = new(0.302f, 0.902f, 1f, 1f);
        [Tooltip("Толщина контура в пикселях экрана: не зависит от дистанции до объекта")]
        [SerializeField] private float _outlineWidth = 2.6f;

        private sealed class Entry
        {
            public ScanTarget Target;
            public readonly List<Renderer> Overlays = new(8);
            public Renderer[] Sources;
            public int SourceIndex;
            public Material Material;
            public Bounds Bounds;
            public float Strength;
            public float Progress;
            public Channel Channels;
            public float Distance;
            public bool Built;
        }

        /// <summary>
        /// Порядок подписей: прицельная цель всегда первая, остальные — по возрастанию
        /// дистанции. Отдельный компаратор, а не лямбда: сортировка идёт каждый кадр,
        /// и делегат в ней означал бы аллокацию на кадр.
        /// </summary>
        private sealed class EntryOrder : IComparer<Entry>
        {
            public static readonly EntryOrder Instance = new();

            public int Compare(Entry a, Entry b)
            {
                var aAim = (a.Channels & Channel.Aim) != 0;
                var bAim = (b.Channels & Channel.Aim) != 0;
                if (aAim != bAim)
                    return aAim ? -1 : 1;

                return a.Distance.CompareTo(b.Distance);
            }
        }

        private readonly Dictionary<ScanTarget, Entry> _entries = new(64);
        private readonly List<Entry> _visible = new(64);
        private readonly List<ScanTarget> _candidates = new(64);
        private readonly List<float> _candidateDistance = new(64);
        private readonly List<ScanView> _views = new(64);
        private readonly Dictionary<ScanTarget, Channel> _flagged = new(64);
        private readonly List<ScanTarget> _dead = new(8);

        private float _waveProgress = 1f;
        private float _scanElapsed;
        private ScanTarget _aimTarget;
        private ScanTarget _aimCandidate;
        private float _aimCandidateSince;
        private float _aimLostAt = -1f;
        private bool _warnedNoShader;

        /// <summary>Режим сонара включён.</summary>
        public bool IsScanning { get; private set; }

        /// <summary>Цели, которые сейчас видит HUD: прицельная первой, дальше по дистанции.</summary>
        public IReadOnlyList<ScanView> Views => _views;

        /// <summary>Цель в области прицела — HUD обводит её рамкой.</summary>
        public ScanTarget AimTarget => _aimTarget;

        /// <summary>Прогресс волны сонара 0..1: HUD подсвечивает индикатор, пока она идёт.</summary>
        public float SonarWave => _waveProgress;

        /// <summary>Волна ещё идёт — кольцо на земле видно.</summary>
        public bool IsWaveActive => IsScanning && _waveProgress < 1f;

        /// <summary>Радиус кругового обзора, м: HUD показывает его в подсказке.</summary>
        public float SonarRadius => _sonarRadius;

        /// <summary>Сколько подписей можно показать одновременно.</summary>
        public int MaxLabels => Mathf.Max(1, _maxLabels);

        /// <summary>Включает и выключает режим сонара (то же, что нажатие Q).</summary>
        public void SetScanning(bool value)
        {
            if (IsScanning == value)
                return;

            IsScanning = value;
            // Выключение — сразу после волны: кольцо не должно догорать.
            _waveProgress = value ? 0f : 1f;
            _scanElapsed = 0f;
            Debug.Log(value ? "[ObjectScanner] сонар включён" : "[ObjectScanner] сонар выключен", this);
        }

        /// <summary>
        /// Пускает новую волну. Q — не переключатель, а разовый выстрел: нажатие всегда
        /// запускает волну заново, даже если прошлая ещё идёт.
        /// </summary>
        public void StartScan()
        {
            IsScanning = true;
            _waveProgress = 0f;
            _scanElapsed = 0f;
        }

        private void Awake()
        {
            if (_view == null)
                _view = Camera.main;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_player == null)
                _player = _rig != null && _rig.Target != null ? _rig.Target : transform;
            if (_shader == null)
                _shader = Shader.Find("FrostSpace/VFX/ScanHighlight");

            if (_shader == null)
            {
                Debug.LogError("[ObjectScanner] не найден шейдер FrostSpace/VFX/ScanHighlight", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            // Пауза и выгрузка сцены: контуры и кольцо не должны остаться на экране.
            IsScanning = false;
            _aimTarget = null;
            _aimCandidate = null;
            _aimLostAt = -1f;
            _views.Clear();
            _flagged.Clear();

            foreach (var entry in _entries.Values)
            {
                entry.Strength = 0f;
                entry.Channels = Channel.None;
                SetOverlaysVisible(entry, false);
            }

            if (_pulse != null)
                _pulse.SetState(false, Vector3.zero, 0f, 0f, 0f);
        }

        private void OnDestroy()
        {
            foreach (var entry in _entries.Values)
            {
                foreach (var overlay in entry.Overlays)
                {
                    if (overlay != null)
                        Destroy(overlay.gameObject);
                }

                if (entry.Material != null)
                    Destroy(entry.Material);
            }

            _entries.Clear();
        }

        private void LateUpdate()
        {
            var paused = _rig != null && _rig.IsPaused;
            if (paused)
                return;

            UpdateToggle();
            AdvanceWave();

            var player = _player != null ? _player.position : transform.position;
            var aim = GameSettings.AimDetection ? ResolveAim() : ClearAim();
            var nearest = GameSettings.NearestDetection ? ResolveNearest(player) : null;

            CollectSonar(player);
            Flag(aim, Channel.Aim);
            Flag(nearest, Channel.Nearest);

            UpdateEntries();
            BuildViews(aim);
            UpdatePulse(player);
        }

        /// <summary>
        /// Волна сонара идёт один раз от игрока до границы радиуса. После прохода подписи
        /// держатся <see cref="_scanHoldSeconds"/> и гаснут сами: Q — разовый выстрел,
        /// а не режим. Повторное нажатие просто пускает новую волну.
        /// </summary>
        private void AdvanceWave()
        {
            if (!IsScanning)
                return;

            _scanElapsed += Time.deltaTime;

            if (_waveProgress < 1f)
            {
                _waveProgress = ScanMath.Advance(_waveProgress, _pulseSeconds, Time.deltaTime);
                return;
            }

            if (_scanElapsed >= _pulseSeconds + _scanHoldSeconds)
                SetScanning(false);
        }

        /// <summary>Сброс прицельной цели, когда канал выключен тумблером.</summary>
        private ScanTarget ClearAim()
        {
            _aimTarget = null;
            _aimCandidate = null;
            _aimLostAt = -1f;
            return null;
        }

        /// <summary>Q включает и выключает сонар; вне третьего лица режим гаснет сам.</summary>
        private void UpdateToggle()
        {
            var third = _rig == null || _rig.Mode == CameraMode.ThirdPerson;
            if (!third)
            {
                SetScanning(false);
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame)
                StartScan();
        }

        private void Flag(ScanTarget target, Channel channel)
        {
            if (target == null)
                return;

            _flagged.TryGetValue(target, out var current);
            _flagged[target] = current | channel;
        }

        /// <summary>
        /// Цель под квадратом прицела. Кандидаты оцениваются по близости точки попадания
        /// к перекрестию, а не по глубине: раньше побеждал ближайший по дистанции объект,
        /// и на заборе или в лесу выбор перескакивал между секциями, стоило чуть повернуть
        /// камеру. Глубина осталась только тай-брейком.
        ///
        /// Плюс гистерезис: прежняя цель держится, пока новая не окажется заметно ближе
        /// к центру, и ещё <see cref="_aimHoldSeconds"/> после полной потери лучей. Без
        /// этого рамка мигала на стыке двух объектов и на краю геометрии.
        /// </summary>
        private ScanTarget ResolveAim()
        {
            if (_view == null)
                return null;

            var samples = Mathf.Clamp(_scanSamples, 1, 9);
            var half = Mathf.Max(0.01f, _scanAreaHalf);

            ScanTarget best = null;
            var bestScore = float.MaxValue;
            var stickyScore = float.MaxValue;

            for (var x = 0; x < samples; x++)
            {
                for (var y = 0; y < samples; y++)
                {
                    var point = ScanMath.ViewportSample(x, y, samples, half);
                    var ray = _view.ViewportPointToRay(new Vector3(point.x, point.y, 0f));
                    if (!Physics.Raycast(ray, out var hit, _maxDistance, _targets, QueryTriggerInteraction.Ignore))
                        continue;

                    var target = ScanTarget.Find(hit.collider.transform);
                    if (target == null)
                        continue;

                    // 0 — перекрестие, 1 — граница квадрата. Дистанция даёт микро-перевес
                    // при равном смещении, но не может перебить попадание в центр.
                    var offset = Mathf.Max(
                        Mathf.Abs(point.x - 0.5f), Mathf.Abs(point.y - 0.5f)) / half;
                    var score = offset + hit.distance * 0.002f;

                    if (target == _aimTarget && score < stickyScore)
                        stickyScore = score;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = target;
                    }
                }
            }

            if (best == null)
            {
                // Лучи не нашли цель: держим прежнюю короткое время, иначе рамка гаснет
                // на каждом пропущенном кадре у края стены или между створками забора.
                if (_aimTarget == null)
                    return null;

                if (_aimLostAt < 0f)
                    _aimLostAt = Time.time;

                if (Time.time - _aimLostAt > _aimHoldSeconds)
                    ClearAim();

                return _aimTarget;
            }

            _aimLostAt = -1f;

            if (_aimTarget == null)
            {
                _aimTarget = best;
                _aimCandidate = null;
                return _aimTarget;
            }

            if (best == _aimTarget)
            {
                _aimCandidate = null;
                return _aimTarget;
            }

            // Новая цель должна продержаться в кадре _aimSwitchSeconds и быть заметно
            // ближе к перекрестию. Иначе тонкий объект — столб забора, ствол — перехватывает
            // выбор на один-два кадра, пока камера проезжает мимо, и подпись мигает.
            if (best != _aimCandidate)
            {
                _aimCandidate = best;
                _aimCandidateSince = Time.time;
            }

            var confirmed = Time.time - _aimCandidateSince >= _aimSwitchSeconds;
            var clearlyBetter = stickyScore >= float.MaxValue ||
                                bestScore + _aimSwitchMargin <= stickyScore;

            if (confirmed && clearlyBetter)
            {
                _aimTarget = best;
                _aimCandidate = null;
            }

            return _aimTarget;
        }

        /// <summary>Ближайшая цель в радиусе автодетекции.</summary>
        private ScanTarget ResolveNearest(Vector3 player)
        {
            ScanTarget best = null;
            var bestDistance = Mathf.Max(0f, _nearestRadius);

            var all = ScanTarget.All;
            for (var i = 0; i < all.Count; i++)
            {
                var target = all[i];
                if (target == null)
                    continue;

                var distance = Vector3.Distance(player, target.transform.position);
                if (distance > bestDistance)
                    continue;

                best = target;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>
        /// Цели, которые накрыла волна сонара. Волна одна на включение: пока она идёт,
        /// в список попадают только объекты за её фронтом, поэтому подписи появляются
        /// по мере расхождения кольца. После прохода фронта через радиус остаются все
        /// цели в радиусе.
        ///
        /// Список обрезается по <see cref="_maxSonarTargets"/>: контур на каждом дереве
        /// в лесу — это десятки материалов и дублей геометрии.
        /// </summary>
        private void CollectSonar(Vector3 player)
        {
            _candidates.Clear();
            _candidateDistance.Clear();

            if (!IsScanning)
                return;

            var radius = Mathf.Max(0f, _sonarRadius);
            var front = _waveProgress >= 1f ? radius : Mathf.Clamp01(_waveProgress) * radius;
            var all = ScanTarget.All;

            for (var i = 0; i < all.Count; i++)
            {
                var target = all[i];
                if (target == null)
                    continue;

                var distance = Vector3.Distance(player, target.transform.position);
                if (distance > front)
                    continue;

                _candidates.Add(target);
                _candidateDistance.Add(distance);
            }

            // Сортировка вставками: список короткий, зато без аллокаций и делегатов.
            for (var i = 1; i < _candidates.Count; i++)
            {
                var target = _candidates[i];
                var distance = _candidateDistance[i];
                var j = i - 1;

                while (j >= 0 && _candidateDistance[j] > distance)
                {
                    _candidates[j + 1] = _candidates[j];
                    _candidateDistance[j + 1] = _candidateDistance[j];
                    j--;
                }

                _candidates[j + 1] = target;
                _candidateDistance[j + 1] = distance;
            }

            var limit = Mathf.Clamp(_maxSonarTargets, 1, _candidates.Count);
            for (var i = 0; i < limit; i++)
                Flag(_candidates[i], Channel.Sonar);
        }

        private void UpdateEntries()
        {
            var dt = Time.deltaTime;
            _visible.Clear();

            // Сначала снимаем флаги, потом ставим заново по текущему кадру: так цель,
            // вышедшая из канала, плавно гаснет, а не исчезает в тот же кадр.
            foreach (var entry in _entries.Values)
                entry.Channels = Channel.None;

            foreach (var pair in _flagged)
            {
                var target = pair.Key;
                if (target == null)
                    continue;

                if (!_entries.TryGetValue(target, out var entry))
                {
                    entry = new Entry { Target = target };
                    _entries[target] = entry;
                }

                entry.Channels = pair.Value;
            }

            _flagged.Clear();

            var player = _player != null ? _player.position : transform.position;

            // Бюджет на кадр: дубли геометрии и материалы строятся по частям. Иначе волна,
            // дошедшая до радиуса, создаёт сразу два десятка дублей и материалов в одном
            // кадре — это рывок на треть секунды, который виден как «телепорт» животных.
            var budget = Mathf.Max(1, _buildsPerFrame);

            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                var target = entry.Target;

                if (target == null)
                {
                    SetOverlaysVisible(entry, false);
                    continue;
                }

                var wanted = entry.Channels != Channel.None;
                if (wanted && !entry.Built)
                    budget = BuildStep(entry, budget);

                entry.Bounds = target.WorldBounds;
                entry.Distance = Vector3.Distance(player, entry.Bounds.center);
                entry.Strength = ScanMath.Approach(entry.Strength, wanted ? 1f : 0f, _fadeSeconds, dt);

                if (wanted)
                    entry.Progress = ScanMath.Advance(entry.Progress, _scanSeconds, dt);
                else if (entry.Strength <= 0.001f)
                    entry.Progress = 0f;

                var visible = entry.Strength > 0.001f;
                SetOverlaysVisible(entry, visible);

                if (visible && entry.Material != null)
                {
                    entry.Material.SetFloat(StrengthId, entry.Strength);
                    entry.Material.SetVector(
                        ScanBandId, new Vector4(entry.Bounds.min.y, entry.Bounds.max.y, entry.Progress, 0f));
                }

                if (visible)
                    _visible.Add(entry);
            }

            // Цели, снятые со сцены, держат материалы: без чистки словарь растёт от сцены к сцене.
            RemoveDeadEntries();
        }

        private void RemoveDeadEntries()
        {
            if (_entries.Count == 0)
                return;

            _dead.Clear();
            foreach (var pair in _entries)
            {
                if (pair.Key != null)
                    continue;

                if (pair.Value.Material != null)
                    Destroy(pair.Value.Material);

                _dead.Add(pair.Key);
            }

            for (var i = 0; i < _dead.Count; i++)
                _entries.Remove(_dead[i]);

            _dead.Clear();
        }

        /// <summary>
        /// Список для HUD: прицельная цель всегда первая (она не должна вытесняться
        /// из пула подписей), остальные — по возрастанию дистанции.
        /// </summary>
        private void BuildViews(ScanTarget aim)
        {
            _views.Clear();
            _visible.Sort(EntryOrder.Instance);

            for (var i = 0; i < _visible.Count; i++)
            {
                var entry = _visible[i];
                var target = entry.Target;

                _views.Add(new ScanView(
                    target,
                    target.DisplayName,
                    target.Category,
                    entry.Bounds.center,
                    entry.Bounds,
                    entry.Distance,
                    entry.Strength,
                    entry.Progress,
                    (entry.Channels & Channel.Aim) != 0,
                    (entry.Channels & Channel.Nearest) != 0,
                    (entry.Channels & Channel.Sonar) != 0));
            }
        }

        /// <summary>
        /// Кольцо на земле живёт только пока идёт волна: одно нажатие Q — один проход.
        /// После прохода кольцо гаснет, а контуры и подписи остаются.
        /// </summary>
        private void UpdatePulse(Vector3 player)
        {
            if (_pulse == null)
            {
                var go = new GameObject("Scan Pulse") { hideFlags = HideFlags.DontSave };
                _pulse = go.AddComponent<ScanPulse>();
            }

            if (!IsWaveActive)
            {
                _pulse.SetState(false, player, _sonarRadius, _waveProgress, 0f);
                return;
            }

            _pulse.SetState(true, player, _sonarRadius, _waveProgress, 1f);
        }

        /// <summary>
        /// Строит дубли геометрии цели порциями: материал — один раз, дальше по
        /// <paramref name="budget"/> рендереров за кадр. Возвращает остаток бюджета.
        /// Цель считается собранной, когда пройдены все её рендереры (или лимит
        /// <see cref="_maxRenderers"/>).
        /// </summary>
        private int BuildStep(Entry entry, int budget)
        {
            if (_shader == null)
            {
                entry.Built = true;
                return budget;
            }

            if (entry.Material == null)
            {
                entry.Material = new Material(_shader)
                {
                    name = "ScanHighlight " + entry.Target.name,
                    hideFlags = HideFlags.DontSave
                };
                entry.Material.SetColor(ScanColorId, _scanColor);
                entry.Material.SetFloat(OutlineWidthId, _outlineWidth);
                entry.Material.SetFloat(StrengthId, 0f);
            }

            entry.Sources ??= entry.Target.GetComponentsInChildren<Renderer>(true);

            while (entry.SourceIndex < entry.Sources.Length && budget > 0)
            {
                var source = entry.Sources[entry.SourceIndex++];

                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy)
                    continue;
                if ((source.gameObject.hideFlags & HideFlags.DontSave) != 0)
                    continue;

                var overlay = CreateOverlay(source, entry.Material);
                if (overlay != null)
                    entry.Overlays.Add(overlay);

                budget--;

                // Лимит рендереров на цель: защита от сканирования целого террейна.
                if (entry.Overlays.Count >= _maxRenderers)
                    break;
            }

            entry.Built = entry.SourceIndex >= entry.Sources.Length || entry.Overlays.Count >= _maxRenderers;
            return budget;
        }

        /// <summary>
        /// Дубль геометрии с материалом скана. Дубль повторяет трансформ источника кадр
        /// в кадр: у меша геометрия лежит в локальных осях рендерера, у скиннинга берутся
        /// те же кости, поэтому деформация совпадает и обводка не отстаёт.
        /// </summary>
        private static Renderer CreateOverlay(Renderer source, Material material)
        {
            var go = new GameObject("Scan Overlay") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(source.transform, false);

            switch (source)
            {
                case SkinnedMeshRenderer skin when skin.sharedMesh != null:
                {
                    var clone = go.AddComponent<SkinnedMeshRenderer>();
                    clone.sharedMesh = skin.sharedMesh;
                    clone.bones = skin.bones;
                    clone.rootBone = skin.rootBone;
                    clone.localBounds = skin.localBounds;
                    clone.updateWhenOffscreen = true;
                    clone.shadowCastingMode = ShadowCastingMode.Off;
                    clone.receiveShadows = false;
                    clone.sharedMaterial = material;
                    return clone;
                }

                case MeshRenderer mesh when mesh.TryGetComponent(out MeshFilter filter):
                {
                    var meshFilter = go.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = filter.sharedMesh;
                    var clone = go.AddComponent<MeshRenderer>();
                    clone.shadowCastingMode = ShadowCastingMode.Off;
                    clone.receiveShadows = false;
                    clone.sharedMaterial = material;
                    return clone;
                }

                default:
                    Destroy(go);
                    return null;
            }
        }

        private static void SetOverlaysVisible(Entry entry, bool visible)
        {
            for (var i = 0; i < entry.Overlays.Count; i++)
            {
                var overlay = entry.Overlays[i];
                if (overlay != null && overlay.gameObject.activeSelf != visible)
                    overlay.gameObject.SetActive(visible);
            }
        }
    }
}
