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

        // Ключ общий с MusicManager: он же применяет сохранённую громкость на старте игры.
        private const string VolumeKey = MusicManager.MasterVolumeKey;
        private const float ButtonWidth = 360f;
        private const float ButtonHeight = 64f;
        private const float ButtonStep = 82f;
        private const float MarkerIdleWidth = 3f;
        private const float LabelIdleOffset = 28f;
        private const float LabelIdleSpacing = 10f;
        private const float UnderlineThickness = 2f;

        /// <summary>
        /// Горизонтальный градиент для свечения: ярко у маркера, плавно уходит в прозрачность
        /// вправо. Текстура одна на все кнопки и создаётся по первому требованию.
        /// </summary>
        private static Texture2D _glowTexture;

        private GameObject _settingsPanel;
        private Slider _volume;
        private Button[] _menuButtons;
        private bool _cursorApplied;

        private void Awake()
        {
            EnsureEventSystem();
            BuildInterface();
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
        }

        private void OnDisable()
        {
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
            shade.texture = BuildShadeTexture();
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

            _settingsPanel = BuildSettingsPanel(root);
            _settingsPanel.SetActive(false);
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
            fx.Duration = _hoverSeconds;
            fx.MarkerIdleWidth = MarkerIdleWidth;
            fx.MarkerHoverWidth = _hoverMarkerWidth;
            fx.LabelIdleOffset = LabelIdleOffset;
            fx.LabelHoverOffset = _hoverLabelOffset;
            fx.SpacingIdle = LabelIdleSpacing;
            fx.SpacingHover = _hoverSpacing;
            fx.AccentMix = _hoverAccentMix;
            fx.GlowAlpha = _hoverGlowAlpha;
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
