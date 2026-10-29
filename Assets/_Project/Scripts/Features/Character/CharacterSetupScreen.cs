using FrostSpace.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Экран выбора корпуса между меню и поездом (ADR 0028).
    ///
    /// Три карточки стоят в ряд, и в каждой своя 3D-модель: отдельная камера
    /// рендерит корпус в RenderTexture (<see cref="CharacterCardPreview"/>).
    /// Игрок видит всех кандидатов сразу и выбирает карточку, а не листает
    /// одного героя кнопками.
    ///
    /// Модульная сборка («CREATE YOUR OWN») видна, но заблокирована: части
    /// роботов — отдельный этап, и кнопка не должна обещать то, чего нет.
    ///
    /// Геометрия строится кодом, как в меню: Canvas и EventSystem создаются
    /// здесь, сцена несёт только фоновую камеру и свет. Строки — латиница,
    /// потому что атлас меню без кириллицы.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CharacterSetupScreen : MonoBehaviour
    {
        [Header("Данные")]
        [Tooltip("Каталог корпусов: карточки строятся по нему")]
        [SerializeField] private CharacterCatalog _catalog;
        [Tooltip("Сцена, которая грузится по CONFIRM")]
        [SerializeField] private string _nextScene = "Train";

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _cardIdle = new(0.045f, 0.075f, 0.095f, 0.92f);
        [SerializeField] private Color _cardHover = new(0.08f, 0.14f, 0.18f, 0.95f);

        private const float CardWidth = 380f;
        private const float CardHeight = 560f;
        private const float CardGap = 40f;
        private const float CardTop = 320f;
        private const float PreviewHeight = 340f;

        private RobotProfile[] _profiles;
        private Button[] _cards;
        private Image[] _cardLines;
        private Image[] _cardMarkers;
        private Image[][] _cardBorders;
        private Button _confirm;
        private int _selected = -1;

        private void Awake()
        {
            EnsureEventSystem();
            ReadCatalog();
            BuildInterface();
            SelectInitial();
            ShowCursor();
        }

        private void Update()
        {
            var controller = SceneController.Instance;
            var busy = controller != null && controller.IsBusy;
            _confirm.interactable = !busy && _selected >= 0;

            // Меню не должно перелистываться под руками на переходе.
            if (!busy && Keyboard.current != null)
            {
                for (var i = 0; i < _cards.Length && i < 9; i++)
                {
                    if (!Keyboard.current[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                        continue;

                    Select(i);
                    break;
                }
            }
        }

        private void OnDisable()
        {
            PlayerPrefs.Save();
        }

        private void ReadCatalog()
        {
            _profiles = _catalog != null
                ? _catalog.Profiles
                : System.Array.Empty<RobotProfile>();

            if (_profiles.Length == 0)
                Debug.LogError("CharacterSetupScreen: каталог пуст — выбирать нечего", this);
        }

        private void SelectInitial()
        {
            var current = CharacterSelection.Resolve(_catalog);
            var index = 0;
            for (var i = 0; i < _profiles.Length; i++)
            {
                if (_profiles[i] == current)
                {
                    index = i;
                    break;
                }
            }

            if (_profiles.Length > 0)
                Select(index);
        }

        private void Select(int index)
        {
            if (_profiles == null || index < 0 || index >= _profiles.Length)
                return;

            _selected = index;

            for (var i = 0; i < _cards.Length; i++)
            {
                var selected = i == index;

                if (_cardMarkers[i] != null)
                    _cardMarkers[i].gameObject.SetActive(selected);

                if (_cardLines[i] != null)
                {
                    var color = selected ? _accent : _dim;
                    color.a = selected ? 1f : 0.35f;
                    _cardLines[i].color = color;
                }

                if (_cardBorders[i] != null)
                {
                    var border = selected
                        ? new Color(_accent.r, _accent.g, _accent.b, 0.85f)
                        : new Color(_accent.r, _accent.g, _accent.b, 0.16f);
                    foreach (var edge in _cardBorders[i])
                        edge.color = border;
                }
            }

            Debug.Log($"[CharacterSetupScreen] выбран {_profiles[index].Id}", this);
        }

        private void OnConfirmPressed()
        {
            if (_selected < 0)
                return;

            var profile = _profiles[_selected];
            CharacterSelection.Select(profile);

            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError(
                    "CharacterSetupScreen: нет SceneController — переход невозможен", this);
                return;
            }

            controller.LoadScene(_nextScene);
        }

        private void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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
            var canvasObject = new GameObject("CharacterCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            var root = canvasObject.transform;

            var backdropObject = new GameObject("Backdrop", typeof(RectTransform));
            backdropObject.transform.SetParent(root, false);
            var backdropRect = (RectTransform)backdropObject.transform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            backdropObject.AddComponent<CharacterBackdrop>().Build();

            NewText(root, "Brand", "COLONY 07", 22f, _accent, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -64f), new Vector2(600f, 34f), 18f);

            NewText(root, "Title", "SELECT CHASSIS", 64f, _text, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -108f), new Vector2(1000f, 90f), 10f);

            NewText(root, "Subtitle", "YOUR FRAME CARRIES THE RUN", 20f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(96f, -196f),
                new Vector2(900f, 30f), 12f);

            var accentRule = NewImage(root, "AccentRule", _accent);
            var ruleRect = accentRule.rectTransform;
            ruleRect.anchorMin = new Vector2(0f, 1f);
            ruleRect.anchorMax = new Vector2(0f, 1f);
            ruleRect.pivot = new Vector2(0f, 1f);
            ruleRect.sizeDelta = new Vector2(64f, 3f);
            ruleRect.anchoredPosition = new Vector2(96f, -238f);

            NewText(root, "CreateNote", "MODULAR ASSEMBLY  /  LOCKED", 16f, _dim,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f),
                new Vector2(-96f, -64f), new Vector2(700f, 26f), 12f);

            NewText(root, "Hint", "CLICK A CARD OR PRESS 1 2 3", 18f, _dim,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f),
                new Vector2(96f, 64f), new Vector2(700f, 28f), 12f);

            BuildCards(root);

            _confirm = NewButton(root, "Confirm", "CONFIRM", OnConfirmPressed);
            var confirmRect = (RectTransform)_confirm.transform;
            confirmRect.anchorMin = new Vector2(1f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(1f, 0f);
            confirmRect.sizeDelta = new Vector2(320f, 64f);
            confirmRect.anchoredPosition = new Vector2(-96f, 64f);

            foreach (var edge in NewBorder(_confirm.transform, 1.5f))
                edge.color = new Color(_accent.r, _accent.g, _accent.b, 0.55f);

            var create = NewButton(root, "CreateOwn", "CREATE YOUR OWN", null);
            var createRect = (RectTransform)create.transform;
            createRect.anchorMin = new Vector2(1f, 0f);
            createRect.anchorMax = new Vector2(1f, 0f);
            createRect.pivot = new Vector2(1f, 0f);
            createRect.sizeDelta = new Vector2(320f, 64f);
            createRect.anchoredPosition = new Vector2(-96f, 148f);
            create.interactable = false;
        }

        private void BuildCards(Transform root)
        {
            var count = _profiles != null ? _profiles.Length : 0;
            var rowWidth = count * CardWidth + Mathf.Max(0, count - 1) * CardGap;

            _cards = new Button[count];
            _cardLines = new Image[count];
            _cardMarkers = new Image[count];
            _cardBorders = new Image[count][];

            for (var i = 0; i < count; i++)
            {
                var profile = _profiles[i];
                var offsetX = -rowWidth * 0.5f + CardWidth * 0.5f + i * (CardWidth + CardGap);

                var card = new GameObject(profile.Id, typeof(RectTransform));
                card.transform.SetParent(root, false);
                var rect = (RectTransform)card.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                rect.anchoredPosition = new Vector2(offsetX, -CardTop);

                var background = card.AddComponent<Image>();
                background.color = _cardIdle;

                var button = card.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = _cardIdle;
                colors.highlightedColor = _cardHover;
                colors.pressedColor = _accent;
                colors.selectedColor = _cardIdle;
                colors.fadeDuration = 0.1f;
                button.colors = colors;
                var index = i;
                button.onClick.AddListener(() => Select(index));

                // Превью занимает верх карточки: своя камера и своя текстура.
                var previewObject = new GameObject("Preview", typeof(RectTransform));
                previewObject.transform.SetParent(card.transform, false);
                var previewRect = (RectTransform)previewObject.transform;
                previewRect.anchorMin = new Vector2(0f, 1f);
                previewRect.anchorMax = new Vector2(1f, 1f);
                previewRect.pivot = new Vector2(0.5f, 1f);
                previewRect.sizeDelta = new Vector2(0f, PreviewHeight);
                previewRect.anchoredPosition = Vector2.zero;

                var previewImage = previewObject.AddComponent<RawImage>();
                previewImage.raycastTarget = false;
                previewImage.color = Color.white;

                var preview = previewObject.AddComponent<CharacterCardPreview>();
                preview.Show(profile, previewImage, i);

                // Линия поверх превью: под ним её закрывала бы картинка модели.
                var line = NewImage(card.transform, "TopLine", _dim);
                var lineRect = line.rectTransform;
                lineRect.anchorMin = new Vector2(0f, 1f);
                lineRect.anchorMax = new Vector2(1f, 1f);
                lineRect.pivot = new Vector2(0.5f, 1f);
                lineRect.sizeDelta = new Vector2(0f, 3f);
                lineRect.anchoredPosition = Vector2.zero;

                // Тёмная плашка под описанием: текст читается поверх любой модели.
                var info = NewImage(card.transform, "Info",
                    new Color(0.018f, 0.030f, 0.045f, 0.92f));
                var infoRect = info.rectTransform;
                infoRect.anchorMin = new Vector2(0f, 0f);
                infoRect.anchorMax = new Vector2(1f, 0f);
                infoRect.pivot = new Vector2(0.5f, 0f);
                infoRect.sizeDelta = new Vector2(0f, CardHeight - PreviewHeight);
                infoRect.anchoredPosition = Vector2.zero;

                var marker = NewImage(card.transform, "SelectedMarker", _accent);
                var markerRect = marker.rectTransform;
                markerRect.anchorMin = new Vector2(0f, 0f);
                markerRect.anchorMax = new Vector2(0f, 1f);
                markerRect.pivot = new Vector2(0f, 0.5f);
                markerRect.sizeDelta = new Vector2(6f, 0f);
                markerRect.anchoredPosition = Vector2.zero;
                marker.gameObject.SetActive(false);

                NewText(card.transform, "Index", (i + 1).ToString("00"), 20f, _accent,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(20f, -16f), new Vector2(60f, 26f), 12f);

                NewText(card.transform, "Name", profile.DisplayName, 28f, _text,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(22f, -PreviewHeight - 18f),
                    new Vector2(CardWidth - 44f, 40f), 5f);

                NewText(card.transform, "Role", profile.Role, 15f, _accent,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(22f, -PreviewHeight - 60f),
                    new Vector2(CardWidth - 44f, 22f), 14f);

                NewText(card.transform, "Stats", StatsLine(profile), 12f, _dim,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(22f, -PreviewHeight - 84f),
                    new Vector2(CardWidth - 44f, 44f), 4f);

                NewText(card.transform, "Description", profile.Description, 13f, _dim,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 0f),
                    new Vector2(22f, 16f), new Vector2(CardWidth - 44f, 62f), 3f);

                // Рамка последней: иначе превью и плашка перекрывали бы её.
                var border = NewBorder(card.transform, 1.5f);

                _cards[i] = button;
                _cardLines[i] = line;
                _cardMarkers[i] = marker;
                _cardBorders[i] = border;
            }
        }

        /// <summary>Рамка из четырёх тонких полос: у Image нет бордера без спрайта.</summary>
        private static Image[] NewBorder(Transform parent, float thickness)
        {
            var top = NewImage(parent, "BorderTop", Color.white);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, thickness);
            top.rectTransform.anchoredPosition = Vector2.zero;

            var bottom = NewImage(parent, "BorderBottom", Color.white);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, thickness);
            bottom.rectTransform.anchoredPosition = Vector2.zero;

            var left = NewImage(parent, "BorderLeft", Color.white);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.pivot = new Vector2(0f, 0.5f);
            left.rectTransform.sizeDelta = new Vector2(thickness, 0f);
            left.rectTransform.anchoredPosition = Vector2.zero;

            var right = NewImage(parent, "BorderRight", Color.white);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.pivot = new Vector2(1f, 0.5f);
            right.rectTransform.sizeDelta = new Vector2(thickness, 0f);
            right.rectTransform.anchoredPosition = Vector2.zero;

            return new[] { top, bottom, left, right };
        }

        private static string StatsLine(RobotProfile profile)
        {
            // Две строки, а не одна: четыре пары «показатель + процент» в ширину
            // карточки не влезают и обрезались по правому краю.
            return
                "WALK " + Percent(profile.WalkMultiplier) +
                "  ·  SPRINT " + Percent(profile.SprintMultiplier) + "\n" +
                "CARRY " + Percent(profile.CarryMultiplier) +
                "  ·  JUMP " + Percent(profile.JumpMultiplier);
        }

        private static string Percent(float multiplier)
        {
            return Mathf.RoundToInt(multiplier * 100f) + "%";
        }

        private Button NewButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(320f, 64f);

            var background = go.AddComponent<Image>();
            background.color = _cardIdle;

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = _cardIdle;
            colors.highlightedColor = _cardHover;
            colors.pressedColor = _accent;
            colors.selectedColor = _cardHover;
            colors.disabledColor = new Color(_cardIdle.r, _cardIdle.g, _cardIdle.b, 0.35f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            if (action != null)
                button.onClick.AddListener(action);

            NewText(go.transform, "Label", label, 26f, _text, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 40f), 12f);

            return button;
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
