using FrostSpace.Features.View;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет переход между позами камеры и границы объёма интерьера.
    ///
    /// Сам риг и регистрация зоны тестом не покрываются: они живут в сцене и зависят
    /// от порядка вызовов Unity. Здесь только чистая математика <see cref="ViewBlend"/>
    /// и <see cref="InteriorZone.ContainsPoint"/>.
    /// </summary>
    public sealed class CameraModeTests
    {
        // ---------- ViewBlend ----------

        [Test]
        public void Progress_ClampsBetweenZeroAndOne()
        {
            Assert.AreEqual(0f, ViewBlend.Progress(0f, 0.22f));
            Assert.AreEqual(0.5f, ViewBlend.Progress(0.11f, 0.22f), 1e-4f);
            Assert.AreEqual(1f, ViewBlend.Progress(1f, 0.22f));
            Assert.AreEqual(0f, ViewBlend.Progress(-1f, 0.22f));
        }

        [Test]
        public void Progress_ZeroDuration_IsImmediate()
        {
            Assert.AreEqual(1f, ViewBlend.Progress(0f, 0f));
            Assert.AreEqual(1f, ViewBlend.Progress(0.1f, 0.01f));
        }

        [Test]
        public void Smooth_HasFlatEndsAndMidPoint()
        {
            Assert.AreEqual(0f, ViewBlend.Smooth(0f), 1e-6f);
            Assert.AreEqual(0.5f, ViewBlend.Smooth(0.5f), 1e-6f);
            Assert.AreEqual(1f, ViewBlend.Smooth(1f), 1e-6f);
        }

        [Test]
        public void Smooth_ClampsOutsideRange()
        {
            Assert.AreEqual(0f, ViewBlend.Smooth(-3f), 1e-6f);
            Assert.AreEqual(1f, ViewBlend.Smooth(4f), 1e-6f);
        }

        [Test]
        public void Lerp_StartsOnFromPose_AndEndsOnTarget()
        {
            var from = new CameraPose(new Vector3(0f, 11f, -9f), Quaternion.Euler(48f, 0f, 0f));
            var to = new CameraPose(new Vector3(0f, 1.6f, 0.08f), Quaternion.Euler(10f, 0f, 0f));

            var start = ViewBlend.Lerp(from, to, 0f);
            var finish = ViewBlend.Lerp(from, to, 1f);

            Assert.Less(Vector3.Distance(from.Position, start.Position), 1e-4f);
            Assert.Less(Quaternion.Angle(from.Rotation, start.Rotation), 0.01f);
            Assert.Less(Vector3.Distance(to.Position, finish.Position), 1e-4f);
            Assert.Less(Quaternion.Angle(to.Rotation, finish.Rotation), 0.01f);
        }

        [Test]
        public void Lerp_MidwayPoseLiesBetweenTheEndpoints()
        {
            var from = new CameraPose(Vector3.zero, Quaternion.identity);
            var to = new CameraPose(new Vector3(0f, 10f, -10f), Quaternion.Euler(0f, 88f, 0f));

            var middle = ViewBlend.Lerp(from, to, 0.5f);

            Assert.AreEqual(0f, middle.Position.x, 1e-5f);
            Assert.AreEqual(5f, middle.Position.y, 1e-5f);
            Assert.AreEqual(-5f, middle.Position.z, 1e-5f);
            Assert.AreEqual(44f, middle.Rotation.eulerAngles.y, 1e-2f);
        }

        // ---------- InteriorZone ----------

        [Test]
        public void InteriorZone_AcceptsPointInsideTheBox()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));

            Assert.IsTrue(InteriorZone.ContainsPoint(bounds, 0.15f, Vector3.zero));
            Assert.IsTrue(InteriorZone.ContainsPoint(bounds, 0.15f, new Vector3(1f, 1f, 1f)));
        }

        [Test]
        public void InteriorZone_RejectsPointOutsideTheBox()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));

            Assert.IsFalse(InteriorZone.ContainsPoint(bounds, 0.15f, new Vector3(5f, 0f, 0f)));
            Assert.IsFalse(InteriorZone.ContainsPoint(bounds, 0.15f, new Vector3(0f, -3f, 0f)));
        }

        [Test]
        public void InteriorZone_ShrinksEdgesSoTheModeDoesNotFlicker()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));

            // 1.9 внутри исходных границ (половина 2.0), но за вычетом запаса 0.15 уже снаружи.
            Assert.IsFalse(InteriorZone.ContainsPoint(bounds, 0.15f, new Vector3(1.9f, 0f, 0f)));
            Assert.IsTrue(InteriorZone.ContainsPoint(bounds, 0.15f, new Vector3(1.7f, 0f, 0f)));
        }

        [Test]
        public void InteriorZone_ZeroShrinkUsesRawBounds()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));

            Assert.IsTrue(InteriorZone.ContainsPoint(bounds, 0f, new Vector3(1.9f, 0f, 0f)));
        }

        [Test]
        public void InteriorZone_WithoutAnyRegisteredZone_ReturnsFalse()
        {
            Assert.IsFalse(InteriorZone.ContainsAny(Vector3.zero));
        }
    }
}
