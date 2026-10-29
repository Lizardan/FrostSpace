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
    /// Вариант «примерочная»: слева список корпусов с миниатюрами, справа —
    /// крупный выбранный робот на голографическом кольце, статы и описание.
    /// Превью строятся <see cref="CharacterCardPreview"/>; выбор меняет героя
    /// с коротким fade/scale-переходом.
    ///
    /// Модульная сборка («СОБРАТЬ СВОЙ») видна, но заблокирована: части
    /// роботов — отдельный этап, и кнопка не должна обещать то, чего нет.
    ///
    /// Геометрия строится кодом, как в меню: Canvas и EventSystem создаются
    /// здесь, сцена несёт только фоновую камеру и свет.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CharacterSetupScreen : MonoBehaviour
    {
        [Header("Данные")]
        [Tooltip("Каталог корпусов: список и герой строятся по нему")]
        [SerializeField] private CharacterCatalog _catalog;
        [Tooltip("Сцена, которая грузится по кнопке подтверждения")]
        [SerializeField] private string _nextScene = "Train";

        [Header("Шрифт")]
        [Tooltip("Кириллический TMP-ассет; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _rowIdle = new(0.035f, 0.060f, 0.078f, 0.38f);
        [SerializeField] private Color _rowHover = new(0.065f, 0.120f, 0.155f, 0.62f);
        [SerializeField] private Color _rowSelected = new(0.040f, 0.110f, 0.145f, 0.82f);
        [SerializeField] private Color _buttonIdle = new(0.045f, 0.075f, 0.095f, 0.92f);
        [SerializeField] private Color _buttonHover = new(0.08f, 0.14f, 0.18f, 0.95f);

        private const float SidebarX = 96f;
        private const float SidebarTop = 330f;
        private const float SidebarWidth = 380f;
        private const float RowHeight = 88f;
        private const float RowStep = 98f;
        private const float HeroX = 520f;
        private const float HeroTop = 250f;
        private const float HeroWidth = 1240f;
        private const float HeroHeight = 600f;
        private const float StatsX = 540f;
        private const float TransitionSeconds = 0.22f;

        private static readonly string[] StatLabels =
            { "ХОДЬБА", "БЕГ", "ПЕРЕНОС", "ПРЫЖОК" };

        private RobotProfile[] _profiles;
        private Button[] _rows;
        private Image[] _rowBackgrounds;
        private Image[] _rowMarkers;
        private RawImage[] _rowImages;
        private TextMeshProUGUI[] _rowNames;
        private TextMeshProUGUI[] _rowRoles;
        private Button _confirm;

        private RectTransform _heroRoot;
        private CanvasGroup _heroGroup;
        private RawImage _heroImage;
        private CharacterCardPreview _heroPreview;
        private Image _heroRing;
        private TextMeshProUGUI _heroIndex;
        private TextMeshProUGUI _heroName;
        private TextMeshProUGUI _heroRole;
        private TextMeshProUGUI _heroDescription;
        private Image[] _statFills;
        private TextMeshProUGUI[] _statValues;
        private Texture2D _ringTexture;
        private Sprite _ringSprite;
        private float _transition = 1f;
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
            if (_confirm != null)
                _confirm.interactable = !busy && _selected >= 0;

            // Меню не должно перелистываться под руками на переходе.
            if (!busy && Keyboard.current != null && _rows != null)
            {
                for (var i = 0; i < _rows.Length && i < 9; i++)
                {
                    if (!Keyboard.current[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                        continue;

                    Select(i);
                    break;
                }
            }

            if (_heroGroup == null)
                return;

            if (_transition < 1f)
            {
                _transition = Mathf.MoveTowards(
                    _transition, 1f, Time.unscaledDeltaTime / TransitionSeconds);
                var eased = Mathf.SmoothStep(0f, 1f, _transition);
                _heroGroup.alpha = eased;
                _heroRoot.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, eased);
            }

            if (_heroRing != null)
            {
                var alpha = (0.34f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.06f) *
                            _heroGroup.alpha;
                _heroRing.color = new Color(_accent.r, _accent.g, _accent.b, alpha);
            }
        }

        private void OnDisable()
        {
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if (_ringSprite != null)
                Destroy(_ringSprite);
            if (_ringTexture != null)
                Destroy(_ringTexture);
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

            for (var i = 0; i < _rows.Length; i++)
            {
                var selected = i == index;
                var background = _rowBackgrounds[i];
                var button = _rows[i];

                background.color = selected ? _rowSelected : _rowIdle;

                var colors = button.colors;
                colors.normalColor = selected ? _rowSelected : _rowIdle;
                colors.highlightedColor = selected ? _rowSelected : _rowHover;
                colors.pressedColor = _accent;
                colors.selectedColor = selected ? _rowSelected : _rowHover;
                button.colors = colors;

                _rowMarkers[i].gameObject.SetActive(selected);
                _rowImages[i].color = selected
                    ? Color.white
                    : new Color(0.55f, 0.65f, 0.70f, 0.72f);
                _rowNames[i].color = selected ? _text : _dim;
                _rowRoles[i].color = selected ? _accent : _dim;
            }

            var profile = _profiles[index];
            _heroPreview.Show(profile, _heroImage, index + 10);
            _heroIndex.text = $"{index + 1:00} / {_profiles.Length:00}";
            _heroName.text = profile.DisplayName;
            _heroRole.text = profile.Role;
            _heroDescription.text = profile.Description;
            SetStats(profile);

            _transition = 0f;
            _heroGroup.alpha = 0f;
            _heroRoot.localScale = Vector3.one * 0.97f;

            Debug.Log($"[CharacterSetupScreen] выбран {profile.Id}", this);
        }

        private void SetStats(RobotProfile profile)
        {
            var values = new[]
            {
                profile.WalkMultiplier,
                profile.SprintMultiplier,
                profile.CarryMultiplier,
                profile.JumpMultiplier
            };

            for (var i = 0; i < _statFills.Length && i < values.Length; i++)
            {
                _statValues[i].text = Percent(values[i]);
                _statValues[i].color = values[i] >= 1f ? _accent : _dim;
                _statFills[i].rectTransform.anchorMax = new Vector2(
                    Mathf.Clamp01(values[i] * 0.8f), 1f);
            }
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

            NewText(root, "Title", "ВЫБЕРИ КОРПУС", 64f, _text, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -108f), new Vector2(1000f, 90f), 8f);

            NewText(root, "Subtitle", "ТВОЙ КОРПУС — ТВОЙ РЕЙС", 20f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(96f, -196f),
                new Vector2(900f, 30f), 8f);

            var accentRule = NewImage(root, "AccentRule", _accent);
            var ruleRect = accentRule.rectTransform;
            ruleRect.anchorMin = new Vector2(0f, 1f);
            ruleRect.anchorMax = new Vector2(0f, 1f);
            ruleRect.pivot = new Vector2(0f, 1f);
            ruleRect.sizeDelta = new Vector2(64f, 3f);
            ruleRect.anchoredPosition = new Vector2(96f, -238f);

            NewText(root, "CreateNote", "МОДУЛЬНАЯ СБОРКА  /  НЕДОСТУПНО", 16f, _dim,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f),
                new Vector2(-96f, -64f), new Vector2(700f, 26f), 8f);

            NewText(root, "Hint", "ВЫБЕРИТЕ КОРПУС ИЛИ НАЖМИТЕ 1 2 3", 18f, _dim,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f),
                new Vector2(96f, 64f), new Vector2(800f, 28f), 8f);

            var divider = NewImage(root, "Divider", new Color(_accent.r, _accent.g, _accent.b, 0.12f));
            var dividerRect = divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(0f, 1f);
            dividerRect.pivot = new Vector2(0f, 1f);
            dividerRect.sizeDelta = new Vector2(1f, 520f);
            dividerRect.anchoredPosition = new Vector2(492f, -300f);

            BuildSidebar(root);
            BuildHero(root);
            BuildButtons(root);
        }

        private void BuildSidebar(Transform root)
        {
            NewText(root, "ListLabel", "ДОСТУПНЫЕ КОРПУСА", 16f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(SidebarX, -292f),
                new Vector2(SidebarWidth, 24f), 8f);

            var count = _profiles != null ? _profiles.Length : 0;
            _rows = new Button[count];
            _rowBackgrounds = new Image[count];
            _rowMarkers = new Image[count];
            _rowImages = new RawImage[count];
            _rowNames = new TextMeshProUGUI[count];
            _rowRoles = new TextMeshProUGUI[count];

            for (var i = 0; i < count; i++)
            {
                var profile = _profiles[i];
                var row = new GameObject(profile.Id, typeof(RectTransform));
                row.transform.SetParent(root, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(0f, 1f);
                rowRect.pivot = new Vector2(0f, 1f);
                rowRect.sizeDelta = new Vector2(SidebarWidth, RowHeight);
                rowRect.anchoredPosition = new Vector2(SidebarX, -SidebarTop - i * RowStep);

                var background = row.AddComponent<Image>();
                background.color = _rowIdle;
                background.raycastTarget = true;

                var button = row.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = _rowIdle;
                colors.highlightedColor = _rowHover;
                colors.pressedColor = _accent;
                colors.selectedColor = _rowHover;
                colors.fadeDuration = 0.1f;
                button.colors = colors;
                var index = i;
                button.onClick.AddListener(() => Select(index));

                var marker = NewImage(row.transform, "SelectedMarker", _accent);
                var markerRect = marker.rectTransform;
                markerRect.anchorMin = new Vector2(0f, 0f);
                markerRect.anchorMax = new Vector2(0f, 1f);
                markerRect.pivot = new Vector2(0f, 0.5f);
                markerRect.sizeDelta = new Vector2(4f, 0f);
                markerRect.anchoredPosition = Vector2.zero;
                marker.gameObject.SetActive(false);

                var previewObject = new GameObject("MiniPreview", typeof(RectTransform));
                previewObject.transform.SetParent(row.transform, false);
                var previewRect = (RectTransform)previewObject.transform;
                previewRect.anchorMin = new Vector2(0f, 1f);
                previewRect.anchorMax = new Vector2(0f, 1f);
                previewRect.pivot = new Vector2(0f, 1f);
                previewRect.sizeDelta = new Vector2(64f, 80f);
                previewRect.anchoredPosition = new Vector2(10f, -4f);

                var previewImage = previewObject.AddComponent<RawImage>();
                previewImage.raycastTarget = false;
                previewImage.color = Color.white;

                var preview = previewObject.AddComponent<CharacterCardPreview>();
                preview.Configure(false, new Vector2Int(256, 320));
                preview.Show(profile, previewImage, i);

                _rowNames[i] = NewText(row.transform, "Name", profile.DisplayName, 17f, _text,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(86f, -12f), new Vector2(280f, 24f), 2f);

                NewText(row.transform, "Index", (i + 1).ToString("00"), 11f, _accent,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(86f, -36f), new Vector2(32f, 18f), 4f);

                _rowRoles[i] = NewText(row.transform, "Role", profile.Role, 11f, _dim,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(116f, -36f), new Vector2(250f, 18f), 4f);

                _rows[i] = button;
                _rowBackgrounds[i] = background;
                _rowMarkers[i] = marker;
                _rowImages[i] = previewImage;
            }
        }

        private void BuildHero(Transform root)
        {
            var hero = new GameObject("Hero", typeof(RectTransform));
            hero.transform.SetParent(root, false);
            _heroRoot = (RectTransform)hero.transform;
            _heroRoot.anchorMin = new Vector2(0f, 1f);
            _heroRoot.anchorMax = new Vector2(0f, 1f);
            _heroRoot.pivot = new Vector2(0.5f, 0.5f);
            _heroRoot.sizeDelta = new Vector2(HeroWidth, HeroHeight);
            _heroRoot.anchoredPosition = new Vector2(
                HeroX + HeroWidth * 0.5f, -HeroTop - HeroHeight * 0.5f);

            _heroGroup = hero.AddComponent<CanvasGroup>();

            _heroIndex = NewText(hero.transform, "Index", "01 / 03", 12f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(90f, 20f), 4f);

            _heroName = NewText(hero.transform, "Name", string.Empty, 34f, _text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(0f, -18f), new Vector2(760f, 44f), 4f);

            _heroRole = NewText(hero.transform, "Role", string.Empty, 16f, _accent,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(0f, -62f), new Vector2(700f, 24f), 10f);

            _heroRing = NewImage(hero.transform, "HoloRing", new Color(_accent.r, _accent.g, _accent.b, 0.34f));
            var ringRect = _heroRing.rectTransform;
            ringRect.anchorMin = new Vector2(0f, 1f);
            ringRect.anchorMax = new Vector2(0f, 1f);
            ringRect.pivot = new Vector2(0f, 1f);
            ringRect.sizeDelta = new Vector2(440f, 80f);
            ringRect.anchoredPosition = new Vector2(10f, -540f);
            _heroRing.sprite = RingSprite;
            _heroRing.preserveAspect = false;

            var previewObject = new GameObject("HeroPreview", typeof(RectTransform));
            previewObject.transform.SetParent(hero.transform, false);
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.anchorMin = new Vector2(0f, 1f);
            previewRect.anchorMax = new Vector2(0f, 1f);
            previewRect.pivot = new Vector2(0f, 1f);
            previewRect.sizeDelta = new Vector2(460f, 500f);
            previewRect.anchoredPosition = new Vector2(0f, -92f);

            _heroImage = previewObject.AddComponent<RawImage>();
            _heroImage.raycastTarget = false;
            _heroImage.color = Color.white;

            _heroPreview = previewObject.AddComponent<CharacterCardPreview>();
            _heroPreview.Configure(true, new Vector2Int(640, 700), 30f, 1f);

            NewText(hero.transform, "StatsTitle", "ХАРАКТЕРИСТИКИ", 13f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(StatsX, -20f), new Vector2(300f, 22f), 8f);

            _statValues = new TextMeshProUGUI[StatLabels.Length];
            _statFills = new Image[StatLabels.Length];

            for (var i = 0; i < StatLabels.Length; i++)
            {
                var y = -50f - i * 62f;
                NewText(hero.transform, "StatLabel" + i, StatLabels[i], 14f, _text,
                    TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                    new Vector2(StatsX, y), new Vector2(240f, 22f), 4f);

                _statValues[i] = NewText(hero.transform, "StatValue" + i, string.Empty,
                    14f, _accent, TextAlignmentOptions.TopRight, new Vector2(1f, 1f),
                    new Vector2(-StatsX + 20f, y), new Vector2(100f, 22f), 4f);

                var track = NewImage(hero.transform, "StatTrack" + i,
                    new Color(_dim.r, _dim.g, _dim.b, 0.28f));
                var trackRect = track.rectTransform;
                trackRect.anchorMin = new Vector2(0f, 1f);
                trackRect.anchorMax = new Vector2(0f, 1f);
                trackRect.pivot = new Vector2(0f, 1f);
                trackRect.sizeDelta = new Vector2(620f, 6f);
                trackRect.anchoredPosition = new Vector2(StatsX, y - 24f);

                var fill = NewImage(trackRect, "StatFill" + i, _accent);
                var fillRect = fill.rectTransform;
                fillRect.anchorMin = new Vector2(0f, 0f);
                fillRect.anchorMax = new Vector2(1f, 1f);
                fillRect.pivot = new Vector2(0f, 0.5f);
                fillRect.sizeDelta = Vector2.zero;
                fillRect.anchoredPosition = Vector2.zero;
                _statFills[i] = fill;
            }

            _heroDescription = NewText(hero.transform, "Description", string.Empty, 14f, _dim,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
                new Vector2(StatsX, -330f), new Vector2(620f, 130f), 2f);
            _heroDescription.textWrappingMode = TextWrappingModes.Normal;
        }

        private void BuildButtons(Transform root)
        {
            _confirm = NewButton(root, "Confirm", "ПОДТВЕРДИТЬ", OnConfirmPressed);
            var confirmRect = (RectTransform)_confirm.transform;
            confirmRect.anchorMin = new Vector2(1f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(1f, 0f);
            confirmRect.sizeDelta = new Vector2(320f, 64f);
            confirmRect.anchoredPosition = new Vector2(-96f, 64f);

            foreach (var edge in NewBorder(_confirm.transform, 1.5f))
                edge.color = new Color(_accent.r, _accent.g, _accent.b, 0.55f);

            var create = NewButton(root, "CreateOwn", "СОБРАТЬ СВОЙ", null);
            var createRect = (RectTransform)create.transform;
            createRect.anchorMin = new Vector2(1f, 0f);
            createRect.anchorMax = new Vector2(1f, 0f);
            createRect.pivot = new Vector2(1f, 0f);
            createRect.sizeDelta = new Vector2(320f, 64f);
            createRect.anchoredPosition = new Vector2(-96f, 148f);
            create.interactable = false;
        }

        private Sprite RingSprite
        {
            get
            {
                if (_ringSprite != null)
                    return _ringSprite;

                _ringTexture = BuildRingTexture();
                _ringSprite = Sprite.Create(
                    _ringTexture,
                    new Rect(0f, 0f, _ringTexture.width, _ringTexture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                return _ringSprite;
            }
        }

        /// <summary>Пунктирное голографическое кольцо под ногами героя.</summary>
        private static Texture2D BuildRingTexture()
        {
            const int width = 512;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ChassisHoloRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var cx = (width - 1) * 0.5f;
            var cy = (height - 1) * 0.5f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var nx = (x - cx) / (width * 0.46f);
                    var ny = (y - cy) / (height * 0.30f);
                    var radius = Mathf.Sqrt(nx * nx + ny * ny);
                    var angle = Mathf.Atan2(ny, nx);
                    var dashed = Mathf.Sin(angle * 18f) > 0.22f;
                    var ring = Mathf.Abs(radius - 1f) < 0.035f && dashed;
                    var inner = Mathf.Abs(radius - 0.84f) < 0.014f;
                    var glow = Mathf.Exp(-Mathf.Pow((radius - 1f) / 0.11f, 2f)) * 0.22f;
                    var alpha = ring ? 1f : inner ? 0.55f : glow;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private static string Percent(float multiplier)
        {
            return Mathf.RoundToInt(multiplier * 100f) + "%";
        }

        private Button NewButton(
            Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(320f, 64f);

            var background = go.AddComponent<Image>();
            background.color = _buttonIdle;

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
            if (action != null)
                button.onClick.AddListener(action);

            NewText(go.transform, "Label", label, 22f, _text, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 40f), 6f);

            return button;
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
    }
}
