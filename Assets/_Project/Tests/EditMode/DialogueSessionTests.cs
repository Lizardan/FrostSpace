using FrostSpace.Features.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет переходы по графу без UI: старт, линейный ход, ветвление, циклы и
    /// поведение на битых ссылках. Сессия — обычный класс, сцена не нужна.
    /// </summary>
    public sealed class DialogueSessionTests
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
        public void NullAsset_IsFinished()
        {
            var session = new DialogueSession(null);

            Assert.IsTrue(session.IsFinished);
            Assert.IsNull(session.Current);
            Assert.IsFalse(session.HasChoices);
            Assert.IsFalse(session.Advance());
            Assert.IsFalse(session.Choose(0));
        }

        [Test]
        public void EmptyAsset_IsFinished()
        {
            var session = NewAsset().CreateSession();

            Assert.IsTrue(session.IsFinished);
            Assert.IsNull(session.Current);
        }

        [Test]
        public void LinearGraph_AdvancesToEnd()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Первая реплика");
            var second = asset.AddNode("Вторая реплика");
            first.SetNextId(second.Id);

            var session = asset.CreateSession();

            Assert.AreSame(first, session.Current);
            Assert.IsFalse(session.HasChoices);
            Assert.IsTrue(session.Advance());
            Assert.AreSame(second, session.Current);
            Assert.IsFalse(session.Advance());
            Assert.IsTrue(session.IsFinished);
        }

        [Test]
        public void StartNode_IsTheFirstAddedNode()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Старт");
            asset.AddNode("Хвост");

            Assert.AreEqual(first.Id, asset.StartId);
            Assert.AreSame(first, asset.StartNode);
        }

        [Test]
        public void Choose_MovesToTheSelectedBranch()
        {
            var asset = NewAsset();
            var question = asset.AddNode("Кто ты?");
            var left = asset.AddNode("Левый ответ");
            var right = asset.AddNode("Правый ответ");
            question.AddChoice("Левый", left.Id);
            question.AddChoice("Правый", right.Id);

            var session = asset.CreateSession();

            Assert.IsTrue(session.HasChoices);
            Assert.IsTrue(session.Choose(1));
            Assert.AreSame(right, session.Current);
            Assert.IsFalse(session.Choose(0), "У терминального узла нет вариантов");
        }

        [Test]
        public void Choose_OutOfRange_DoesNothing()
        {
            var asset = NewAsset();
            var question = asset.AddNode("Вопрос");
            question.AddChoice("Ответ", string.Empty);

            var session = asset.CreateSession();

            Assert.IsFalse(session.Choose(-1));
            Assert.IsFalse(session.Choose(1));
            Assert.AreSame(question, session.Current);
        }

        [Test]
        public void Advance_OnChoiceNode_DoesNothing()
        {
            var asset = NewAsset();
            var question = asset.AddNode("Вопрос");
            question.SetNextId(question.Id);
            question.AddChoice("Ответ", string.Empty);

            var session = asset.CreateSession();

            Assert.IsFalse(session.Advance());
            Assert.AreSame(question, session.Current);
            Assert.IsTrue(session.HasChoices);
        }

        [Test]
        public void Cycles_AreAllowed()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Первый");
            var second = asset.AddNode("Второй");
            first.AddChoice("Дальше", second.Id);
            second.AddChoice("Назад", first.Id);

            var session = asset.CreateSession();
            Assert.IsTrue(session.Choose(0));
            Assert.AreSame(second, session.Current);
            Assert.IsTrue(session.Choose(0));
            Assert.AreSame(first, session.Current);
            Assert.IsFalse(session.IsFinished);
        }

        [Test]
        public void BrokenLink_FinishesConversation()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Реплика");
            first.AddChoice("В никуда", "node_404");

            var session = asset.CreateSession();

            Assert.IsFalse(session.Choose(0));
            Assert.IsTrue(session.IsFinished);
        }

        [Test]
        public void EmptyNextId_FinishesConversation()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Реплика");
            first.SetNextId(string.Empty);

            var session = asset.CreateSession();

            Assert.IsFalse(session.Advance());
            Assert.IsTrue(session.IsFinished);
        }
    }
}
