using FrostSpace.Features.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет данные действия узла: значение по умолчанию и сохранение при
    /// дублировании, чтобы автор не терял AcceptQuest/CompleteQuest.
    /// </summary>
    public sealed class DialogueActionTests
    {
        private DialogueAsset _asset;

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                Object.DestroyImmediate(_asset);
            _asset = null;
        }

        private DialogueAsset NewAsset()
        {
            _asset = ScriptableObject.CreateInstance<DialogueAsset>();
            return _asset;
        }

        [Test]
        public void NewNode_HasNoAction()
        {
            var node = NewAsset().AddNode("Реплика");

            Assert.AreEqual(DialogueAction.None, node.Action);
            Assert.IsFalse(node.HasAction);
            Assert.IsEmpty(node.QuestId);
        }

        [Test]
        public void SetAction_StoresActionAndQuestId()
        {
            var node = NewAsset().AddNode("Реплика");

            node.SetAction(DialogueAction.AcceptQuest, "train_batteries");

            Assert.AreEqual(DialogueAction.AcceptQuest, node.Action);
            Assert.AreEqual("train_batteries", node.QuestId);
            Assert.IsTrue(node.HasAction);
        }

        [Test]
        public void SetAction_DefaultQuestIdIsEmpty()
        {
            var node = NewAsset().AddNode("Реплика");

            node.SetAction(DialogueAction.CompleteQuest);

            Assert.AreEqual(DialogueAction.CompleteQuest, node.Action);
            Assert.IsEmpty(node.QuestId);
        }

        [Test]
        public void DuplicateNode_PreservesAction()
        {
            var asset = NewAsset();
            var original = asset.AddNode("Реплика");
            original.SetAction(DialogueAction.AcceptQuest, "train_batteries");

            var copy = asset.DuplicateNode(0);

            Assert.IsNotNull(copy);
            Assert.AreEqual(DialogueAction.AcceptQuest, copy.Action);
            Assert.AreEqual("train_batteries", copy.QuestId);
        }
    }
}
