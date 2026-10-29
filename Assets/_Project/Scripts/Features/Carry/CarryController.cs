using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Locomotion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Carry
{
    /// <summary>
    /// Подбор, перенос и установка груза по E.
    ///
    /// Один предмет в руках: аккумулятор тяжёлый, второй не унести. Пока руки
    /// заняты, <see cref="RobotLocomotion.SetCarrying"/> выключает спринт и прыжок
    /// и включает клип переноски. Установка ищет ближайший свободный слот в
    /// радиусе; если слотов рядом нет — предмет падает на исходное место.
    ///
    /// E — то же действие <c>Player/Interact</c>, что у диалога и сканера.
    /// Пока диалог открыт, переноска не реагирует: иначе одно нажатие и
    /// заговорило бы, и подняло груз.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarryController : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Точка в руках: сюда парентится предмет")]
        [SerializeField] private Transform _handAnchor;

        [Tooltip("Локомоция игрока: переключает клип и скорость переноски")]
        [SerializeField] private RobotLocomotion _locomotion;

        [Tooltip("Диалог игрока: пока он открыт, E не трогает груз")]
        [SerializeField] private DialogueController _dialogue;

        [Header("Руки")]
        [Tooltip("Кость левой кисти: груз висит на середине между кистями")]
        [SerializeField] private Transform _handLeft;

        [Tooltip("Кость правой кисти")]
        [SerializeField] private Transform _handRight;

        [Tooltip("Ассет ввода; если пусто — InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Дистанции")]
        [Tooltip("Радиус, с которого предмет берётся в руки, м")]
        [SerializeField] private float _pickupRadius = 2.4f;

        [Tooltip("Радиус, с которого предмет ставится в слот, м")]
        [SerializeField] private float _slotRadius = 2.6f;

        [Tooltip("Слои предметов; слой Player исключить")]
        [SerializeField] private LayerMask _mask = ~0;

        private InputAction _interact;
        private Carryable _held;
        private float _gripOffset = 0.475f;
        private bool _handsSearched;

        /// <summary>В руках есть предмет.</summary>
        public bool IsCarrying => _held != null;

        private void Awake()
        {
            if (_locomotion == null)
            {
                _locomotion = GetComponent<RobotLocomotion>();
            }

            if (_dialogue == null)
            {
                _dialogue = GetComponent<DialogueController>();
            }

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _interact = source.FindAction("Player/Interact");
            }
        }

        private void OnEnable()
        {
            _interact?.Enable();
        }

        private void OnDisable()
        {
            _interact?.Disable();
        }

        private void LateUpdate()
        {
            if (_held == null || _handAnchor == null)
            {
                return;
            }

            EnsureHands();
            if (_handLeft == null || _handRight == null)
            {
                return;
            }

            var left = _handLeft.position;
            var right = _handRight.position;
            var axis = right - left;
            if (axis.sqrMagnitude < 0.0004f)
            {
                return;
            }

            var up = transform.up;
            var forward = Vector3.Cross(axis.normalized, up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = transform.forward;
            }

            var middle = (left + right) * 0.5f;
            _handAnchor.position = middle - up * _gripOffset;
            _handAnchor.rotation = Quaternion.LookRotation(forward.normalized, up);
        }

        /// <summary>
        /// Кости кистей ищутся один раз: визуал игрока подменяется после Awake,
        /// поэтому поиск идёт лениво, при первом подборе.
        /// </summary>
        private void EnsureHands()
        {
            if (_handLeft != null && _handRight != null)
            {
                return;
            }

            if (_handsSearched)
            {
                return;
            }

            _handsSearched = true;
            var all = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var bone = all[i];
                if (bone.name == "hand_l")
                {
                    _handLeft = bone;
                }
                else if (bone.name == "hand_r")
                {
                    _handRight = bone;
                }
            }
        }

        private void Update()
        {
            if (_interact == null || !_interact.WasPressedThisFrame())
            {
                return;
            }

            if (_dialogue != null && _dialogue.IsOpen)
            {
                return;
            }

            if (_held != null)
            {
                TryPlaceOrDrop();
                return;
            }

            var item = FindNearestItem();
            if (item != null)
            {
                var renderer = item.GetComponentInChildren<Renderer>();
                _gripOffset = renderer != null ? renderer.bounds.size.y * 0.62f : 0.58f;
                EnsureHands();
                item.Take(_handAnchor);
                _held = item;
                _locomotion?.SetCarrying(true);
            }
        }

        private void TryPlaceOrDrop()
        {
            var slot = FindNearestSlot();
            if (slot != null && slot.TryPlace(_held))
            {
                Release();
                return;
            }

            _held.Drop();
            Release();
        }

        private void Release()
        {
            _held = null;
            _locomotion?.SetCarrying(false);
        }

        private Carryable FindNearestItem()
        {
            var hits = Physics.OverlapSphere(transform.position, _pickupRadius, _mask,
                                             QueryTriggerInteraction.Collide);
            Carryable best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < hits.Length; i++)
            {
                var item = hits[i].GetComponentInParent<Carryable>();
                if (item == null || item.IsCarried || item.IsPlaced)
                {
                    continue;
                }

                var distance = (item.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }

            return best;
        }

        private CarrySlot FindNearestSlot()
        {
            var slots = FindObjectsByType<CarrySlot>(FindObjectsSortMode.None);
            CarrySlot best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.IsFree)
                {
                    continue;
                }

                var distance = (slot.transform.position - transform.position).sqrMagnitude;
                if (distance > _slotRadius * _slotRadius)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = slot;
                }
            }

            return best;
        }
    }
}
