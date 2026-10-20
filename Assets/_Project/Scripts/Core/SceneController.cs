using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FrostSpace.Core
{
    /// <summary>
    /// Единственный владелец перехода между сценами. Белый занавес строится кодом на
    /// объекте с <c>DontDestroyOnLoad</c> и живёт вне сцен, поэтому переживает подмену
    /// кадра: он не гаснет перед загрузкой новой сцены и не появляется заново — одна и та
    /// же накладка ведёт переход от начала и до проявления.
    ///
    /// Объект создаётся в сцене <c>Boot</c> и переносится в <c>DontDestroyOnLoad</c> в
    /// <see cref="Awake"/>. Если игру запустили прямо с другой сцены, контроллер поднимается
    /// сам (см. <see cref="EnsureExists"/>) — уже без проявления из белого.
    ///
    /// Все тайминги, текст и краски перехода лежат в инспекторе этого компонента: отдельного
    /// ассета нет, поэтому переход правится в одном месте и не зависит от ссылок на ассеты.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneController : MonoBehaviour
    {
        [Header("Фазы перехода, с")]
        [Tooltip("Сколько новая сцена проявляется из белого в начале")]
        [SerializeField, Min(0f)] private float _fadeInSeconds = 0.9f;

        [Tooltip("Сколько текущая сцена уходит в белый перед загрузкой")]
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.8f;

        [Tooltip("За сколько до полного белого строка начинает проявляться. 0 — строка стартует на полном белом, 0.8 — вместе с осветлением")]
        [SerializeField, Min(0f)] private float _statusLeadInSeconds = 0.35f;

        [Tooltip("Сколько строка статуса проявляется")]
        [SerializeField, Min(0f)] private float _statusFadeInSeconds = 0.3f;

        [Tooltip("Сколько строка статуса висит на белом")]
        [SerializeField, Min(0f)] private float _statusHoldSeconds = 0.9f;

        [Tooltip("Сколько строка статуса гаснет")]
        [SerializeField, Min(0f)] private float _statusFadeOutSeconds = 0.25f;

        [Tooltip("Пауза на белом после строки, перед подменой сцены")]
        [SerializeField, Min(0f)] private float _whiteHoldSeconds = 0.2f;

        [Header("Строка на белом")]
        [Tooltip("Текст поверх белого; пусто — без текста")]
        [SerializeField] private string _statusText = "COLONY LINK ESTABLISHED";

        [Tooltip("Кегль строки")]
        [SerializeField, Min(1f)] private float _statusFontSize = 34f;

        [Tooltip("Разрядка букв строки")]
        [SerializeField] private float _statusSpacing = 18f;

        [Tooltip("Цвет строки: на белом читается только тёмный")]
        [SerializeField] private Color _statusColor = new(0.06f, 0.09f, 0.13f, 1f);

        [Header("Занавес")]
        [Tooltip("Цвет занавеса: белый — по замыслу, но правится без кода")]
        [SerializeField] private Color _curtainColor = Color.white;

        [Tooltip("Порядок отрисовки накладки: выше любого игрового канваса")]
        [SerializeField] private int _sortingOrder = 1000;

        [Tooltip("Опорное разрешение накладки, на котором раскладка не плывёт")]
        [SerializeField] private Vector2 _referenceResolution = new(1920f, 1080f);

        [Header("Старт")]
        [Tooltip("Сцена начинается с белого и проявляется из него")]
        [SerializeField] private bool _fadeInOnStart = true;

        [Tooltip("Писать в консоль фактическую длительность фаз — удобно сравнивать редактор и WebGL")]
        [SerializeField] private bool _logPhases = true;

        private static SceneController _instance;

        /// <summary>Персистентный контроллер; null только до его создания.</summary>
        public static SceneController Instance => _instance;

        /// <summary>Идёт переход: повторный запрос игнорируется.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// Идёт стартовое проявление из белого: под занавесом сцену не видно, поэтому
        /// счётчики самой сцены (например, отсчёт <c>BootScreen</c>) ждут его конца.
        /// </summary>
        public bool IsIntroPlaying { get; private set; }

        private Canvas _canvas;
        private CanvasGroup _group;
        private CanvasGroup _statusGroup;

        /// <summary>
        /// Domain reload выключен, поэтому статика живёт между запусками игры и сбрасывается
        /// здесь, а не новой сборкой домена.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>
        /// Запасной путь для запуска не с <c>Boot</c>: если в стартовой сцене контроллера нет,
        /// он создаётся сам. Накладка сразу гасится: проявление из белого принадлежит только
        /// сцене загрузки.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (_instance != null)
                return;

            var go = new GameObject("SceneController");
            var controller = go.AddComponent<SceneController>();
            controller._fadeInOnStart = false;
            controller.SetAlpha(0f);
            controller._canvas.enabled = false;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Второй контроллер (например, сцена загружена напрямую поверх персистентного)
                // не нужен: занавес уже есть.
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            BuildOverlay();
        }

        private void Start()
        {
            // Занавес уже закрыт из Awake, поэтому кадр старта не мелькает сценой.
            if (!_fadeInOnStart || _canvas == null)
                return;

            StartCoroutine(IntroRoutine());
        }

        /// <summary>
        /// Стартовое проявление помечается флагом: сцена под занавесом ещё не видна,
        /// и всё, что считает время внутри сцены, должно ждать конца проявления.
        /// </summary>
        private IEnumerator IntroRoutine()
        {
            IsIntroPlaying = true;
            yield return FadeRoutine(1f, 0f, _fadeInSeconds);
            IsIntroPlaying = false;
        }

        /// <summary>
        /// Уводит текущую сцену в белый, подменяет её и проявляет новую. Сцена должна быть
        /// в Build Settings, иначе в консоль уйдёт ошибка, а белый останется на экране.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (IsBusy)
                return;
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("SceneController: пустое имя сцены", this);
                return;
            }

            StartCoroutine(LoadRoutine(sceneName));
        }

        /// <summary>
        /// Фазы идут по порядку, но отсчёт ведётся по реальному времени, а готовность сцены
        /// не сдвигает видимые фазы: строка статуса стартует вместе с уходом в белый и не
        /// ждёт загрузки. Поэтому одинаковый переход выглядит одинаково и в редакторе, и в
        /// собранном WebGL, а разница остаётся только в том, сколько белый держится в
        /// ожидании загрузки сцены.
        /// </summary>
        private IEnumerator LoadRoutine(string sceneName)
        {
            IsBusy = true;

            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError("SceneController: сцена '" + sceneName + "' не добавлена в Build Settings", this);
                IsBusy = false;
                yield break;
            }

            operation.allowSceneActivation = false;

            var startedAt = Time.realtimeSinceStartup;

            // Строка проявляется на хвосте осветления и идёт параллельно занавесу:
            // она не ждёт, пока сцена догрузится.
            var statusRoutine = StartCoroutine(StatusRoutine());

            yield return FadeRoutine(_group.alpha, 1f, _fadeOutSeconds);
            var curtainAt = Time.realtimeSinceStartup;

            yield return statusRoutine;
            var statusAt = Time.realtimeSinceStartup;

            if (_whiteHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(_whiteHoldSeconds);

            var loadWaitStart = Time.realtimeSinceStartup;
            while (operation.progress < 0.9f)
                yield return null;

            if (_logPhases)
            {
                // Отчёт по фазам в секундах: по этой строке в консоли браузера (F12) видно,
                // что тайминги в сборке те же, что в редакторе.
                Debug.Log(string.Format(
                    "SceneController: '{0}' фазы от старта: занавес {1:0.00}s, строка закрыта {2:0.00}s, ожидание загрузки {3:0.00}s, подмена {4:0.00}s",
                    sceneName, curtainAt - startedAt, statusAt - startedAt,
                    Time.realtimeSinceStartup - loadWaitStart, Time.realtimeSinceStartup - startedAt));
            }

            // Страховка: к моменту подмены занавес обязан быть непрозрачным.
            SetAlpha(1f);
            _canvas.enabled = true;
            operation.allowSceneActivation = true;
            while (!operation.isDone)
                yield return null;

            // Один кадр новой сцены уже под белым, и только после него начинается проявление.
            yield return null;

            yield return FadeRoutine(1f, 0f, _fadeInSeconds);

            IsBusy = false;
        }

        /// <summary>
        /// Строка статуса на своём отсчёте: проявляется за <see cref="_statusLeadInSeconds"/>
        /// до полного белого, висит и гаснет ещё до подмены сцены.
        /// </summary>
        private IEnumerator StatusRoutine()
        {
            if (string.IsNullOrEmpty(_statusText))
                yield break;

            var delay = Mathf.Max(0f, _fadeOutSeconds - _statusLeadInSeconds);
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            yield return FadeStatusRoutine(0f, 1f, _statusFadeInSeconds);

            if (_statusHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(_statusHoldSeconds);

            yield return FadeStatusRoutine(1f, 0f, _statusFadeOutSeconds);
        }

        /// <summary>
        /// Прогресс считается от метки реального времени, а не суммой дельт кадра:
        /// <c>Time.unscaledDeltaTime</c> обрезается <c>Time.maximumDeltaTime</c>, и на редких
        /// или тяжёлых кадрах (редактор, первый заход в сцену) фаза растягивалась бы по
        /// секундомеру. С меткой длительность всегда равна заданной.
        /// </summary>
        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            _canvas.enabled = true;

            var startedAt = Time.realtimeSinceStartup;
            var time = duration <= 0f ? 1f : 0f;
            while (time < 1f)
            {
                time = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
                SetAlpha(Mathf.Lerp(from, to, Smooth(time)));
                yield return null;
            }

            SetAlpha(to);
            if (to <= 0.001f)
            {
                _canvas.enabled = false;
                _statusGroup.alpha = 0f;
            }
        }

        private IEnumerator FadeStatusRoutine(float from, float to, float duration)
        {
            if (string.IsNullOrEmpty(_statusText))
                yield break;

            _canvas.enabled = true;

            var startedAt = Time.realtimeSinceStartup;
            var time = duration <= 0f ? 1f : 0f;
            while (time < 1f)
            {
                time = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
                _statusGroup.alpha = Mathf.Lerp(from, to, Smooth(time));
                yield return null;
            }

            _statusGroup.alpha = to;
        }

        /// <summary>
        /// Сглаживание фаз: медленный вход и выход, поэтому край кадра не «щёлкает»
        /// на старте и на финише перехода.
        /// </summary>
        private static float Smooth(float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return 1f;
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void SetAlpha(float alpha)
        {
            _group.alpha = Mathf.Clamp01(alpha);
        }

        private void BuildOverlay()
        {
            var root = new GameObject("SceneControllerOverlay", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _canvas = canvas;

            var fill = new GameObject("White", typeof(RectTransform), typeof(Image));
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.SetParent(root.transform, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var image = fill.GetComponent<Image>();
            image.color = _curtainColor;
            image.raycastTarget = false;

            var label = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(fill.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            // Группа — только на строке. CanvasGroup гасит и Graphic своего объекта,
            // поэтому группа на белом выключала бы вместе со строкой сам занавес:
            // белый пропадал бы до подмены сцены.
            _statusGroup = label.AddComponent<CanvasGroup>();
            _statusGroup.alpha = 0f;
            _statusGroup.blocksRaycasts = false;
            _statusGroup.interactable = false;

            var text = label.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = _statusText;
            text.fontSize = _statusFontSize;
            text.color = _statusColor;
            text.characterSpacing = _statusSpacing;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            SetAlpha(_fadeInOnStart ? 1f : 0f);
            _canvas.enabled = _fadeInOnStart;
        }
    }
}
