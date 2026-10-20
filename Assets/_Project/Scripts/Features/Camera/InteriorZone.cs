using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Объём интерьера: пока игрок внутри, камера уходит от первого лица.
    ///
    /// Проверка идёт по границам коллайдера, а не по событиям триггера: у игрока
    /// CharacterController, и OnTriggerEnter теряется при телепорте и не приходит
    /// после загрузки сцены. Зоны сами себя регистрируют в статическом списке,
    /// поэтому новое здание добавляется компонентом на корень дома — без правок
    /// рига камеры.
    ///
    /// Коллайдер обязан быть триггером: он не должен ни держать игрока, ни попадать
    /// в пробные лучи просвета (те игнорируют триггеры).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public sealed class InteriorZone : MonoBehaviour
    {
        private static readonly List<InteriorZone> Zones = new();

        [Tooltip("Запас внутрь от границ коллайдера, м: у самой стены режим не мигает")]
        [SerializeField] private float _shrink = 0.15f;

        private BoxCollider _box;

        /// <summary>Есть ли под точкой хоть один интерьер.</summary>
        /// <param name="padding">
        /// Расширение границ наружу, м. Нужно для гистерезиса: пока игрок в первом лице,
        /// зона считается чуть больше, поэтому режим не мигает ровно на плоскости стены.
        /// </param>
        public static bool ContainsAny(Vector3 position, float padding = 0f)
        {
            for (var i = 0; i < Zones.Count; i++)
            {
                var zone = Zones[i];
                if (zone != null && zone.Contains(position, padding))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Попадает ли точка в объём с запасом внутрь (положительный shrink) или наружу (отрицательный).
        /// Вынесено в статику ради EditMode-теста: сам компонент проверяется только в сцене.
        /// </summary>
        public static bool ContainsPoint(Bounds bounds, float shrink, Vector3 position)
        {
            if (Mathf.Abs(shrink) > 1e-4f)
                bounds.Expand(-2f * shrink);

            return bounds.Contains(position);
        }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            if (_box != null)
                _box.isTrigger = true;
        }

        private void OnEnable()
        {
            if (!Zones.Contains(this))
                Zones.Add(this);
        }

        private void OnDisable()
        {
            Zones.Remove(this);
        }

        private bool Contains(Vector3 position, float padding)
        {
            if (_box == null)
                return false;

            return ContainsPoint(_box.bounds, _shrink - padding, position);
        }
    }
}
