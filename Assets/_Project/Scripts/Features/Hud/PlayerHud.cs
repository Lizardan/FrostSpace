using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FrostSpace.Features.Scan;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Hud
{
    /// <summary>
    /// Игровой HUD: перекрестие, пул подписей сканера, рамка прицельной цели, индикатор
    /// режима сонара и счётчик FPS в правом верхнем углу.
    ///
    /// Собирается кодом при старте сцены — в <c>Game</c> нет ни Canvas, ни EventSystem,
    /// а HUD только отображает состояние, мышь в него не кликает. Поэтому GraphicRaycaster
    /// не добавляется: без него UI не может «съесть» захваченный курсор и ничего не стоит
    /// в EventSystem. Меню паузы свой Canvas и GraphicRaycaster строит само.
    ///
    /// Оформление повторяет меню (<c>MainMenuScreen</c>): тот же циановый акцент, dim-текст,
    /// разрядка букв и размеры под 1920×1080. Все подписи латиницей — в атласе
    /// <c>LiberationSans SDF</c> нет кириллицы.
    ///
    /// Правой ленты модулей больше нет: просвет живёт на ПКМ постоянно, а сканирование
    /// стало способностью на Q, поэтому подписи приходят списком от
    /// <see cref="ObjectScanner"/> и раскладываются по пулу.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(250)]
    public sealed class PlayerHud : MonoBehaviour
    {
        /// <summary>Габарит плашки подписи от её точки, px: по нему считается перекрытие.</summary>
        private static readonly Vector2 LabelExtent = new(300f, 108f);

        [Header("Ссылки")]
        [Tooltip("Риг камеры: режим и пауза")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Сканер: что подписать и где рамка")]
        [SerializeField] private ObjectScanner _scanner;

        [Header("Палитра")]
        [Tooltip("Акцент цифрового слоя #4DE6FF")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [Tooltip("Основной текст — тот же, что в меню")]
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [Tooltip("Неактивный текст")]
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);

        [Header("Перекрестие")]
        [SerializeField] private float _crosshairLength = 11f;
        [SerializeField] private float _crosshairThickness = 2f;
        [SerializeField] private float _crosshairGap = 7f;
        [Tooltip("Прозрачность перекрестия: оно не должно закрывать то, что просвечивает")]
        [SerializeField, Range(0f, 1f)] private float _crosshairAlpha = 0.6f;

        [Header("Подпись сканера")]
        [Tooltip("Высота выноса от объекта, px")]
        [SerializeField] private float _labelLift = 68f;
        [Tooltip("Длина горизонтального плеча выноса, px")]
        [SerializeField] private float _labelArm = 150f;
        [SerializeField] private float _labelNameSize = 34f;
        [SerializeField] private float _labelCategorySize = 20f;
        [Tooltip("Толщина линий выноса, px")]
        [SerializeField] private float _labelLineWidth = 3f;
        [Tooltip("Обводка текста: без неё подпись теряется на светлом снегу, 0..1")]
        [SerializeField, Range(0f, 1f)] private float _labelOutline = 0.24f;
        [Tooltip("Цвет обводки — почти чёрный холодный")]
        [SerializeField] private Color _labelOutlineColor = new(0.02f, 0.05f, 0.07f, 1f);
        [Tooltip("Сколько подписей может висеть одновременно")]
        [SerializeField] private int _labelCount = 12;

        [Header("Подложка подписи")]
        [Tooltip("Заливка плашки: полупрозрачный циан — текст на нём читается и на снегу")]
        [SerializeField] private Color _plateFill = new(0.13f, 0.38f, 0.42f, 0.68f);
        [Tooltip("Высота плашки, px")]
        [SerializeField] private float _plateHeight = 104f;
        [Tooltip("Поле вокруг текста внутри плашки, px")]
        [SerializeField] private float _platePadding = 18f;
        [Tooltip("Отступ рамки цели от экранной проекции объекта, px")]
        [SerializeField] private float _framePadding = 14f;

        [Header("Индикатор скана")]
        [Tooltip("Подсказка о способности внизу экрана")]
        [SerializeField] private string _scanHint = "Q SCAN";
        [Tooltip("Индикатор включённого режима")]
        [SerializeField] private string _scanActive = "SCANNING";
        [Tooltip("Отступ строк от нижнего края, px")]
        [SerializeField] private float _statusMargin = 64f;
        [SerializeField] private float _hintSize = 18f;
        [SerializeField] private float _statusSize = 22f;

        [Header("Счётчик FPS")]
        [Tooltip("Отступ счётчика от правого и верхнего края экрана, px")]
        [SerializeField] private float _fpsMargin = 36f;
        [SerializeField] private float _fpsLabelSize = 16f;
        [SerializeField] private float _fpsValueSize = 26f;
        [Tooltip("Как часто пересчитывать значение, с")]
        [SerializeField] private float _fpsInterval = 0.5f;

        private RectTransform _canvasRect;
        private GameObject _crosshair;
        private ScanLabelView[] _labels;
        private ScanTarget[] _slotTargets;
        private Rect[] _slotRects;
        private readonly GameObject[] _frames = new GameObject[2];
        private readonly CanvasGroup[] _frameGroups = new CanvasGroup[2];
        private TextMeshProUGUI _hint;
        private TextMeshProUGUI _status;

        private TextMeshProUGUI _fpsValue;
        private int _fpsFrames;
        private float _fpsElapsed;
        private int _fpsShown = -1;

        private Camera _camera;

        private void Awake()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();

            _camera = _rig != null ? _rig.ViewCamera : Camera.main;

            BuildCanvas();
            BuildCrosshair();
            BuildLabels();
            BuildTargetFrame();
            BuildStatus();
            BuildFps();
        }

        private void LateUpdate()
        {
            var mode = _rig != null ? _rig.Mode : CameraMode.ThirdPerson;
            var third = mode == CameraMode.ThirdPerson;

            if (_crosshair != null)
                _crosshair.SetActive(third || mode == CameraMode.FirstPerson);

            UpdateLabels(third);
            UpdateTargetFrame(third);
            UpdateStatus(third);
            UpdateFps();
        }

        private void OnDestroy()
        {
            if (_canvasRect != null)
                Destroy(_canvasRect.gameObject);
        }

        // ---------- сборка ----------

        private void BuildCanvas()
        {
            var go = new GameObject("HUD Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRect = (RectTransform)go.transform;
        }

        private void BuildCrosshair()
        {
            _crosshair = NewContainer(_canvasRect, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(40f, 40f));

            var color = new Color(_accent.r, _accent.g, _accent.b, _crosshairAlpha);
            var reach = _crosshairGap + _crosshairLength * 0.5f;

            // Четыре короткие риски вместо точки: центр экрана остаётся чистым —
            // сквозь него видно то, что вырезает просвет.
            NewTick(_crosshair.transform, "Tick Up", color,
                new Vector2(0f, reach), new Vector2(_crosshairThickness, _crosshairLength), 0f);
            NewTick(_crosshair.transform, "Tick Down", color,
                new Vector2(0f, -reach), new Vector2(_crosshairThickness, _crosshairLength), 0f);
            NewTick(_crosshair.transform, "Tick Left", color,
                new Vector2(-reach, 0f), new Vector2(_crosshairLength, _crosshairThickness), 0f);
            NewTick(_crosshair.transform, "Tick Right", color,
                new Vector2(reach, 0f), new Vector2(_crosshairLength, _crosshairThickness), 0f);
        }

        /// <summary>
        /// Пул подписей: все элементы строятся один раз, дальше только заполняются.
        /// Пересоздавать их каждый кадр — это аллокации Canvas и рваная раскладка.
        /// </summary>
        private void BuildLabels()
        {
            var style = new ScanLabelStyle
            {
                Accent = _accent,
                Text = _text,
                Dim = _dim,
                PlateFill = _plateFill,
                OutlineColor = _labelOutlineColor,
                Lift = _labelLift,
                Arm = _labelArm,
                NameSize = _labelNameSize,
                CategorySize = _labelCategorySize,
                LineWidth = _labelLineWidth,
                Outline = _labelOutline,
                PlateHeight = _plateHeight,
                PlatePadding = _platePadding
            };

            var count = Mathf.Clamp(_labelCount, 1, 32);
            _labels = new ScanLabelView[count];
            _slotTargets = new ScanTarget[count];
            _slotRects = new Rect[count];
            for (var i = 0; i < count; i++)
                _labels[i] = new ScanLabelView(_canvasRect, style);
        }

        /// <summary>
        /// Квадратные рамки целей: те же уголки, что у плашки, но без фона — объект просто
        /// обводится по экранной проекции своих границ.
        ///
        /// Рамок две: прицельная цель и ближайшая. Обе роли равноправны — ближайший объект
        /// тоже подсвечивается рамкой, иначе он отличался бы от прицельного только текстом.
        /// Если обе роли выпали на одну цель, рисуется одна рамка: вторая легла бы ровно
        /// поверх первой и мигала от разницы альф.
        /// </summary>
        private void BuildTargetFrame()
        {
            for (var i = 0; i < _frames.Length; i++)
            {
                var frame = NewContainer(_canvasRect,
                    i == 0 ? "Target Frame Aim" : "Target Frame Nearest",
                    new Vector2(0.5f, 0.5f), new Vector2(180f, 180f));

                var group = frame.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;

                var rect = (RectTransform)frame.transform;
                AddBracket(rect, new Vector2(0f, 1f), 1f, -1f);
                AddBracket(rect, new Vector2(0f, 0f), 1f, 1f);
                AddBracket(rect, new Vector2(1f, 1f), -1f, -1f);
                AddBracket(rect, new Vector2(1f, 0f), -1f, 1f);

                frame.SetActive(false);
                _frames[i] = frame;
                _frameGroups[i] = group;
            }
        }

        /// <summary>
        /// Низ экрана: подсказка способности и индикатор режима. Подсказка видна всегда,
        /// пока скан доступен; индикатор появляется только при включённом сонаре.
        /// </summary>
        private void BuildStatus()
        {
            _hint = NewText(_canvasRect, "Scan Hint", _scanHint, _hintSize, _dim,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, _statusMargin),
                new Vector2(400f, 28f), 14f);

            _status = NewText(_canvasRect, "Scan Status", _scanActive, _statusSize, _accent,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
                new Vector2(0f, _statusMargin + 30f), new Vector2(400f, 32f), 16f);
            _status.gameObject.SetActive(false);

            AddOutline(_hint);
            AddOutline(_status);
        }

        /// <summary>
        /// Счётчик FPS в правом верхнем углу: число акцентом и подпись «FPS» dim-цветом.
        /// Плашки нет — на фоне неба и снега текст держит тёмная обводка, та же, что
        /// у подписи сканера.
        /// </summary>
        private void BuildFps()
        {
            var readout = NewContainer(_canvasRect, "FPS", new Vector2(1f, 1f),
                new Vector2(120f, 32f));
            var rect = (RectTransform)readout.transform;
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-_fpsMargin, -_fpsMargin);

            // Число стоит левее подписи: строку читают как «60 FPS», а не как название.
            _fpsValue = NewText(readout.transform, "Value", "--", _fpsValueSize, _accent,
                TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(-46f, 0f),
                new Vector2(72f, 32f), 2f);

            var label = NewText(readout.transform, "Label", "FPS", _fpsLabelSize, _dim,
                TextAlignmentOptions.Right, new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(44f, 24f), 8f);

            // Обводка создаёт отдельный экземпляр материала только у этих двух текстов;
            // дефолтный шрифт проекта и меню не меняются.
            AddOutline(_fpsValue);
            AddOutline(label);
        }

        /// <summary>
        /// Камера для проекции. Ищется один раз и кэшируется: обращение к
        /// <c>Camera.main</c> каждый кадр — это поиск по тегу и молчаливая подмена
        /// вместо явной ссылки.
        /// </summary>
        private Camera ResolveCamera()
        {
            if (_camera == null)
                _camera = _rig != null ? _rig.ViewCamera : Camera.main;

            return _camera;
        }

        // ---------- обновление ----------

        /// <summary>
        /// Подписи по пулу. Сканер отдаёт список уже в нужном порядке — прицельная цель
        /// первой, остальные по дистанции, — поэтому HUD заполняет первые слоты и прячет
        /// остальные.
        ///
        /// Плашки не наезжают друг на друга: подпись, чей габарит пересекается с уже
        /// поставленной, не рисуется. На кучном заборе из десятка секций иначе получается
        /// сплошное пятно, в котором не прочитать ни одного имени. Контур у такой цели
        /// остаётся, поэтому объект не теряется — он просто не подписан дважды в одном месте.
        /// </summary>
        /// <summary>
        /// Подписи по пулу. Слот **закреплён за целью**: пока цель в списке, подпись живёт
        /// в своём слоте и едет за ней. Раньше слоты раздавались по порядку списка каждый
        /// кадр, и стоило порядку измениться (цель вошла в прицел, обогнала другую по
        /// дистанции), подписи перескакивали с объекта на объект — это читалось как рывки.
        ///
        /// Новые подписи занимают свободные слоты, но не ставятся поверх уже занятых:
        /// плашка, чей габарит пересекается с поставленной, ждёт. На кучном заборе иначе
        /// получается сплошное пятно, в котором не прочитать ни одного имени.
        /// </summary>
        private void UpdateLabels(bool visible)
        {
            if (_labels == null || _slotTargets == null)
                return;

            var views = _scanner != null && visible ? _scanner.Views : null;
            var limit = _scanner != null ? Mathf.Min(_labels.Length, _scanner.MaxLabels) : 0;
            var camera = ResolveCamera();

            // Первый проход: слоты, чья цель ещё в списке, обновляем на месте.
            for (var i = 0; i < _labels.Length; i++)
            {
                var target = _slotTargets[i];
                if (target == null)
                    continue;

                var index = IndexOf(views, target, limit);
                if (index < 0 || !TryProjectPoint(views[index].Anchor, camera, out var local))
                {
                    ReleaseSlot(i);
                    continue;
                }

                _slotRects[i] = LabelRect(local);
                _labels[i].Show(local, views[index], views[index].Aim);
            }

            // Второй проход: свободные слоты получают цели, которым места ещё нет.
            for (var i = 0; views != null && i < views.Count && i < limit; i++)
            {
                var view = views[i];
                if (IsAssigned(view.Target))
                    continue;

                var slot = FreeSlot();
                if (slot < 0)
                    break;

                if (!TryProjectPoint(view.Anchor, camera, out var local))
                    continue;

                var rect = LabelRect(local);
                if (Overlaps(rect, slot))
                    continue;

                _slotTargets[slot] = view.Target;
                _slotRects[slot] = rect;
                _labels[slot].Show(local, view, view.Aim);
            }
        }

        /// <summary>Индекс цели в списке сканера или <c>-1</c>.</summary>
        private static int IndexOf(IReadOnlyList<ScanView> views, ScanTarget target, int limit)
        {
            if (views == null)
                return -1;

            var count = Mathf.Min(views.Count, limit);
            for (var i = 0; i < count; i++)
            {
                if (views[i].Target == target)
                    return i;
            }

            return -1;
        }

        private bool IsAssigned(ScanTarget target)
        {
            for (var i = 0; i < _slotTargets.Length; i++)
            {
                if (_slotTargets[i] == target)
                    return true;
            }

            return false;
        }

        private int FreeSlot()
        {
            for (var i = 0; i < _slotTargets.Length; i++)
            {
                if (_slotTargets[i] == null)
                    return i;
            }

            return -1;
        }

        private void ReleaseSlot(int index)
        {
            _slotTargets[index] = null;
            _slotRects[index] = default;
            _labels[index].Hide();
        }

        /// <summary>Габарит плашки: она уходит вправо от точки и стоит по центру по высоте.</summary>
        private static Rect LabelRect(Vector2 point)
        {
            return new Rect(
                point.x - 20f,
                point.y - LabelExtent.y * 0.5f,
                LabelExtent.x,
                LabelExtent.y);
        }

        /// <summary>Габарит пересекается с подписью в другом слоте.</summary>
        private bool Overlaps(Rect rect, int own)
        {
            for (var i = 0; i < _slotTargets.Length; i++)
            {
                if (i == own || _slotTargets[i] == null)
                    continue;

                if (_slotRects[i].Overlaps(rect))
                    return true;
            }

            return false;
        }

        /// <summary>Мировая точка в координатах Canvas; <c>false</c> — точка за камерой.</summary>
        private bool TryProjectPoint(Vector3 world, Camera camera, out Vector2 local)
        {
            local = default;
            if (camera == null || _canvasRect == null)
                return false;

            var screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
                return false;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screen, null, out local);
        }

        /// <summary>
        /// Экранная проекция мировых границ: восемь углов дают прямоугольник в координатах
        /// Canvas. Если хоть один угол за камерой, рамку не рисуем — иначе она растянется
        /// на пол-экрана и перестанет что-либо обозначать.
        /// </summary>
        private bool TryProjectBounds(Bounds bounds, out Vector2 center, out Vector2 size)
        {
            center = default;
            size = default;

            var view = ResolveCamera();
            if (view == null)
                return false;

            var min = bounds.min;
            var max = bounds.max;
            var low = new Vector2(float.MaxValue, float.MaxValue);
            var high = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                var screen = view.WorldToScreenPoint(corner);
                if (screen.z <= 0f)
                    return false;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvasRect, screen, null, out var local))
                    return false;

                low = Vector2.Min(low, local);
                high = Vector2.Max(high, local);
            }

            center = (low + high) * 0.5f;
            size = high - low;
            return size.x > 1f && size.y > 1f;
        }

        /// <summary>
        /// Рамки целей: [0] — прицельная, [1] — ближайшая. Обе едут по свежим границам
        /// цели, поэтому рамка стоит вокруг объекта, а не догоняет его рывками.
        /// </summary>
        private void UpdateTargetFrame(bool visible)
        {
            if (_scanner == null || !visible)
            {
                HideFrame(0);
                HideFrame(1);
                return;
            }

            var views = _scanner.Views;
            var aim = FindView(views, true);
            var nearest = FindView(views, false);

            // Одна цель на две роли — одна рамка.
            if (aim.HasValue && nearest.HasValue && aim.Value.Target == nearest.Value.Target)
                nearest = null;

            UpdateFrame(0, aim);
            UpdateFrame(1, nearest);
        }

        /// <summary>Первая цель с нужным флагом или <c>null</c>.</summary>
        private static ScanView? FindView(IReadOnlyList<ScanView> views, bool aim)
        {
            for (var i = 0; i < views.Count; i++)
            {
                if (aim ? views[i].Aim : views[i].Nearest)
                    return views[i];
            }

            return null;
        }

        private void UpdateFrame(int index, ScanView? view)
        {
            if (!view.HasValue || !TryProjectBounds(view.Value.Bounds, out var center, out var size))
            {
                HideFrame(index);
                return;
            }

            var frame = _frames[index];
            if (frame == null)
                return;

            if (!frame.activeSelf)
                frame.SetActive(true);

            var rect = (RectTransform)frame.transform;
            rect.sizeDelta = new Vector2(size.x + _framePadding * 2f, size.y + _framePadding * 2f);
            rect.anchoredPosition = center;
            _frameGroups[index].alpha = Mathf.Clamp01(view.Value.Strength);
        }

        private void HideFrame(int index)
        {
            var frame = _frames[index];
            if (frame != null && frame.activeSelf)
                frame.SetActive(false);
        }

        /// <summary>
        /// Подсказка и индикатор режима. Пока идёт волна, индикатор пульсирует в такт
        /// кольцу; после прохода волны горит ровно — режим остался включённым, но волны
        /// больше нет, и мигать нечему.
        /// </summary>
        private void UpdateStatus(bool visible)
        {
            if (_hint == null || _status == null)
                return;

            var scanning = _scanner != null && _scanner.IsScanning;

            // В первом лице сканера нет: подсказка про Q там врала бы.
            var hintVisible = visible && !scanning;
            if (_hint.gameObject.activeSelf != hintVisible)
                _hint.gameObject.SetActive(hintVisible);

            if (_status.gameObject.activeSelf != scanning)
                _status.gameObject.SetActive(scanning);

            if (!scanning)
                return;

            // Пока волна идёт — пульс в такт кольцу, после — ровный акцент.
            var alpha = _scanner.IsWaveActive
                ? Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(_scanner.SonarWave))
                : 1f;
            _status.color = WithAlpha(_accent, alpha);
        }

        /// <summary>
        /// Счётчик FPS: среднее за интервал, а не мгновенное значение — иначе цифра
        /// дёргается на каждом кадре. Время нескалированное: счётчик показывает
        /// частоту отрисовки и при остановленной игре.
        /// </summary>
        private void UpdateFps()
        {
            if (_fpsValue == null)
                return;

            _fpsFrames++;
            _fpsElapsed += Time.unscaledDeltaTime;

            var interval = Mathf.Max(0.05f, _fpsInterval);
            if (_fpsElapsed < interval)
                return;

            var fps = Mathf.RoundToInt(_fpsFrames / _fpsElapsed);
            _fpsFrames = 0;
            _fpsElapsed = 0f;

            // Строка пересоздаётся только при смене значения: на стабильных 60 FPS
            // аллокаций нет.
            if (fps == _fpsShown)
                return;

            _fpsShown = fps;
            _fpsValue.text = fps.ToString(CultureInfo.InvariantCulture);
        }

        // ---------- примитивы ----------

        /// <summary>Пара штрихов в углу: горизонтальный и вертикальный.</summary>
        private void AddBracket(Transform parent, Vector2 anchor, float dirX, float dirY)
        {
            var color = WithAlpha(_accent, 0.85f);
            var pivot = new Vector2(dirX > 0f ? 0f : 1f, dirY > 0f ? 0f : 1f);

            var horizontal = NewImage(parent, "Bracket H", color);
            var hRect = horizontal.rectTransform;
            hRect.anchorMin = anchor;
            hRect.anchorMax = anchor;
            hRect.pivot = pivot;
            hRect.sizeDelta = new Vector2(18f, 2f);
            hRect.anchoredPosition = new Vector2(dirX * 2f, dirY * 2f);

            var vertical = NewImage(parent, "Bracket V", color);
            var vRect = vertical.rectTransform;
            vRect.anchorMin = anchor;
            vRect.anchorMax = anchor;
            vRect.pivot = pivot;
            vRect.sizeDelta = new Vector2(2f, 18f);
            vRect.anchoredPosition = new Vector2(dirX * 2f, dirY * 2f);
        }

        /// <summary>Тёмная обводка текста: единственный способ удержать читаемость на светлом фоне.</summary>
        private void AddOutline(TextMeshProUGUI text)
        {
            if (text == null || _labelOutline <= 0f)
                return;

            text.outlineWidth = _labelOutline;
            text.outlineColor = _labelOutlineColor;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static GameObject NewContainer(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return go;
        }

        private static void NewTick(
            Transform parent, string name, Color color, Vector2 position, Vector2 size, float rotation)
        {
            var image = NewImage(parent, name, color);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI NewText(
            Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 boxSize, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(
                anchor.x <= 0f ? 0f : anchor.x >= 1f ? 1f : 0.5f,
                anchor.y <= 0f ? 0f : anchor.y >= 1f ? 1f : 0.5f);
            rect.sizeDelta = boxSize;
            rect.anchoredPosition = position;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }
    }
}
