using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Подсветка пункта меню в стиле приборной панели: при наведении левый маркер
    /// утолщается и зажигается, снизу разворачивается акцентная линия, из-под текста
    /// проступает холодное свечение, а сама строка уезжает вправо и набирает разрядку
    /// с уходом в циан. Всё едет по unscaled-времени, поэтому подсветка не зависит
    /// от <c>Time.timeScale</c> и одинаково работает в редакторе и в WebGL.
    ///
    /// Компонент не строит геометрию сам: готовые элементы передаёт
    /// <see cref="MainMenuScreen"/>, потому что меню собирается кодом и стиль живёт там же.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TechButtonFx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [Header("Тайминг")]
        [Tooltip("За сколько секунд подсветка доезжает до конечного состояния")]
        public float Duration = 0.16f;

        [Header("Маркер")]
        [Tooltip("Толщина маркера в покое")]
        public float MarkerIdleWidth = 3f;

        [Tooltip("Толщина маркера под курсором")]
        public float MarkerHoverWidth = 7f;

        [Header("Строка")]
        [Tooltip("Отступ строки от левого края в покое")]
        public float LabelIdleOffset = 28f;

        [Tooltip("Отступ строки от левого края под курсором")]
        public float LabelHoverOffset = 42f;

        [Tooltip("Разрядка букв в покое")]
        public float SpacingIdle = 10f;

        [Tooltip("Разрядка букв под курсором")]
        public float SpacingHover = 18f;

        [Tooltip("Насколько строка уходит в цвет акцента под курсором, 0..1")]
        [Range(0f, 1f)] public float AccentMix = 0.35f;

        [Header("Свечение и линия")]
        [Tooltip("Прозрачность холодного свечения под курсором, 0..1")]
        [Range(0f, 1f)] public float GlowAlpha = 0.22f;

        [Tooltip("Прозрачность акцентной линии снизу под курсором, 0..1")]
        [Range(0f, 1f)] public float UnderlineAlpha = 0.95f;

        private RectTransform _marker;
        private Image _markerImage;
        private RectTransform _underline;
        private Image _underlineImage;
        private RawImage _glow;
        private RectTransform _label;
        private TextMeshProUGUI _labelText;
        private float _width;
        private Color _accent;
        private Color _labelColor;
        private float _value;
        private float _target;

        /// <summary>
        /// Привязывает элементы кнопки к подсветке и сразу ставит их в состояние покоя.
        /// </summary>
        public void Bind(
            RectTransform marker, RectTransform underline, RawImage glow,
            RectTransform label, TextMeshProUGUI labelText,
            float width, Color accent, Color labelColor)
        {
            _marker = marker;
            _markerImage = marker != null ? marker.GetComponent<Image>() : null;
            _underline = underline;
            _underlineImage = underline != null ? underline.GetComponent<Image>() : null;
            _glow = glow;
            _label = label;
            _labelText = labelText;
            _width = width;
            _accent = accent;
            _labelColor = labelColor;

            _value = 0f;
            _target = 0f;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData) => _target = 1f;

        public void OnPointerExit(PointerEventData eventData) => _target = 0f;

        public void OnSelect(BaseEventData eventData) => _target = 1f;

        public void OnDeselect(BaseEventData eventData) => _target = 0f;

        private void Update()
        {
            if (Mathf.Approximately(_value, _target))
                return;

            _value = Mathf.MoveTowards(_value, _target, Time.unscaledDeltaTime / Mathf.Max(Duration, 0.0001f));
            Apply();
        }

        private void Apply()
        {
            var t = Mathf.SmoothStep(0f, 1f, _value);

            if (_marker != null)
            {
                var size = _marker.sizeDelta;
                _marker.sizeDelta = new Vector2(Mathf.Lerp(MarkerIdleWidth, MarkerHoverWidth, t), size.y);

                if (_markerImage != null)
                {
                    var color = _accent;
                    color.a = Mathf.Lerp(0.45f, 1f, t);
                    _markerImage.color = color;
                }
            }

            if (_underline != null)
            {
                var size = _underline.sizeDelta;
                _underline.sizeDelta = new Vector2(_width * t, size.y);

                if (_underlineImage != null)
                {
                    var color = _accent;
                    color.a = UnderlineAlpha * t;
                    _underlineImage.color = color;
                }
            }

            if (_glow != null)
            {
                var color = _accent;
                color.a = GlowAlpha * t;
                _glow.color = color;
            }

            if (_label != null)
            {
                var position = _label.anchoredPosition;
                _label.anchoredPosition = new Vector2(Mathf.Lerp(LabelIdleOffset, LabelHoverOffset, t), position.y);
            }

            if (_labelText != null)
            {
                _labelText.characterSpacing = Mathf.Lerp(SpacingIdle, SpacingHover, t);
                _labelText.color = Color.Lerp(_labelColor, _accent, AccentMix * t);
            }
        }
    }
}
