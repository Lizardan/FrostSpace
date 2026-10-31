using FrostSpace.Features.Interior;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Логика автодвери кабины: гистерезис, шаг прогресса и запрет закрытия
    /// на стоящем в проёме. Сам компонент проверяется только в сцене.
    /// </summary>
    public sealed class CabinDoorTests
    {
        [Test]
        public void WantsOpen_Hysteresis_OpensAndClosesAtDifferentDistances()
        {
            // Закрытая открывается на 3,2 м и ближе.
            Assert.IsTrue(CabinDoor.WantsOpen(3.0f, false, 3.2f, 4.2f));
            Assert.IsFalse(CabinDoor.WantsOpen(3.5f, false, 3.2f, 4.2f));

            // Открытая держится до 4,2 м: между порогами состояние не мигает.
            Assert.IsTrue(CabinDoor.WantsOpen(3.9f, true, 3.2f, 4.2f));
            Assert.IsFalse(CabinDoor.WantsOpen(4.3f, true, 3.2f, 4.2f));
        }

        [Test]
        public void Step_ReachesTargetAndClamps()
        {
            Assert.AreEqual(1f, CabinDoor.Step(0f, 1f, 10f, 0.8f));
            Assert.AreEqual(0f, CabinDoor.Step(0.5f, 0f, 10f, 0.8f));

            var half = CabinDoor.Step(0f, 1f, 0.4f, 0.8f);
            Assert.That(half, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void IsInAperture_TrueOnlyInsideOpening()
        {
            Assert.IsTrue(CabinDoor.IsInAperture(
                new Vector3(0f, 0f, 11.9f), 2.4f, 0.6f, 11.9f));
            Assert.IsFalse(CabinDoor.IsInAperture(
                new Vector3(0f, 0f, 9f), 2.4f, 0.6f, 11.9f));
            Assert.IsFalse(CabinDoor.IsInAperture(
                new Vector3(3f, 0f, 11.9f), 2.4f, 0.6f, 11.9f));
        }
    }
}
