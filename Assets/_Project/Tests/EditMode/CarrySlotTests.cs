using FrostSpace.Features.Carry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет гнездо тележки: принимает только предмет в руках и только
    /// своего типа, второму предмету отказывает и поднимает <c>Placed</c> один раз.
    /// </summary>
    public sealed class CarrySlotTests
    {
        private GameObject _slotObject;
        private GameObject _itemObject;
        private GameObject _anchorObject;
        private CarrySlot _slot;
        private Carryable _item;

        [SetUp]
        public void SetUp()
        {
            _slotObject = new GameObject("SlotTest");
            _slot = _slotObject.AddComponent<CarrySlot>();

            _itemObject = new GameObject("BatteryTest");
            _item = _itemObject.AddComponent<Carryable>();

            _anchorObject = new GameObject("HandsTest");

            // Awake в EditMode не зовётся, поэтому якорь ставим явно: иначе
            // проверялась бы не расстановка предмета, а отсутствие якоря.
            var serialized = new SerializedObject(_slot);
            serialized.FindProperty("_anchor").objectReferenceValue = _anchorObject.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_slotObject);
            Object.DestroyImmediate(_itemObject);
            Object.DestroyImmediate(_anchorObject);
            _slotObject = null;
            _itemObject = null;
            _anchorObject = null;
            _slot = null;
            _item = null;
        }

        private void SetAcceptedType(string type)
        {
            var serialized = new SerializedObject(_slot);
            serialized.FindProperty("_acceptedType").stringValue = type;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void NewSlot_IsFree()
        {
            Assert.IsTrue(_slot.IsFree);
            Assert.IsNull(_slot.Occupant);
            Assert.AreEqual("battery", _slot.AcceptedType);
        }

        [Test]
        public void Place_AcceptsCarriedMatchingItem()
        {
            _item.Take(_anchorObject.transform);

            Assert.IsTrue(_slot.TryPlace(_item));
            Assert.IsFalse(_slot.IsFree);
            Assert.AreSame(_item, _slot.Occupant);
            Assert.IsTrue(_item.IsPlaced);
        }

        [Test]
        public void Place_RejectsItemNotInHands()
        {
            Assert.IsFalse(_slot.TryPlace(_item));
            Assert.IsTrue(_slot.IsFree);
        }

        [Test]
        public void Place_RejectsWrongType()
        {
            SetAcceptedType("scrap");
            _item.Take(_anchorObject.transform);

            Assert.IsFalse(_slot.TryPlace(_item));
            Assert.IsTrue(_slot.IsFree);
        }

        [Test]
        public void Place_RejectsSecondItem()
        {
            _item.Take(_anchorObject.transform);
            _slot.TryPlace(_item);

            var secondObject = new GameObject("SecondBattery");
            var second = secondObject.AddComponent<Carryable>();
            second.Take(_anchorObject.transform);

            Assert.IsFalse(_slot.TryPlace(second));
            Assert.AreSame(_item, _slot.Occupant);

            Object.DestroyImmediate(secondObject);
        }

        [Test]
        public void Place_RejectsNull()
        {
            Assert.IsFalse(_slot.TryPlace(null));
            Assert.IsTrue(_slot.IsFree);
        }

        [Test]
        public void Place_RaisesPlacedOnce()
        {
            var raised = 0;
            CarrySlot raisedSlot = null;
            Carryable raisedItem = null;
            _slot.Placed += (slot, item) =>
            {
                raised++;
                raisedSlot = slot;
                raisedItem = item;
            };

            _item.Take(_anchorObject.transform);
            _slot.TryPlace(_item);
            _slot.TryPlace(_item);

            Assert.AreEqual(1, raised);
            Assert.AreSame(_slot, raisedSlot);
            Assert.AreSame(_item, raisedItem);
        }
    }
}
