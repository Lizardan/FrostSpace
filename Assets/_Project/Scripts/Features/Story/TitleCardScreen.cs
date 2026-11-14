using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Титульный слой между поездом и депо: имя игры, строка рейса, процедурный снег
    /// и низкий гул. Чёрным владеет <see cref="Core.SceneController"/> — у титула
    /// собственной подложки нет, иначе два независимых затемнения накладываются и
    /// переход начинает «уходить долго, а выходить резко».
    ///
    /// Живёт поверх занавеса и переживает загрузку сцены, поэтому создаётся один раз
    /// и уничтожается после гашения.
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
        [Tooltip("Цвет названия: светлый, потому что под ним чёрный занавес")]
        [SerializeField] private Color _titleColor = new(0.8627f, 0.9137f, 0.949f, 1f);
        [SerializeField] private Color _dim = new(0.22f, 0.38f, 0.45f, 1f);
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);

        [Header("Звук")]
        [Tooltip("Низкий гул титра; гаснет вместе с экраном")]
        [SerializeField] private AudioClip _drone;
        [SerializeField, Range(0f, 1f)] private float _droneVolume = 0.4f;

        private CanvasGroup _group;
        private AudioSource _audio;
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

        /// <summary>
        /// Титр целиком внутри чёрного: проявление, держание, уход. Занавес запускает
        /// эту рутину между «закрыт» и «проявление» и ждёт её конца, поэтому титр не
        /// делит кадр ни с аварией, ни с двором. Собственного фона у титра нет —
        /// чёрным владеет один <see cref="Core.SceneController"/>.
        /// </summary>
        /// <remarks>
        /// Гул гаснет отдельной корутиной уже в новой сцене: если ждать активации
        /// внутри этой рутины, получится кольцо — занавес ждёт титр, а титр ждёт
        /// сцену, которую занавес ещё не активировал.
        /// </remarks>
        public IEnumerator PlayInsideBlack(string nextScene, float fadeIn, float hold,
            float fadeOut, float droneOut, float timeout)
        {
            if (_playing)
                yield break;

            _playing = true;

            yield return Fade(0f, 1f, fadeIn);

            if (hold > 0f)
                yield return new WaitForSecondsRealtime(hold);

            yield return Fade(_group.alpha, 0f, fadeOut);

            StartCoroutine(DroneOut(nextScene, droneOut, timeout));
        }

        /// <summary>
        /// Гул переживает склейку и гаснет под проявлением депо, пока ветер набирает
        /// громкость: на стыке не остаётся немой дыры. Ждать сцену обязан сам титр —
        /// он на `DontDestroyOnLoad`, а режиссёр финала умирает вместе со старой сценой.
        /// </summary>
        private IEnumerator DroneOut(string nextScene, float seconds, float timeout)
        {
            var startedAt = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != nextScene &&
                   Time.realtimeSinceStartup - startedAt < timeout)
            {
                yield return null;
            }

            yield return FadeDrone(seconds, 0f);
            Destroy(gameObject);
        }

        /// <summary>
        /// Гул стартует вместе с занавесом, а не с титром: под чёрным пар вагона ещё
        /// звучит, и гул входит поверх него, а не в тишину.
        /// </summary>
        public void StartDrone(float seconds)
        {
            if (_drone == null)
                return;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = _drone;
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0f;
            _audio.Play();

            StartCoroutine(FadeDrone(seconds, _droneVolume));
        }

        private IEnumerator FadeDrone(float seconds, float to)
        {
            if (_audio == null)
                yield break;

            var from = _audio.volume;
            if (seconds <= 0f)
            {
                _audio.volume = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _audio.volume = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                yield return null;
            }

            _audio.volume = to;
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

            BuildSnow(root.transform);

            var title = NewText(root.transform, "Title", _title, 112f, _titleColor,
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

        private void BuildSnow(Transform parent)
        {
            var go = new GameObject("Snow", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var snow = go.AddComponent<TitleSnow>();
            snow.raycastTarget = false;
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
