using System.Collections.Generic;
using FrostSpace.Features.Quest;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>Проверяет валидацию данных квеста без сцены и инспектора.</summary>
    public sealed class QuestAssetTests
    {
        private QuestAsset _asset;

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                Object.DestroyImmediate(_asset);
            _asset = null;
        }

        private QuestAsset NewQuest()
        {
            _asset = ScriptableObject.CreateInstance<QuestAsset>();
            _asset.hideFlags = HideFlags.DontSave;
            return _asset;
        }

        private List<string> Problems()
        {
            var errors = new List<string>();
            _asset.CollectProblems(errors, null);
            return errors;
        }

        [Test]
        public void ValidQuest_HasNoProblems()
        {
            var asset = NewQuest();
            asset.SetIdentity("train_batteries", "BATTERIES");
            asset.AddObjective(new QuestObjective(
                "q_talk", "Поговорить", QuestObjectiveKind.Talk, "R13_Prospector"));
            asset.AddObjective(new QuestObjective(
                "q_load", "Погрузить", QuestObjectiveKind.Deliver, "battery", 4));

            Assert.IsEmpty(Problems());
        }

        [Test]
        public void EmptyQuestId_IsError()
        {
            var asset = NewQuest();
            asset.SetIdentity(string.Empty, "BATTERIES");
            asset.AddObjective(new QuestObjective(
                "q_talk", "Поговорить", QuestObjectiveKind.Talk, "R13_Prospector"));

            Assert.IsNotEmpty(Problems());
        }

        [Test]
        public void NoObjectives_IsError()
        {
            var asset = NewQuest();
            asset.SetIdentity("train_batteries", "BATTERIES");

            Assert.IsNotEmpty(Problems());
        }

        [Test]
        public void DuplicateObjectiveIds_AreErrors()
        {
            var asset = NewQuest();
            asset.SetIdentity("train_batteries", "BATTERIES");
            asset.AddObjective(new QuestObjective(
                "q_same", "Первая", QuestObjectiveKind.Talk, "R13_Prospector"));
            asset.AddObjective(new QuestObjective(
                "q_same", "Вторая", QuestObjectiveKind.Talk, "R13_Prospector"));

            Assert.IsNotEmpty(Problems());
        }

        [Test]
        public void ZeroRequired_IsError()
        {
            var asset = NewQuest();
            asset.SetIdentity("train_batteries", "BATTERIES");
            asset.AddObjective(new QuestObjective(
                "q_load", "Погрузить", QuestObjectiveKind.Deliver, "battery", 0));

            Assert.IsNotEmpty(Problems());
        }

        [Test]
        public void EmptyTargetId_IsError()
        {
            var asset = NewQuest();
            asset.SetIdentity("train_batteries", "BATTERIES");
            asset.AddObjective(new QuestObjective(
                "q_talk", "Поговорить", QuestObjectiveKind.Talk, string.Empty));

            Assert.IsNotEmpty(Problems());
        }
    }
}
