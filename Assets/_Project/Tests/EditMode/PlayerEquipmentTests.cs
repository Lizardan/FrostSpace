using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Inventory;
using FrostSpace.Features.Scan;
using FrostSpace.Features.Story;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>Проверяет выдачу карты и сканера диалогом и её идемпотентность.</summary>
    public sealed class PlayerEquipmentTests
    {
        private GameObject _go;
        private PlayerEquipment _equipment;
        private GameObject _scannerGo;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("EquipmentTest");
            _equipment = _go.AddComponent<PlayerEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_scannerGo != null)
                Object.DestroyImmediate(_scannerGo);
            if (_go != null)
                Object.DestroyImmediate(_go);
            _scannerGo = null;
            _go = null;
            _equipment = null;
        }

        private ObjectScanner AttachScanner()
        {
            _scannerGo = new GameObject("ScannerTest");
            var scanner = _scannerGo.AddComponent<ObjectScanner>();
            var serialized = new SerializedObject(_equipment);
            serialized.FindProperty("_scanner").objectReferenceValue = scanner;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return scanner;
        }

        [Test]
        public void NewEquipment_StartsEmpty()
        {
            Assert.IsFalse(_equipment.HasScanner);
            Assert.IsFalse(_equipment.HasMap);
        }

        [Test]
        public void Apply_GrantScannerSetsFlag()
        {
            _equipment.Apply(DialogueAction.GrantScanner);

            Assert.IsTrue(_equipment.HasScanner);
            Assert.IsFalse(_equipment.HasMap);
        }

        [Test]
        public void Apply_GrantMapSetsFlag()
        {
            _equipment.Apply(DialogueAction.GrantMap);

            Assert.IsTrue(_equipment.HasMap);
            Assert.IsFalse(_equipment.HasScanner);
        }

        [Test]
        public void Apply_RepeatedGrantChangesNothing()
        {
            var changes = 0;
            _equipment.Changed += () => changes++;

            _equipment.Apply(DialogueAction.GrantScanner);
            _equipment.Apply(DialogueAction.GrantScanner);
            _equipment.Apply(DialogueAction.GrantMap);
            _equipment.Apply(DialogueAction.GrantMap);

            Assert.IsTrue(_equipment.HasScanner);
            Assert.IsTrue(_equipment.HasMap);
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void Apply_QuestActionsAreIgnored()
        {
            _equipment.Apply(DialogueAction.AcceptQuest);
            _equipment.Apply(DialogueAction.CompleteQuest);
            _equipment.Apply(DialogueAction.None);

            Assert.IsFalse(_equipment.HasScanner);
            Assert.IsFalse(_equipment.HasMap);
        }

        [Test]
        public void Apply_GrantAlsoEquips()
        {
            _equipment.Apply(DialogueAction.GrantMap);

            Assert.IsTrue(_equipment.IsEquipped(EquipmentKind.Map));
        }

        [Test]
        public void SetEquipped_FalseMovesItemToGridAndRaisesChanged()
        {
            _equipment.Apply(DialogueAction.GrantScanner);

            var changes = 0;
            _equipment.Changed += () => changes++;

            Assert.IsTrue(_equipment.SetEquipped(EquipmentKind.Scanner, false));
            Assert.IsFalse(_equipment.IsEquipped(EquipmentKind.Scanner));
            Assert.IsTrue(_equipment.HasScanner);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void SetEquipped_TrueReturnsItemToSlot()
        {
            _equipment.Apply(DialogueAction.GrantScanner);
            _equipment.SetEquipped(EquipmentKind.Scanner, false);

            Assert.IsTrue(_equipment.SetEquipped(EquipmentKind.Scanner, true));
            Assert.IsTrue(_equipment.IsEquipped(EquipmentKind.Scanner));
        }

        /// <summary>
        /// Снятое оборудование не должно оставаться работающим: «выдано» и «надето»
        /// — разные состояния, и ни одно из них не отменяет другое.
        /// </summary>
        [Test]
        public void UnequippedItem_IsStillGranted()
        {
            _equipment.Apply(DialogueAction.GrantMap);
            _equipment.SetEquipped(EquipmentKind.Map, false);

            Assert.IsTrue(_equipment.HasMap);
            Assert.IsFalse(_equipment.IsEquipped(EquipmentKind.Map));
        }

        [Test]
        public void SetEquipped_OnUngrantedItemIsRefused()
        {
            var changes = 0;
            _equipment.Changed += () => changes++;

            Assert.IsFalse(_equipment.SetEquipped(EquipmentKind.Scanner, true));
            Assert.IsFalse(_equipment.SetEquipped(EquipmentKind.Scanner, false));
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void SetEquipped_ToStateItAlreadyInIsRefused()
        {
            _equipment.Apply(DialogueAction.GrantScanner);

            Assert.IsFalse(_equipment.SetEquipped(EquipmentKind.Scanner, true));
        }

        /// <summary>
        /// Перетаскивание из слота в сетку — это снятие: сканер обязан отключиться
        /// так же, как при снятии кнопкой, а событие приходит ровно один раз.
        /// </summary>
        [Test]
        public void MoveToCell_OutOfSlot_DisablesScanner()
        {
            var scanner = AttachScanner();
            _equipment.Apply(DialogueAction.GrantScanner);
            Assert.IsTrue(scanner.Available);

            var changes = 0;
            _equipment.Changed += () => changes++;

            Assert.IsTrue(_equipment.MoveToCell(EquipmentKind.Scanner, 0));
            Assert.IsFalse(_equipment.IsEquipped(EquipmentKind.Scanner));
            Assert.IsFalse(scanner.Available);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void MoveToCell_ThenReequip_EnablesScanner()
        {
            var scanner = AttachScanner();
            _equipment.Apply(DialogueAction.GrantScanner);
            _equipment.MoveToCell(EquipmentKind.Scanner, 0);

            Assert.IsTrue(_equipment.SetEquipped(EquipmentKind.Scanner, true));
            Assert.IsTrue(scanner.Available);
        }

        [Test]
        public void MoveToCell_UngrantedItem_IsRefusedWithoutEvent()
        {
            var scanner = AttachScanner();

            var changes = 0;
            _equipment.Changed += () => changes++;

            Assert.IsFalse(_equipment.MoveToCell(EquipmentKind.Scanner, 0));
            Assert.AreEqual(0, changes);
            Assert.IsTrue(scanner.Available);
        }
    }
}
