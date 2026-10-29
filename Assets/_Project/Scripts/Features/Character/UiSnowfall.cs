using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Снег на UI-фоне: точки с разной скоростью и размером падают сверху и
    /// возвращаются наверх. Размер и скорость задают глубину — медленные крупные
    /// хлопья читаются как ближний план.
    ///
    /// Одна общая текстура на все точки; <see cref="Update"/> не создаёт мусора.
    /// Считается в координатах 1920×1080, как и весь экран.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSnowfall : MonoBehaviour
    {
        private const float Width = 1920f;
        private const float Height = 1080f;
        private const float WrapMargin = 24f;

        private Texture2D _dot;
        private RectTransform[] _flakes;
        private float[] _speed;
        private float[] _baseX;
        private float[] _phase;
        private float[] _amplitude;
        private float[] _y;
        private float _elapsed;

        public void Build(int count, float minSize, float maxSize, float minSpeed, float maxSpeed, float opacity)
        {
            _dot = BuildDotTexture();

            _flakes = new RectTransform[count];
            _speed = new float[count];
            _baseX = new float[count];
            _phase = new float[count];
            _amplitude = new float[count];
            _y = new float[count];

            var random = new System.Random(77031);
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject("Flake" + i, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                var size = Mathf.Lerp(minSize, maxSize, (float)random.NextDouble());
                rect.sizeDelta = new Vector2(size, size);

                var image = go.AddComponent<RawImage>();
                image.texture = _dot;
                image.raycastTarget = false;
                image.color = new Color(1f, 1f, 1f, opacity * Mathf.Lerp(0.45f, 1f, (float)random.NextDouble()));

                _flakes[i] = rect;
                _speed[i] = Mathf.Lerp(minSpeed, maxSpeed, (float)random.NextDouble());
                _baseX[i] = Mathf.Lerp(-WrapMargin, Width + WrapMargin, (float)random.NextDouble());
                _phase[i] = (float)random.NextDouble() * Mathf.PI * 2f;
                _amplitude[i] = Mathf.Lerp(6f, 22f, (float)random.NextDouble());
                _y[i] = Mathf.Lerp(-WrapMargin, Height + WrapMargin, (float)random.NextDouble());

                rect.anchoredPosition = new Vector2(_baseX[i], -_y[i]);
            }
        }

        private void Update()
        {
            if (_flakes == null)
                return;

            var delta = Time.unscaledDeltaTime;
            _elapsed += delta;

            for (var i = 0; i < _flakes.Length; i++)
            {
                _y[i] -= _speed[i] * delta;
                if (_y[i] < -WrapMargin)
                    _y[i] = Height + WrapMargin;

                var x = _baseX[i] + Mathf.Sin(_elapsed * 0.5f + _phase[i]) * _amplitude[i];
                _flakes[i].anchoredPosition = new Vector2(x, -_y[i]);
            }
        }

        private static Texture2D BuildDotTexture()
        {
            const int size = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CharacterSnowflake",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = (x + 0.5f) / size * 2f - 1f;
                    var ny = (y + 0.5f) / size * 2f - 1f;
                    var radius = Mathf.Sqrt(nx * nx + ny * ny);
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 1.9f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (_dot != null)
                Destroy(_dot);
        }
    }
}
