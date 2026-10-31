using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Story;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>Проверяет выдачу карты и сканера диалогом и её идемпотентность.</summary>
    public sealed class PlayerEquipmentTests
    {
        private GameObject _go;
        private PlayerEquipment _equipment;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("EquipmentTest");
            _equipment = _go.AddComponent<PlayerEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            _go = null;
            _equipment = null;
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
    }
}
