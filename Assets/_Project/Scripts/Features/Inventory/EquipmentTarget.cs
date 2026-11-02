using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FrostSpace.Features.Inventory
{
    /// <summary>Тип мишени: отдельный слот оборудования или ячейка сетки.</summary>
    public enum EquipmentTargetKind
    {
        Slot = 0,
        Cell = 1,
    }

    /// <summary>
    /// Мишень перетаскивания в окне экипировки: один слот или одна ячейка сетки.
    ///
    /// Решений не принимает: сообщает экрану, с чего начали тянуть, над чем оказались
    /// и где отпустили, а разложение решает <see cref="EquipmentBoard"/>. Это и есть
    /// защита от «перетащил — а предмет пропал»: экран перерисовывается по факту
    /// изменения состояния, а не по ожиданию.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentTarget : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerEnterHandler
    {
        private EquipmentScreen _screen;

        /// <summary>Слот это или ячейка.</summary>
        public EquipmentTargetKind Kind { get; private set; }

        /// <summary>Какому виду оборудования принадлежит слот (для ячеек — не значимо).</summary>
        public EquipmentKind SlotKind { get; private set; }

        /// <summary>Номер ячейки сетки (для слотов — не значимо).</summary>
        public int CellIndex { get; private set; }

        /// <summary>Фон мишени: по нему видно свободно, занято или это цель.</summary>
        public Image Background { get; private set; }

        /// <summary>Значок лежащего предмета.</summary>
        public Image Icon { get; private set; }

        /// <summary>Подпись под слотом.</summary>
        public TMPro.TextMeshProUGUI Label { get; private set; }

        /// <summary>Чем занят слот: своим предметом или ничем (у ячейки — по модели).</summary>
        public EquipmentKind? Item => _screen != null ? _screen.ItemAt(this) : null;

        public void Setup(
            EquipmentScreen screen, EquipmentTargetKind kind, EquipmentKind slotKind,
            int cellIndex, Image background, Image icon,
            TMPro.TextMeshProUGUI label)
        {
            _screen = screen;
            Kind = kind;
            SlotKind = slotKind;
            CellIndex = cellIndex;
            Background = background;
            Icon = icon;
            Label = label;
        }

        public void OnBeginDrag(PointerEventData eventData) => _screen?.BeginDrag(this);

        public void OnDrag(PointerEventData eventData) => _screen?.MoveDrag(eventData);

        public void OnEndDrag(PointerEventData eventData) => _screen?.EndDrag(this);

        public void OnDrop(PointerEventData eventData) => _screen?.MarkDropTarget(this);

        public void OnPointerEnter(PointerEventData eventData) => _screen?.ShowDescription(this);
    }
}
