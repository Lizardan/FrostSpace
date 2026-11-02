using System.Collections;
using FrostSpace.Features.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Hud
{
    /// <summary>
    /// Сегментная полоса опыта внизу экрана: без подложки, только циановые
    /// насечки, уровни по краям и текст внутри. Данные читает у
    /// <see cref="PlayerExperience"/>, своего состояния не хранит.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(255)]
    public sealed class ExperienceBar : MonoBehaviour
    {
        private const int SegmentCount = 24;
        private const float BarWidth = 1100f;
        private const float BarHeight = 44f;
        private const float SideWidth = 100f;
        private const float SegmentGap = 3f;

        [Header("Ссылки")]
        [Tooltip("Опыт игрока; пусто — ищется в сцене")]
        [SerializeField] private PlayerExperience _experience;

        [Tooltip("Кириллический TMP-ассет; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.98f, 1f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _segmentOff = new(0.302f, 0.902f, 1f, 0.12f);

        private GameObject _root;
        private CanvasGroup _group;
        private Coroutine _fade;
        private TextMeshProUGUI _level;
        private TextMeshProUGUI _nextLevel;
        private TextMeshProUGUI _progress;
        private readonly Image[] _segments = new Image[SegmentCount];
        private int _shownFilled = -1;
        private int _shownLevel = -1;
        private int _shownNextLevel = -1;
        private int _shownExperience = -1;
        private int _shownRequired = -1;

        private void Awake()
        {
            if (_experience == null)
                _experience = FindAnyObjectByType<PlayerExperience>();

            Build();
        }

        private void LateUpdate()
        {
            if (_experience == null)
            {
                _root.SetActive(false);
                return;
            }

            _root.SetActive(true);

            var level = _experience.Level;
            var next = level + 1;
            var required = _experience.ExperienceToNextLevel;
            var experience = _experience.Experience;
            var progress = _experience.Progress01;
            var percent = Mathf.RoundToInt(progress * 100f);

            if (level != _shownLevel)
            {
                _level.text = $"УР. {level}";
                _shownLevel = level;
            }

            if (next != _shownNextLevel)
            {
                _nextLevel.text = $"УР. {next}";
                _shownNextLevel = next;
            }

            if (experience != _shownExperience || required != _shownRequired)
            {
                _progress.text = $"{experience} / {required}  ({percent}%)";
                _shownExperience = experience;
                _shownRequired = required;
            }

            var filled = Mathf.FloorToInt(progress * SegmentCount + 0.0001f);
            if (filled == _shownFilled)
                return;

            _shownFilled = filled;
            for (var i = 0; i < _segments.Length; i++)
            {
                if (_segments[i] != null)
                    _segments[i].color = i < filled ? _accent : _segmentOff;
            }
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root);
        }

        /// <summary>
        /// Плавно скрывает или показывает полосу. Нужно сценовым сценам: во время
        /// прибытия поезда интерфейс не должен висеть поверх кадра.
        /// </summary>
        public void SetVisible(bool visible, float seconds = 0f)
        {
            if (_group == null)
                return;

            if (_fade != null)
                StopCoroutine(_fade);

            _fade = StartCoroutine(FadeTo(visible ? 1f : 0f, seconds));
        }

        private IEnumerator FadeTo(float to, float seconds)
        {
            var from = _group.alpha;
            if (seconds <= 0f)
            {
                _group.alpha = to;
                _fade = null;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                _group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                yield return null;
            }

            _group.alpha = to;
            _fade = null;
        }

        private void Build()
        {
            _root = new GameObject("Experience Canvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var parent = (RectTransform)_root.transform;

            var line = new GameObject("XpLine", typeof(RectTransform));
            line.transform.SetParent(parent, false);
            var lineRect = (RectTransform)line.transform;
            lineRect.anchorMin = new Vector2(0.5f, 0f);
            lineRect.anchorMax = new Vector2(0.5f, 0f);
            lineRect.pivot = new Vector2(0.5f, 0f);
            lineRect.anchoredPosition = new Vector2(0f, 20f);
            lineRect.sizeDelta = new Vector2(BarWidth, BarHeight);

            _level = NewText(lineRect, "Level", "УР. 1", 18f, _accent,
                new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(SideWidth - 12f, 30f),
                TextAlignmentOptions.MidlineLeft, 3f);

            _nextLevel = NewText(lineRect, "NextLevel", "УР. 2", 18f, _dim,
                new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(SideWidth - 12f, 30f),
                TextAlignmentOptions.MidlineRight, 3f);

            var segments = new GameObject("Segments", typeof(RectTransform));
            segments.transform.SetParent(lineRect, false);
            var segmentsRect = (RectTransform)segments.transform;
            segmentsRect.anchorMin = new Vector2(0f, 0.5f);
            segmentsRect.anchorMax = new Vector2(1f, 0.5f);
            segmentsRect.pivot = new Vector2(0.5f, 0.5f);
            segmentsRect.offsetMin = new Vector2(SideWidth, -17f);
            segmentsRect.offsetMax = new Vector2(-SideWidth, 17f);

            for (var i = 0; i < SegmentCount; i++)
            {
                var segment = new GameObject($"Segment_{i:00}", typeof(RectTransform));
                segment.transform.SetParent(segmentsRect, false);

                var rect = (RectTransform)segment.transform;
                rect.anchorMin = new Vector2(i / (float)SegmentCount, 0f);
                rect.anchorMax = new Vector2((i + 1) / (float)SegmentCount, 1f);
                rect.offsetMin = new Vector2(i == 0 ? 0f : SegmentGap * 0.5f, 0f);
                rect.offsetMax = new Vector2(
                    i == SegmentCount - 1 ? 0f : -SegmentGap * 0.5f, 0f);

                var image = segment.AddComponent<Image>();
                image.color = _segmentOff;
                image.raycastTarget = false;
                _segments[i] = image;
            }

            _progress = NewText(lineRect, "Progress", string.Empty, 16f, _text,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(BarWidth - SideWidth * 2f, 26f),
                TextAlignmentOptions.Center, 2f);

            _root.SetActive(false);
        }

        private TextMeshProUGUI NewText(
            RectTransform parent, string name, string content, float size, Color color,
            Vector2 anchor, Vector2 position, Vector2 boxSize,
            TextAlignmentOptions alignment, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(
                anchor.x <= 0f ? 0f : anchor.x >= 1f ? 1f : 0.5f,
                anchor.y <= 0f ? 0f : anchor.y >= 1f ? 1f : 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = boxSize;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            AddOutline(text);
            return text;
        }

        private static void AddOutline(TextMeshProUGUI text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.1f, -1.1f);
        }
    }
}
