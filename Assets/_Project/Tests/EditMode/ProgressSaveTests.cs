using FrostSpace.Features.Quest;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет ключ `FrostSpace.Save` (ADR 0031): запись, чтение, перенос
    /// сцены, стирание и отказ от битых, чужих по версии и неизвестных данных.
    /// </summary>
    public sealed class ProgressSaveTests
    {
        private const string CharacterId = "R15_Courier";
        private const string QuestId = "train_batteries";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(ProgressSave.Key);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(ProgressSave.Key);
        }

        private static QuestSnapshot Active(int objective, int progress) =>
            new(QuestState.Active, objective, progress);

        private static string ValidJson(int version = ProgressSave.CurrentVersion) =>
            "{\"version\":" + version + "," +
            "\"scene\":\"Train\"," +
            "\"characterId\":\"" + CharacterId + "\"," +
            "\"questId\":\"" + QuestId + "\"," +
            "\"state\":\"Active\"," +
            "\"objective\":1," +
            "\"progress\":2}";

        private static void PutRaw(string json)
        {
            PlayerPrefs.SetString(ProgressSave.Key, json);
            PlayerPrefs.Save();
        }

        [Test]
        public void NoSave_LoadsNothing()
        {
            Assert.IsFalse(ProgressSave.HasSave);
            Assert.IsFalse(ProgressSave.TryLoad(out var data));
            Assert.IsNull(data);
            Assert.AreEqual(string.Empty, ProgressSave.SavedScene);
        }

        [Test]
        public void Write_ThenLoad_RoundTrips()
        {
            ProgressSave.Write(ProgressSave.TrainScene, CharacterId, QuestId, Active(1, 2));

            Assert.IsTrue(ProgressSave.HasSave);
            Assert.IsTrue(ProgressSave.TryLoad(out var data));
            Assert.AreEqual(ProgressSave.CurrentVersion, data.version);
            Assert.AreEqual(ProgressSave.TrainScene, data.scene);
            Assert.AreEqual(CharacterId, data.characterId);
            Assert.AreEqual(QuestId, data.questId);
            Assert.AreEqual("Active", data.state);
            Assert.AreEqual(1, data.objective);
            Assert.AreEqual(2, data.progress);
            Assert.AreEqual(ProgressSave.TrainScene, ProgressSave.SavedScene);
        }

        [Test]
        public void Write_Completed_KeepsCompletedState()
        {
            ProgressSave.Write(
                ProgressSave.TrainScene, CharacterId, QuestId,
                new QuestSnapshot(QuestState.Completed, 3, 0));

            Assert.IsTrue(ProgressSave.TryLoad(out var data));
            Assert.IsTrue(ProgressSave.TryGetState(data, out var state));
            Assert.AreEqual(QuestState.Completed, state);
        }

        [Test]
        public void Write_EmptyQuestId_IsIgnored()
        {
            ProgressSave.Write(ProgressSave.TrainScene, CharacterId, " ", Active(0, 0));

            Assert.IsFalse(ProgressSave.HasSave);
        }

        [Test]
        public void Write_UnknownScene_FallsBackToTrain()
        {
            ProgressSave.Write("Game", CharacterId, QuestId, Active(0, 0));

            Assert.IsTrue(ProgressSave.TryLoad(out var data));
            Assert.AreEqual(ProgressSave.TrainScene, data.scene);
        }

        [Test]
        public void Clear_RemovesSave()
        {
            ProgressSave.Write(ProgressSave.TrainScene, CharacterId, QuestId, Active(0, 0));
            ProgressSave.Clear();

            Assert.IsFalse(ProgressSave.HasSave);
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void BrokenJson_IsStripped()
        {
            PutRaw("{not json");

            Assert.IsFalse(ProgressSave.TryLoad(out _));
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void VersionMismatch_IsStripped()
        {
            PutRaw(ValidJson(ProgressSave.CurrentVersion + 1));

            Assert.IsFalse(ProgressSave.TryLoad(out _));
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void UnknownScene_IsStripped()
        {
            PutRaw(ValidJson().Replace("\"scene\":\"Train\"", "\"scene\":\"Moon\""));

            Assert.IsFalse(ProgressSave.TryLoad(out _));
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void UnknownState_IsStripped()
        {
            PutRaw(ValidJson().Replace("\"state\":\"Active\"", "\"state\":\"Broken\""));

            Assert.IsFalse(ProgressSave.TryLoad(out _));
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void NumericState_IsStripped()
        {
            PutRaw(ValidJson().Replace("\"state\":\"Active\"", "\"state\":\"7\""));

            Assert.IsFalse(ProgressSave.TryLoad(out _));
        }

        [Test]
        public void EmptyJson_IsStripped()
        {
            PutRaw(string.Empty);

            Assert.IsFalse(ProgressSave.TryLoad(out _));
            Assert.IsFalse(PlayerPrefs.HasKey(ProgressSave.Key));
        }

        [Test]
        public void MarkScene_MovesSaveToFrontier_KeepingProgress()
        {
            ProgressSave.Write(ProgressSave.TrainScene, CharacterId, QuestId, Active(1, 3));

            Assert.IsTrue(ProgressSave.MarkScene(ProgressSave.FrontierScene));
            Assert.IsTrue(ProgressSave.TryLoad(out var data));
            Assert.AreEqual(ProgressSave.FrontierScene, data.scene);
            Assert.AreEqual(QuestId, data.questId);
            Assert.AreEqual(1, data.objective);
            Assert.AreEqual(3, data.progress);
        }

        [Test]
        public void MarkScene_WithoutSave_DoesNothing()
        {
            Assert.IsFalse(ProgressSave.MarkScene(ProgressSave.FrontierScene));
            Assert.IsFalse(ProgressSave.HasSave);
        }

        [Test]
        public void MarkScene_UnknownScene_IsIgnored()
        {
            ProgressSave.Write(ProgressSave.TrainScene, CharacterId, QuestId, Active(0, 0));

            Assert.IsFalse(ProgressSave.MarkScene("Game"));
            Assert.AreEqual(ProgressSave.TrainScene, ProgressSave.SavedScene);
        }

        [Test]
        public void TryGetState_RejectsMissingData()
        {
            Assert.IsFalse(ProgressSave.TryGetState(null, out _));
            Assert.IsFalse(ProgressSave.TryGetState(new ProgressData(), out _));
        }
    }
}
