using System.Collections;
using FrostSpace.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Boot
{
    /// <summary>
    /// Экран загрузки сервисов: тёмная консоль, лог подключения подсистем,
    /// сегментный прогресс и кольцо процессора. Ровно <c>_bootSeconds</c> секунд
    /// имитации инициализации, затем белый переход в <c>_nextScene</c>.
    ///
    /// Список сервисов — данные инспектора, а не код: реальные сервисы, когда
    /// появятся, встанут в тот же таймлайн и заменят строки по одной.
    /// Геометрия интерфейса собирается кодом, чтобы сцена не тащила Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootScreen : MonoBehaviour
    {
        [Header("Таймлайн")]
        [Tooltip("Сколько идёт имитация загрузки сервисов, с")]
        [SerializeField] private float _bootSeconds = 3f;

        [Tooltip("Сколько держится надпись READY перед переходом, с")]
        [SerializeField] private float _readyHoldSeconds = 0.3f;

        [Tooltip("Строки лога по порядку: каждая закрывается по мере прогресса")]
        [SerializeField] private string[] _steps =
        {
            "THERMAL CORE",
            "PNEUMACORE BUS",
            "ROBOT REGISTRY  20 UNITS",
            "SENSOR ARRAY",
            "SNOW SIMULATION",
            "COLONY UPLINK",
        };

        [Tooltip("Сцена, которая грузится после загрузки сервисов")]
        [SerializeField] private string _nextScene = "MainMenu";

        [Header("Фон")]
        [Tooltip("Верх неба: холодная синева полярной ночи")]
        [SerializeField] private Color _skyTop = new(0.070f, 0.118f, 0.180f, 1f);

        [Tooltip("Низ неба: почти чёрный лёд")]
        [SerializeField] private Color _skyBottom = new(0.008f, 0.014f, 0.024f, 1f);

        [Tooltip("Полярное свечение за кольцом процессора")]
        [SerializeField] private Color _aurora = new(0.302f, 0.902f, 1f, 0.14f);

        [Tooltip("Техническая сетка: тонкая, почти на грани видимости")]
        [SerializeField] private Color _grid = new(0.302f, 0.902f, 1f, 0.055f);

        [Tooltip("Шаг сетки в пикселях опорного разрешения")]
        [SerializeField] private float _gridCell = 72f;

        [Tooltip("Насколько сильно темнятся края кадра")]
        [SerializeField, Range(0f, 1f)] private float _vignette = 0.6f;

        [Header("Палитра")]
        [Tooltip("Циановый акцент — единственный холодный цвет в стиле проекта")]
        [SerializeField] private Color _cyan = new(0.302f, 0.902f, 1f, 1f);

        [Tooltip("Незажжённый сегмент прогресса и кольца")]
        [SerializeField] private Color _dim = new(0.10f, 0.19f, 0.23f, 1f);

        [Tooltip("Цвет текста лога")]
        [SerializeField] private Color _textColor = new(0.72f, 0.86f, 0.92f, 1f);

        [Header("Раскладка")]
        [SerializeField] private int _segmentCount = 40;
        [SerializeField] private int _tickCount = 48;

        private const float BarWidth = 620f;
        private const float BarHeight = 16f;
        private const float SegmentGap = 3f;
        private const float RingRadius = 232f;

        private TextMeshProUGUI _log;
        private TextMeshProUGUI _percent;
        private TextMeshProUGUI _status;
        private Image[] _segments;
        private Image[] _ticks;
        private RectTransform _ring;
        private float _time;
        private int _closedSteps;
        private bool _released;

        private void Awake()
        {
            BuildInterface();
            RefreshLog(0);
            RefreshProgress(0f);
        }

        private void Update()
        {
            if (_released)
                return;

            _time += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(_time / Mathf.Max(_bootSeconds, 0.01f));
            RefreshProgress(progress);

            var closed = Mathf.FloorToInt(progress * _steps.Length);
            if (closed != _closedSteps)
                RefreshLog(closed);

            if (_ring != null)
                _ring.localRotation *= Quaternion.Euler(0f, 0f, -14f * Time.unscaledDeltaTime);

            if (progress < 1f)
                return;

            _status.text = "READY";
            _status.color = _cyan;
            StartCoroutine(ReleaseRoutine());
        }

        private IEnumerator ReleaseRoutine()
        {
            _released = true;
            yield return new WaitForSecondsRealtime(_readyHoldSeconds);

            // Контроллер персистентный: он создан в этой же сцене и уже перенесён
            // в DontDestroyOnLoad, поэтому переход переживёт подмену сцены.
            var controller = SceneController.Instance;
            if (controller != null)
                controller.LoadScene(_nextScene);
            else
                Debug.LogError("BootScreen: нет SceneController — переход невозможен", this);
        }

        private void RefreshLog(int closedSteps)
        {
            _closedSteps = closedSteps;
            var builder = new System.Text.StringBuilder(256);
            for (var i = 0; i < _steps.Length; i++)
            {
                var done = i < closedSteps;
                builder.Append(done ? "> " : "  ");
                builder.Append(_steps[i]);
                builder.Append(' ');
                builder.Append(done ? "OK" : "..");
                if (i < _steps.Length - 1)
                    builder.Append('\n');
            }

            _log.text = builder.ToString();
        }

        private void RefreshProgress(float progress)
        {
            _percent.text = Mathf.FloorToInt(progress * 100f).ToString("000");
            _status.text = progress < 1f ? "MOUNTING SERVICES" : "READY";

            var lit = Mathf.CeilToInt(progress * _segments.Length);
            for (var i = 0; i < _segments.Length; i++)
                _segments[i].color = i < lit ? _cyan : _dim;

            var litTicks = Mathf.CeilToInt(progress * _ticks.Length);
            for (var i = 0; i < _ticks.Length; i++)
                _ticks[i].color = i < litTicks ? _cyan : _dim;
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("BootCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.transform;

            // Фон собран из четырёх слоёв: холодный градиент неба, техническая сетка,
            // полярное свечение за кольцом и виньетка по краям. Ни одного полосатого
            // растра: сетка читается как приборная панель, а не как развёртка ЭЛТ.
            NewRaw(root, "Sky", BuildVerticalGradient(_skyTop, _skyBottom), Color.white);
            NewRaw(root, "Grid", BuildGridTexture(), _grid)
                .uvRect = new Rect(0f, 0f, 1920f / Mathf.Max(_gridCell, 8f), 1080f / Mathf.Max(_gridCell, 8f));
            NewGlow(root);
            NewRaw(root, "Vignette", BuildRadialTexture(true), new Color(0f, 0f, 0f, _vignette));

            NewText(root, "Brand", "FROSTSPACE", 26f, _cyan, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -64f), new Vector2(700f, 40f), 16f);

            NewText(root, "Title", "SYSTEM COLD START", 46f, _textColor, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -104f), new Vector2(900f, 60f), 12f);

            _log = NewText(root, "Log", string.Empty, 24f, _textColor, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(96f, -212f), new Vector2(780f, 320f), 6f);
            _log.lineSpacing = 18f;

            NewText(root, "FooterLeft", "COLONY BOOTLOADER 0.1", 20f, _dim, TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f), new Vector2(96f, 64f), new Vector2(700f, 30f), 14f);

            NewText(root, "FooterRight", "THERMAL CYCLE 07", 20f, _dim, TextAlignmentOptions.BottomRight,
                new Vector2(1f, 0f), new Vector2(-96f, 64f), new Vector2(700f, 30f), 14f);

            var ringObject = new GameObject("Ring", typeof(RectTransform)).GetComponent<RectTransform>();
            ringObject.SetParent(root, false);
            ringObject.anchorMin = new Vector2(0.5f, 0.5f);
            ringObject.anchorMax = new Vector2(0.5f, 0.5f);
            ringObject.pivot = new Vector2(0.5f, 0.5f);
            ringObject.anchoredPosition = new Vector2(330f, 60f);
            ringObject.sizeDelta = new Vector2(RingRadius * 2f, RingRadius * 2f);
            _ring = ringObject.transform as RectTransform;
            _ticks = new Image[_tickCount];
            for (var i = 0; i < _tickCount; i++)
            {
                var tick = NewImage(_ring, "Tick" + i, _dim);
                var rect = tick.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(4f, i % 4 == 0 ? 34f : 20f);
                var angle = i * (Mathf.PI * 2f / _tickCount);
                rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * RingRadius;
                rect.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                _ticks[i] = tick;
            }

            _percent = NewText(root, "Percent", "000", 168f, _cyan, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(330f, 60f), new Vector2(520f, 220f), 2f);

            _status = NewText(root, "Status", "MOUNTING SERVICES", 24f, _dim, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(330f, -96f), new Vector2(620f, 40f), 20f);

            var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(root, false);
            bar.anchorMin = new Vector2(0.5f, 0.5f);
            bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(330f, -140f);
            bar.sizeDelta = new Vector2(BarWidth, BarHeight);

            _segments = new Image[Mathf.Max(_segmentCount, 1)];
            var segmentWidth = (BarWidth - (_segments.Length - 1) * SegmentGap) / _segments.Length;
            for (var i = 0; i < _segments.Length; i++)
            {
                var segment = NewImage(bar, "Segment" + i, _dim);
                var rect = segment.rectTransform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(segmentWidth, BarHeight);
                rect.anchoredPosition = new Vector2(i * (segmentWidth + SegmentGap), 0f);
                _segments[i] = segment;
            }
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Слой фона: RawImage во весь экран, тайлинг задаётся вызывающим.</summary>
        private static RawImage NewRaw(Transform parent, string name, Texture2D texture, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var raw = go.AddComponent<RawImage>();
            raw.texture = texture;
            raw.color = tint;
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>
        /// Полярное свечение: радиальное пятно за кольцом процессора, чуть выше центра.
        /// Держит «северное» настроение, не споря с цифровым цианом интерфейса.
        /// </summary>
        private void NewGlow(Transform parent)
        {
            var glow = NewRaw(parent, "Aurora", BuildRadialTexture(false), _aurora);
            var rect = glow.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1180f, 1180f);
            rect.anchoredPosition = new Vector2(330f, 120f);
        }

        private static TextMeshProUGUI NewText(
            Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 boxSize, float spacing)
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

        /// <summary>Вертикальный градиент неба: одна колонка пикселей, растянутая на экран.</summary>
        private static Texture2D BuildVerticalGradient(Color top, Color bottom)
        {
            const int height = 256;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                name = "BootSky",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var y = 0; y < height; y++)
            {
                var t = y / (height - 1f);
                texture.SetPixel(0, y, Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, t)));
            }

            texture.Apply();
            return texture;
        }

        /// <summary>Техническая сетка: линия по левой и нижней кромке тайла.</summary>
        private static Texture2D BuildGridTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BootGrid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onEdge = x == 0 || y == 0;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, onEdge ? 1f : 0f));
                }
            }

            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Радиальная маска: <c>outside = false</c> — свечение из центра,
        /// <c>outside = true</c> — затемнение к краям (виньетка).
        /// </summary>
        private static Texture2D BuildRadialTexture(bool outside)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = outside ? "BootVignette" : "BootGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size * 2f - 1f;
                    var v = (y + 0.5f) / size * 2f - 1f;
                    var distance = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                    var alpha = outside
                        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, distance))
                        : Mathf.Pow(1f - distance, 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
