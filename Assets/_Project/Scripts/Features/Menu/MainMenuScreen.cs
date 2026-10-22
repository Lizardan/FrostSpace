using FrostSpace.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Интерфейс главного меню поверх живой диорамы: заголовок, вертикальное
    /// меню из трёх пунктов и модальная панель настроек с громкостью.
    ///
    /// Как и <c>BootScreen</c>, вся геометрия собирается кодом: сцена меню не
    /// тащит Canvas, а стиль экранов живёт в одном месте и правится в одном месте.
    /// Строки — латиница, потому что атлас <c>LiberationSans SDF</c> не содержит
    /// кириллицы. Комментарии остаются русскими.
    ///
    /// Кнопка «START» отдаёт переход <see cref="SceneController"/>, а не грузит сцену
    /// сама: белый занавес и удержание статуса — его ответственность.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class MainMenuScreen : MonoBehaviour
    {
        [Header("Переход")]
        [Tooltip("Сцена, которая грузится по кнопке START")]
        [SerializeField] private string _gameScene = "Game";

        [Header("Тексты")]
        [SerializeField] private string _title = "FROSTSPACE";
        [SerializeField] private string _subtitle = "COLONY 07  /  NORTH PERIMETER";
        [SerializeField] private string _hint = "LINK READY";

        [Header("Палитра")]
        [Tooltip("Циан — единственный холодный акцент стиля")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);

        [Tooltip("Основной текст")]
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);

        [Tooltip("Приглушённый текст и неактивные элементы")]
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);

        [Tooltip("Фон модальной панели настроек")]
        [SerializeField] private Color _panelColor = new(0.016f, 0.026f, 0.038f, 0.94f);

        [Tooltip("Фон кнопки в покое")]
        [SerializeField] private Color _buttonIdle = new(0.055f, 0.085f, 0.105f, 0.82f);

        [Tooltip("Фон кнопки под курсором")]
        [SerializeField] private Color _buttonHover = new(0.10f, 0.19f, 0.24f, 0.95f);

        [Header("Подсветка пунктов")]
        [Tooltip("За сколько секунд подсветка доезжает до конечного состояния")]
        [SerializeField] private float _hoverSeconds = 0.16f;

        [Tooltip("Толщина акцентного маркера слева под курсором")]
        [SerializeField] private float _hoverMarkerWidth = 7f;

        [Tooltip("Насколько строка уезжает вправо под курсором")]
        [SerializeField] private float _hoverLabelOffset = 42f;

        [Tooltip("Разрядка букв под курсором")]
        [SerializeField] private float _hoverSpacing = 18f;

        [Tooltip("Насколько строка уходит в цвет акцента под курсором, 0..1")]
        [SerializeField, Range(0f, 1f)] private float _hoverAccentMix = 0.35f;

        [Tooltip("Прозрачность холодного свечения под курсором, 0..1")]
        [SerializeField, Range(0f, 1f)] private float _hoverGlowAlpha = 0.22f;

        [Header("Виды меню")]
        [Tooltip("Переключатель пяти видов меню на MenuFlow")]
        [SerializeField] private MenuViewSwitch _viewSwitch;

        [Tooltip("Скорость переезда подчёркивания между пунктами, 1/с")]
        [SerializeField] private float _viewIndicatorSpeed = 12f;

        // Ключ общий с MusicManager: он же применяет сохранённую громкость на старте игры.
        private const string VolumeKey = MusicManager.MasterVolumeKey;
        private const float ButtonWidth = 360f;
        private const float ButtonHeight = 64f;
        private const float ButtonStep = 82f;
        private const float MarkerIdleWidth = 3f;
        private const float LabelIdleOffset = 28f;
        private const float LabelIdleSpacing = 10f;
        private const float UnderlineThickness = 2f;

        // Лента видов внизу: ширина пункта, шаг и внутренний отступ панели.
        private const float ViewItemWidth = 200f;
        private const float ViewItemHeight = 44f;
        private const float ViewItemStep = 208f;
        private const float ViewRibbonPadding = 8f;

        /// <summary>
        /// Подписи на случай, если вид ещё не назначен в MenuViewSwitch:
        /// лента должна собираться и без сцены-источника данных.
        /// </summary>
        private static readonly string[] FallbackViewLabels =
            { "PERIMETER", "OPEN", "REPAIR", "LAB", "DEPOT" };

        /// <summary>
        /// Горизонтальный градиент для свечения: ярко у маркера, плавно уходит в прозрачность
        /// вправо. Текстура одна на все кнопки и создаётся по первому требованию.
        /// </summary>
        private static Texture2D _glowTexture;

        /// <summary>Затемнение под лентой меню. Тоже одна на весь экран.</summary>
        private static Texture2D _shadeTexture;

        /// <summary>
        /// Сброс статики между запусками Play Mode: домен не перезагружается, поэтому
        /// сгенерированные текстуры иначе живут вечно и переживают сцену.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_glowTexture != null)
                Object.Destroy(_glowTexture);

            if (_shadeTexture != null)
                Object.Destroy(_shadeTexture);

            _glowTexture = null;
            _shadeTexture = null;
        }

        private GameObject _settingsPanel;
        private Slider _volume;
        private Button[] _menuButtons;
        private Button[] _viewButtons;
        private TextMeshProUGUI[] _viewLabels;
        private RectTransform _viewIndicator;
        private RectTransform _viewIndicatorTrack;
        private Image _viewIndicatorFill;
        private CanvasGroup _viewFade;
        private Image _viewFadeLine;
        private float _viewIndicatorTargetX;
        private int _viewIndex;
        private bool _cursorApplied;

        private void Awake()
        {
            EnsureEventSystem();
            // Виды разрешаем до сборки UI: лента берёт из них подписи и количество.
            ResolveViewSwitch();
            BuildInterface();
            BindViewSwitch();
            ShowCursor();
        }

        private void Update()
        {
            // Курсор мог перехватить игровой риг предыдущей сцены — возвращаем каждый кадр,
            // пока меню на экране и настройки открыты.
            if (!_cursorApplied)
                ShowCursor();

            var controller = SceneController.Instance;
            var busy = controller != null && controller.IsBusy;
            for (var i = 0; i < _menuButtons.Length; i++)
            {
                if (_menuButtons[i] != null)
                    _menuButtons[i].interactable = !busy;
            }

            if (busy && _settingsPanel != null && _settingsPanel.activeSelf)
                _settingsPanel.SetActive(false);

            if (_settingsPanel != null && _settingsPanel.activeSelf &&
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                _settingsPanel.SetActive(false);

            var settingsOpen = _settingsPanel != null && _settingsPanel.activeSelf;
            var canSwitch = !busy && _viewSwitch != null && !_viewSwitch.IsSwitching;
            if (_viewButtons != null)
            {
                for (var i = 0; i < _viewButtons.Length; i++)
                {
                    if (_viewButtons[i] != null)
                        _viewButtons[i].interactable = canSwitch;
                }
            }

            // Меню не должно перелистываться под руками: пауза на настройках и переходе.
            if (_viewSwitch != null)
                _viewSwitch.SetAutoAdvancePaused(busy || settingsOpen);

            if (_viewSwitch != null && canSwitch && !settingsOpen && Keyboard.current != null)
            {
                var count = _viewButtons != null ? _viewButtons.Length : 0;
                for (var i = 0; i < count && i < 9; i++)
                {
                    if (!Keyboard.current[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                        continue;

                    OnViewPressed(i);
                    break;
                }
            }

            var smoothing = 1f - Mathf.Exp(-_viewIndicatorSpeed * Time.unscaledDeltaTime);
            if (_viewIndicator != null)
            {
                var x = Mathf.Lerp(
                    _viewIndicator.anchoredPosition.x, _viewIndicatorTargetX, smoothing);
                _viewIndicator.anchoredPosition = new Vector2(
                    x, _viewIndicator.anchoredPosition.y);
            }

            if (_viewIndicatorTrack != null)
            {
                var x = Mathf.Lerp(
                    _viewIndicatorTrack.anchoredPosition.x, _viewIndicatorTargetX, smoothing);
                _viewIndicatorTrack.anchoredPosition = new Vector2(
                    x, _viewIndicatorTrack.anchoredPosition.y);
            }

            // Заливка подчёркивания — остаток времени до авто-смены: пункт сам
            // показывает, сколько ещё ждать, и обнуляется после переключения.
            if (_viewIndicatorFill != null && _viewSwitch != null)
                _viewIndicatorFill.fillAmount = _viewSwitch.AutoProgress;
        }

        private void OnDisable()
        {
            if (_viewSwitch != null)
                _viewSwitch.VariantChanged -= OnVariantChanged;

            _cursorApplied = false;
            PlayerPrefs.Save();
        }

        private void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorApplied = true;
        }

        /// <summary>
        /// Меню живёт без EventSystem в сцене, поэтому модуль ввода создаётся кодом.
        /// Действия назначаются явно: у добавленного из кода модуля их нет, и без них
        /// клик не превращается в событие UI.
        /// </summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private void OnStartPressed()
        {
            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError("MainMenuScreen: нет SceneController — переход невозможен", this);
                return;
            }

            controller.LoadScene(_gameScene);
        }

        private void OnSettingsPressed()
        {
            if (_settingsPanel == null)
                return;

            _settingsPanel.SetActive(!_settingsPanel.activeSelf);
        }

        private void OnExitPressed()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnVolumeChanged(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        }

        private void ResolveViewSwitch()
        {
            if (_viewSwitch == null)
                _viewSwitch = GetComponent<MenuViewSwitch>();

            if (_viewSwitch == null)
            {
                Debug.LogError(
                    "MainMenuScreen: на MenuFlow нет MenuViewSwitch — переключение видов недоступно",
                    this);
            }
        }

        private void BindViewSwitch()
        {
            if (_viewSwitch == null)
                return;

            _viewSwitch.BindTransition(_viewFade, _viewFadeLine);
            _viewSwitch.VariantChanged += OnVariantChanged;
            OnVariantChanged(_viewSwitch.Variant);
        }

        private void OnViewPressed(int index)
        {
            _viewSwitch?.RequestVariant(index);
        }

        private void OnVariantChanged(int index)
        {
            var count = _viewButtons != null ? _viewButtons.Length : FallbackViewLabels.Length;
            _viewIndex = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            _viewIndicatorTargetX = ViewRibbonPadding + _viewIndex * ViewItemStep;
            RefreshViewButtons();
        }

        /// <summary>
        /// Активный пункт держит акцентный фон и цвет, неактивный остаётся
        /// приглушённым. Цвета пишутся в ColorTint, иначе Button вернул бы свой
        /// normalColor после первого наведения.
        /// </summary>
        private void RefreshViewButtons()
        {
            if (_viewButtons == null)
                return;

            var activeBackground = new Color(0.10f, 0.19f, 0.24f, 0.95f);
            for (var i = 0; i < _viewButtons.Length; i++)
            {
                var active = i == _viewIndex;
                if (_viewLabels != null && _viewLabels[i] != null)
                    _viewLabels[i].color = active ? _accent : _dim;

                if (_viewButtons[i] == null)
                    continue;

                var colors = _viewButtons[i].colors;
                colors.normalColor = active ? activeBackground : _buttonIdle;
                colors.selectedColor = active ? activeBackground : _buttonHover;
                colors.highlightedColor = _buttonHover;
                _viewButtons[i].colors = colors;

                if (_viewButtons[i].targetGraphic != null)
                    _viewButtons[i].targetGraphic.color = active ? activeBackground : _buttonIdle;
            }
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("MenuCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Без GraphicRaycaster луч указателя не попадает ни в один элемент:
            // кнопки и слайдер остаются видимыми, но не нажимаются.
            canvasObject.AddComponent<GraphicRaycaster>();

            var root = canvasObject.transform;

            // Затемнение слева: мягкий градиент в прозрачность, чтобы правый край
            // не читался как прямоугольник поверх диорамы.
            var shadeObject = new GameObject("Shade", typeof(RectTransform));
            shadeObject.transform.SetParent(root, false);
            var shadeRect = (RectTransform)shadeObject.transform;
            shadeRect.anchorMin = new Vector2(0f, 0f);
            shadeRect.anchorMax = new Vector2(0.54f, 1f);
            shadeRect.offsetMin = Vector2.zero;
            shadeRect.offsetMax = Vector2.zero;
            var shade = shadeObject.AddComponent<RawImage>();
            shade.texture = ShadeTexture;
            shade.color = Color.white;
            shade.raycastTarget = false;

            NewText(root, "Brand", "COLONY 07", 22f, _accent, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -64f), new Vector2(600f, 34f), 18f);

            NewText(root, "Title", _title, 92f, _text, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -150f), new Vector2(900f, 130f), 14f);

            NewText(root, "Subtitle", _subtitle, 22f, _dim, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -300f), new Vector2(900f, 34f), 12f);

            var accent = NewImage(root, "AccentLine", _accent);
            var accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 1f);
            accentRect.sizeDelta = new Vector2(64f, 3f);
            accentRect.anchoredPosition = new Vector2(96f, -352f);

            _menuButtons = new[]
            {
                NewButton(root, "Start", "START", new Vector2(96f, -424f), OnStartPressed),
                NewButton(root, "Settings", "SETTINGS", new Vector2(96f, -424f - ButtonStep), OnSettingsPressed),
                NewButton(root, "Exit", "EXIT", new Vector2(96f, -424f - ButtonStep * 2f), OnExitPressed),
            };

            NewText(root, "Hint", _hint, 20f, _dim, TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f), new Vector2(96f, 64f), new Vector2(700f, 30f), 14f);

            NewText(root, "Build", "BUILD 0.1  /  WEBGL", 20f, _dim, TextAlignmentOptions.BottomRight,
                new Vector2(1f, 0f), new Vector2(-96f, 64f), new Vector2(700f, 30f), 14f);

            BuildViewSwitch(root);
            BuildViewFade();

            _settingsPanel = BuildSettingsPanel(root);
            _settingsPanel.SetActive(false);
        }

        /// <summary>
        /// Нижняя лента видов: пункты по числу видов из MenuViewSwitch, по центру
        /// экрана, с дорожкой и заполняющимся подчёркиванием под активным пунктом.
        /// Ширина считается из количества видов: пятый пункт не ломает сетку,
        /// а шестой добавится сам.
        /// </summary>
        private void BuildViewSwitch(Transform root)
        {
            var count = _viewSwitch != null ? _viewSwitch.ViewCount : 0;
            if (count < 2)
                count = FallbackViewLabels.Length;

            var ribbonWidth = count * ViewItemStep - (ViewItemStep - ViewItemWidth);

            NewText(root, "ViewLabel", "VIEW MODE   /   AUTO", 15f, _dim,
                TextAlignmentOptions.Bottom, new Vector2(0.5f, 0f),
                new Vector2(0f, 164f), new Vector2(ribbonWidth, 22f), 10f);

            var panelObject = new GameObject("ViewSwitch", typeof(RectTransform));
            panelObject.transform.SetParent(root, false);
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(ribbonWidth + ViewRibbonPadding * 2f, 56f);
            panelRect.anchoredPosition = new Vector2(0f, 96f);

            var panel = panelObject.AddComponent<Image>();
            panel.color = new Color(_panelColor.r, _panelColor.g, _panelColor.b, 0.72f);
            panel.raycastTarget = false;

            var edge = NewImage(panelObject.transform, "TopEdge", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 2f);
            edgeRect.anchoredPosition = Vector2.zero;

            _viewButtons = new Button[count];
            _viewLabels = new TextMeshProUGUI[count];
            for (var i = 0; i < count; i++)
            {
                _viewButtons[i] = NewViewButton(
                    panelObject.transform,
                    $"View{i + 1}",
                    $"{i + 1:00}  {LabelFor(i)}",
                    i,
                    new Vector2(ViewRibbonPadding + i * ViewItemStep, 8f));
                _viewLabels[i] = _viewButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }

            // Дорожка на всю ширину пункта: видно, где стоим, даже когда заливка
            // прогресса обнулилась после переключения.
            var track = NewImage(panelObject.transform, "ActiveTrack",
                new Color(_accent.r, _accent.g, _accent.b, 0.16f));
            _viewIndicatorTrack = track.rectTransform;
            _viewIndicatorTrack.anchorMin = Vector2.zero;
            _viewIndicatorTrack.anchorMax = Vector2.zero;
            _viewIndicatorTrack.pivot = Vector2.zero;
            _viewIndicatorTrack.sizeDelta = new Vector2(ViewItemWidth, 3f);
            _viewIndicatorTrack.anchoredPosition = new Vector2(ViewRibbonPadding, 5f);

            var indicator = NewImage(panelObject.transform, "ActiveLine", _accent);
            _viewIndicator = indicator.rectTransform;
            _viewIndicator.anchorMin = Vector2.zero;
            _viewIndicator.anchorMax = Vector2.zero;
            _viewIndicator.pivot = Vector2.zero;
            _viewIndicator.sizeDelta = new Vector2(ViewItemWidth, 3f);
            _viewIndicator.anchoredPosition = new Vector2(ViewRibbonPadding, 5f);

            // Заливка слева направо: длина полосы и есть остаток до авто-смены.
            indicator.type = Image.Type.Filled;
            indicator.fillMethod = Image.FillMethod.Horizontal;
            indicator.fillOrigin = (int)Image.OriginHorizontal.Left;
            indicator.fillAmount = 0f;
            _viewIndicatorFill = indicator;

            _viewIndicatorTargetX = ViewRibbonPadding;
        }

        private string LabelFor(int index)
        {
            var label = _viewSwitch != null ? _viewSwitch.ViewLabel(index) : string.Empty;
            if (!string.IsNullOrEmpty(label))
                return label;

            return index < FallbackViewLabels.Length ? FallbackViewLabels[index] : "VIEW";
        }

        /// <summary>
        /// Оверлей перехода живёт на отдельном Canvas поверх меню: затемняет
        /// картинку и не перехватывает клики. Циановая линия — тот же цифровой
        /// акцент, что у силуэта и выреза, поэтому переход не вводит новый цвет.
        /// </summary>
        private void BuildViewFade()
        {
            var canvasObject = new GameObject("ViewFadeCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;

            _viewFade = canvasObject.AddComponent<CanvasGroup>();
            _viewFade.alpha = 0f;
            _viewFade.blocksRaycasts = false;
            _viewFade.interactable = false;

            var fadeObject = new GameObject("Fade", typeof(RectTransform));
            fadeObject.transform.SetParent(canvasObject.transform, false);
            var fadeRect = (RectTransform)fadeObject.transform;
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = Vector2.zero;
            fadeRect.offsetMax = Vector2.zero;
            var fade = fadeObject.AddComponent<Image>();
            fade.color = new Color(0.012f, 0.022f, 0.032f, 1f);
            fade.raycastTarget = false;

            var lineObject = new GameObject("ScanLine", typeof(RectTransform));
            lineObject.transform.SetParent(canvasObject.transform, false);
            var lineRect = (RectTransform)lineObject.transform;
            lineRect.anchorMin = new Vector2(0.35f, 0.5f);
            lineRect.anchorMax = new Vector2(0.65f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = new Vector2(0f, 2f);
            lineRect.anchoredPosition = Vector2.zero;

            _viewFadeLine = lineObject.AddComponent<Image>();
            _viewFadeLine.color = new Color(_accent.r, _accent.g, _accent.b, 0f);
            _viewFadeLine.raycastTarget = false;
        }

        private Button NewViewButton(
            Transform parent, string name, string label, int index, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(ViewItemWidth, ViewItemHeight);
            rect.anchoredPosition = position;

            var background = go.AddComponent<Image>();
            background.color = _buttonIdle;
            background.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = _buttonIdle;
            colors.highlightedColor = _buttonHover;
            colors.pressedColor = _accent;
            colors.selectedColor = _buttonHover;
            colors.disabledColor = new Color(_buttonIdle.r, _buttonIdle.g, _buttonIdle.b, 0.35f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            button.onClick.AddListener(() => OnViewPressed(index));

            NewText(go.transform, "Label", label, 19f, _dim, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(ViewItemWidth, ViewItemHeight), 8f);

            return button;
        }

        private GameObject BuildSettingsPanel(Transform root)
        {
            var panel = new GameObject("SettingsPanel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(680f, 400f);
            rect.anchoredPosition = Vector2.zero;

            var background = panel.AddComponent<Image>();
            background.color = _panelColor;
            background.raycastTarget = true;

            var edge = NewImage(panel.transform, "EdgeLine", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 3f);
            edgeRect.anchoredPosition = Vector2.zero;

            NewText(panel.transform, "SettingsTitle", "SETTINGS", 38f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(48f, -40f), new Vector2(560f, 48f), 12f);

            NewText(panel.transform, "VolumeLabel", "MASTER VOLUME", 20f, _dim, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(48f, -132f), new Vector2(400f, 30f), 14f);

            _volume = BuildSlider(panel.transform, new Vector2(48f, -186f), 584f);
            _volume.value = PlayerPrefs.GetFloat(VolumeKey, AudioListener.volume);
            AudioListener.volume = _volume.value;
            _volume.onValueChanged.AddListener(OnVolumeChanged);

            NewText(panel.transform, "SettingsNote", "SETTINGS PERSIST BETWEEN RUNS", 18f, _dim,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(48f, -252f),
                new Vector2(500f, 28f), 12f);

            var close = NewButton(panel.transform, "Close", "CLOSE", Vector2.zero, OnSettingsPressed);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(0f, 0f);
            closeRect.anchorMax = new Vector2(0f, 0f);
            closeRect.pivot = new Vector2(0f, 0f);
            closeRect.sizeDelta = new Vector2(240f, 56f);
            closeRect.anchoredPosition = new Vector2(48f, 40f);

            return panel;
        }

        private Slider BuildSlider(Transform parent, Vector2 position, float width)
        {
            var go = new GameObject("VolumeSlider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, 40f);
            rect.anchoredPosition = position;

            var background = NewImage(go.transform, "Track", new Color(0.11f, 0.16f, 0.19f, 1f));
            var trackRect = background.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, 6f);
            trackRect.anchoredPosition = Vector2.zero;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.pivot = new Vector2(0.5f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-24f, 6f);
            fillAreaRect.anchoredPosition = Vector2.zero;

            var fill = NewImage(fillArea.transform, "Fill", _accent);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.sizeDelta = new Vector2(12f, 0f);
            fillRect.anchoredPosition = Vector2.zero;

            var handleArea = new GameObject("HandleSlideArea", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(12f, 0f);
            handleAreaRect.offsetMax = new Vector2(-12f, 0f);

            var handle = NewImage(handleArea.transform, "Handle", _text);
            var handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(14f, 30f);
            handleRect.anchoredPosition = Vector2.zero;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        private Button NewButton(Transform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = position;

            var background = go.AddComponent<Image>();
            background.color = _buttonIdle;
            background.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = _buttonIdle;
            colors.highlightedColor = _buttonHover;
            colors.pressedColor = _accent;
            colors.selectedColor = _buttonHover;
            colors.disabledColor = new Color(_buttonIdle.r, _buttonIdle.g, _buttonIdle.b, 0.35f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.onClick.AddListener(action);

            // Свечение: горизонтальный градиент под текстом, проявляется при наведении.
            var glowObject = new GameObject("Glow", typeof(RectTransform));
            glowObject.transform.SetParent(go.transform, false);
            var glowRect = (RectTransform)glowObject.transform;
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-10f, -8f);
            glowRect.offsetMax = new Vector2(10f, 8f);
            var glow = glowObject.AddComponent<RawImage>();
            glow.texture = GlowTexture;
            glow.color = new Color(_accent.r, _accent.g, _accent.b, 0f);
            glow.raycastTarget = false;

            var marker = NewImage(go.transform, "Marker", _accent);
            var markerRect = marker.rectTransform;
            markerRect.anchorMin = new Vector2(0f, 0f);
            markerRect.anchorMax = new Vector2(0f, 1f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(MarkerIdleWidth, 0f);
            markerRect.anchoredPosition = Vector2.zero;

            // Линия снизу разворачивается слева направо и работает как «протяжка» сканера.
            var underline = NewImage(go.transform, "Underline", new Color(_accent.r, _accent.g, _accent.b, 0f));
            var underlineRect = underline.rectTransform;
            underlineRect.anchorMin = new Vector2(0f, 0f);
            underlineRect.anchorMax = new Vector2(0f, 0f);
            underlineRect.pivot = new Vector2(0f, 0f);
            underlineRect.sizeDelta = new Vector2(0f, UnderlineThickness);
            underlineRect.anchoredPosition = Vector2.zero;

            var text = NewText(go.transform, "Label", label, 30f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(LabelIdleOffset, 0f),
                new Vector2(ButtonWidth - 40f, 40f), LabelIdleSpacing);

            var fx = go.AddComponent<TechButtonFx>();
            fx.Initialize(
                duration: _hoverSeconds,
                markerIdleWidth: MarkerIdleWidth,
                markerHoverWidth: _hoverMarkerWidth,
                labelIdleOffset: LabelIdleOffset,
                labelHoverOffset: _hoverLabelOffset,
                spacingIdle: LabelIdleSpacing,
                spacingHover: _hoverSpacing,
                accentMix: _hoverAccentMix,
                glowAlpha: _hoverGlowAlpha);
            fx.Bind(markerRect, underlineRect, glow, text.rectTransform, text, ButtonWidth, _accent, _text);

            return button;
        }

        private static Texture2D GlowTexture
        {
            get
            {
                if (_glowTexture == null)
                    _glowTexture = BuildGlowTexture();

                return _glowTexture;
            }
        }

        private static Texture2D ShadeTexture
        {
            get
            {
                if (_shadeTexture == null)
                    _shadeTexture = BuildShadeTexture();

                return _shadeTexture;
            }
        }

        private static Texture2D BuildGlowTexture()
        {
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "MenuButtonGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var x = 0; x < width; x++)
            {
                var t = x / (float)(width - 1);
                texture.SetPixel(x, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - t, 2.2f)));
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D BuildShadeTexture()
        {
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "MenuShadeGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var x = 0; x < width; x++)
            {
                var t = x / (float)(width - 1);
                texture.SetPixel(x, 0, new Color(0.008f, 0.016f, 0.026f, Mathf.Lerp(0.78f, 0f, t * t)));
            }

            texture.Apply();
            return texture;
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
