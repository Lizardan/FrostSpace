using System.Collections.Generic;
using System.Linq;
using FrostSpace.Features.Dialogue;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет стартовый контент в <c>Content/Data/Dialogues</c>: графы без ошибок,
    /// ветвление и концовки на месте, реплики на русском. Список берётся из папки,
    /// поэтому новый диалог попадает под проверку без правки теста.
    /// </summary>
    public sealed class DialogueContentTests
    {
        private const string Folder = "Assets/_Project/Content/Data/Dialogues";

        private static DialogueAsset[] LoadAll()
        {
            return AssetDatabase.FindAssets("t:DialogueAsset", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<DialogueAsset>)
                .Where(asset => asset != null)
                .ToArray();
        }

        [Test]
        public void Folder_ContainsFourStarterDialogues()
        {
            Assert.AreEqual(4, LoadAll().Length, "Ожидаются диалоги для четырёх роботов сцены Game.");
        }

        [Test]
        public void AllDialogues_AreValid()
        {
            foreach (var asset in LoadAll())
            {
                var errors = new List<string>();
                asset.CollectProblems(errors, null);
                Assert.IsEmpty(errors, $"{asset.name}: {string.Join("; ", errors)}");
            }
        }

        [Test]
        public void AllDialogues_StartAtExistingNode()
        {
            foreach (var asset in LoadAll())
                Assert.IsNotNull(asset.StartNode, $"{asset.name}: стартовый узел не найден.");
        }

        [Test]
        public void AllDialogues_BranchAndReachAnEnd()
        {
            foreach (var asset in LoadAll())
            {
                var branch = asset.Nodes.Any(node => node != null && node.Choices.Count >= 2);
                Assert.IsTrue(branch, $"{asset.name}: нет узла с ветвлением.");

                var endings = asset.Nodes.Count(Ends);
                Assert.GreaterOrEqual(endings, 2, $"{asset.name}: нужно минимум две концовки.");
            }
        }

        [Test]
        public void AllDialogues_RespectChoiceLimit()
        {
            foreach (var asset in LoadAll())
            {
                foreach (var node in asset.Nodes)
                {
                    if (node == null)
                        continue;

                    Assert.LessOrEqual(
                        node.Choices.Count, DialogueAsset.MaxChoices,
                        $"{asset.name}/{node.Id}: вариантов больше, чем помещается в панель.");
                }
            }
        }

        [Test]
        public void AllLines_FitWarningLimit()
        {
            foreach (var asset in LoadAll())
            {
                foreach (var node in asset.Nodes)
                {
                    if (node == null)
                        continue;

                    Assert.LessOrEqual(
                        node.Line.Length, DialogueAsset.LongLineWarning,
                        $"{asset.name}/{node.Id}: реплика не помещается в панель.");
                }
            }
        }

        [Test]
        public void AllLines_AreRussian()
        {
            foreach (var asset in LoadAll())
            {
                foreach (var node in asset.Nodes)
                {
                    if (node == null)
                        continue;

                    Assert.IsTrue(
                        HasCyrillic(node.Line),
                        $"{asset.name}/{node.Id}: реплика не на русском.");
                }
            }
        }

        private static bool Ends(DialogueNode node)
        {
            if (node == null)
                return false;
            if (node.IsTerminal)
                return true;
            return node.Choices.Any(choice => string.IsNullOrWhiteSpace(choice.NextId));
        }

        private static bool HasCyrillic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var symbol in text)
            {
                if (symbol >= '\u0400' && symbol <= '\u04FF')
                    return true;
            }

            return false;
        }
    }
}
