using FrostSpace.Features.Progression;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>Проверяет накопление опыта и переходы между уровнями без сцены.</summary>
    public sealed class PlayerExperienceTests
    {
        private GameObject _go;
        private PlayerExperience _experience;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ExperienceTest");
            _experience = _go.AddComponent<PlayerExperience>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            _go = null;
            _experience = null;
        }

        [Test]
        public void NewExperience_StartsAtLevelOne()
        {
            Assert.AreEqual(1, _experience.Level);
            Assert.AreEqual(0, _experience.Experience);
            Assert.AreEqual(1000, _experience.ExperienceToNextLevel);
            Assert.AreEqual(0f, _experience.Progress01);
        }

        [Test]
        public void AddExperience_AccumulatesBelowThreshold()
        {
            _experience.AddExperience(250);

            Assert.AreEqual(1, _experience.Level);
            Assert.AreEqual(250, _experience.Experience);
            Assert.AreEqual(0.25f, _experience.Progress01, 0.0001f);
        }

        [Test]
        public void AddExperience_LevelsUpAndKeepsRemainder()
        {
            _experience.AddExperience(1000);

            Assert.AreEqual(2, _experience.Level);
            Assert.AreEqual(0, _experience.Experience);
            Assert.AreEqual(Mathf.RoundToInt(1000f * 1.35f), _experience.ExperienceToNextLevel);
        }

        [Test]
        public void AddExperience_CanLevelUpSeveralTimes()
        {
            _experience.AddExperience(2500);

            Assert.AreEqual(3, _experience.Level);
            Assert.Greater(_experience.Experience, 0);
            Assert.Less(_experience.Experience, _experience.ExperienceToNextLevel);
        }

        [Test]
        public void AddExperience_IgnoresNonPositiveAmount()
        {
            _experience.AddExperience(0);
            _experience.AddExperience(-10);

            Assert.AreEqual(1, _experience.Level);
            Assert.AreEqual(0, _experience.Experience);
        }
    }
}
