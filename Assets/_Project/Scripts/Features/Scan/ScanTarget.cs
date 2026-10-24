using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Scan
{
    /// <summary>
    /// Имя и категория объекта для подписи сканера. Компонент необязателен: без него
    /// сканер берёт имя GameObject и чистит его от суффиксов вроде <c>_Model</c>.
    ///
    /// Подписи — латиница: атлас <c>LiberationSans SDF</c> не содержит кириллицы,
    /// а имена ассетов в проекте английские.
    ///
    /// Компонент же служит маркером «этот объект сканируется»: <see cref="ObjectScanner"/>
    /// больше не читает произвольную геометрию под лучом, а берёт цели из реестра
    /// <see cref="All"/>. Поэтому рельеф, снег и стены подписей не получают — маркера
    /// у них нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanTarget : MonoBehaviour
    {
        private static readonly List<ScanTarget> Registry = new(64);

        [Tooltip("Имя в подписи; пусто — берётся имя GameObject")]
        [SerializeField] private string _displayName;

        [Tooltip("Строка категории над именем, например STRUCTURE или NPC")]
        [SerializeField] private string _category = "OBJECT";

        private Renderer[] _renderers;
        private bool _hasBounds;
        private Bounds _bounds;

        /// <summary>Все активные цели в сцене.</summary>
        public static IReadOnlyList<ScanTarget> All => Registry;

        /// <summary>Имя для подписи.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName)
            ? Sanitize(name)
            : _displayName;

        /// <summary>Категория для подписи.</summary>
        public string Category => string.IsNullOrWhiteSpace(_category) ? "OBJECT" : _category;

        /// <summary>
        /// Мировые границы цели по всем рендерерам — считаются каждый вызов.
        ///
        /// Кэша нет намеренно: у движущихся NPC и животных границы меняются кадр от кадра,
        /// и подпись с рамкой прыгали раз в четверть секунды вместо того, чтобы ехать за
        /// объектом. Список рендереров при этом кэшируется один раз, поэтому измерения —
        /// это чтение <c>Renderer.bounds</c> без обхода иерархии и без аллокаций.
        /// </summary>
        public Bounds WorldBounds => MeasureBounds();

        /// <summary>Точка подписи: центр мировых границ.</summary>
        public Vector3 Anchor => MeasureBounds().center;

        /// <summary>Границы цели в мировых координатах, без кэша по времени.</summary>
        public Bounds MeasureBounds()
        {
            if (_renderers == null)
                _renderers = GetComponentsInChildren<Renderer>(true);

            var found = false;
            var bounds = new Bounds(transform.position, Vector3.one);

            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                // Собственные дубли подсветки в границы не входят: они помечены DontSave
                // и висят детьми тех же рендереров, поэтому без этой проверки границы
                // пересчитывались бы вместе с обводкой.
                if ((renderer.gameObject.hideFlags & HideFlags.DontSave) != 0)
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (found)
                _bounds = bounds;

            _hasBounds = _hasBounds || found;
            return _hasBounds ? _bounds : bounds;
        }

        /// <summary>
        /// Domain reload выключен: без сброса реестр пережил бы выход из Play Mode и
        /// наполнился бы ссылками на уничтоженные объекты.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
        }

        private void OnEnable()
        {
            if (!Registry.Contains(this))
                Registry.Add(this);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>Ближайшая метка вверх по иерархии или <c>null</c>.</summary>
        public static ScanTarget Find(Transform from)
        {
            return from == null ? null : from.GetComponentInParent<ScanTarget>();
        }

        /// <summary>
        /// Имя объекта в вид для сканера: без служебных суффиксов, подчёркивания
        /// заменены пробелами, всё в верхнем регистре — так подпись читается как
        /// показание прибора, а не как имя файла.
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "UNKNOWN";

            var text = raw.Replace("_Model", string.Empty)
                .Replace("(Clone)", string.Empty)
                .Replace('_', ' ')
                .Trim();

            return text.Length == 0 ? "UNKNOWN" : text.ToUpperInvariant();
        }
    }
}
