using System.Collections.Generic;
using FrostSpace.Features.Scan;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет чистую математику сканера и чистку имён для подписи.
    ///
    /// Сам сканер и HUD тестом не покрываются: они собирают материалы и Canvas и зависят
    /// от порядка вызовов Unity. Здесь только функции, которые решают, какая цель
    /// ближайшая, где проходят лучи прицела и как гаснет контур.
    /// </summary>
    public sealed class ScanMathTests
    {
        // ---------- ближайшая цель ----------

        [Test]
        public void NearestIndex_EmptyList_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, ScanMath.NearestIndex(Vector3.zero, new List<Vector3>(), 10f));
            Assert.AreEqual(-1, ScanMath.NearestIndex(Vector3.zero, null, 10f));
        }

        [Test]
        public void NearestIndex_PicksTheClosestInsideRadius()
        {
            var positions = new List<Vector3>
            {
                new(5f, 0f, 0f),
                new(2f, 0f, 0f),
                new(8f, 0f, 0f)
            };

            Assert.AreEqual(1, ScanMath.NearestIndex(Vector3.zero, positions, 10f));
        }

        [Test]
        public void NearestIndex_IgnoresTargetsOutsideRadius()
        {
            var positions = new List<Vector3> { new(30f, 0f, 0f), new(-25f, 0f, 0f) };

            Assert.AreEqual(-1, ScanMath.NearestIndex(Vector3.zero, positions, 20f));
        }

        [Test]
        public void NearestIndex_TargetOnRadiusEdgeCounts()
        {
            var positions = new List<Vector3> { new(20f, 0f, 0f) };

            Assert.AreEqual(0, ScanMath.NearestIndex(Vector3.zero, positions, 20f));
        }

        // ---------- сетка прицела ----------

        [Test]
        public void ViewportSample_SingleSampleSitsInTheCrosshair()
        {
            var point = ScanMath.ViewportSample(0, 0, 1, 0.14f);

            Assert.AreEqual(0.5f, point.x, 1e-5f);
            Assert.AreEqual(0.5f, point.y, 1e-5f);
        }

        [Test]
        public void ViewportSample_CornersLieOnTheSquareBorder()
        {
            var low = ScanMath.ViewportSample(0, 0, 5, 0.14f);
            var high = ScanMath.ViewportSample(4, 4, 5, 0.14f);

            Assert.AreEqual(0.36f, low.x, 1e-5f);
            Assert.AreEqual(0.36f, low.y, 1e-5f);
            Assert.AreEqual(0.64f, high.x, 1e-5f);
            Assert.AreEqual(0.64f, high.y, 1e-5f);
        }

        [Test]
        public void ViewportSample_CenterOfOddGridIsTheCrosshair()
        {
            var center = ScanMath.ViewportSample(2, 2, 5, 0.14f);

            Assert.AreEqual(0.5f, center.x, 1e-5f);
            Assert.AreEqual(0.5f, center.y, 1e-5f);
        }

        [Test]
        public void ViewportSample_ClampsOutOfRangeIndices()
        {
            var low = ScanMath.ViewportSample(-3, -3, 5, 0.14f);
            var high = ScanMath.ViewportSample(99, 99, 5, 0.14f);

            Assert.AreEqual(0.36f, low.x, 1e-5f);
            Assert.AreEqual(0.64f, high.y, 1e-5f);
        }

        // ---------- затухание ----------

        [Test]
        public void DistanceWeight_IsFullInsideFadeStartAndZeroAtRadius()
        {
            Assert.AreEqual(1f, ScanMath.DistanceWeight(0f, 20f, 14f), 1e-5f);
            Assert.AreEqual(1f, ScanMath.DistanceWeight(14f, 20f, 14f), 1e-5f);
            Assert.AreEqual(0f, ScanMath.DistanceWeight(20f, 20f, 14f), 1e-5f);
            Assert.AreEqual(0f, ScanMath.DistanceWeight(35f, 20f, 14f), 1e-5f);
        }

        [Test]
        public void DistanceWeight_FallsLinearlyBetweenEdges()
        {
            // Половина пути между 10 и 20 — половина веса.
            Assert.AreEqual(0.5f, ScanMath.DistanceWeight(15f, 20f, 10f), 1e-5f);
        }

        // ---------- сглаживание ----------

        [Test]
        public void Approach_ReachesTheTargetAndNeverOvershoots()
        {
            var value = 1f;
            for (var i = 0; i < 200; i++)
                value = ScanMath.Approach(value, 0f, 0.16f, 1f / 60f);

            Assert.Less(value, 0.001f);
            Assert.GreaterOrEqual(value, 0f);
        }

        [Test]
        public void Approach_ZeroDeltaTimeKeepsTheValue()
        {
            Assert.AreEqual(0.7f, ScanMath.Approach(0.7f, 0f, 0.16f, 0f), 1e-6f);
        }

        [Test]
        public void Approach_ZeroSecondsJumpsToTarget()
        {
            Assert.AreEqual(0f, ScanMath.Approach(0.7f, 0f, 0f, 1f / 60f), 1e-6f);
        }

        [Test]
        public void Advance_ReachesOneAndStops()
        {
            var progress = 0f;
            for (var i = 0; i < 100; i++)
                progress = ScanMath.Advance(progress, 0.6f, 1f / 60f);

            Assert.AreEqual(1f, progress, 1e-5f);
        }

        [Test]
        public void Advance_ZeroSecondsIsInstant()
        {
            Assert.AreEqual(1f, ScanMath.Advance(0f, 0f, 1f / 60f), 1e-6f);
        }

        // ---------- подпись сканера ----------

        [Test]
        public void Sanitize_RemovesSuffixAndUnderscores()
        {
            Assert.AreEqual("SNOW CABIN", ScanTarget.Sanitize("Snow_Cabin"));
            Assert.AreEqual("HOVERBIKE", ScanTarget.Sanitize("Hoverbike_Model"));
        }

        [Test]
        public void Sanitize_FallsBackToUnknownForEmptyNames()
        {
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize(null));
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize("   "));
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize("_Model"));
        }
    }
}
