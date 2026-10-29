using FrostSpace.Features.Quest;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет прогресс квеста без сцены: строгий порядок целей, накопление
    /// доставки, завершение и игнор событий после финала (ADR 0027).
    /// </summary>
    public sealed class QuestLogTests
    {
        private const string TalkTarget = "R13_Prospector";
        private const string DeliveryTarget = "battery";

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
            _asset.SetIdentity("train_batteries", "BATTERIES");
            _asset.AddObjective(new QuestObjective(
                "q_talk_intro", "Поговорить", QuestObjectiveKind.Talk, TalkTarget));
            _asset.AddObjective(new QuestObjective(
                "q_load", "Погрузить", QuestObjectiveKind.Deliver, DeliveryTarget, 4));
            _asset.AddObjective(new QuestObjective(
                "q_talk_done", "Доложить", QuestObjectiveKind.Talk, TalkTarget));
            return _asset;
        }

        private QuestLog StartedQuest()
        {
            var log = NewQuest().CreateLog();
            Assert.IsTrue(log.Activate());
            return log;
        }

        private static QuestEvent Talk() => new(QuestObjectiveKind.Talk, TalkTarget);

        private static QuestEvent Deliver(int amount = 1) =>
            new(QuestObjectiveKind.Deliver, DeliveryTarget, amount);

        [Test]
        public void NewLog_IsInactive()
        {
            var log = NewQuest().CreateLog();

            Assert.AreEqual(QuestState.Inactive, log.State);
            Assert.AreEqual(3, log.ObjectiveCount);
            Assert.IsNull(log.CurrentObjective);
            Assert.IsFalse(log.IsActive);
            Assert.IsFalse(log.IsCompleted);
        }

        [Test]
        public void Activate_StartsFirstObjective()
        {
            var log = NewQuest().CreateLog();

            Assert.IsTrue(log.Activate());
            Assert.AreEqual(QuestState.Active, log.State);
            Assert.AreEqual(0, log.ObjectiveIndex);
            Assert.AreEqual(0, log.Progress);
            Assert.AreEqual("q_talk_intro", log.CurrentObjective.Id);
        }

        [Test]
        public void Activate_Twice_DoesNothing()
        {
            var log = StartedQuest();

            Assert.IsFalse(log.Activate());
            Assert.AreEqual(QuestState.Active, log.State);
            Assert.AreEqual(0, log.ObjectiveIndex);
        }

        [Test]
        public void EventOutOfOrder_IsIgnored()
        {
            var log = StartedQuest();

            Assert.IsFalse(log.Apply(Deliver(1)));
            Assert.AreEqual("q_talk_intro", log.CurrentObjective.Id);
            Assert.AreEqual(0, log.Progress);
        }

        [Test]
        public void WrongTarget_IsIgnored()
        {
            var log = StartedQuest();

            Assert.IsFalse(log.Apply(new QuestEvent(
                QuestObjectiveKind.Talk, "R12_Stranger")));
            Assert.AreEqual("q_talk_intro", log.CurrentObjective.Id);
        }

        [Test]
        public void WrongKind_IsIgnored()
        {
            var log = StartedQuest();

            Assert.IsFalse(log.Apply(new QuestEvent(
                QuestObjectiveKind.Reach, TalkTarget)));
            Assert.AreEqual("q_talk_intro", log.CurrentObjective.Id);
        }

        [Test]
        public void NonPositiveAmount_IsIgnored()
        {
            var log = StartedQuest();

            Assert.IsFalse(log.Apply(new QuestEvent(
                QuestObjectiveKind.Talk, TalkTarget, 0)));
            Assert.IsFalse(log.Apply(new QuestEvent(
                QuestObjectiveKind.Talk, TalkTarget, -1)));
            Assert.AreEqual("q_talk_intro", log.CurrentObjective.Id);
        }

        [Test]
        public void Talk_CompletesIntroObjective()
        {
            var log = StartedQuest();

            Assert.IsTrue(log.Apply(Talk()));
            Assert.AreEqual(1, log.ObjectiveIndex);
            Assert.AreEqual(0, log.Progress);
            Assert.AreEqual("q_load", log.CurrentObjective.Id);
        }

        [Test]
        public void Deliver_AccumulatesUntilRequired()
        {
            var log = StartedQuest();
            log.Apply(Talk());

            Assert.IsTrue(log.Apply(Deliver()));
            Assert.AreEqual(1, log.Progress);
            Assert.IsTrue(log.Apply(Deliver()));
            Assert.AreEqual(2, log.Progress);
            Assert.IsTrue(log.Apply(Deliver()));
            Assert.AreEqual(3, log.Progress);
            Assert.AreEqual("q_load", log.CurrentObjective.Id);

            Assert.IsTrue(log.Apply(Deliver()));
            Assert.AreEqual(2, log.ObjectiveIndex);
            Assert.AreEqual(0, log.Progress);
            Assert.AreEqual("q_talk_done", log.CurrentObjective.Id);
        }

        [Test]
        public void Deliver_Overshoot_CompletesObjective()
        {
            var log = StartedQuest();
            log.Apply(Talk());

            Assert.IsTrue(log.Apply(Deliver(10)));
            Assert.AreEqual("q_talk_done", log.CurrentObjective.Id);
        }

        [Test]
        public void FinalTalk_CompletesQuest()
        {
            var log = StartedQuest();
            log.Apply(Talk());
            log.Apply(Deliver(4));

            Assert.IsTrue(log.Apply(Talk()));
            Assert.AreEqual(QuestState.Completed, log.State);
            Assert.IsTrue(log.IsCompleted);
            Assert.IsNull(log.CurrentObjective);
        }

        [Test]
        public void Completed_IgnoresFurtherEvents()
        {
            var log = StartedQuest();
            log.Apply(Talk());
            log.Apply(Deliver(4));
            log.Apply(Talk());

            Assert.IsFalse(log.Apply(Talk()));
            Assert.IsFalse(log.Apply(Deliver(1)));
            Assert.AreEqual(QuestState.Completed, log.State);
        }

        [Test]
        public void Snapshot_ReflectsProgress()
        {
            var log = StartedQuest();
            log.Apply(Talk());
            log.Apply(Deliver(2));

            var snapshot = log.Snapshot();

            Assert.AreEqual(QuestState.Active, snapshot.State);
            Assert.AreEqual(1, snapshot.ObjectiveIndex);
            Assert.AreEqual(2, snapshot.Progress);
        }

        [Test]
        public void NullQuest_IsInert()
        {
            var log = new QuestLog(null);

            Assert.AreEqual(QuestState.Inactive, log.State);
            Assert.AreEqual(0, log.ObjectiveCount);
            Assert.IsFalse(log.Activate());
            Assert.IsFalse(log.Apply(Talk()));
            Assert.IsNull(log.CurrentObjective);
        }

        [Test]
        public void QuestWithoutObjectives_DoesNotActivate()
        {
            _asset = ScriptableObject.CreateInstance<QuestAsset>();
            _asset.hideFlags = HideFlags.DontSave;
            _asset.SetIdentity("empty", "EMPTY");
            var log = _asset.CreateLog();

            Assert.IsFalse(log.Activate());
            Assert.AreEqual(QuestState.Inactive, log.State);
        }
    }
}
