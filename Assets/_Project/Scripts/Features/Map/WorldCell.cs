using UnityEngine;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Клетка мира: один участок террейна 600×600 м со своим индексом сетки.
    ///
    /// Индекс живёт здесь, а не в имени объекта, по той же причине, по которой
    /// весь проект ссылается полями: имя можно переименовать руками, и тогда
    /// резидентность молча перестанет узнавать клетку и оставит её выключенной
    /// навсегда. Здесь значение видно в инспекторе и его проверяет тест.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldCell : MonoBehaviour
    {
        [Tooltip("Индекс клетки сетки раскладки (x, z) — тот же, что в имени FBX")]
        [SerializeField] private Vector2Int _cell;

        /// <summary>Индекс клетки в осях раскладки (мир, не сцена).</summary>
        public Vector2Int Cell => _cell;

        /// <summary>Записать индекс — для расстановки и для теста.</summary>
        public void Setup(Vector2Int cell)
        {
            _cell = cell;
        }
    }
}
