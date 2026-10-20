using UnityEngine;

namespace FrostSpace.Core
{
    /// <summary>
    /// Единственное место, где живут тайминги и краски перехода между сценами.
    ///
    /// Сам <see cref="SceneController"/> — единственный владелец перехода и не хранит общих
    /// значений: ассет ссылается из сцены <c>Boot</c>, поэтому переход правится в одном
    /// инспекторе, а не в трёх сценах.
    ///
    /// Фазы идут строго по порядку: уход в белый → белый держится → строка статуса
    /// появляется и висит → строка гаснет → подмена сцены → проявление новой сцены из белого.
    /// Ни одна фаза не пересекается с другой, поэтому после подмены на экране нет остатков
    /// прошлой сцены.
    /// </summary>
    [CreateAssetMenu(menuName = "FrostSpace/Scene Flow Settings", fileName = "SceneFlowSettings")]
    public sealed class SceneFlowSettings : ScriptableObject
    {
        [Header("Фазы, с")]
        [Tooltip("Сколько новая сцена проявляется из белого в начале")]
        [SerializeField, Min(0f)] private float _fadeInSeconds = 1.1f;

        [Tooltip("Сколько текущая сцена уходит в белый перед загрузкой")]
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.9f;

        [Tooltip("Сколько белый держится до появления строки статуса")]
        [SerializeField, Min(0f)] private float _whiteHoldSeconds = 0.35f;

        [Tooltip("Сколько строка статуса проявляется")]
        [SerializeField, Min(0f)] private float _statusFadeInSeconds = 0.4f;

        [Tooltip("Сколько строка статуса висит на белом")]
        [SerializeField, Min(0f)] private float _statusHoldSeconds = 1.1f;

        [Tooltip("Сколько строка статуса гаснет перед подменой сцены")]
        [SerializeField, Min(0f)] private float _statusFadeOutSeconds = 0.35f;

        [Header("Строка на белом")]
        [Tooltip("Текст поверх белого; пусто — без текста")]
        [SerializeField] private string _statusText = "COLONY LINK ESTABLISHED";

        [Tooltip("Кегль строки")]
        [SerializeField, Min(1f)] private float _statusFontSize = 34f;

        [Tooltip("Разрядка букв строки")]
        [SerializeField] private float _statusSpacing = 18f;

        [Header("Краски")]
        [Tooltip("Цвет занавеса: белый — по замыслу, но правится без кода")]
        [SerializeField] private Color _color = Color.white;

        [Tooltip("Цвет строки: на белом читается только тёмный")]
        [SerializeField] private Color _statusColor = new(0.06f, 0.09f, 0.13f, 1f);

        /// <summary>Проявление новой сцены из белого, с.</summary>
        public float FadeInSeconds => _fadeInSeconds;

        /// <summary>Уход текущей сцены в белый, с.</summary>
        public float FadeOutSeconds => _fadeOutSeconds;

        /// <summary>Пауза на белом до строки статуса, с.</summary>
        public float WhiteHoldSeconds => _whiteHoldSeconds;

        /// <summary>Проявление строки статуса, с.</summary>
        public float StatusFadeInSeconds => _statusFadeInSeconds;

        /// <summary>Выдержка строки статуса, с.</summary>
        public float StatusHoldSeconds => _statusHoldSeconds;

        /// <summary>Угасание строки статуса перед подменой сцены, с.</summary>
        public float StatusFadeOutSeconds => _statusFadeOutSeconds;

        /// <summary>Текст на белом.</summary>
        public string StatusText => _statusText;

        /// <summary>Кегль строки на белом.</summary>
        public float StatusFontSize => _statusFontSize;

        /// <summary>Разрядка строки на белом.</summary>
        public float StatusSpacing => _statusSpacing;

        /// <summary>Цвет занавеса.</summary>
        public Color Color => _color;

        /// <summary>Цвет строки на занавесе.</summary>
        public Color StatusColor => _statusColor;
    }
}
