using System.Text;
using FrostSpace.Core;
using FrostSpace.Features.Inventory;
using FrostSpace.Features.Quest;
using FrostSpace.Features.Scan;
using FrostSpace.Features.Story;
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
    /// Экран паузы: затемнение, левая навигация, журнал заданий и настройки.
    /// Геометрия собирается кодом, как у главного меню; настройки открываются
    /// шестерёнкой, журнал — отдельной кнопкой. Esc из любого раздела возвращает
    /// обзор, а из обзора — в игру.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        private enum Content
        {
            Overview,
            Journal,
            Settings
        }

        [Header("Ссылки")]
        [Tooltip("Сцена главного меню")]
        [SerializeField] private string _mainMenuScene = "MainMenu";
        [Tooltip("Риг камеры: заморозка обзора и захват курсора")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Сканер: на паузе выключается, чтобы Q не переключал режим под меню")]
        [SerializeField] private ObjectScanner _scanner;
        [Tooltip("Квест сцены: наполняет журнал; пусто — журнал пуст")]
        [SerializeField] private QuestDirector _questDirector;
        [Tooltip("Снаряжение игрока: карта и сканер в обзоре; пусто — строка скрыта")]
        [SerializeField] private PlayerEquipment _equipment;

        [Header("Шрифт")]
        [Tooltip("Кириллический TMP-ассет; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _panelColor = new(0.016f, 0.026f, 0.038f, 0.96f);
        [SerializeField] private Color _shadeColor = new(0.008f, 0.016f, 0.026f, 0.78f);
        [SerializeField] private Color _buttonIdle = new(0.055f, 0.085f, 0.105f, 0.82f);
        [SerializeField] private Color _buttonHover = new(0.10f, 0.19f, 0.24f, 0.95f);
        [SerializeField] private Color _contentColor = new(0.028f, 0.045f, 0.060f, 0.55f);

        private const float ButtonWidth = 300f;
        private const float ButtonHeight = 58f;
        private const float ButtonStep = 72f;
        private const float MarkerWidth = 4f;
        private const float PanelWidth = 1180f;
        private const float PanelHeight = 720f;

        private sealed class Switch
        {
            public Image Track;
            public RectTransform Knob;
            public TextMeshProUGUI State;
            public bool Value;

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
                    State.text = on ? "ВКЛ" : "ВЫКЛ";
                    State.color = on ? accent : dim;
                }
            }
        }

        private GameObject _root;
        private bool _scannerWasEnabled;
        private GameObject _overviewPanel;
        private GameObject _journalPanel;
        private GameObject _settingsPanel;
        private TextMeshProUGUI _overviewText;
        private TextMeshProUGUI _journalText;
        private Switch _music;
        private Switch _nearest;
        private Switch _aim;
        private Content _content;
        private Texture2D _gearTexture;
        private Sprite _gearSprite;

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
            if (IsPaused)
                Resume();
        }

        private void OnDestroy()
        {
            if (_gearSprite != null)
                Destroy(_gearSprite);
            if (_gearTexture != null)
                Destroy(_gearTexture);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            if (IsPaused && _content != Content.Overview)
            {
                ShowContent(Content.Overview);
                return;
            }

            if (IsPaused)
                Resume();
            else
                Open();
        }

        /// <summary>Открывает паузу. Публичный: паузу может включить не только Esc.</summary>
        public void Open()
        {
            // Диалог, экипировка и катсцена уже держат риг. Пауза не должна
            // перехватить блокировку, а Resume — снять её раньше владельца.
            if (IsPaused || (_rig != null && _rig.IsPaused))
                return;

            IsPaused = true;
            Time.timeScale = 0f;

            if (_rig != null)
                _rig.SetPaused(true);
            if (_scanner != null)
            {
                _scannerWasEnabled = _scanner.enabled;
                _scanner.enabled = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ShowContent(Content.Overview);
            RefreshSwitches();
            RefreshOverview();
            RefreshJournal();
            SetVisible(true);
        }

        /// <summary>Возвращает игру: время, камера, сканер и курсор — в исходное состояние.</summary>
        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;

            if (_scanner != null)
                _scanner.enabled = _scannerWasEnabled;
            if (_rig != null)
                _rig.SetPaused(false);

            SetVisible(false);
        }

        private void OnSettingsPressed()
        {
            if (_content == Content.Settings)
            {
                ShowContent(Content.Overview);
                return;
            }

            ShowContent(Content.Settings);
            RefreshSwitches();
        }

        private void OnJournalPressed()
        {
            if (_content == Content.Journal)
            {
                ShowContent(Content.Overview);
                return;
            }

            ShowContent(Content.Journal);
            RefreshJournal();
        }

        private void OnBackPressed() => ShowContent(Content.Overview);

        private void OnMainMenuPressed()
        {
            Resume();

            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError("PauseMenuScreen: нет SceneController — переход в меню невозможен", this);
                return;
            }

            controller.LoadScene(_mainMenuScene);
        }

        private void ShowContent(Content content)
        {
            _content = content;
            _overviewPanel.SetActive(content == Content.Overview);
            _journalPanel.SetActive(content == Content.Journal);
            _settingsPanel.SetActive(content == Content.Settings);

            if (content == Content.Overview)
                RefreshOverview();
            else if (content == Content.Journal)
                RefreshJournal();
            else
                RefreshSwitches();
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

        private void RefreshOverview()
        {
            if (_overviewText != null)
                _overviewText.text = BuildQuestText(false);
        }

        private void RefreshJournal()
        {
            if (_journalText != null)
                _journalText.text = BuildQuestText(true);
        }

        private string BuildQuestText(bool detailed)
        {
            var builder = new StringBuilder(BuildQuestBody(detailed));
            AppendEquipment(builder);
            return builder.ToString();
        }

        /// <summary>
        /// Строка снаряжения живёт в обзоре и журнале: после R7 игрок видит,
        /// что карта и сканер выданы, даже когда активных заданий нет.
        ///
        /// Показывается надетое состояние, а не только факт выдачи: со снятой картой
        /// миникарты нет, и писать «КАРТА» в этом случае значит врать игроку.
        /// </summary>
        private void AppendEquipment(StringBuilder builder)
        {
            if (_equipment == null)
                return;

            builder.AppendLine().AppendLine();
            builder.Append("СНАРЯЖЕНИЕ: ");
            builder.Append(EquipmentState(EquipmentKind.Map, "КАРТА"));
            builder.Append("   ");
            builder.Append(EquipmentState(EquipmentKind.Scanner, "СКАНЕР"));
        }

        private string EquipmentState(EquipmentKind kind, string label)
        {
            if (!_equipment.Has(kind))
                return label + " — НЕТ";

            return _equipment.IsEquipped(kind) ? label : label + " · СНЯТО";
        }

        private string BuildQuestBody(bool detailed)
        {
            var log = _questDirector != null ? _questDirector.Log : null;
            if (log == null || log.Quest == null || log.State == QuestState.Inactive)
            {
                return detailed
                    ? "Журнал пуст.\n\nНовых заданий нет."
                    : _questDirector != null
                        ? "Активных заданий нет.\n\nПодойдите к R13 и нажмите E."
                        : "Активных заданий нет.\n\nОсмотритесь.";
            }

            var quest = log.Quest;
            var builder = new StringBuilder();

            if (detailed)
            {
                builder.Append("<size=30><b>").Append(quest.Title).AppendLine("</b></size>");
                if (!string.IsNullOrWhiteSpace(quest.Description))
                    builder.AppendLine(quest.Description);
                builder.AppendLine();
            }
            else
            {
                builder.Append("<size=26><b>").Append(quest.Title).AppendLine("</b></size>");
            }

            if (log.IsCompleted)
            {
                for (var i = 0; i < quest.Objectives.Count; i++)
                {
                    var objective = quest.Objectives[i];
                    if (objective != null)
                        builder.Append("[x] ").AppendLine(objective.Text);
                }

                builder.AppendLine();
                builder.Append("Задание выполнено.");
                return builder.ToString();
            }

            var current = log.CurrentObjective;
            if (!detailed && current != null)
            {
                builder.AppendLine(current.Text);
                if (current.Required > 1)
                    builder.Append($"Прогресс: {log.Progress} / {current.Required}");
                return builder.ToString();
            }

            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (objective == null)
                    continue;

                var marker = i < log.ObjectiveIndex
                    ? "[x]"
                    : i == log.ObjectiveIndex ? "[>]" : "[ ]";
                builder.Append(marker).Append(' ').Append(objective.Text);

                if (i == log.ObjectiveIndex && objective.Required > 1)
                    builder.Append($"  {log.Progress}/{objective.Required}");

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }

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
            canvas.sortingOrder = 50;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            var shade = NewImage(_root.transform, "Shade", _shadeColor);
            Stretch(shade.rectTransform);

            BuildMenuPanel(_root.transform);
        }

        private GameObject BuildMenuPanel(Transform parent)
        {
            var panel = NewPanel(parent, "PausePanel", new Vector2(PanelWidth, PanelHeight));

            NewText(panel.transform, "Title", "ПАУЗА", 54f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(52f, -42f), new Vector2(420f, 64f), 12f);

            NewText(panel.transform, "Subtitle", "КОЛОНИЯ 07  /  СИСТЕМА НА ПАУЗЕ", 14f, _dim,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(52f, -108f),
                new Vector2(400f, 26f), 4f);

            NewButton(panel.transform, "Resume", "ПРОДОЛЖИТЬ", new Vector2(52f, -184f), Resume);
            NewButton(panel.transform, "Journal", "ЖУРНАЛ", new Vector2(52f, -184f - ButtonStep),
                OnJournalPressed);
            NewButton(panel.transform, "MainMenu", "В ГЛАВНОЕ МЕНЮ",
                new Vector2(52f, -184f - ButtonStep * 2f), OnMainMenuPressed);

            var divider = NewImage(panel.transform, "Divider", new Color(_dim.r, _dim.g, _dim.b, 0.24f));
            var dividerRect = divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(0f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 0.5f);
            dividerRect.sizeDelta = new Vector2(1f, -180f);
            dividerRect.anchoredPosition = new Vector2(392f, -40f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(420f, 48f);
            contentRect.offsetMax = new Vector2(-48f, -140f);

            _overviewPanel = BuildOverviewPanel(content.transform);
            _journalPanel = BuildJournalPanel(content.transform);
            _settingsPanel = BuildSettingsPanel(content.transform);
            _journalPanel.SetActive(false);
            _settingsPanel.SetActive(false);

            BuildGearButton(panel.transform);
            return panel;
        }

        private GameObject BuildOverviewPanel(Transform parent)
        {
            var panel = NewContentPanel(parent, "OverviewPanel");
            NewText(panel.transform, "Title", "ОБЗОР", 30f, _accent, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(400f, 40f), 4f);

            _overviewText = NewText(panel.transform, "Text", string.Empty, 21f, _text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(28f, -80f),
                new Vector2(640f, 360f), 0f);
            _overviewText.textWrappingMode = TextWrappingModes.Normal;

            NewText(panel.transform, "Hint", "ESC — ПРОДОЛЖИТЬ", 16f, _dim,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(28f, 24f),
                new Vector2(400f, 24f), 4f);

            return panel;
        }

        private GameObject BuildJournalPanel(Transform parent)
        {
            var panel = NewContentPanel(parent, "JournalPanel");
            NewText(panel.transform, "Title", "ЖУРНАЛ", 30f, _accent, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(400f, 40f), 4f);

            _journalText = NewText(panel.transform, "Text", string.Empty, 20f, _text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(28f, -78f),
                new Vector2(640f, 370f), 0f);
            _journalText.textWrappingMode = TextWrappingModes.Normal;

            NewBackButton(panel.transform);
            return panel;
        }

        private GameObject BuildSettingsPanel(Transform parent)
        {
            var panel = NewContentPanel(parent, "SettingsPanel");
            NewText(panel.transform, "Title", "НАСТРОЙКИ", 30f, _accent, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(500f, 40f), 4f);

            _music = BuildSwitch(panel.transform, "Music", "МУЗЫКА", -112f, ToggleMusic);
            _nearest = BuildSwitch(panel.transform, "Nearest", "ДЕТЕКЦИЯ БЛИЖАЙШИХ", -188f, ToggleNearest);
            _aim = BuildSwitch(panel.transform, "Aim", "ДЕТЕКЦИЯ ПРИЦЕЛА", -264f, ToggleAim);

            NewText(panel.transform, "Note", "НАСТРОЙКИ СОХРАНЯЮТСЯ", 16f, _dim,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(28f, 82f),
                new Vector2(520f, 24f), 4f);

            NewBackButton(panel.transform);
            return panel;
        }

        private void BuildGearButton(Transform parent)
        {
            var go = new GameObject("SettingsGear", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(180f, 54f);
            rect.anchoredPosition = new Vector2(-48f, -42f);

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
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.onClick.AddListener(OnSettingsPressed);

            var iconObject = new GameObject("GearIcon", typeof(RectTransform));
            iconObject.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(44f, 44f);
            iconRect.anchoredPosition = new Vector2(12f, 0f);

            var icon = iconObject.AddComponent<Image>();
            icon.sprite = GearSprite;
            icon.color = _accent;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            NewText(go.transform, "Label", "НАСТРОЙКИ", 16f, _text, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(106f, 26f), 2f);
        }

        private Switch BuildSwitch(
            Transform parent, string name, string label, float y, UnityEngine.Events.UnityAction action)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(28f, 0f);
            rowRect.offsetMax = new Vector2(-28f, 0f);
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

            NewText(row.transform, "Label", label, 19f, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(440f, 28f), 4f);

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

            var state = NewText(row.transform, "State", "ВКЛ", 17f, _accent, TextAlignmentOptions.Right,
                new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(60f, 24f), 2f);

            return new Switch { Track = track, Knob = knobRect, State = state, Value = true };
        }

        private GameObject NewContentPanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            Stretch(rect);

            var background = panel.AddComponent<Image>();
            background.color = _contentColor;
            background.raycastTarget = false;

            var edge = NewImage(panel.transform, "EdgeLine", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 2f);
            edgeRect.anchoredPosition = Vector2.zero;

            return panel;
        }

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
            UnityEngine.Events.UnityAction action, float width = ButtonWidth, float fontSize = 24f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, ButtonHeight);
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

            NewText(go.transform, "Label", label, fontSize, _text, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(width - 42f, 34f), 4f);

            return button;
        }

        private void NewBackButton(Transform parent)
        {
            var button = NewButton(parent, "Back", "НАЗАД", Vector2.zero, OnBackPressed, 160f, 20f);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = new Vector2(28f, 24f);
        }

        private Sprite GearSprite
        {
            get
            {
                if (_gearSprite != null)
                    return _gearSprite;

                _gearTexture = BuildGearTexture();
                _gearSprite = Sprite.Create(
                    _gearTexture,
                    new Rect(0f, 0f, _gearTexture.width, _gearTexture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                return _gearSprite;
            }
        }

        private static Texture2D BuildGearTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PauseGear",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var radius = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx);
                    var sector = Mathf.PI * 2f / 8f;
                    var phase = Mathf.Repeat(angle + Mathf.PI, sector);
                    var tooth = phase < sector * 0.55f;
                    var body = radius >= 7f && radius <= 24f;
                    var rim = radius > 24f && radius <= 30f && tooth;
                    var alpha = body || rim ? 1f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
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

        private TextMeshProUGUI NewText(
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
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
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
