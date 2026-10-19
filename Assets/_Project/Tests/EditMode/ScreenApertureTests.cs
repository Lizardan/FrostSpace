using FrostSpace.Features.View;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет математику экранного выреза: радиус в долях высоты и шаг
    /// пикселизации. Сам шейдер тестом не покрывается — его проверяет кадр.
    /// </summary>
    public sealed class ScreenApertureTests
    {
        [Test]
        public void RadiusFraction_MatchesPerspectiveProjection()
        {
            const float meters = 1.8f;
            const float depth = 20f;
            const float fov = 50f;

            var expected = meters / (2f * depth * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            var actual = ScreenAperture.RadiusFraction(meters, depth, fov);

            Assert.AreEqual(expected, actual, 1e-5f);
        }

        [Test]
        public void RadiusFraction_GrowsWhenCameraComesCloser()
        {
            var far = ScreenAperture.RadiusFraction(1.8f, 30f, 50f);
            var near = ScreenAperture.RadiusFraction(1.8f, 10f, 50f);

            Assert.Greater(near, far);
        }

        [Test]
        public void RadiusFraction_ReturnsZero_ForInvalidInput()
        {
            Assert.AreEqual(0f, ScreenAperture.RadiusFraction(0f, 20f, 50f));
            Assert.AreEqual(0f, ScreenAperture.RadiusFraction(1.8f, 0f, 50f));
            Assert.AreEqual(0f, ScreenAperture.RadiusFraction(1.8f, 20f, 0f));
        }

        [Test]
        public void RadiusFraction_ClampsToMaximum()
        {
            var actual = ScreenAperture.RadiusFraction(100f, 1f, 60f);

            Assert.AreEqual(ScreenAperture.MaxRadiusFraction, actual, 1e-4f);
        }

        [Test]
        public void PixelStep_IsBlockOverHeight()
        {
            Assert.AreEqual(5f / 1080f, ScreenAperture.PixelStep(1080, 5f), 1e-6f);
        }

        [Test]
        public void PixelStep_ReturnsZero_ForInvalidInput()
        {
            Assert.AreEqual(0f, ScreenAperture.PixelStep(0, 5f));
            Assert.AreEqual(0f, ScreenAperture.PixelStep(1080, 0f));
        }

        [Test]
        public void Aspect_HandlesZeroHeight()
        {
            Assert.AreEqual(1f, ScreenAperture.Aspect(1920, 0));
            Assert.AreEqual(16f / 9f, ScreenAperture.Aspect(1920, 1080), 1e-4f);
        }
    }
}
