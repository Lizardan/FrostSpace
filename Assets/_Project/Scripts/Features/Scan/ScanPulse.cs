using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.Scan
{
    /// <summary>
    /// Кольцо сонара: плоский круг на земле под игроком, от центра к границе радиуса
    /// расходится светящаяся кромка. Живёт, пока включён режим сканирования по Q.
    ///
    /// Геометрия — обычный квад, а кольцо рисует шейдер <c>FrostSpace/VFX/ScanPulse</c>
    /// по UV: радиус точки считается от центра квада, поэтому кольцо остаётся круглым
    /// при любом масштабе и не требует ни меша-аннулуса, ни сегментов. Квад лежит в
    /// плоскости XZ и поднят над землёй на <see cref="_groundOffset"/>: без подъёма он
    /// конфликтует по глубине со снегом и мигает полосами.
    ///
    /// Глубина не пишется, но тестируется: кольцо светится поверх геометрии и не видно
    /// сквозь стены и склоны.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScanPulse : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");

        [Header("Оформление")]
        [Tooltip("Шейдер FrostSpace/VFX/ScanPulse")]
        [SerializeField] private Shader _shader;
        [Tooltip("Цвет кольца — циан цифрового слоя #4DE6FF")]
        [SerializeField] private Color _scanColor = new(0.302f, 0.902f, 1f, 1f);
        [Tooltip("Толщина кольца в долях радиуса: 0.06 — тонкая кромка")]
        [SerializeField, Range(0.005f, 0.4f)] private float _thickness = 0.06f;
        [Tooltip("Мягкость внешнего края кольца, доля толщины")]
        [SerializeField, Range(0.05f, 1f)] private float _softness = 0.6f;

        [Header("Посадка")]
        [Tooltip("Насколько кольцо поднято над точкой игрока, м")]
        [SerializeField] private float _groundOffset = 0.07f;

        private Mesh _mesh;
        private Material _material;

        private void Awake()
        {
            if (_shader == null)
                _shader = Shader.Find("FrostSpace/VFX/ScanPulse");

            if (_shader == null)
            {
                Debug.LogError("[ScanPulse] не найден шейдер FrostSpace/VFX/ScanPulse", this);
                enabled = false;
                return;
            }

            _mesh = CreateQuad();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            _material = new Material(_shader)
            {
                name = "ScanPulse (runtime)",
                hideFlags = HideFlags.DontSave
            };
            _material.SetColor(ScanColorId, _scanColor);
            _material.SetFloat(ThicknessId, _thickness);
            _material.SetFloat(SoftnessId, _softness);
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
        /// Ставит кольцо под игроком. <paramref name="progress"/> 0..1 — доля радиуса,
        /// до которой дошла кромка; <paramref name="radius"/> — радиус сонара в метрах.
        /// </summary>
        public void SetState(bool visible, Vector3 origin, float radius, float progress, float strength)
        {
            if (_material == null)
                return;

            if (!visible || strength <= 0.001f || radius <= 0.01f)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            // Квад лежит в плоскости XZ: поворот на 90° вокруг X переводит локальные XY
            // в мировые XZ, а масштаб задаёт диаметр кольца.
            transform.SetPositionAndRotation(
                origin + Vector3.up * _groundOffset,
                Quaternion.Euler(90f, 0f, 0f));
            transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            _material.SetFloat(ProgressId, Mathf.Clamp01(progress));
            _material.SetFloat(StrengthId, Mathf.Clamp01(strength));
        }

        /// <summary>Единичный квад с UV 0..1: шейдер считает радиус от его центра.</summary>
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh
            {
                name = "Scan Pulse (runtime)",
                hideFlags = HideFlags.DontSave
            };

            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;
        }
    }
}
