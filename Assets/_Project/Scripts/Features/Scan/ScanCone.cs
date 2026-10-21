using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.Scan
{
    /// <summary>
    /// Голографический луч сканирования: квадратный объём, который выходит из головы робота
    /// и упирается в точку прицела. Показывает область, которую сканер реально проверяет.
    ///
    /// Геометрия строится кодом: квадратная пирамида с вершиной в нуле и сечением
    /// в единицу на z = 1. UV размечены так, что uv.x — доля периметра (рёбра квадрата
    /// стоят в углах при _spokes = 4), а uv.y — доля длины от головы к цели (квадратные
    /// сечения). Оформление — <c>FrostSpace/VFX/ScanCone</c>: квадратные сечения вдоль оси,
    /// ярче каждый второй сегмент, рёбра по углам и бегущий фронт скана.
    ///
    /// Луч не пишет глубину и тестируется по ней: светится поверх геометрии, но за стеной
    /// не виден — скан не читает объекты сквозь перекрытия.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanCone : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        private static readonly int RingsId = Shader.PropertyToID("_Rings");
        private static readonly int SpokesId = Shader.PropertyToID("_Spokes");
        private static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        private static readonly int BodyId = Shader.PropertyToID("_Body");
        private static readonly int SegmentId = Shader.PropertyToID("_Segment");
        private static readonly int AlphaMidId = Shader.PropertyToID("_AlphaMid");
        private static readonly int AlphaPowerId = Shader.PropertyToID("_AlphaPower");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Форма")]
        [Tooltip("Половина угла раствора луча, градусы")]
        [SerializeField] private float _halfAngle = 9f;
        [Tooltip("Короткая граница длины, м: вплотную к цели луч не схлопывается в точку")]
        [SerializeField] private float _minLength = 1.6f;
        [Tooltip("Длинная граница длины, м: совпадает с дальностью сканера")]
        [SerializeField] private float _maxLength = 40f;

        [Header("Оформление")]
        [Tooltip("Шейдер FrostSpace/VFX/ScanCone")]
        [SerializeField] private Shader _shader;
        [Tooltip("Цвет скана — циан цифрового слоя #4DE6FF")]
        [SerializeField] private Color _scanColor = new(0.302f, 0.902f, 1f, 1f);
        [Tooltip("Сколько квадратных сечений укладывается по длине луча")]
        [SerializeField] private float _rings = 10f;
        [Tooltip("Сколько рёбер по периметру: 4 ставит рёбра ровно в углы квадрата")]
        [SerializeField] private float _spokes = 4f;
        [Tooltip("Скорость бега сечений от головы к цели, сечений в секунду")]
        [SerializeField] private float _scroll = 0.45f;
        [Tooltip("Базовая яркость тела луча: выше 0.05 забивает сцену")]
        [SerializeField, Range(0f, 0.2f)] private float _body = 0.004f;
        [Tooltip("Добавка яркости каждому второму квадратному сегменту")]
        [SerializeField, Range(0f, 3f)] private float _segment = 0.18f;
        [Tooltip("Альфа в середине луча: у головы и у цели луч гаснет в ноль")]
        [SerializeField, Range(0f, 1f)] private float _alphaMid = 0.5f;
        [Tooltip("Степень профиля альфы: больше 1 — свечение жмётся к середине луча")]
        [SerializeField, Range(0.25f, 4f)] private float _alphaPower = 1f;
        [Tooltip("Общая яркость луча: аддитивное сложение быстро выжигает сцену")]
        [SerializeField, Range(0f, 3f)] private float _intensity = 0.75f;

        private Mesh _mesh;
        private Material _material;

        /// <summary>Луч виден с ненулевой яркостью.</summary>
        public bool IsVisible => gameObject.activeSelf;

        private void Awake()
        {
            if (_shader == null)
                _shader = Shader.Find("FrostSpace/VFX/ScanCone");

            if (_shader == null)
            {
                Debug.LogError("[ScanCone] не найден шейдер FrostSpace/VFX/ScanCone", this);
                enabled = false;
                return;
            }

            _mesh = CreateBeam();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            _material = new Material(_shader)
            {
                name = "ScanCone (runtime)",
                hideFlags = HideFlags.DontSave
            };
            _material.SetColor(ScanColorId, _scanColor);
            _material.SetFloat(RingsId, _rings);
            _material.SetFloat(SpokesId, _spokes);
            _material.SetFloat(ScrollId, _scroll);
            _material.SetFloat(BodyId, _body);
            _material.SetFloat(SegmentId, _segment);
            _material.SetFloat(AlphaMidId, _alphaMid);
            _material.SetFloat(AlphaPowerId, _alphaPower);
            _material.SetFloat(IntensityId, _intensity);
            renderer.sharedMaterial = _material;

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_mesh != null)
                Destroy(_mesh);
        }

        /// <summary>
        /// Ставит луч из <paramref name="origin"/> по направлению <paramref name="direction"/>
        /// длиной <paramref name="length"/>. Яркость и прогресс фронта идут теми же
        /// значениями, что у контура и полосы на самом объекте, поэтому скан читается
        /// как один процесс, а не как два независимых эффекта.
        /// </summary>
        public void SetState(
            bool visible, Vector3 origin, Vector3 direction, float length, float strength, float progress)
        {
            if (_material == null)
                return;

            if (!visible || strength <= 0.001f)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            length = Mathf.Clamp(length, _minLength, Mathf.Max(_minLength, _maxLength));
            var radius = Mathf.Tan(Mathf.Clamp(_halfAngle, 1f, 60f) * Mathf.Deg2Rad) * length;

            var forward = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward, Vector3.up));
            // Меш единичный: половина стороны квадрата 1 на z = 1, масштаб задаёт раствор и длину.
            transform.localScale = new Vector3(radius, radius, length);

            _material.SetFloat(StrengthId, Mathf.Clamp01(strength));
            _material.SetFloat(ProgressId, Mathf.Clamp01(progress));
        }

        /// <summary>
        /// Квадратная пирамида без основания: вершина в нуле, сечение — квадрат со стороной 2
        /// на z = 1. uv.x — доля периметра, uv.y — доля длины. Вершина разведена по вершинам
        /// с разными uv, поэтому параметр периметра не «съезжает» на грани.
        /// </summary>
        private static Mesh CreateBeam()
        {
            var corners = new[]
            {
                new Vector3(-1f, -1f, 1f),
                new Vector3(1f, -1f, 1f),
                new Vector3(1f, 1f, 1f),
                new Vector3(-1f, 1f, 1f)
            };

            var vertices = new List<Vector3>(16);
            var normals = new List<Vector3>(16);
            var uvs = new List<Vector2>(16);
            var triangles = new List<int>(24);

            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                var u0 = i / 4f;
                var u1 = (i + 1) / 4f;

                // Нормаль грани: нужна только френелю кромки, поэтому годится приближение.
                var normal = (a + b).normalized;

                var v = vertices.Count;
                vertices.Add(Vector3.zero);
                vertices.Add(Vector3.zero);
                vertices.Add(b);
                vertices.Add(a);

                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);

                uvs.Add(new Vector2(u0, 0f));
                uvs.Add(new Vector2(u1, 0f));
                uvs.Add(new Vector2(u1, 1f));
                uvs.Add(new Vector2(u0, 1f));

                triangles.Add(v);
                triangles.Add(v + 1);
                triangles.Add(v + 2);

                triangles.Add(v);
                triangles.Add(v + 2);
                triangles.Add(v + 3);
            }

            var mesh = new Mesh
            {
                name = "Scan Beam (runtime)",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            // Границы в локальных осях: масштаб по длине не должен отсекаться фрустумом.
            mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2f, 2f, 1.2f));
            return mesh;
        }
    }
}
