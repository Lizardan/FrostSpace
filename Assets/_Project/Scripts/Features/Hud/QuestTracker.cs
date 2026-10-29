using FrostSpace.Features.Quest;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Hud
{
    /// <summary>
    /// Карточка текущего задания в левом верхнем углу: название, цель и полоса
    /// прогресса. Читает <see cref="QuestLog"/> через сценовый
    /// <see cref="QuestDirector"/> и не хранит состояние (ADR 0027).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class QuestTracker : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Директор квеста сцены; пусто — ищется в сцене")]
        [SerializeField] private QuestDirector _director;

        [Tooltip("Шрифт трекера; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _plate = new(0.016f, 0.026f, 0.038f, 0.82f);
        [SerializeField] private Color _track = new(0.10f, 0.15f, 0.18f, 0.9f);

        private GameObject _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _objective;
        private TextMeshProUGUI _progressLabel;
        private GameObject _progressRoot;
        private Image _progressFill;
        private string _shownTitle;
        private string _shownObjective;
        private int _shownProgress = -1;
        private int _shownRequired = -1;

        private void Awake()
        {
            if (_director == null)
                _director = FindAnyObjectByType<QuestDirector>();

            Build();
        }

        private void LateUpdate()
        {
            var log = _director != null ? _director.Log : null;
            var objective = log != null && log.IsActive ? log.CurrentObjective : null;
            if (log == null || objective == null)
            {
                _root.SetActive(false);
                return;
            }

            _root.SetActive(true);

            var title = log.Quest != null ? log.Quest.Title : string.Empty;
            var line = objective.Text;
            if (title != _shownTitle)
            {
                _title.text = title;
                _shownTitle = title;
            }

            if (line != _shownObjective)
            {
                _objective.text = line;
                _shownObjective = line;
            }

            var required = Mathf.Max(1, objective.Required);
            var showProgress = required > 1;
            if (_progressRoot.activeSelf != showProgress)
                _progressRoot.SetActive(showProgress);

            if (!showProgress)
                return;

            if (_progressFill != null)
            {
                var fraction = Mathf.Clamp01(log.Progress / (float)required);
                _progressFill.rectTransform.anchorMax = new Vector2(fraction, 1f);
            }

            if (log.Progress != _shownProgress || required != _shownRequired)
            {
                _progressLabel.text = $"{log.Progress} / {required}";
                _shownProgress = log.Progress;
                _shownRequired = required;
            }
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root);
        }

        private void Build()
        {
            _root = new GameObject("Quest Canvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var parent = (RectTransform)_root.transform;

            var plate = NewImage(parent, "Plate", _plate);
            var plateRect = plate.rectTransform;
            plateRect.anchorMin = new Vector2(0f, 1f);
            plateRect.anchorMax = new Vector2(0f, 1f);
            plateRect.pivot = new Vector2(0f, 1f);
            plateRect.anchoredPosition = new Vector2(40f, -36f);
            plateRect.sizeDelta = new Vector2(600f, 176f);

            var edge = NewImage(plateRect, "TopEdge", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 3f);
            edgeRect.anchoredPosition = Vector2.zero;

            var stripe = NewImage(plateRect, "LeftStripe", _accent);
            var stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.sizeDelta = new Vector2(4f, -24f);
            stripeRect.anchoredPosition = new Vector2(0f, 0f);

            NewText(plateRect, "Caption", "ТЕКУЩИЙ КВЕСТ", 15f, _dim,
                new Vector2(28f, -18f), new Vector2(300f, 20f), 4f);

            _title = NewText(plateRect, "Title", string.Empty, 30f, _text,
                new Vector2(28f, -44f), new Vector2(540f, 38f), 2f);
            _title.fontStyle = FontStyles.Bold;

            _objective = NewText(plateRect, "Objective", string.Empty, 19f, _text,
                new Vector2(28f, -88f), new Vector2(540f, 28f), 0f);
            _objective.textWrappingMode = TextWrappingModes.Normal;

            _progressRoot = new GameObject("Progress", typeof(RectTransform));
            _progressRoot.transform.SetParent(plateRect, false);
            var progressRect = (RectTransform)_progressRoot.transform;
            progressRect.anchorMin = new Vector2(0f, 0f);
            progressRect.anchorMax = new Vector2(1f, 0f);
            progressRect.pivot = new Vector2(0.5f, 0f);
            progressRect.offsetMin = new Vector2(28f, 28f);
            progressRect.offsetMax = new Vector2(-28f, 0f);
            progressRect.sizeDelta = new Vector2(-56f, 28f);

            var track = NewImage(progressRect, "Track", _track);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.sizeDelta = new Vector2(0f, 8f);
            trackRect.anchoredPosition = new Vector2(0f, 0f);

            _progressFill = NewImage(trackRect, "Fill", _accent);
            var fillRect = _progressFill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = Vector2.zero;
            fillRect.anchoredPosition = Vector2.zero;

            _progressLabel = NewText(progressRect, "Count", string.Empty, 16f, _accent,
                new Vector2(0f, 8f), new Vector2(540f, 20f), 2f);
            _progressLabel.alignment = TextAlignmentOptions.TopRight;

            _root.SetActive(false);
        }

        private TextMeshProUGUI NewText(
            RectTransform parent, string name, string content, float size, Color color,
            Vector2 position, Vector2 boxSize, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = boxSize;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        private static Image NewImage(RectTransform parent, string name, Color color)
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
