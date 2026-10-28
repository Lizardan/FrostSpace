using System.Collections.Generic;
using System.Linq;
using FrostSpace.Features.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет валидацию графа и операции авторинга: удаление узла переводит
    /// ссылки в «Конец», дубликат получает новый id, ошибки отделены от
    /// предупреждений. Инспектор и меню проверки опираются на эти же методы.
    /// </summary>
    public sealed class DialogueAssetValidationTests
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

        /// <summary>Валидный диалог: вопрос и две концовки.</summary>
        private DialogueAsset NewValid()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Стой! Кто идёт?");
            var yes = asset.AddNode("Проходи, механик.");
            var no = asset.AddNode("Тогда разворачивайся.");
            root.AddChoice("Свой.", yes.Id);
            root.AddChoice("Тебе какое дело?", no.Id);
            return asset;
        }

        private (List<string> errors, List<string> warnings) Problems(DialogueAsset asset)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            asset.CollectProblems(errors, warnings);
            return (errors, warnings);
        }

        [Test]
        public void ValidGraph_HasNoProblems()
        {
            var (errors, warnings) = Problems(NewValid());

            Assert.IsEmpty(errors);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void EmptyAsset_IsAnError()
        {
            var (errors, _) = Problems(NewAsset());

            Assert.That(errors, Has.Some.Contains("нет ни одного узла"));
        }

        [Test]
        public void EmptyLine_IsAnError()
        {
            var asset = NewAsset();
            asset.AddNode(string.Empty);

            var (errors, _) = Problems(asset);

            Assert.That(errors, Has.Some.Contains("пустая реплика"));
        }

        [Test]
        public void EmptyStart_IsAnError()
        {
            var asset = NewAsset();
            asset.AddNode("Реплика");
            asset.SetStart(null);

            var (errors, _) = Problems(asset);

            Assert.That(errors, Has.Some.Contains("стартовый узел"));
        }

        [Test]
        public void TooManyChoices_IsAnError()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Вопрос");
            for (var i = 0; i < DialogueAsset.MaxChoices + 1; i++)
                root.AddChoice($"Ответ {i}", string.Empty);

            var (errors, _) = Problems(asset);

            Assert.That(errors, Has.Some.Contains($"максимум {DialogueAsset.MaxChoices}"));
        }

        [Test]
        public void EmptyChoiceText_IsAnError()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Вопрос");
            root.AddChoice(string.Empty, string.Empty);

            var (errors, _) = Problems(asset);

            Assert.That(errors, Has.Some.Contains("пустой текст"));
        }

        [Test]
        public void BrokenTransition_IsAnError()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Вопрос");
            root.AddChoice("Туда", "node_404");
            asset.AddNode("Хвост");

            var (errors, _) = Problems(asset);

            Assert.That(errors, Has.Some.Contains("node_404"));
        }

        [Test]
        public void UnreachableNode_IsAWarning()
        {
            var asset = NewValid();
            asset.AddNode("Никто сюда не придёт");

            var (errors, warnings) = Problems(asset);

            Assert.IsEmpty(errors);
            Assert.That(warnings, Has.Some.Contains("недостижим"));
        }

        [Test]
        public void CycleWithoutEnd_IsAWarning()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Первый");
            var second = asset.AddNode("Второй");
            first.AddChoice("Дальше", second.Id);
            second.AddChoice("Назад", first.Id);

            var (errors, warnings) = Problems(asset);

            Assert.IsEmpty(errors);
            Assert.That(warnings, Has.Some.Contains("нет достижимого конца"));
        }

        [Test]
        public void LongLine_IsAWarning()
        {
            var asset = NewAsset();
            asset.AddNode(new string('а', DialogueAsset.LongLineWarning + 1));

            var (errors, warnings) = Problems(asset);

            Assert.IsEmpty(errors);
            Assert.That(warnings, Has.Some.Contains("длиннее"));
        }

        [Test]
        public void RemoveNode_ClearsReferencesToIt()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Вопрос");
            var answer = asset.AddNode("Ответ");
            root.AddChoice("Дальше", answer.Id);

            asset.RemoveNode(1);

            Assert.AreEqual(string.Empty, root.Choices[0].NextId);
            Assert.IsNull(asset.Find(answer.Id));
            var (errors, _) = Problems(asset);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void RemoveStart_PicksFirstRemainingNode()
        {
            var asset = NewAsset();
            var first = asset.AddNode("Первый");
            var second = asset.AddNode("Второй");

            asset.RemoveNode(0);

            Assert.AreEqual(second.Id, asset.StartId);
            Assert.IsNull(asset.Find(first.Id));
        }

        [Test]
        public void DuplicateNode_GetsNewIdAndKeepsContent()
        {
            var asset = NewAsset();
            var root = asset.AddNode("Вопрос");
            var answer = asset.AddNode("Ответ");
            root.AddChoice("Дальше", answer.Id);

            var copy = asset.DuplicateNode(1);

            Assert.IsNotNull(copy);
            Assert.AreNotEqual(answer.Id, copy.Id);
            Assert.AreEqual(answer.Line, copy.Line);
            Assert.AreEqual(3, asset.Nodes.Count);
            Assert.AreSame(copy, asset.Nodes[2]);
            Assert.IsEmpty(Problems(asset).errors);
        }

        [Test]
        public void GeneratedIds_StayUnique()
        {
            var asset = NewAsset();
            asset.AddNode("А");
            asset.AddNode("Б");
            asset.DuplicateNode(0);
            asset.DuplicateNode(2);

            var ids = asset.Nodes.Select(node => node.Id).ToArray();

            Assert.AreEqual(ids.Length, ids.Distinct().Count());
        }
    }
}
