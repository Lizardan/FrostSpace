using System;

namespace FrostSpace.Features.Inventory
{
    /// <summary>
    /// Доска экипировки: где лежит каждый предмет — в своём слоте или в ячейке сетки.
    ///
    /// Чистая модель без <see cref="UnityEngine.MonoBehaviour"/>: её проверяют
    /// EditMode-тесты, а экран перерисовывает только то, что модель разрешила.
    /// Порядок намеренный — иначе интерфейс показывал бы перетаскивание, которое
    /// состояние не приняло, и игрок видел бы вещь там, где её нет.
    ///
    /// Хранится не «что лежит в ячейке», а «где лежит предмет»: по одной записи на вид
    /// оборудования. Свободная ячейка — производное состояние, поэтому обмен не может
    /// оставить предмет в двух местах или потерять его.
    ///
    /// Слот принимает только свой вид: сонар не встаёт в слот карты. Это не защита от
    /// дурака, а часть правила «снятое оборудование отключает свою функцию»: предмет
    /// либо в своём слоте и работает, либо в сетке и молчит.
    /// </summary>
    public sealed class EquipmentBoard
    {
        /// <summary>Предмет лежит в своём слоте, то есть надет.</summary>
        public const int InSlot = -1;

        /// <summary>Предмета нет — он ещё не выдан.</summary>
        public const int Absent = -2;

        private readonly int[] _place;

        public EquipmentBoard(int cellCount)
        {
            if (cellCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellCount));

            CellCount = cellCount;
            _place = new int[2];
            _place[(int)EquipmentKind.Scanner] = Absent;
            _place[(int)EquipmentKind.Map] = Absent;
        }

        /// <summary>Сколько ячеек в сетке.</summary>
        public int CellCount { get; }

        /// <summary>Предмет выдан и может лежать в слоте или в сетке.</summary>
        public bool Has(EquipmentKind kind) => Slot(kind) != Absent;

        /// <summary>Предмет надет: лежит в своём слоте и работает.</summary>
        public bool IsEquipped(EquipmentKind kind) => Slot(kind) == InSlot;

        /// <summary>
        /// Где предмет: <see cref="InSlot"/>, <see cref="Absent"/> или индекс ячейки.
        /// </summary>
        public int CellOf(EquipmentKind kind) => Slot(kind);

        /// <summary>Предмет в ячейки или null, если ячейка свободна.</summary>
        public EquipmentKind? ItemAt(int cell)
        {
            if (!IsCellIndex(cell))
                return null;

            for (var i = 0; i < _place.Length; i++)
                if (_place[i] == cell)
                    return (EquipmentKind)i;

            return null;
        }

        /// <summary>Ячейка ничего не держит.</summary>
        public bool IsCellFree(int cell) => IsCellIndex(cell) && ItemAt(cell) == null;

        /// <summary>
        /// Выдаёт предмет и сразу надевает его: по замыслу владельца выданное
        /// оборудование появляется в своём слоте, а не ждёт в сетке.
        /// </summary>
        public bool Grant(EquipmentKind kind)
        {
            if (Has(kind))
                return false;

            _place[(int)kind] = InSlot;
            return true;
        }

        /// <summary>
        /// Надевает предмет в его слот — из ячейки или обратно после снятия.
        /// </summary>
        public bool Equip(EquipmentKind kind)
        {
            var place = Slot(kind);
            if (place == Absent || place == InSlot)
                return false;

            _place[(int)kind] = InSlot;
            return true;
        }

        /// <summary>
        /// Кладёт предмет в ячейку. Свободная ячейка — просто перенос; занятая —
        /// обмен, и только если предмет уходит из ячейки в ячейку.
        ///
        /// Обмен со слотом запрещён: предмет, которого вытесняют в слот, не может
        /// в нём лежать, а исчезнуть он не имеет права. Перетаскивание в занятую
        /// ячейку из слота модель отклоняет, и экран показывает отказ честно.
        /// </summary>
        public bool MoveToCell(EquipmentKind kind, int cell)
        {
            var place = Slot(kind);
            if (place == Absent || place == cell || !IsCellIndex(cell))
                return false;

            var other = ItemAt(cell);
            if (other.HasValue)
            {
                if (place == InSlot)
                    return false;

                _place[(int)other.Value] = place;
            }

            _place[(int)kind] = cell;
            return true;
        }

        private bool IsCellIndex(int cell) => cell >= 0 && cell < CellCount;

        private int Slot(EquipmentKind kind)
        {
            var index = (int)kind;
            if (index < 0 || index >= _place.Length)
                throw new ArgumentOutOfRangeException(nameof(kind));

            return _place[index];
        }
    }
}
