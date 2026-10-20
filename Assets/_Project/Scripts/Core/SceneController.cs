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
    /// Тайминги и краски вынесены в общий <see cref="SceneFlowSettings"/>: один ассет
    /// ссылается из сцены <c>Boot</c>, поэтому переход правится в одном месте.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneController : MonoBehaviour
    {
        [Header("Настройки")]
        [Tooltip("Общий ассет перехода: фазы, цвета и строка статуса. Пусто — значения по умолчанию.")]
        [SerializeField] private SceneFlowSettings _settings;

        [Tooltip("Сцена начинается с белого и проявляется из него")]
        [SerializeField] private bool _fadeInOnStart = true;

        private static SceneController _instance;

        /// <summary>Персистентный контроллер; null только до его создания.</summary>
        public static SceneController Instance => _instance;

        /// <summary>Идёт переход: повторный запрос игнорируется.</summary>
        public bool IsBusy { get; private set; }

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

            if (_settings == null)
                _settings = ScriptableObject.CreateInstance<SceneFlowSettings>();

            BuildOverlay();
        }

        private void Start()
        {
            // Занавес уже закрыт из Awake, поэтому кадр старта не мелькает сценой.
            if (!_fadeInOnStart || _canvas == null)
                return;

            StartCoroutine(FadeRoutine(1f, 0f, _settings.FadeInSeconds));
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
        /// Фазы идут строго друг за другом, без пересечений: сначала белый, потом строка,
        /// выдержка, угасание строки и только затем подмена сцены. Занавес при этом один и
        /// тот же объект, поэтому он остаётся на экране и до, и после подмены: новая сцена
        /// не может показаться раньше, чем начнётся её проявление.
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

            yield return FadeRoutine(_group.alpha, 1f, _settings.FadeOutSeconds);

            if (_settings.WhiteHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(_settings.WhiteHoldSeconds);

            while (operation.progress < 0.9f)
                yield return null;

            if (!string.IsNullOrEmpty(_settings.StatusText))
            {
                yield return FadeStatusRoutine(0f, 1f, _settings.StatusFadeInSeconds);

                if (_settings.StatusHoldSeconds > 0f)
                    yield return new WaitForSecondsRealtime(_settings.StatusHoldSeconds);

                yield return FadeStatusRoutine(1f, 0f, _settings.StatusFadeOutSeconds);
            }

            Debug.Log("SceneController: '" + sceneName + "' curtain=" + _group.alpha.ToString("0.00")
                + " text=" + _statusGroup.alpha.ToString("0.00"));
            // Страховка: к моменту подмены занавес обязан быть непрозрачным.
            SetAlpha(1f);
            _canvas.enabled = true;
            operation.allowSceneActivation = true;
            while (!operation.isDone)
                yield return null;

            // Один кадр новой сцены уже под белым, и только после него начинается проявление.
            yield return null;

            yield return FadeRoutine(1f, 0f, _settings.FadeInSeconds);

            IsBusy = false;
        }

        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            _canvas.enabled = true;
            var time = 0f;
            if (duration <= 0f)
                time = 1f;

            while (time < 1f)
            {
                time = Mathf.Clamp01(time + Time.unscaledDeltaTime / Mathf.Max(duration, 0.0001f));
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
            if (string.IsNullOrEmpty(_settings.StatusText))
                yield break;

            _canvas.enabled = true;
            var time = 0f;
            if (duration <= 0f)
                time = 1f;

            while (time < 1f)
            {
                time = Mathf.Clamp01(time + Time.unscaledDeltaTime / Mathf.Max(duration, 0.0001f));
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
            canvas.sortingOrder = 1000;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
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
            image.color = _settings.Color;
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
            text.text = _settings.StatusText;
            text.fontSize = _settings.StatusFontSize;
            text.color = _settings.StatusColor;
            text.characterSpacing = _settings.StatusSpacing;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            SetAlpha(_fadeInOnStart ? 1f : 0f);
            _canvas.enabled = _fadeInOnStart;
        }
    }
}
