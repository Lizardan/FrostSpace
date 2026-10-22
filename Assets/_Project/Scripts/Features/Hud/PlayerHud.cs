using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FrostSpace.Features.Scan;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Hud
{
    /// <summary>
    /// Игровой HUD: перекрестие, лента модулей справа и подпись отсканированного объекта.
    ///
    /// Собирается кодом при старте сцены — в <c>Game</c> нет ни Canvas, ни EventSystem,
    /// а HUD только отображает состояние, мышь в него не кликает. Поэтому GraphicRaycaster
    /// не добавляется: без него UI не может «съесть» захваченный курсор и ничего не стоит
    /// в EventSystem.
    ///
    /// Оформление повторяет меню (<c>MainMenuScreen</c>): тот же циановый акцент, dim-текст,
    /// разрядка букв и размеры под 1920×1080. Все подписи латиницей — в атласе
    /// <c>LiberationSans SDF</c> нет кириллицы.
    ///
    /// Лента модулей показывается только в третьем лице: в первом лице и на байке модули
    /// не работают, и заголовок из двух пунктов вводил бы в заблуждение.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(250)]
    public sealed class PlayerHud : MonoBehaviour
    {
        private static readonly ViewModule[] Modules = (ViewModule[])Enum.GetValues(typeof(ViewModule));

        /// <summary>Подпись модуля в ленте HUD.</summary>
        private static string ModuleLabel(ViewModule module) => module switch
        {
            ViewModule.Aperture => "APERTURE",
            ViewModule.Scanner => "SCANNER",
            _ => module.ToString().ToUpperInvariant()
        };

        [Header("Ссылки")]
        [Tooltip("Риг камеры: видимость ленты модулей и проекция подписи")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Модули HUD: какой модуль выбран")]
        [SerializeField] private ViewModuleSwitch _modules;
        [Tooltip("Сканер: что нарисовать в подписи")]
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

        [Header("Лента модулей")]
        [Tooltip("Шаг между строками, px")]
        [SerializeField] private float _rowSpacing = 38f;
        [Tooltip("Отступ ленты от правого края, px")]
        [SerializeField] private float _carouselMargin = 68f;
        [SerializeField] private float _carouselWidth = 280f;
        [Tooltip("Фон ленты: тот же полупрозрачный циан, что у подписи сканера")]
        [SerializeField] private Color _carouselFill = new(0.13f, 0.38f, 0.42f, 0.68f);
        [Tooltip("Насколько фон ленты шире строк, px")]
        [SerializeField] private float _carouselPadding = 18f;
        [SerializeField] private float _rowFontSize = 21f;
        [Tooltip("Сглаживание прокрутки ленты, 1/с")]
        [SerializeField] private float _scrollSmoothing = 16f;

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
        [Header("Подложка подписи")]
        [Tooltip("Заливка плашки: полупрозрачный циан — текст на нём читается и на снегу")]
        [SerializeField] private Color _plateFill = new(0.13f, 0.38f, 0.42f, 0.68f);
        [Tooltip("Высота плашки, px")]
        [SerializeField] private float _plateHeight = 104f;
        [Tooltip("Поле вокруг текста внутри плашки, px")]
        [SerializeField] private float _platePadding = 18f;
        [Tooltip("Отступ рамки цели от экранной проекции объекта, px")]
        [SerializeField] private float _framePadding = 14f;

        private RectTransform _canvasRect;
        private GameObject _crosshair;
        private GameObject _carousel;
        private RectTransform[] _rows;
        private TextMeshProUGUI[] _rowLabels;
        private Image[] _rowMarkers;

        private GameObject _label;
        private RectTransform _labelRect;
        private Image _labelDiagonal;
        private Image _labelArmImage;
        private TextMeshProUGUI _labelName;
        private TextMeshProUGUI _labelCategory;
        private TextMeshProUGUI _labelDistance;
        private RectTransform _plateRect;
        private CanvasGroup _plateGroup;
        private GameObject _frame;
        private CanvasGroup _frameGroup;

        private float _scrollOffset;
        private int _selected;
        private Camera _camera;

        // Кэш подписи скана: строки и ширина плашки пересчитываются только при изменении.
        private string _lastName;
        private string _lastCategory;
        private float _lastDistance = -1f;
        private bool _labelTextDirty = true;
        private float _plateWidth = 180f;

        private void Awake()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_modules == null)
                _modules = FindAnyObjectByType<ViewModuleSwitch>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();

            _camera = _rig != null ? _rig.ViewCamera : Camera.main;

            BuildCanvas();
            BuildCrosshair();
            BuildCarousel();
            BuildScanLabel();
            BuildTargetFrame();
        }

        private void LateUpdate()
        {
            var mode = _rig != null ? _rig.Mode : CameraMode.ThirdPerson;
            var third = mode == CameraMode.ThirdPerson;

            if (_crosshair != null)
                _crosshair.SetActive(third || mode == CameraMode.FirstPerson);

            UpdateCarousel(third);
            UpdateScanLabel(third);
            UpdateTargetFrame(third);
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

        private void BuildCarousel()
        {
            _carousel = NewContainer(_canvasRect, "Modules", new Vector2(1f, 0.5f),
                new Vector2(_carouselWidth, _rowSpacing * Modules.Length + 24f));
            var rect = (RectTransform)_carousel.transform;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-_carouselMargin, 0f);

            // Фон ленты: без него строки висят в воздухе и теряются на светлом снегу.
            // Рисуется первым ребёнком, поэтому остаётся за строками.
            var back = NewImage(_carousel.transform, "Back", _carouselFill);
            var backRect = back.rectTransform;
            Stretch(backRect);
            backRect.offsetMin = new Vector2(-_carouselPadding, -12f);
            backRect.offsetMax = new Vector2(_carouselPadding, 12f);

            _rows = new RectTransform[Modules.Length];
            _rowLabels = new TextMeshProUGUI[Modules.Length];
            _rowMarkers = new Image[Modules.Length];

            for (var i = 0; i < Modules.Length; i++)
            {
                var row = new GameObject(Modules[i].ToString(), typeof(RectTransform));
                row.transform.SetParent(_carousel.transform, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = new Vector2(1f, 0.5f);
                rowRect.anchorMax = new Vector2(1f, 0.5f);
                rowRect.pivot = new Vector2(1f, 0.5f);
                rowRect.sizeDelta = new Vector2(_carouselWidth, 30f);
                _rows[i] = rowRect;

                var label = NewText(row.transform, "Label",
                    $"{i + 1:00}   {ModuleLabel(Modules[i])}", _rowFontSize, _dim,
                    TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(-24f, 0f),
                    new Vector2(_carouselWidth - 24f, 30f), 8f);
                _rowLabels[i] = label;

                var marker = NewImage(row.transform, "Marker", new Color(_accent.r, _accent.g, _accent.b, 0f));
                var markerRect = marker.rectTransform;
                markerRect.anchorMin = new Vector2(1f, 0.5f);
                markerRect.anchorMax = new Vector2(1f, 0.5f);
                markerRect.pivot = new Vector2(1f, 0.5f);
                markerRect.sizeDelta = new Vector2(3f, 26f);
                markerRect.anchoredPosition = Vector2.zero;
                _rowMarkers[i] = marker;
            }
        }

        private void BuildScanLabel()
        {
            _label = NewContainer(_canvasRect, "Scan Label", new Vector2(0.5f, 0.5f), new Vector2(1f, 1f));
            _labelRect = (RectTransform)_label.transform;
            _labelRect.pivot = Vector2.zero;
            _labelRect.anchoredPosition = Vector2.zero;

            _labelDiagonal = NewLine(_label.transform, "Diagonal", out var diagonalRect);
            // Диагональ задаётся от точки объекта вверх-наискосок; геометрия
            // пересчитывается каждый кадр, потому что цель уезжает.
            diagonalRect.anchorMin = Vector2.zero;
            diagonalRect.anchorMax = Vector2.zero;

            _labelArmImage = NewLine(_label.transform, "Arm", out var armRect);
            armRect.anchorMin = Vector2.zero;
            armRect.anchorMax = Vector2.zero;

            var dx = _labelLift * 0.75f;
            var textX = dx + _labelArm + 14f;
            var sideBox = new Vector2(460f, _labelCategorySize * 1.5f);
            BuildHoloPlate(textX);

            _labelCategory = NewText(_label.transform, "Category", "SCAN", _labelCategorySize, _dim,
                TextAlignmentOptions.Left, Vector2.zero, Vector2.zero, sideBox, 10f);
            _labelCategory.rectTransform.pivot = new Vector2(0f, 0.5f);
            _labelCategory.rectTransform.anchoredPosition =
                new Vector2(textX, _labelLift + _labelNameSize * 0.8f);

            _labelName = NewText(_label.transform, "Name", string.Empty, _labelNameSize, _text,
                TextAlignmentOptions.Left, Vector2.zero, Vector2.zero,
                new Vector2(460f, _labelNameSize * 1.4f), 4f);
            _labelName.rectTransform.pivot = new Vector2(0f, 0.5f);
            _labelName.rectTransform.anchoredPosition = new Vector2(textX, _labelLift);

            _labelDistance = NewText(_label.transform, "Distance", string.Empty, _labelCategorySize,
                new Color(_accent.r, _accent.g, _accent.b, 0.75f),
                TextAlignmentOptions.Left, Vector2.zero, Vector2.zero, sideBox, 10f);
            _labelDistance.rectTransform.pivot = new Vector2(0f, 0.5f);
            _labelDistance.rectTransform.anchoredPosition =
                new Vector2(textX, _labelLift - _labelNameSize * 0.8f - 6f);

            // Обводка создаёт у текста отдельный экземпляр материала (TMP): дефолтный
            // шрифт проекта при этом не мутируется и меню не меняется.
            AddOutline(_labelCategory);
            AddOutline(_labelName);
            AddOutline(_labelDistance);

            _label.SetActive(false);
        }
        /// <summary>
        /// Квадратная рамка цели: те же уголки, что у плашки, но без фона — объект просто
        /// обводится по экранной проекции своих границ. Контейнер меняет размер под цель,
        /// уголки привязаны к его углам, поэтому рамка масштабируется вместе с объектом.
        /// </summary>
        private void BuildTargetFrame()
        {
            _frame = NewContainer(_canvasRect, "Target Frame", new Vector2(0.5f, 0.5f),
                new Vector2(180f, 180f));
            _frameGroup = _frame.AddComponent<CanvasGroup>();
            _frameGroup.alpha = 0f;
            _frameGroup.interactable = false;
            _frameGroup.blocksRaycasts = false;

            var rect = (RectTransform)_frame.transform;
            AddBracket(rect, new Vector2(0f, 1f), 1f, -1f);
            AddBracket(rect, new Vector2(0f, 0f), 1f, 1f);
            AddBracket(rect, new Vector2(1f, 1f), -1f, -1f);
            AddBracket(rect, new Vector2(1f, 0f), -1f, 1f);

            _frame.SetActive(false);
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
        /// Рамка вокруг цели: появляется и гаснет вместе с подписью и контуром скана.
        /// </summary>
        private void UpdateTargetFrame(bool visible)
        {
            if (_frame == null)
                return;

            var show = visible && _scanner != null && _scanner.HasTarget && _scanner.IsVisible;
            if (!show || !TryProjectBounds(_scanner.TargetBounds, out var center, out var size))
            {
                if (_frame.activeSelf)
                    _frame.SetActive(false);
                return;
            }

            if (!_frame.activeSelf)
                _frame.SetActive(true);

            var rect = (RectTransform)_frame.transform;
            rect.sizeDelta = new Vector2(size.x + _framePadding * 2f, size.y + _framePadding * 2f);
            rect.anchoredPosition = center;
            _frameGroup.alpha = Mathf.Clamp01(_scanner.Strength);
        }

        /// <summary>
        /// Подложка под подпись: ровный полупрозрачный циан цифрового слоя, тонкая линия
        /// сверху и уголки по краям — без сетки и других узоров.
        /// Без неё имя объекта терялось на светлом снегу и сливалось с тёмным металлом.
        ///
        /// Альфа задаётся группой: подпись гаснет целиком вместе с контуром скана.
        /// </summary>
        private void BuildHoloPlate(float textX)
        {
            var plate = NewContainer(_label.transform, "Holo Plate", Vector2.zero,
                new Vector2(320f, _plateHeight));
            _plateRect = (RectTransform)plate.transform;
            _plateRect.pivot = new Vector2(0f, 0.5f);
            _plateRect.anchoredPosition = new Vector2(textX - _platePadding, _labelLift + 2f);

            _plateGroup = plate.AddComponent<CanvasGroup>();
            _plateGroup.alpha = 0f;
            _plateGroup.interactable = false;
            _plateGroup.blocksRaycasts = false;

            // Фон — ровная полупрозрачная заливка цветом цифрового слоя, без узоров.
            var fill = NewImage(plate.transform, "Fill", _plateFill);
            Stretch(fill.rectTransform);

            // Верхняя линия — «шапка прибора»: по ней читается верхний край плашки.
            var header = NewImage(plate.transform, "Header", WithAlpha(_accent, 0.55f));
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(-20f, 1.5f);
            headerRect.anchoredPosition = new Vector2(0f, -7f);

            // Уголки: по два коротких штриха на угол — плашка читается как экран.
            AddBracket(plate.transform, new Vector2(0f, 1f), 1f, -1f);
            AddBracket(plate.transform, new Vector2(0f, 0f), 1f, 1f);
            AddBracket(plate.transform, new Vector2(1f, 1f), -1f, -1f);
            AddBracket(plate.transform, new Vector2(1f, 0f), -1f, 1f);
        }

        /// <summary>Пара штрихов в углу плашки: горизонтальный и вертикальный.</summary>
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

        /// <summary>Растягивает элемент по всей плашке.</summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }



        // ---------- обновление ----------

        /// <summary>
        /// Лента модулей: выбранная строка стоит на месте, остальные уезжают вниз.
        /// Смещение сглаживается экспоненциально, поэтому прокрутка идёт плавно,
        /// а выбор при этом ступенчатый — за кадр лента не проскакивает модуль.
        /// </summary>
        private void UpdateCarousel(bool visible)
        {
            if (_carousel == null)
                return;

            _carousel.SetActive(visible);
            if (!visible || _rows == null)
                return;

            _selected = _modules != null ? (int)_modules.Current : 0;
            _scrollOffset = ModuleCarousel.Approach(_scrollOffset, 0f, _scrollSmoothing, Time.unscaledDeltaTime);

            for (var i = 0; i < _rows.Length; i++)
            {
                var offset = ModuleCarousel.Offset(i, _selected, _scrollOffset);
                var emphasis = ModuleCarousel.Emphasis(offset);

                _rows[i].anchoredPosition = new Vector2(0f, -offset * _rowSpacing);

                var labelColor = Color.Lerp(_dim, _accent, emphasis);
                labelColor.a = Mathf.Lerp(0.35f, 1f, emphasis);
                _rowLabels[i].color = labelColor;

                var markerColor = _accent;
                markerColor.a = emphasis;
                _rowMarkers[i].color = markerColor;
            }
        }

        /// <summary>
        /// Подпись объекта: выносная линия от объекта вверх-наискосок и плечо вбок,
        /// на конце — категория, имя и дистанция. Гаснет вместе с контуром скана.
        /// </summary>
        private void UpdateScanLabel(bool visible)
        {
            if (_label == null)
                return;

            var show = visible && _scanner != null && _scanner.HasTarget && _scanner.IsVisible;
            if (!show)
            {
                if (_label.activeSelf)
                    _label.SetActive(false);
                return;
            }

            var camera = ResolveCamera();
            if (camera == null)
                return;

            var screen = camera.WorldToScreenPoint(_scanner.Current.Anchor);
            if (screen.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screen, null, out var local))
            {
                _label.SetActive(false);
                return;
            }

            if (!_label.activeSelf)
                _label.SetActive(true);

            _labelRect.anchoredPosition = local;

            var strength = Mathf.Clamp01(_scanner.Strength);
            var dx = _labelLift * 0.75f;
            PlaceSegment(_labelDiagonal.rectTransform, Vector2.zero, new Vector2(dx, _labelLift));
            PlaceSegment(_labelArmImage.rectTransform, new Vector2(dx, _labelLift),
                new Vector2(dx + _labelArm, _labelLift));

            var lineColor = new Color(_accent.r, _accent.g, _accent.b, 0.95f * strength);
            _labelDiagonal.color = lineColor;
            _labelArmImage.color = lineColor;

            _labelCategory.text = _scanner.Current.Category;
            _labelCategory.color = WithAlpha(Color.Lerp(_dim, _accent, 0.75f), strength);

            _labelName.text = _scanner.Current.Name;
            _labelName.color = WithAlpha(_text, strength);

            // Строки и ширина плашки меняются редко: пересчёт каждый кадр — это
            // аллокация строки и три дорогих GetPreferredValues.
            if (!ReferenceEquals(_lastName, _scanner.Current.Name))
            {
                _lastName = _scanner.Current.Name;
                _labelTextDirty = true;
            }

            if (!ReferenceEquals(_lastCategory, _scanner.Current.Category))
            {
                _lastCategory = _scanner.Current.Category;
                _labelTextDirty = true;
            }

            var distance = _scanner.Current.Distance;
            if (!Mathf.Approximately(distance, _lastDistance))
            {
                _lastDistance = distance;

                // Инвариантная культура: на русской локали разделитель стал бы запятой
                // и подпись выбивалась бы из латинского стиля HUD.
                _labelDistance.text = distance.ToString("0.0", CultureInfo.InvariantCulture) + " M";
                _labelTextDirty = true;
            }

            _labelDistance.color = WithAlpha(_accent, 0.9f * strength);
            if (_plateRect != null && _plateGroup != null)
            {
                if (_labelTextDirty)
                {
                    _labelTextDirty = false;

                    // Ширина подложки идёт за текстом: плашка не тянется в пустоту и не режет
                    // длинные имена одним и тем же размером на всех объектах.
                    var width = Mathf.Max(
                        _labelName.GetPreferredValues(_scanner.Current.Name).x,
                        _labelCategory.GetPreferredValues(_scanner.Current.Category).x);
                    width = Mathf.Max(width, _labelDistance.GetPreferredValues(_labelDistance.text).x);
                    _plateWidth = Mathf.Clamp(width + _platePadding * 2f, 180f, 520f);
                }

                _plateRect.sizeDelta = new Vector2(_plateWidth, _plateHeight);
                _plateGroup.alpha = strength;
            }
        }

        /// <summary>Ставит отрезок между двумя точками в локальных координатах подписи.</summary>
        private void PlaceSegment(RectTransform rect, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(delta.magnitude, _labelLineWidth);
            rect.anchoredPosition = from + delta * 0.5f;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        /// <summary>Тёмная обводка текста: единственный способ удержать читаемость на светлом фоне.</summary>
        private void AddOutline(TextMeshProUGUI text)
        {
            if (text == null || _labelOutline <= 0f)
                return;

            text.outlineWidth = _labelOutline;
            text.outlineColor = _labelOutlineColor;
        }

        // ---------- примитивы ----------

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

        private static Image NewLine(Transform parent, string name, out RectTransform rect)
        {
            var image = NewImage(parent, name, new Color(0.302f, 0.902f, 1f, 0f));
            rect = image.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 2f);
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
