using FrostSpace.Features.Story;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Авария в конце рейса проверяется числами там, где числа вообще есть: тряска
    /// должна начинаться и кончаться нулём, не выходить за бюджет и не превращаться
    /// в постоянное качание. Сам кадр проверяется живым прогоном — тест его не заменит.
    /// </summary>
    public sealed class TrainCrashTests
    {
        [Test]
        public void ShakeIsZeroOutsideTheImpact()
        {
            Assert.AreEqual(Vector3.zero, TrainCrashRig.ShakeOffset(0f),
                "до удара камера обязана стоять ровно");
            Assert.AreEqual(Vector3.zero, TrainCrashRig.ShakeOffset(1f),
                "после удара тряска обязана кончиться: иначе занавес закрывает дрожащий кадр");
            Assert.AreEqual(0f, TrainCrashRig.ShakeRoll(0f), 1e-6f);
            Assert.AreEqual(0f, TrainCrashRig.ShakeRoll(1f), 1e-6f);
            Assert.AreEqual(Vector3.zero, TrainCrashRig.ShakeOffset(-0.5f));
            Assert.AreEqual(Vector3.zero, TrainCrashRig.ShakeOffset(2f));
        }

        [Test]
        public void ShakePeaksNearTheImpactAndStaysWithinBudget()
        {
            var peak = 0f;
            for (var i = 0; i <= 200; i++)
            {
                var t = i / 200f;
                var magnitude = TrainCrashRig.ShakeOffset(t).magnitude;
                peak = Mathf.Max(peak, magnitude);

                Assert.LessOrEqual(magnitude, 1.01f,
                    "смещение тряски не должно выходить за единичную амплитуду");
                Assert.LessOrEqual(Mathf.Abs(TrainCrashRig.ShakeRoll(t)), 1.01f,
                    "крен камеры не должен выходить за единичную амплитуду");
            }

            Assert.Greater(peak, 0.2f, "тряска должна читаться, а не быть пустой");
        }

        [Test]
        public void ShakeDecaysToZero()
        {
            var early = TrainCrashRig.ShakeOffset(0.1f).magnitude;
            var late = TrainCrashRig.ShakeOffset(0.9f).magnitude;
            Assert.Greater(early, late,
                "к концу удара тряска обязана затухать, а не усиливаться");
        }

        [Test]
        public void CrashTimelineFitsTheFinale()
        {
            var total = TrainCrashRig.BrakeSeconds + TrainCrashRig.ImpactSeconds +
                        TrainCrashRig.SettleSeconds + TrainCrashRig.QuietSeconds;

            Assert.Greater(TrainCrashRig.BrakeSeconds, 0f);
            Assert.Greater(TrainCrashRig.ImpactSeconds, 0f);
            Assert.Greater(TrainCrashRig.SettleSeconds, 0f);
            Assert.Greater(TrainCrashRig.QuietSeconds, 0f);
            Assert.Less(total, 6f,
                "авария не должна превращаться в отдельную сцену: финал короче шести секунд");
        }

        [Test]
        public void EaseStartsAtZeroAndSettlesAtOne()
        {
            Assert.AreEqual(0f, TrainCrashRig.Ease(0f), 1e-6f);
            Assert.AreEqual(1f, TrainCrashRig.Ease(1f), 1e-6f);
            Assert.Greater(TrainCrashRig.Ease(0.5f), 0.5f,
                "просадка корпуса обязана начинаться резко, а не линейно");
        }
    }
}
