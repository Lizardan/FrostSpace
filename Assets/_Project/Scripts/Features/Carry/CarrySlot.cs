using System;
using UnityEngine;

namespace FrostSpace.Features.Carry
{
    /// <summary>
    /// Гнездо под предмет: принимает только свой тип и только если свободно.
    ///
    /// Событие <see cref="Placed"/> — единственный сигнал квесту: директор
    /// подписывается на конкретные слоты сцены, глобальной шины нет (ADR 0027).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarrySlot : MonoBehaviour
    {
        [Tooltip("Тип предмета, который принимает слот")]
        [SerializeField] private string _acceptedType = "battery";

        [Tooltip("Точка, куда встаёт предмет; пусто — сам объект слота")]
        [SerializeField] private Transform _anchor;

        /// <summary>Слот занял предмет: аргументы — слот и предмет.</summary>
        public event Action<CarrySlot, Carryable> Placed;

        /// <summary>Тип, который принимает слот.</summary>
        public string AcceptedType => _acceptedType;

        /// <summary>Предмет в слоте; null — свободно.</summary>
        public Carryable Occupant { get; private set; }

        /// <summary>Слот свободен.</summary>
        public bool IsFree => Occupant == null;

        private void Awake()
        {
            if (_anchor == null)
            {
                _anchor = transform;
            }
        }

        /// <summary>Поставить предмет, если он подходит и слот свободен.</summary>
        public bool TryPlace(Carryable item)
        {
            if (item == null || !IsFree || !item.IsCarried)
            {
                return false;
            }

            if (!string.Equals(item.ItemType, _acceptedType, StringComparison.Ordinal))
            {
                return false;
            }

            Occupant = item;
            item.Place(_anchor);
            Placed?.Invoke(this, item);
            return true;
        }
    }
}
