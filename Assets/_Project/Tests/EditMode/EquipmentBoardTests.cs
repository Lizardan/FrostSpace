using System;
using FrostSpace.Features.Inventory;
using NUnit.Framework;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Правила доски экипировки: куда предмет может встать и какой ход отвергается.
    /// Экран рисует ровно то, что здесь разрешено, поэтому отказ должен быть
    /// проверен до того, как игрок увидит перетаскивание.
    /// </summary>
    public sealed class EquipmentBoardTests
    {
        private const int Cells = 24;

        private EquipmentBoard _board;

        [SetUp]
        public void SetUp() => _board = new EquipmentBoard(Cells);

        [Test]
        public void NewBoard_NothingGranted()
        {
            Assert.IsFalse(_board.Has(EquipmentKind.Scanner));
            Assert.IsFalse(_board.Has(EquipmentKind.Map));
            Assert.IsFalse(_board.IsEquipped(EquipmentKind.Scanner));
        }

        [Test]
        public void Grant_PutsItemStraightIntoItsSlot()
        {
            Assert.IsTrue(_board.Grant(EquipmentKind.Map));

            Assert.IsTrue(_board.Has(EquipmentKind.Map));
            Assert.IsTrue(_board.IsEquipped(EquipmentKind.Map));
            Assert.AreEqual(EquipmentBoard.InSlot, _board.CellOf(EquipmentKind.Map));
        }

        [Test]
        public void Grant_TwiceIsRefused()
        {
            _board.Grant(EquipmentKind.Scanner);

            Assert.IsFalse(_board.Grant(EquipmentKind.Scanner));
            Assert.IsTrue(_board.IsEquipped(EquipmentKind.Scanner));
        }

        [Test]
        public void MoveToCell_UnequipsIntoFreeCell()
        {
            _board.Grant(EquipmentKind.Scanner);

            Assert.IsTrue(_board.MoveToCell(EquipmentKind.Scanner, 5));
            Assert.IsFalse(_board.IsEquipped(EquipmentKind.Scanner));
            Assert.AreEqual(5, _board.CellOf(EquipmentKind.Scanner));
            Assert.AreEqual(EquipmentKind.Scanner, _board.ItemAt(5));
        }

        [Test]
        public void Equip_ReturnsItemFromCellToSlot()
        {
            _board.Grant(EquipmentKind.Scanner);
            _board.MoveToCell(EquipmentKind.Scanner, 5);

            Assert.IsTrue(_board.Equip(EquipmentKind.Scanner));
            Assert.IsTrue(_board.IsEquipped(EquipmentKind.Scanner));
            Assert.IsNull(_board.ItemAt(5));
            Assert.IsTrue(_board.IsCellFree(5));
        }

        [Test]
        public void Equip_AlreadyEquippedIsRefused()
        {
            _board.Grant(EquipmentKind.Map);

            Assert.IsFalse(_board.Equip(EquipmentKind.Map));
        }

        [Test]
        public void Equip_NotGrantedIsRefused()
        {
            Assert.IsFalse(_board.Equip(EquipmentKind.Map));
        }

        [Test]
        public void MoveToCell_SwapsTwoCells()
        {
            _board.Grant(EquipmentKind.Scanner);
            _board.Grant(EquipmentKind.Map);
            _board.MoveToCell(EquipmentKind.Scanner, 3);
            _board.MoveToCell(EquipmentKind.Map, 9);

            Assert.IsTrue(_board.MoveToCell(EquipmentKind.Scanner, 9));

            Assert.AreEqual(9, _board.CellOf(EquipmentKind.Scanner));
            Assert.AreEqual(3, _board.CellOf(EquipmentKind.Map));
            Assert.AreEqual(EquipmentKind.Scanner, _board.ItemAt(9));
            Assert.AreEqual(EquipmentKind.Map, _board.ItemAt(3));
        }

        /// <summary>
        /// Слот принимает только свой предмет, поэтому вытеснить в него чужой
        /// нельзя. Разрешить такой ход означало бы потерять предмет: у него нет
        /// другого места, кроме чужого слота.
        /// </summary>
        [Test]
        public void MoveToCell_SlotIntoOccupiedCellIsRefused()
        {
            _board.Grant(EquipmentKind.Scanner);
            _board.Grant(EquipmentKind.Map);
            _board.MoveToCell(EquipmentKind.Map, 7);

            Assert.IsFalse(_board.MoveToCell(EquipmentKind.Scanner, 7));

            Assert.IsTrue(_board.IsEquipped(EquipmentKind.Scanner));
            Assert.AreEqual(7, _board.CellOf(EquipmentKind.Map));
        }

        [Test]
        public void MoveToCell_RejectsBadCellAndSamePlace()
        {
            _board.Grant(EquipmentKind.Scanner);
            _board.MoveToCell(EquipmentKind.Scanner, 4);

            Assert.IsFalse(_board.MoveToCell(EquipmentKind.Scanner, -1));
            Assert.IsFalse(_board.MoveToCell(EquipmentKind.Scanner, Cells));
            Assert.IsFalse(_board.MoveToCell(EquipmentKind.Scanner, 4));
            Assert.AreEqual(4, _board.CellOf(EquipmentKind.Scanner));
        }

        [Test]
        public void ItemAt_OutsideGridIsNull()
        {
            Assert.IsNull(_board.ItemAt(-3));
            Assert.IsNull(_board.ItemAt(Cells));
        }

        [Test]
        public void Constructor_NonPositiveCellsThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EquipmentBoard(0));
        }
    }
}
