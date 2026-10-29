using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Северный фон экрана выбора корпуса: ночное небо, полосы сияния, звёзды,
    /// три слоя снежных хребтов и идущий снег.
    ///
    /// Всё рисуется кодом в одном Canvas под карточками: фон не тянет ни
    /// текстур из ассетов, ни лишних камер. Текстуры создаются один раз на
    /// экземпляр и уничтожаются вместе с экраном.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBackdrop : MonoBehaviour
    {
        private static readonly Color SkyTop = new(0.010f, 0.017f, 0.028f, 1f);
        private static readonly Color SkyMid = new(0.016f, 0.036f, 0.054f, 1f);
        private static readonly Color SkyHorizon = new(0.040f, 0.100f, 0.125f, 1f);
        private static readonly Color AuroraTeal = new(0.18f, 0.90f, 0.78f, 1f);
        private static readonly Color AuroraViolet = new(0.45f, 0.40f, 1.00f, 1f);
        private static readonly Color AuroraGreen = new(0.32f, 1.00f, 0.58f, 1f);

        private Texture2D _sky;
        private Texture2D _glow;
        private Texture2D _vignette;
        private Texture2D _star;
        private Texture2D _ridgeFar;
        private Texture2D _ridgeMid;
        private Texture2D _ridgeNear;
        private Texture2D _fog;

        /// <summary>Собирает фон в текущий объект: вызвать один раз до карточек.</summary>
        public void Build()
        {
            var root = (RectTransform)transform;

            _sky = BuildSkyTexture();
            NewRaw(root, "Sky", _sky, Color.white, Vector2.zero, Vector2.one);

            BuildAurora(root);
            BuildStars(root);

            _ridgeFar = BuildRidgeTexture(20261029, 0.62f, new Color(0.055f, 0.098f, 0.120f), 0.30f);
            _ridgeMid = BuildRidgeTexture(4711, 0.48f, new Color(0.038f, 0.072f, 0.090f), 0.22f);
            _ridgeNear = BuildRidgeTexture(90210, 0.32f, new Color(0.018f, 0.040f, 0.052f), 0.16f);

            NewRidge(root, "RidgeFar", _ridgeFar, 430f);

            _fog = BuildFogTexture();
            var fog = NewRaw(root, "Haze", _fog, Color.white, Vector2.zero, Vector2.zero);
            var fogRect = fog.rectTransform;
            fogRect.anchorMin = new Vector2(0f, 0f);
            fogRect.anchorMax = new Vector2(1f, 0f);
            fogRect.pivot = new Vector2(0.5f, 0f);
            fogRect.sizeDelta = new Vector2(0f, 280f);
            fogRect.anchoredPosition = new Vector2(0f, 210f);

            NewRidge(root, "RidgeMid", _ridgeMid, 330f);
            NewRidge(root, "RidgeNear", _ridgeNear, 245f);

            _vignette = BuildVignetteTexture();
            NewRaw(root, "Vignette", _vignette, Color.white, Vector2.zero, Vector2.one);

            var snowObject = new GameObject("Snow", typeof(RectTransform));
            snowObject.transform.SetParent(root, false);
            var snowRect = (RectTransform)snowObject.transform;
            snowRect.anchorMin = Vector2.zero;
            snowRect.anchorMax = Vector2.one;
            snowRect.offsetMin = Vector2.zero;
            snowRect.offsetMax = Vector2.zero;
            snowObject.AddComponent<UiSnowfall>().Build(90, 1.5f, 5f, 14f, 46f, 0.42f);
        }

        private void BuildAurora(Transform root)
        {
            _glow = BuildGlowTexture();

            var teal = NewRaw(root, "AuroraTeal", _glow,
                new Color(AuroraTeal.r, AuroraTeal.g, AuroraTeal.b, 0.22f),
                new Vector2(-0.1f, 0.55f), new Vector2(0.75f, 1.05f));
            teal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -9f);

            var violet = NewRaw(root, "AuroraViolet", _glow,
                new Color(AuroraViolet.r, AuroraViolet.g, AuroraViolet.b, 0.15f),
                new Vector2(0.35f, 0.62f), new Vector2(1.12f, 1.18f));
            violet.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 7f);

            var green = NewRaw(root, "AuroraGreen", _glow,
                new Color(AuroraGreen.r, AuroraGreen.g, AuroraGreen.b, 0.10f),
                new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.78f));
            green.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);
        }

        private void BuildStars(Transform root)
        {
            _star = BuildStarTexture();
            var random = new System.Random(20261029);

            for (var i = 0; i < 130; i++)
            {
                var star = NewRaw(root, "Star" + i, _star,
                    new Color(1f, 1f, 1f, Mathf.Lerp(0.12f, 0.65f, (float)random.NextDouble())),
                    Vector2.zero, Vector2.zero);

                var rect = star.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                var size = Mathf.Lerp(1.5f, 3.6f, (float)random.NextDouble());
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(
                    Mathf.Lerp(0f, 1920f, (float)random.NextDouble()),
                    -Mathf.Lerp(0f, 760f, (float)System.Math.Pow(random.NextDouble(), 1.4)));
            }
        }

        private void NewRidge(Transform root, string name, Texture2D texture, float height)
        {
            var image = NewRaw(root, name, texture, Color.white, Vector2.zero, Vector2.zero);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;
        }

        private static RawImage NewRaw(
            Transform parent, string name, Texture texture, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Texture2D BuildSkyTexture()
        {
            const int height = 256;
            var texture = NewTexture(1, height, "CharacterSky");

            for (var y = 0; y < height; y++)
            {
                var t = y / (float)(height - 1);
                var color = t < 0.62f
                    ? Color.Lerp(SkyTop, SkyMid, t / 0.62f)
                    : Color.Lerp(SkyMid, SkyHorizon, (t - 0.62f) / 0.38f);
                texture.SetPixel(0, y, color);
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D BuildGlowTexture()
        {
            const int size = 128;
            var texture = NewTexture(size, size, "CharacterAurora");

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = x / (float)(size - 1) * 2f - 1f;
                    var ny = y / (float)(size - 1) * 2f - 1f;
                    var radius = Mathf.Sqrt(nx * nx + ny * ny);
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 2.6f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D BuildStarTexture()
        {
            const int size = 8;
            var texture = NewTexture(size, size, "CharacterStar");

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = (x + 0.5f) / size * 2f - 1f;
                    var ny = (y + 0.5f) / size * 2f - 1f;
                    var radius = Mathf.Sqrt(nx * nx + ny * ny);
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 1.8f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Силуэт хребта: случайный сглаженный профиль, заливка ниже линии и
        /// светлая кромка сверху. Зерно фиксировано, чтобы фон не менялся
        /// между запусками.
        /// </summary>
        private static Texture2D BuildRidgeTexture(int seed, float baseHeight, Color color, float rim)
        {
            const int width = 512;
            const int height = 256;
            var texture = NewTexture(width, height, "CharacterRidge" + seed);

            var random = new System.Random(seed);
            var profile = new float[width];
            var level = baseHeight;
            for (var x = 0; x < width; x++)
            {
                level += ((float)random.NextDouble() - 0.5f) * 0.09f;
                level = Mathf.Clamp(level, 0.18f, 0.92f);
                profile[x] = level;
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var smoothed = new float[width];
                for (var x = 0; x < width; x++)
                {
                    var left = profile[Mathf.Max(0, x - 1)];
                    var right = profile[Mathf.Min(width - 1, x + 1)];
                    smoothed[x] = (left + profile[x] * 2f + right) * 0.25f;
                }

                profile = smoothed;
            }

            var rimColor = Color.Lerp(color, Color.white, rim);
            for (var x = 0; x < width; x++)
            {
                var top = Mathf.RoundToInt(profile[x] * (height - 1));
                for (var y = 0; y < height; y++)
                {
                    var pixel = y <= top
                        ? (y >= top - 1 ? rimColor : color)
                        : new Color(0f, 0f, 0f, 0f);
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D BuildFogTexture()
        {
            const int height = 128;
            var texture = NewTexture(1, height, "CharacterHaze");

            for (var y = 0; y < height; y++)
            {
                var t = y / (float)(height - 1);
                var alpha = Mathf.Lerp(0.20f, 0f, t * t);
                texture.SetPixel(0, y, new Color(AuroraTeal.r, AuroraTeal.g, AuroraTeal.b, alpha));
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D BuildVignetteTexture()
        {
            const int size = 256;
            var texture = NewTexture(size, size, "CharacterVignette");

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = x / (float)(size - 1) * 2f - 1f;
                    var ny = y / (float)(size - 1) * 2f - 1f;
                    var radius = Mathf.Sqrt(nx * nx + ny * ny);
                    var alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, radius)) * 0.55f;
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private void OnDestroy()
        {
            DestroyTexture(_sky);
            DestroyTexture(_glow);
            DestroyTexture(_vignette);
            DestroyTexture(_star);
            DestroyTexture(_ridgeFar);
            DestroyTexture(_ridgeMid);
            DestroyTexture(_ridgeNear);
            DestroyTexture(_fog);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
