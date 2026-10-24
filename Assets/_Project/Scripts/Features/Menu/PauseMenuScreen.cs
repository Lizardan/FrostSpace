using FrostSpace.Core;
using FrostSpace.Features.Scan;
using FrostSpace.Features.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Меню паузы игровой сцены: Esc замораживает время, отпускает курсор и показывает
    /// <c>RESUME</c> / <c>SETTINGS</c> / <c>MAIN MENU</c>.
    ///
    /// Геометрия, как и у <c>MainMenuScreen</c>, собирается кодом: в сцене <c>Game</c>
    /// нет ни Canvas, ни EventSystem, а стиль экранов живёт в одном месте. Отличие в том,
    /// что меню паузы обязано добавить <c>GraphicRaycaster</c> — HUD его не ставит, потому
    /// что курсор в игре захвачен и кликать некуда.
    ///
    /// Настройки лежат здесь же, в панели <c>SETTINGS</c>: музыка, детекция ближайшего
    /// объекта и детекция по прицелу. Значения хранит <see cref="GameSettings"/>,
    /// поэтому они переживают перезапуск и не дублируются в главном меню.
    ///
    /// Пауза — это три согласованных состояния: <c>Time.timeScale = 0</c>, камера
    /// в режиме паузы (обзор заморожен, курсор свободен) и выключенный сканер. Выход
    /// из паузы обязан вернуть все три, иначе игра останется стоять или перекрестие
    /// останется без захвата курсора.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Сцена главного меню")]
        [SerializeField] private string _mainMenuScene = "MainMenu";
        [Tooltip("Риг камеры: заморозка обзора и захват курсора")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Сканер: на паузе выключается, чтобы Q не переключал режим под меню")]
        [SerializeField] private ObjectScanner _scanner;

        [Header("Палитра")]
        [Tooltip("Циан — единственный холодный акцент стиля")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [Tooltip("Фон панели")]
        [SerializeField] private Color _panelColor = new(0.016f, 0.026f, 0.038f, 0.96f);
        [Tooltip("Затемнение сцены под меню")]
        [SerializeField] private Color _shadeColor = new(0.008f, 0.016f, 0.026f, 0.78f);
        [SerializeField] private Color _buttonIdle = new(0.055f, 0.085f, 0.105f, 0.82f);
        [SerializeField] private Color _buttonHover = new(0.10f, 0.19f, 0.24f, 0.95f);

        private const float ButtonWidth = 360f;
        private const float ButtonHeight = 62f;
        private const float ButtonStep = 78f;
        private const float MarkerWidth = 4f;

        /// <summary>Строка настроек с переключателем: плашка, подпись и состояние.</summary>
        private sealed class Switch
        {
            public Image Track;
            public RectTransform Knob;
            public TextMeshProUGUI State;
            public bool Value;

            /// <summary>Перерисовывает переключатель под текущее значение.</summary>
            public void Apply(Color accent, Color dim)
            {
                var on = Value;

                if (Track != null)
                    Track.color = on
                        ? new Color(accent.r, accent.g, accent.b, 0.55f)
                        : new Color(dim.r, dim.g, dim.b, 0.35f);

                if (Knob != null)
                {
                    Knob.anchorMin = new Vector2(on ? 1f : 0f, 0.5f);
                    Knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
                    Knob.anchoredPosition = new Vector2(on ? -16f : 16f, 0f);
                }

                if (State != null)
                {
                    State.text = on ? "ON" : "OFF";
                    State.color = on ? accent : dim;
                }
            }
        }

        private GameObject _root;
        private GameObject _menuPanel;
        private GameObject _settingsPanel;
        private Switch _music;
        private Switch _nearest;
        private Switch _aim;

        /// <summary>Меню открыто.</summary>
        public bool IsPaused { get; private set; }

        private void Awake()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();

            EnsureEventSystem();
            BuildInterface();
            SetVisible(false);
        }

        private void OnDisable()
        {
            // Сцена выгружается или компонент выключили — время обязано идти дальше,
            // иначе следующая сцена стартует замороженной.
            if (IsPaused)
                Resume();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            // Esc в настройках закрывает настройки, а не игру: иначе из панели
            // невозможно выйти, не возобновив игру.
            if (_settingsPanel != null && _settingsPanel.activeSelf)
            {
                _settingsPanel.SetActive(false);
                return;
            }

            if (IsPaused)
                Resume();
            else
                Open();
        }

        /// <summary>Открывает паузу. Публичный, потому что паузу может включить не только Esc.</summary>
        public void Open()
        {
            if (IsPaused)
                return;

            IsPaused = true;
            Time.timeScale = 0f;

            if (_rig != null)
                _rig.SetPaused(true);
            if (_scanner != null)
                _scanner.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _settingsPanel.SetActive(false);
            _menuPanel.SetActive(true);
            SetVisible(true);
            RefreshSwitches();
        }

        /// <summary>Возвращает игру: время, камера, сканер и курсор — в исходное состояние.</summary>
        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;

            if (_scanner != null)
                _scanner.enabled = true;
            if (_rig != null)
                _rig.SetPaused(false);

            SetVisible(false);
        }

        private void OnSettingsPressed()
        {
            var open = !_settingsPanel.activeSelf;
            _settingsPanel.SetActive(open);
            _menuPanel.SetActive(!open);

            if (open)
                RefreshSwitches();
        }

        private void OnMainMenuPressed()
        {
            // Сначала снимаем паузу: SceneController грузит сцену в реальном времени,
            // но следующая сцена не должна получить timeScale = 0.
            Resume();

            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError("PauseMenuScreen: нет SceneController — переход в меню невозможен", this);
                return;
            }

            controller.LoadScene(_mainMenuScene);
        }

        private void RefreshSwitches()
        {
            if (_music == null || _nearest == null || _aim == null)
                return;

            _music.Value = GameSettings.MusicEnabled;
            _nearest.Value = GameSettings.NearestDetection;
            _aim.Value = GameSettings.AimDetection;

            _music.Apply(_accent, _dim);
            _nearest.Apply(_accent, _dim);
            _aim.Apply(_accent, _dim);
        }

        private void ToggleMusic()
        {
            GameSettings.MusicEnabled = !GameSettings.MusicEnabled;
            RefreshSwitches();
        }

        private void ToggleNearest()
        {
            GameSettings.NearestDetection = !GameSettings.NearestDetection;
            RefreshSwitches();
        }

        private void ToggleAim()
        {
            GameSettings.AimDetection = !GameSettings.AimDetection;
            RefreshSwitches();
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
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

        private void BuildInterface()
        {
            _root = new GameObject("PauseCanvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Выше HUD (10) и ниже занавеса перехода SceneController (1000).
            canvas.sortingOrder = 50;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Без GraphicRaycaster луч указателя не попадает ни в один элемент:
            // кнопки остаются видимыми, но не нажимаются.
            _root.AddComponent<GraphicRaycaster>();

            var shade = NewImage(_root.transform, "Shade", _shadeColor);
            Stretch(shade.rectTransform);

            _menuPanel = BuildMenuPanel(_root.transform);
            _settingsPanel = BuildSettingsPanel(_root.transform);
            _settingsPanel.SetActive(false);
        }

        private GameObject BuildMenuPanel(Transform parent)
        {
            var panel = NewPanel(parent, "PausePanel", new Vector2(620f, 420f));

            NewText(panel.transform, "Title", "PAUSED", 44f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(52f, -46f), new Vector2(500f, 56f), 16f);

            NewText(panel.transform, "Subtitle", "COLONY 07  /  SYSTEM HOLD", 18f, _dim,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(52f, -104f),
                new Vector2(500f, 28f), 12f);

            NewButton(panel.transform, "Resume", "RESUME", new Vector2(52f, -168f), Resume);
            NewButton(panel.transform, "Settings", "SETTINGS", new Vector2(52f, -168f - ButtonStep),
                OnSettingsPressed);
            NewButton(panel.transform, "MainMenu", "MAIN MENU", new Vector2(52f, -168f - ButtonStep * 2f),
                OnMainMenuPressed);

            return panel;
        }

        private GameObject BuildSettingsPanel(Transform parent)
        {
            var panel = NewPanel(parent, "SettingsPanel", new Vector2(700f, 470f));

            NewText(panel.transform, "Title", "SETTINGS", 38f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(48f, -42f), new Vector2(560f, 48f), 12f);

            _music = BuildSwitch(panel.transform, "Music", "MUSIC", -140f, ToggleMusic);
            _nearest = BuildSwitch(panel.transform, "Nearest", "NEAREST DETECTION", -216f, ToggleNearest);
            _aim = BuildSwitch(panel.transform, "Aim", "AIM DETECTION", -292f, ToggleAim);

            NewText(panel.transform, "Note", "SETTINGS PERSIST BETWEEN RUNS", 18f, _dim,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(48f, 108f),
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

        /// <summary>
        /// Строка настройки: подпись слева, переключатель справа. Кликается вся строка —
        /// целиться в маленький тумблер мышью в паузе неудобно.
        /// </summary>
        private Switch BuildSwitch(
            Transform parent, string name, string label, float y, UnityEngine.Events.UnityAction action)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(48f, 0f);
            rowRect.offsetMax = new Vector2(-48f, 0f);
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, 56f);
            rowRect.anchoredPosition = new Vector2(0f, y);

            var hit = row.AddComponent<Image>();
            hit.color = new Color(_buttonIdle.r, _buttonIdle.g, _buttonIdle.b, 0.55f);

            var button = row.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(_buttonIdle.r, _buttonIdle.g, _buttonIdle.b, 0.55f);
            colors.highlightedColor = _buttonHover;
            colors.pressedColor = _accent;
            colors.selectedColor = _buttonHover;
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.onClick.AddListener(action);

            NewText(row.transform, "Label", label, 20f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(420f, 28f), 12f);

            // Тумблер: дорожка, ползунок и подпись состояния справа.
            var track = NewImage(row.transform, "Track", new Color(_dim.r, _dim.g, _dim.b, 0.35f));
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(1f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(72f, 26f);
            trackRect.anchoredPosition = new Vector2(-96f, 0f);

            var knob = NewImage(track.transform, "Knob", _text);
            var knobRect = knob.rectTransform;
            knobRect.anchorMin = new Vector2(1f, 0.5f);
            knobRect.anchorMax = new Vector2(1f, 0.5f);
            knobRect.pivot = new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta = new Vector2(20f, 20f);
            knobRect.anchoredPosition = new Vector2(-16f, 0f);

            var state = NewText(row.transform, "State", "ON", 18f, _accent, TextAlignmentOptions.Right,
                new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(60f, 24f), 10f);

            return new Switch { Track = track, Knob = knobRect, State = state, Value = true };
        }

        /// <summary>Панель по центру: тёмная плашка с акцентной линией по верхнему краю.</summary>
        private GameObject NewPanel(Transform parent, string name, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
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

            return panel;
        }

        private Button NewButton(
            Transform parent, string name, string label, Vector2 position,
            UnityEngine.Events.UnityAction action)
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

            var marker = NewImage(go.transform, "Marker", _accent);
            var markerRect = marker.rectTransform;
            markerRect.anchorMin = new Vector2(0f, 0f);
            markerRect.anchorMax = new Vector2(0f, 1f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(MarkerWidth, 0f);
            markerRect.anchoredPosition = Vector2.zero;

            NewText(go.transform, "Label", label, 28f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(ButtonWidth - 48f, 36f), 10f);

            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
