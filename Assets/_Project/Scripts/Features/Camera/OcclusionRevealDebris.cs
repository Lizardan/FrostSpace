using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Кубы-осколки, которые вылетают из растворяемой поверхности.
    ///
    /// Обычные меши через <see cref="Graphics.RenderMesh"/>, а не VFX Graph и не
    /// compute: проект собирается в WebGL, где ни то, ни другое не работает.
    /// Пул фиксированного размера, в кадре не аллоцируется.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class OcclusionRevealDebris : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Вид")]
        [Tooltip("Шейдер FrostSpace/VFX/OcclusionDebris")]
        [SerializeField] private Shader _shader;
        [SerializeField] private Color _color = new(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private float _intensity = 2.5f;
        [SerializeField, Range(1, 32)] private int _maxCubes = 28;

        [Header("Движение")]
        [SerializeField] private Vector2 _sizeRange = new(0.08f, 0.16f);
        [SerializeField] private float _speed = 2.2f;
        [Tooltip("Разброс направления от нормали поверхности")]
        [SerializeField] private float _spread = 0.9f;
        [SerializeField] private float _gravity = -3.2f;
        [SerializeField] private float _drag = 1.6f;
        [SerializeField] private Vector2 _lifeRange = new(0.35f, 0.65f);

        private Mesh _mesh;
        private Material _material;
        private int _capacity;

        private Vector3[] _positions;
        private Vector3[] _velocities;
        private Quaternion[] _rotations;
        private Quaternion[] _spawnRotations;
        private Vector3[] _angular;
        private float[] _ages;
        private float[] _lives;
        private float[] _sizes;
        private int _next;
        private bool _warnedNoShader;

        private void Awake()
        {
            EnsureReady();
        }

        /// <summary>Всплеск осколков в точке попадания луча.</summary>
        public void Burst(Vector3 point, Vector3 normal, int count)
        {
            EnsureReady();
            if (_material == null)
                return;

            for (var i = 0; i < count; i++)
                Spawn(point, normal);
        }

        private void EnsureReady()
        {
            if (_mesh == null)
            {
                _mesh = BuildCubeMesh();
                _capacity = Mathf.Clamp(_maxCubes, 1, 32);

                _positions = new Vector3[_capacity];
                _velocities = new Vector3[_capacity];
                _rotations = new Quaternion[_capacity];
                _spawnRotations = new Quaternion[_capacity];
                _angular = new Vector3[_capacity];
                _ages = new float[_capacity];
                _lives = new float[_capacity];
                _sizes = new float[_capacity];
                for (var i = 0; i < _capacity; i++)
                    _ages[i] = float.MaxValue;
            }

            // Шейдер может быть назначен уже после Awake (сцена/инспектор),
            // поэтому материал создаём повторно, а не только один раз.
            if (_material == null && _shader != null)
            {
                _material = new Material(_shader) { name = "OcclusionDebris (runtime)" };
                _material.SetColor(ColorId, _color);
                _material.SetFloat(IntensityId, _intensity);
            }

            if (_material == null && !_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "OcclusionRevealDebris: не назначен шейдер FrostSpace/VFX/OcclusionDebris",
                    this);
            }
        }

        private void Spawn(Vector3 point, Vector3 normal)
        {
            var index = _next;
            _next = (_next + 1) % _capacity;

            _positions[index] = point + Random.insideUnitSphere * 0.08f;

            var direction = normal.normalized
                            + Random.insideUnitSphere * _spread
                            + Vector3.up * 0.35f;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.up;

            _velocities[index] = direction.normalized
                                 * Random.Range(_speed * 0.6f, _speed * 1.3f);
            _sizes[index] = Random.Range(_sizeRange.x, _sizeRange.y);
            _lives[index] = Random.Range(_lifeRange.x, _lifeRange.y);
            _ages[index] = 0f;
            _spawnRotations[index] = Random.rotation;
            _rotations[index] = _spawnRotations[index];
            _angular[index] = Random.insideUnitSphere * 240f;
        }

        private void LateUpdate()
        {
            if (_material == null)
                return;

            var dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            for (var i = 0; i < _capacity; i++)
            {
                if (_ages[i] >= _lives[i])
                    continue;

                _ages[i] += dt;
                if (_ages[i] >= _lives[i])
                    continue;

                var velocity = _velocities[i];
                velocity += Vector3.up * (_gravity * dt);
                velocity *= Mathf.Max(0f, 1f - _drag * dt);
                _velocities[i] = velocity;
                _positions[i] += velocity * dt;

                // Вращение считается от исходной ориентации и возраста, а не
                // накоплением: иначе кватернион уходит от единичной длины
                // и Unity пишет «Quaternion To Matrix conversion failed».
                var spin = _angular[i] * _ages[i];
                _rotations[i] = Quaternion.Euler(spin) * _spawnRotations[i];

                var t = _ages[i] / _lives[i];
                var scale = _sizes[i] * (1f - t * t);
                if (scale <= 0.0001f)
                    continue;

                var matrix = Matrix4x4.TRS(_positions[i], _rotations[i], Vector3.one * scale);
                var bounds = new Bounds(_positions[i], Vector3.one * (scale * 2f + 0.5f));
                Graphics.RenderMesh(
                    new RenderParams(_material)
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                        worldBounds = bounds
                    },
                    _mesh,
                    0,
                    matrix);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_mesh != null)
                Destroy(_mesh);
        }

        /// <summary>Куб с плоскими нормалями: 6 граней по 4 вершины.</summary>
        private static Mesh BuildCubeMesh()
        {
            var mesh = new Mesh { name = "OcclusionDebrisCube" };
            mesh.SetVertices(new[]
            {
                // +X
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                // -X
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                // +Y
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                // -Y
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                // +Z
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                // -Z
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f)
            });
            mesh.SetNormals(new[]
            {
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            });

            var indices = new int[36];
            for (var face = 0; face < 6; face++)
            {
                var v = face * 4;
                var t = face * 6;
                indices[t] = v;
                indices[t + 1] = v + 1;
                indices[t + 2] = v + 2;
                indices[t + 3] = v;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;
            }

            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
