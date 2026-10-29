using UnityEngine;

namespace FrostSpace.Features.Carry
{
    /// <summary>
    /// Предмет, который можно взять в руки и поставить в слот.
    ///
    /// Дом предмета запоминается в <see cref="Awake"/>: брошенный предмет
    /// возвращается туда, чтобы квест нельзя было сломать, утащив аккумулятор
    /// в угол. Коллайдеры выключаются, пока предмет в руках или в слоте —
    /// иначе игрок толкает груз собственным капсульным коллайдером.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Carryable : MonoBehaviour
    {
        [Tooltip("Тип предмета: слот принимает только совпадающий тип")]
        [SerializeField] private string _itemType = "battery";

        [Tooltip("Квест, без которого предмет нельзя взять; пусто — можно всегда")]
        [SerializeField] private string _requiredQuestId = string.Empty;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Collider[] _colliders;

        /// <summary>Тип предмета для сверки со слотом.</summary>
        public string ItemType => _itemType;

        /// <summary>Квест, который должен быть взят; пусто — предмет доступен сразу.</summary>
        public string RequiredQuestId => _requiredQuestId;

        /// <summary>Предмет сейчас в руках.</summary>
        public bool IsCarried { get; private set; }

        /// <summary>Предмет стоит в слоте.</summary>
        public bool IsPlaced { get; private set; }

        private void Awake()
        {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>Взять в руки: предмет становится ребёнком якоря.</summary>
        public void Take(Transform anchor)
        {
            if (anchor == null || IsCarried || IsPlaced)
            {
                return;
            }

            IsCarried = true;
            SetColliders(false);
            transform.SetParent(anchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Поставить в слот: предмет фиксируется на якоре слота.</summary>
        public void Place(Transform anchor)
        {
            if (anchor == null)
            {
                return;
            }

            IsCarried = false;
            IsPlaced = true;
            transform.SetParent(anchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            SetColliders(false);
        }

        /// <summary>Бросить: предмет возвращается на исходное место.</summary>
        public void Drop()
        {
            IsCarried = false;
            IsPlaced = false;
            transform.SetParent(null, true);
            transform.position = _homePosition;
            transform.rotation = _homeRotation;
            SetColliders(true);
        }

        private void SetColliders(bool enabled)
        {
            if (_colliders == null)
            {
                return;
            }

            for (var i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].enabled = enabled;
                }
            }
        }
    }
}
