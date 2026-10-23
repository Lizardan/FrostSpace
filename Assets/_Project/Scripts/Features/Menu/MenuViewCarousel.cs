using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Вертикальная карусель видов меню у правого края: выбранная строка стоит
    /// по центру и крупнее соседних, соседние — меньше и прозрачнее.
    ///
    /// Переключение не подменяет подписи, а прокручивает весь столбец: каждый
    /// пункт едет к своей новой высоте, поэтому смена вида читается как
    /// движение карусели вверх или вниз. Позиция считается от
    /// <see cref="Time.unscaledDeltaTime"/>, чтобы пауза не замораживала UI.
    ///
    /// Компонент не знает про <see cref="MenuViewSwitch"/>: он строит строки,
    /// отдаёт клик наружу и принимает выбранный индекс. Так UI остаётся
    /// отдельно от логики видов.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuViewCarousel : MonoBehaviour
    {
        private const float RowStep = 86f;
        private const float ColumnWidth = 420f;
        private const float RowHeight = 64f;
        private const float SmoothSeconds = 0.22f;
        private const float ShadeWidth = 460f;

        private static Texture2D _shadeTexture;

        /// <summary>
        /// Сброс статики между запусками Play Mode: домен не перезагружается, и
        /// HideAndDontSave-текстура иначе пережила бы сцену.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_shadeTexture != null)
                Destroy(_shadeTexture);

            _shadeTexture = null;
        }

        private string[] _labels = Array.Empty<string>();
        private Button[] _buttons = Array.Empty<Button>();
        private TextMeshProUGUI[] _texts = Array.Empty<TextMeshProUGUI>();
        private RectTransform[] _rows = Array.Empty<RectTransform>();

        private Action<int> _onSelect;
        private RectTransform _root;
        private RectTransform _progressRect;
        private Image _progress;
        private TextMeshProUGUI _counter;
        private float _scroll;
        private float _target;
        private float _velocity;
        private int _selected;
        private bool _ready;

        private Color _accent;
        private Color _text;
        private Color _dim;

        /// <summary>
        /// Собрать карусель внутри готового Canvas. Вызывается один раз из
        /// <see cref="MainMenuScreen"/> после создания Canvas.
        /// </summary>
        public void Build(
            Transform parent,
            string[] labels,
            int selected,
            Action<int> onSelect,
            Color accent,
            Color text,
            Color dim)
        {
            _labels = labels ?? Array.Empty<string>();
            _onSelect = onSelect;
            _accent = accent;
            _text = text;
            _dim = dim;

            BuildShade(parent);
            BuildRoot(parent);
            BuildRows();
            SetSelected(selected, true);
            _ready = true;
        }

        /// <summary>Плавно перевести карусель к виду; immediate — без прокрутки.</summary>
        public void SetSelected(int index, bool immediate)
        {
            if (_labels.Length == 0)
                return;

            _selected = Mathf.Clamp(index, 0, _labels.Length - 1);
            _target = _selected;
            if (immediate)
            {
                _scroll = _target;
                _velocity = 0f;
            }

            if (_counter != null)
                _counter.text = $"{_selected + 1:00} / {_labels.Length:00}";
        }

        /// <summary>Остаток времени до авто-смены: заливка под выбранной строкой.</summary>
        public void SetAutoProgress(float value)
        {
            if (_progress != null)
                _progress.fillAmount = Mathf.Clamp01(value);
        }

        public void SetInteractable(bool interactable)
        {
            for (var i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] != null)
                    _buttons[i].interactable = interactable;
            }
        }

        private void Update()
        {
            if (!_ready)
                return;

            _scroll = Mathf.SmoothDamp(
                _scroll, _target, ref _velocity, SmoothSeconds, Mathf.Infinity,
                Time.unscaledDeltaTime);
            if (Mathf.Abs(_scroll - _target) < 0.002f)
                _scroll = _target;

            RefreshRows();
        }

        private void RefreshRows()
        {
            for (var i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] == null || _texts[i] == null)
                    continue;

                var distance = Mathf.Abs(i - _scroll);
                var near = 1f - Mathf.Clamp01(distance / 2.35f);
                // Список читается сверху вниз: младший вид выше, старший ниже,
                // выбранная строка всегда остаётся в центре столбца.
                var y = (_scroll - i) * RowStep;

                _rows[i].anchoredPosition = new Vector2(
                    Mathf.Lerp(-4f, -18f, near), y);

                var size = Mathf.Lerp(19f, 40f, near);
                var alpha = Mathf.Lerp(0.20f, 1f, near);
                var color = Color.Lerp(_dim, _accent, near);
                color.a = alpha;
                _texts[i].fontSize = size;
                _texts[i].characterSpacing = Mathf.Lerp(4f, 13f, near);
                _texts[i].color = color;

                var background = _rows[i].GetComponent<Image>();
                if (background != null)
                {
                    var tint = Color.Lerp(_dim, _accent, near);
                    tint.a = near * 0.055f;
                    background.color = tint;
                }
            }

            if (_progressRect != null)
            {
                _progressRect.anchoredPosition = new Vector2(
                    -10f, (_scroll - _selected) * RowStep - 31f);
            }
        }

        private void OnRowPressed(int index)
        {
            _onSelect?.Invoke(index);
        }

        private void BuildShade(Transform parent)
        {
            var shade = new GameObject("CarouselShade", typeof(RectTransform));
            shade.transform.SetParent(parent, false);
            var rect = (RectTransform)shade.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ShadeWidth, 0f);
            rect.anchoredPosition = Vector2.zero;

            var image = shade.AddComponent<RawImage>();
            image.texture = ShadeTexture;
            image.color = new Color(0.008f, 0.016f, 0.026f, 0.95f);
            image.raycastTarget = false;
        }

        private void BuildRoot(Transform parent)
        {
            var go = new GameObject("ViewCarousel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = new Vector2(1f, 0.5f);
            _root.anchorMax = new Vector2(1f, 0.5f);
            _root.pivot = new Vector2(1f, 0.5f);
            _root.sizeDelta = new Vector2(ColumnWidth, 640f);
            _root.anchoredPosition = new Vector2(-64f, 0f);

            Header(_root, "VIEW MODE", 13f, _dim, new Vector2(0f, 428f), 10f);
            _counter = Header(_root, "01 / 05", 22f, _accent,
                new Vector2(0f, 392f), 5f);
        }

        private TextMeshProUGUI Header(
            Transform parent, string content, float size, Color color,
            Vector2 position, float spacing)
        {
            var go = new GameObject("Caption", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ColumnWidth, 34f);
            rect.anchoredPosition = position;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Right;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private void BuildRows()
        {
            var count = _labels.Length;
            _buttons = new Button[count];
            _texts = new TextMeshProUGUI[count];
            _rows = new RectTransform[count];

            for (var i = 0; i < count; i++)
            {
                var go = new GameObject($"View{i + 1}", typeof(RectTransform));
                go.transform.SetParent(_root, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(ColumnWidth, RowHeight);
                _rows[i] = rect;

                var background = go.AddComponent<Image>();
                background.color = new Color(1f, 1f, 1f, 0f);
                background.raycastTarget = true;

                var button = go.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                var index = i;
                button.onClick.AddListener(() => OnRowPressed(index));
                _buttons[i] = button;

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var textRect = (RectTransform)textGo.transform;
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.offsetMin = new Vector2(8f, 0f);
                textRect.offsetMax = new Vector2(-6f, 0f);

                var text = textGo.AddComponent<TextMeshProUGUI>();
                if (TMP_Settings.defaultFontAsset != null)
                    text.font = TMP_Settings.defaultFontAsset;
                text.text = $"{i + 1:00}   {_labels[i]}";
                text.fontSize = 22f;
                text.color = _text;
                text.alignment = TextAlignmentOptions.Right;
                text.characterSpacing = 6f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.raycastTarget = false;
                _texts[i] = text;
            }

            // Полоска прогресса авто-смены: заливка слева направо под выбранной
            // строкой, а не отдельный индикатор в доке.
            var progressGo = new GameObject("AutoProgress", typeof(RectTransform));
            progressGo.transform.SetParent(_root, false);
            _progressRect = (RectTransform)progressGo.transform;
            _progressRect.anchorMin = new Vector2(1f, 0.5f);
            _progressRect.anchorMax = new Vector2(1f, 0.5f);
            _progressRect.pivot = new Vector2(1f, 0.5f);
            _progressRect.sizeDelta = new Vector2(168f, 2f);

            _progress = progressGo.AddComponent<Image>();
            _progress.color = new Color(_accent.r, _accent.g, _accent.b, 0.80f);
            _progress.raycastTarget = false;
            _progress.type = Image.Type.Filled;
            _progress.fillMethod = Image.FillMethod.Horizontal;
            _progress.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progress.fillAmount = 0f;
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

        private static Texture2D BuildShadeTexture()
        {
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "MenuCarouselShade",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var x = 0; x < width; x++)
            {
                var t = x / (float)(width - 1);
                var alpha = Mathf.Lerp(0f, 0.62f, Mathf.Pow(t, 1.6f));
                texture.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
            }

            texture.Apply();
            return texture;
        }
    }
}
