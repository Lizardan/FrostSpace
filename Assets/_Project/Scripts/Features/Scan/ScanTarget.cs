using UnityEngine;

namespace FrostSpace.Features.Scan
{
    /// <summary>
    /// Имя и категория объекта для подписи сканера. Компонент необязателен: без него
    /// сканер берёт имя GameObject и чистит его от суффиксов вроде <c>_Model</c>.
    ///
    /// Подписи — латиница: атлас <c>LiberationSans SDF</c> не содержит кириллицы,
    /// а имена ассетов в проекте английские.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanTarget : MonoBehaviour
    {
        [Tooltip("Имя в подписи; пусто — берётся имя GameObject")]
        [SerializeField] private string _displayName;

        [Tooltip("Строка категории над именем, например STRUCTURE или NPC")]
        [SerializeField] private string _category = "OBJECT";

        /// <summary>Имя для подписи.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName)
            ? Sanitize(name)
            : _displayName;

        /// <summary>Категория для подписи.</summary>
        public string Category => string.IsNullOrWhiteSpace(_category) ? "OBJECT" : _category;

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
