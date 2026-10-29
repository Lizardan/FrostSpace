using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Белый титульный экран между поездом и депо: имя игры и строка рейса.
    /// Живёт поверх занавеса <see cref="Core.SceneController"/> и переживает
    /// загрузку сцены, поэтому создаётся один раз и уничтожается после fade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleCardScreen : MonoBehaviour
    {
        private static TitleCardScreen _instance;

        [Header("Шрифт")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Тексты")]
        [SerializeField] private string _title = "FROSTSPACE";
        [SerializeField] private string _subtitle = "СЕВЕРНЫЙ РЕЙС  ·  ДЕПО";

        [Header("Палитра")]
        [SerializeField] private Color _ink = new(0.025f, 0.060f, 0.085f, 1f);
        [SerializeField] private Color _dim = new(0.22f, 0.38f, 0.45f, 1f);
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);

        private CanvasGroup _group;
        private bool _playing;

        /// <summary>Экран уже показан; повторный вызов игнорируется.</summary>
        public bool IsPlaying => _playing;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>Показывает белый экран и гаснет после загрузки следующей сцены.</summary>
        public void Play(float holdSeconds = 2f, float fadeIn = 0.45f, float fadeOut = 0.75f)
        {
            if (_playing)
                return;

            _playing = true;
            StartCoroutine(Run(holdSeconds, fadeIn, fadeOut));
        }

        private IEnumerator Run(float holdSeconds, float fadeIn, float fadeOut)
        {
            _group.alpha = 0f;
            yield return Fade(0f, 1f, fadeIn);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds));
            yield return Fade(1f, 0f, fadeOut);
            Destroy(gameObject);
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f)
            {
                _group.alpha = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                yield return null;
            }

            _group.alpha = to;
        }

        private void Build()
        {
            var root = new GameObject("TitleCardCanvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1200;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var background = new GameObject("White", typeof(RectTransform));
            background.transform.SetParent(root.transform, false);
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var image = background.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;

            var title = NewText(root.transform, "Title", _title, 112f, _ink,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 46f), new Vector2(1400f, 150f), 12f);
            title.fontStyle = FontStyles.Bold;

            var accent = new GameObject("Accent", typeof(RectTransform));
            accent.transform.SetParent(root.transform, false);
            var accentRect = (RectTransform)accent.transform;
            accentRect.anchorMin = new Vector2(0.5f, 0.5f);
            accentRect.anchorMax = new Vector2(0.5f, 0.5f);
            accentRect.pivot = new Vector2(0.5f, 0.5f);
            accentRect.sizeDelta = new Vector2(96f, 4f);
            accentRect.anchoredPosition = new Vector2(0f, -34f);
            var accentImage = accent.AddComponent<Image>();
            accentImage.color = _accent;
            accentImage.raycastTarget = false;

            NewText(root.transform, "Subtitle", _subtitle, 24f, _dim,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -86f), new Vector2(1200f, 36f), 10f);
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
            rect.pivot = new Vector2(0.5f, 0.5f);
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
