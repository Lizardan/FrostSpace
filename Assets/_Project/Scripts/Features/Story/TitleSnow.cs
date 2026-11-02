using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Снег на чёрном титре: два слоя квадов, которые живут в одном меше.
    /// Один CanvasRenderer и одна перестройка меша в кадр дешевле шестидесяти
    /// объектов <see cref="Image"/> и читаются как крупа, а не как точки.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleSnow : MaskableGraphic
    {
        [Header("Слои")]
        [SerializeField, Min(0)] private int _farCount = 48;
        [SerializeField, Min(0)] private int _nearCount = 18;

        [Header("Размер, px")]
        [SerializeField] private float _farSize = 2.5f;
        [SerializeField] private float _nearSize = 6f;

        [Header("Скорость, px/с")]
        [SerializeField] private float _farSpeed = 22f;
        [SerializeField] private float _nearSpeed = 55f;

        [Tooltip("Боковой снос, px/с")]
        [SerializeField] private float _drift = 12f;

        [Header("Цвет")]
        [SerializeField] private Color _farColor = new(0.86f, 0.92f, 0.97f, 0.35f);
        [SerializeField] private Color _nearColor = new(0.93f, 0.97f, 1f, 0.55f);

        private Flake[] _flakes;

        private struct Flake
        {
            public Vector2 Position;
            public float Size;
            public float Speed;
            public float Phase;
            public Color Color;
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void Update()
        {
            var rect = rectTransform.rect;
            if (_flakes == null || rect.height <= 1f)
                return;

            var dt = Time.unscaledDeltaTime;
            var sway = _drift * dt / Mathf.Max(1f, rect.width);

            for (var i = 0; i < _flakes.Length; i++)
            {
                var flake = _flakes[i];
                flake.Position.y -= flake.Speed * dt / rect.height;

                if (flake.Position.y < -0.02f)
                {
                    flake.Position.y += 1.04f;
                    flake.Position.x = Random.value;
                }

                flake.Position.x += Mathf.Sin(Time.unscaledTime * 0.7f + flake.Phase) * sway;
                if (flake.Position.x < 0f)
                    flake.Position.x += 1f;
                else if (flake.Position.x > 1f)
                    flake.Position.x -= 1f;

                _flakes[i] = flake;
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var rect = GetPixelAdjustedRect();
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            EnsureFlakes();

            for (var i = 0; i < _flakes.Length; i++)
            {
                var flake = _flakes[i];
                var center = new Vector2(
                    rect.xMin + flake.Position.x * rect.width,
                    rect.yMin + flake.Position.y * rect.height);
                AddQuad(vh, center, flake.Size, flake.Color);
            }
        }

        private void EnsureFlakes()
        {
            if (_flakes != null)
                return;

            var far = Mathf.Clamp(_farCount, 0, 400);
            var near = Mathf.Clamp(_nearCount, 0, 200);
            _flakes = new Flake[far + near];

            for (var i = 0; i < _flakes.Length; i++)
            {
                var isNear = i >= far;
                _flakes[i] = new Flake
                {
                    Position = new Vector2(Random.value, Random.value),
                    Size = isNear ? _nearSize : _farSize,
                    Speed = isNear ? _nearSpeed : _farSpeed,
                    Phase = Random.value * Mathf.PI * 2f,
                    Color = isNear ? _nearColor : _farColor
                };
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 center, float size, Color color)
        {
            var half = Mathf.Max(0.5f, size) * 0.5f;
            var start = vh.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = new Vector3(center.x - half, center.y - half, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(center.x - half, center.y + half, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(center.x + half, center.y + half, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(center.x + half, center.y - half, 0f);
            vh.AddVert(vertex);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
