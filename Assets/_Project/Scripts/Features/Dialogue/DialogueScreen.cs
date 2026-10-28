using System;
using System.Collections.Generic;
using FrostSpace.Features.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Панель диалога: шапка с именем робота, реплика с печатью по символам и до
    /// четырёх вариантов ответа. Геометрия собирается кодом, как у HUD и меню паузы:
    /// в сценах нет Canvas и UI-префабов, а стиль живёт в одном месте.
    ///
    /// Canvas сортировки 40: выше HUD (10) и ниже паузы (50), поэтому открытая пауза
    /// всегда перекрывает диалог, но в разговоре она выключена контроллером.
    /// Ввод вариантов — мышь, стрелки/Enter через EventSystem и цифры 1–4 (их
    /// обрабатывает <see cref="DialogueController"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueScreen : MonoBehaviour
    {
        private const float PanelWidth = 1240f;
        private const float PanelHeight = 480f;
        private const float PanelBottom = 72f;
        private const float PanelSlide = 24f;
        private const float ChoiceWidth = 1120f;
        private const float ChoiceHeight = 40f;
        private const float ChoiceStep = 48f;
        private const float ChoiceFirstY = -256f;
        private const float MarkerWidth = 4f;

        [Header("Шрифт")]
        [Tooltip("Кириллический TMP-ассет; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [Tooltip("Циан — единственный холодный акцент стиля")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [Tooltip("Фон панели")]
        [SerializeField] private Color _panelColor = new(0.016f, 0.026f, 0.038f, 0.96f);
        [SerializeField] private Color _choiceIdle = new(0.055f, 0.085f, 0.105f, 0.42f);
        [Tooltip("Фон подсказки у перекрестия")]
        [SerializeField] private Color _promptPlate = new(0.016f, 0.026f, 0.038f, 0.78f);

        [Header("Печать")]
        [Tooltip("Символов в секунду; E/клик завершают строку сразу")]
        [SerializeField] private float _charactersPerSecond = 50f;
        [Tooltip("Появление панели, с")]
        [SerializeField] private float _showSeconds = 0.18f;
        [Tooltip("Закрытие панели, с")]
        [SerializeField] private float _hideSeconds = 0.12f;

        private sealed class ChoiceRow
        {
            public GameObject Root;
            public Button Button;
            public TextMeshProUGUI Label;
        }

        private readonly List<ChoiceRow> _rows = new(4);

        private GameObject _canvasRoot;
        private GameObject _panel;
        private RectTransform _panelRect;
        private CanvasGroup _panelGroup;
        private GameObject _prompt;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _category;
        private TextMeshProUGUI _line;
        private TextMeshProUGUI _hint;
        private DialogueNode _node;
        private float _fade;
        private bool _entering;
        private bool _leaving;
        private bool _typing;
        private float _typingElapsed;
        private int _visibleChars;
        private int _totalChars;

        /// <summary>Вариант выбран кнопкой или цифрой; индекс — в списке узла.</summary>
        public event Action<int> ChoicePressed;

        /// <summary>Клик по панели: завершить печать или перейти дальше.</summary>
        public event Action AdvancePressed;

        /// <summary>Панель на экране (включая анимацию закрытия).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Реплика ещё печатается.</summary>
        public bool IsTyping => _typing;

        private void Awake()
        {
            if (_font == null)
                Debug.LogWarning(
                    "DialogueScreen: шрифт не назначен — кириллица может не отобразиться", this);

            EnsureEventSystem();
            BuildInterface();
            HideImmediately();
        }

        private void Update()
        {
            Animate();
            Type();
        }

        /// <summary>Показывает реплику; панель открывается с fade/slide в первый раз.</summary>
        public void Show(DialogueNode node, string speaker, string category)
        {
            if (node == null)
                return;

            _node = node;
            _leaving = false;
            _panel.SetActive(true);
            _panelGroup.blocksRaycasts = true;

            // Повторное открытие во время закрытия: продолжаем с текущей прозрачности,
            // а не оставляем панель полупрозрачной.
            if (!IsOpen || _entering || _fade < 1f)
            {
                if (!IsOpen)
                {
                    _fade = 0f;
                    _panelRect.anchoredPosition = new Vector2(0f, PanelBottom - PanelSlide);
                }

                IsOpen = true;
                _entering = true;
                _panelGroup.alpha = Mathf.SmoothStep(0f, 1f, _fade);
            }

            SetPrompt(false);
            ClearSelection();

            _name.text = (string.IsNullOrWhiteSpace(speaker) ? "UNKNOWN" : speaker)
                .ToUpperInvariant();
            _category.text = (string.IsNullOrWhiteSpace(category) ? "NPC" : category)
                .ToUpperInvariant();

            _line.text = node.Line;
            _line.maxVisibleCharacters = 0;
            _line.ForceMeshUpdate();
            _totalChars = _line.textInfo.characterCount;
            _visibleChars = 0;
            _typingElapsed = 0f;
            _typing = _totalChars > 0;

            HideChoices();
            ApplyHint(node, false);

            if (!_typing)
                CompleteTyping();
        }

        /// <summary>Завершает печать немедленно и показывает варианты.</summary>
        public void CompleteTyping()
        {
            if (!_typing && _visibleChars == _totalChars)
            {
                RevealChoices();
                return;
            }

            _typing = false;
            _visibleChars = _totalChars;
            _line.maxVisibleCharacters = _totalChars;
            ApplyHint(_node, true);
            RevealChoices();
        }

        /// <summary>Закрывает панель с коротким фейдом.</summary>
        public void Hide()
        {
            if (!IsOpen || _leaving)
                return;

            _typing = false;
            _leaving = true;
            _entering = false;
            HideChoices();
            ClearSelection();
            _panelGroup.blocksRaycasts = false;
        }

        /// <summary>Подсказка «E ГОВОРИТЬ» у перекрестия.</summary>
        public void SetPrompt(bool visible)
        {
            if (_prompt == null || _prompt.activeSelf == visible)
                return;

            _prompt.SetActive(visible);
        }

        private void Type()
        {
            if (!_typing)
                return;

            _typingElapsed += Time.unscaledDeltaTime;
            var next = Mathf.Min(
                _totalChars, Mathf.FloorToInt(_typingElapsed * _charactersPerSecond));
            if (next == _visibleChars)
                return;

            _visibleChars = next;
            _line.maxVisibleCharacters = _visibleChars;

            if (_visibleChars >= _totalChars)
                CompleteTyping();
        }

        private void Animate()
        {
            if (_entering)
            {
                _fade = Mathf.MoveTowards(
                    _fade, 1f, Time.unscaledDeltaTime / Mathf.Max(_showSeconds, 0.0001f));
                ApplyFade();
                if (_fade >= 1f)
                    _entering = false;
            }
            else if (_leaving)
            {
                _fade = Mathf.MoveTowards(
                    _fade, 0f, Time.unscaledDeltaTime / Mathf.Max(_hideSeconds, 0.0001f));
                ApplyFade();
                if (_fade > 0f)
                    return;

                IsOpen = false;
                _leaving = false;
                _node = null;
                _panel.SetActive(false);
            }
        }

        private void ApplyFade()
        {
            var t = Mathf.SmoothStep(0f, 1f, _fade);
            _panelGroup.alpha = t;
            _panelRect.anchoredPosition = new Vector2(
                0f, Mathf.Lerp(PanelBottom - PanelSlide, PanelBottom, t));
        }

        private void HideImmediately()
        {
            IsOpen = false;
            _entering = false;
            _leaving = false;
            _typing = false;
            _node = null;
            _fade = 0f;
            _panelGroup.alpha = 0f;
            _panel.SetActive(false);
            _prompt.SetActive(false);
        }

        private void ApplyHint(DialogueNode node, bool choicesReady)
        {
            var accent = $"#{ColorUtility.ToHtmlStringRGB(_accent)}";

            if (_typing)
            {
                _hint.text = $"<color={accent}>E</color>  ПРОПУСТИТЬ";
                return;
            }

            if (choicesReady && node != null && node.Choices.Count > 0)
            {
                _hint.text =
                    "↑↓  ВЫБОР   ·   <color=" + accent + ">ENTER</color>  ПОДТВЕРДИТЬ   ·   " +
                    "<color=" + accent + ">ESC</color>  ВЫЙТИ";
                return;
            }

            _hint.text = string.IsNullOrWhiteSpace(node?.NextId)
                ? $"<color={accent}>E</color>  ЗАВЕРШИТЬ"
                : $"<color={accent}>E</color>  ДАЛЬШЕ";
        }

        private void HideChoices()
        {
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);
        }

        private void RevealChoices()
        {
            if (_node == null || _node.Choices.Count == 0)
            {
                HideChoices();
                return;
            }

            var count = Mathf.Min(_node.Choices.Count, _rows.Count);
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var active = i < count;
                row.Root.SetActive(active);
                if (!active)
                    continue;

                var number = $"<color=#{ColorUtility.ToHtmlStringRGB(_dim)}>{i + 1}</color>";
                row.Label.text = $"{number}  {_node.Choices[i].Text}";
            }

            for (var i = 0; i < count; i++)
            {
                var up = i > 0 ? _rows[i - 1].Button : _rows[count - 1].Button;
                var down = i < count - 1 ? _rows[i + 1].Button : _rows[0].Button;
                var navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = up,
                    selectOnDown = down
                };
                _rows[i].Button.navigation = navigation;
            }

            if (EventSystem.current == null)
                return;

            var selected = EventSystem.current.currentSelectedGameObject;
            var valid = false;
            for (var i = 0; i < count; i++)
            {
                if (_rows[i].Root == selected)
                    valid = true;
            }

            if (!valid)
                EventSystem.current.SetSelectedGameObject(_rows[0].Root);
        }

        private void ClearSelection()
        {
            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void OnChoiceClicked(int index)
        {
            if (_typing || _node == null || index < 0 || index >= _node.Choices.Count)
                return;

            ChoicePressed?.Invoke(index);
        }

        private void OnPanelClicked()
        {
            if (_typing)
                CompleteTyping();
            else
                AdvancePressed?.Invoke();
        }

        /// <summary>
        /// EventSystem нужен и меню паузы, но диалог может открыться раньше его
        /// сборки. Модуль создаётся с явными действиями: без них клик не доходит.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private void BuildInterface()
        {
            _canvasRoot = new GameObject("DialogueCanvas", typeof(RectTransform));
            _canvasRoot.transform.SetParent(transform, false);

            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Выше HUD (10) и ниже паузы (50).
            canvas.sortingOrder = 40;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            BuildPrompt(_canvasRoot.transform);
            BuildPanel(_canvasRoot.transform);
        }

        private void BuildPrompt(Transform parent)
        {
            _prompt = new GameObject("TalkPrompt", typeof(RectTransform));
            _prompt.transform.SetParent(parent, false);
            var rect = (RectTransform)_prompt.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(252f, 40f);
            rect.anchoredPosition = new Vector2(0f, -76f);

            var plate = _prompt.AddComponent<Image>();
            plate.color = _promptPlate;
            plate.raycastTarget = false;

            var edge = NewImage(_prompt.transform, "Edge", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 0.5f);
            edgeRect.anchorMax = new Vector2(0f, 0.5f);
            edgeRect.pivot = new Vector2(0f, 0.5f);
            edgeRect.sizeDelta = new Vector2(3f, 24f);
            edgeRect.anchoredPosition = new Vector2(0f, 0f);

            var accent = $"#{ColorUtility.ToHtmlStringRGB(_accent)}";
            var label = NewText(
                _prompt.transform, "Label",
                $"<color={accent}>E</color>   ГОВОРИТЬ",
                19f, _text, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
                new Vector2(228f, 28f), 12f);
            label.rectTransform.anchoredPosition = new Vector2(2f, 0f);

            _prompt.SetActive(false);
        }

        private void BuildPanel(Transform parent)
        {
            _panel = new GameObject("DialoguePanel", typeof(RectTransform));
            _panel.transform.SetParent(parent, false);
            _panelRect = (RectTransform)_panel.transform;
            _panelRect.anchorMin = new Vector2(0.5f, 0f);
            _panelRect.anchorMax = new Vector2(0.5f, 0f);
            _panelRect.pivot = new Vector2(0.5f, 0f);
            _panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panelRect.anchoredPosition = new Vector2(0f, PanelBottom);

            _panelGroup = _panel.AddComponent<CanvasGroup>();

            var background = _panel.AddComponent<Image>();
            background.color = _panelColor;
            background.raycastTarget = true;

            // Клик по панели завершает печать или листает реплику; клик по строке
            // варианта перехватывает её собственная кнопка.
            var panelButton = _panel.AddComponent<Button>();
            panelButton.targetGraphic = background;
            panelButton.transition = Selectable.Transition.None;
            panelButton.onClick.AddListener(OnPanelClicked);

            var edge = NewImage(_panel.transform, "EdgeLine", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 3f);
            edgeRect.anchoredPosition = Vector2.zero;

            NewCorner("CornerLeft", new Vector2(0f, 1f), new Vector2(18f, 2f),
                new Vector2(0f, -12f));
            NewCorner("CornerRight", new Vector2(1f, 0f), new Vector2(18f, 2f),
                new Vector2(0f, 12f));

            _name = NewText(
                _panel.transform, "SpeakerName", "UNKNOWN", 30f, _accent,
                TextAlignmentOptions.Left, new Vector2(0f, 1f),
                new Vector2(52f, -32f), new Vector2(760f, 40f), 8f);

            _category = NewText(
                _panel.transform, "SpeakerCategory", "NPC", 16f, _dim,
                TextAlignmentOptions.Right, new Vector2(1f, 1f),
                new Vector2(-52f, -40f), new Vector2(360f, 26f), 8f);

            var divider = NewImage(_panel.transform, "HeaderLine",
                new Color(_accent.r, _accent.g, _accent.b, 0.28f));
            var dividerRect = divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.offsetMin = new Vector2(52f, 0f);
            dividerRect.offsetMax = new Vector2(-52f, 0f);
            dividerRect.sizeDelta = new Vector2(dividerRect.sizeDelta.x, 1f);
            dividerRect.anchoredPosition = new Vector2(0f, -86f);

            _line = NewText(
                _panel.transform, "Line", string.Empty, 26f, _text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(52f, -100f), new Vector2(1136f, 148f), 0f);
            _line.textWrappingMode = TextWrappingModes.Normal;
            _line.overflowMode = TextOverflowModes.Ellipsis;
            _line.lineSpacing = 6f;

            for (var i = 0; i < DialogueAsset.MaxChoices; i++)
                _rows.Add(BuildChoiceRow(_panel.transform, i, ChoiceFirstY - ChoiceStep * i));

            _hint = NewText(
                _panel.transform, "Hint", string.Empty, 15f, _dim,
                TextAlignmentOptions.Left, new Vector2(0f, 1f),
                new Vector2(52f, -448f), new Vector2(1000f, 22f), 8f);
        }

        private ChoiceRow BuildChoiceRow(Transform parent, int index, float y)
        {
            var root = new GameObject($"Choice{index + 1}", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(ChoiceWidth, ChoiceHeight);
            rect.anchoredPosition = new Vector2(52f, y);

            var background = root.AddComponent<Image>();
            background.color = _choiceIdle;
            background.raycastTarget = true;

            var button = root.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            var captured = index;
            button.onClick.AddListener(() => OnChoiceClicked(captured));

            var marker = NewImage(root.transform, "Marker", _accent);
            var markerRect = marker.rectTransform;
            markerRect.anchorMin = new Vector2(0f, 0f);
            markerRect.anchorMax = new Vector2(0f, 1f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(MarkerWidth, 0f);
            markerRect.anchoredPosition = Vector2.zero;

            var underline = NewImage(root.transform, "Underline", _accent);
            var underlineRect = underline.rectTransform;
            underlineRect.anchorMin = new Vector2(0f, 0f);
            underlineRect.anchorMax = new Vector2(0f, 0f);
            underlineRect.pivot = new Vector2(0f, 0f);
            underlineRect.sizeDelta = new Vector2(0f, 2f);
            underlineRect.anchoredPosition = Vector2.zero;

            var label = NewText(
                root.transform, "Label", string.Empty, 22f, _text,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f),
                new Vector2(28f, 0f), new Vector2(ChoiceWidth - 48f, 30f), 10f);
            var labelRect = label.rectTransform;
            labelRect.pivot = new Vector2(0f, 0.5f);

            var fx = root.AddComponent<TechButtonFx>();
            fx.Initialize(0.12f, MarkerWidth, 8f, 28f, 42f, 8f, 16f, 0.45f, 0.18f);
            fx.Bind(markerRect, underlineRect, null, labelRect, label, ChoiceWidth, _accent, _text);

            root.SetActive(false);
            return new ChoiceRow
            {
                Root = root,
                Button = button,
                Label = label
            };
        }

        private void NewCorner(string name, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var corner = NewImage(_panel.transform, name, new Color(_accent.r, _accent.g, _accent.b, 0.7f));
            var rect = corner.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private TextMeshProUGUI NewText(
            Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position,
            Vector2 boxSize, float spacing)
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

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
