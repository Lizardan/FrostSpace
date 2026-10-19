using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Экранная геометрия кругового выреза: радиус в долях высоты экрана и шаг
    /// пикселизации. Чистые функции без состояния, чтобы математику можно было
    /// проверить EditMode-тестом.
    /// </summary>
    public static class ScreenAperture
    {
        public const float MinRadiusFraction = 0.005f;
        public const float MaxRadiusFraction = 0.75f;

        /// <summary>
        /// Радиус выреза в долях высоты экрана. Радиус задан в метрах на глубине
        /// персонажа и переводится через вертикальный FOV камеры.
        /// </summary>
        public static float RadiusFraction(
            float radiusMeters, float eyeDepth, float verticalFovDegrees)
        {
            if (radiusMeters <= 0f || eyeDepth <= 0.01f || verticalFovDegrees <= 0f)
                return 0f;

            var halfHeight = eyeDepth * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            if (halfHeight <= 1e-4f)
                return 0f;

            return Mathf.Clamp(radiusMeters / (2f * halfHeight), MinRadiusFraction, MaxRadiusFraction);
        }

        /// <summary>Шаг пикселизации в UV: размер блока в пикселях на высоту экрана.</summary>
        public static float PixelStep(int screenHeight, float pixelBlock)
        {
            if (screenHeight <= 0 || pixelBlock <= 0f)
                return 0f;

            return pixelBlock / screenHeight;
        }

        public static float Aspect(int screenWidth, int screenHeight)
        {
            return screenHeight <= 0 ? 1f : Mathf.Max(0.01f, (float)screenWidth / screenHeight);
        }
    }
}
