using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// X-ray силуэт игрока: пока геометрия закрывает робота, его видно сквозь неё.
    ///
    /// Второй <see cref="SkinnedMeshRenderer"/> создаётся в <c>Awake</c> как дочерний
    /// объект модели и получает тот же меш, те же кости и те же веса blend-shape —
    /// поэтому он повторяет позу кадра без второй анимации. Материал —
    /// <c>FrostSpace/VFX/OcclusionSilhouette</c> с <c>ZTest Greater</c>: рисуются только
    /// те пиксели, что оказались за уже нарисованной геометрией. Перекрытие при этом
    /// остаётся целым, дырок в крыше и стенах нет.
    ///
    /// Силуэт включается лучами камеры в тело игрока (как раньше делал вырез), плавно
    /// появляется, держится <c>_holdSeconds</c> и так же плавно уходит.
    ///
    /// Компонент — только для локального игрока: в мультиплеере у чужих моделей он
    /// выключен (<c>SetLocalPlayer(false)</c>), поэтому ничего не исчезает и не
    /// подсвечивается у других.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class PlayerSilhouette : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Ссылки")]
        [Tooltip("Камера, из которой проверяются перекрытия")]
        [SerializeField] private Camera _view;
        [Tooltip("Корень игрока: вокруг него берутся пробные точки")]
        [SerializeField] private Transform _target;
        [Tooltip("Риг камеры: в первом лице силуэт не нужен")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Исходный рендерер модели; если пусто — берётся дочерний")]
        [SerializeField] private SkinnedMeshRenderer _source;
        [Tooltip("Шейдер FrostSpace/VFX/OcclusionSilhouette")]
        [SerializeField] private Shader _shader;

        [Header("Вид силуэта")]
        [SerializeField] private Color _color = new(0.3f, 0.9f, 1f, 0.55f);
        [SerializeField] private float _intensity = 1.6f;
        [Tooltip("Точка на игроке, вокруг которой берутся пробы")]
        [SerializeField] private Vector3 _targetOffset = new(0f, 0.95f, 0f);
        [Tooltip("Разброс пробных лучей по телу игрока, м")]
        [SerializeField] private float _bodyRadius = 0.45f;

        [Header("Что считается перекрытием")]
        [Tooltip("Слои перекрытий (слой Player исключить)")]
        [SerializeField] private LayerMask _occluders = ~0;

        [Header("Переход")]
        [Tooltip("Время появления и возврата, с")]
        [SerializeField] private float _fadeSeconds = 0.16f;
        [Tooltip("Пауза перед возвратом, с")]
        [SerializeField] private float _holdSeconds = 0.1f;

        [Header("Локальность")]
        [Tooltip("Силуэт рисуется только для локального игрока: у чужих моделей его быть не должно")]
        [SerializeField] private bool _isLocalPlayer = true;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Vector3[] _points = new Vector3[5];
        private SkinnedMeshRenderer _ghost;
        private Material _material;
        private float _strength;
        private float _holdUntil;
        private bool _logged;
        private bool _warned;

        /// <summary>Локальный ли это игрок: у чужих моделей силуэт не создаётся.</summary>
        public bool IsLocalPlayer => _isLocalPlayer;

        /// <summary>
        /// Пометка владельца модели. Вызывается слоем сети: у удалённого игрока силуэт
        /// выключается и не тратит второй проход скиннинга.
        /// </summary>
        public void SetLocalPlayer(bool isLocal)
        {
            _isLocalPlayer = isLocal;
            if (!isLocal)
                Disable();
        }

        private void Awake()
        {
            if (_view == null)
                _view = Camera.main;
            if (_target == null)
                _target = transform;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_source == null)
                _source = GetComponentInChildren<SkinnedMeshRenderer>();

            if (!_isLocalPlayer)
            {
                Disable();
                return;
            }

            BuildGhost();
        }

        private void BuildGhost()
        {
            if (_source == null || _source.sharedMesh == null)
            {
                Warn("не найден SkinnedMeshRenderer модели");
                return;
            }

            var shader = _shader != null ? _shader : Shader.Find("FrostSpace/VFX/OcclusionSilhouette");
            if (shader == null)
            {
                Warn("не найден шейдер FrostSpace/VFX/OcclusionSilhouette");
                return;
            }

            _material = new Material(shader) { name = "PlayerSilhouette (runtime)" };
            _material.SetColor(ColorId, _color);
            _material.SetFloat(IntensityId, _intensity);
            _material.renderQueue = (int)RenderQueue.Transparent + 50;

            var host = new GameObject("PlayerSilhouette");
            host.transform.SetParent(_source.transform, false);

            _ghost = host.AddComponent<SkinnedMeshRenderer>();
            _ghost.sharedMesh = _source.sharedMesh;
            _ghost.bones = _source.bones;
            _ghost.rootBone = _source.rootBone;
            _ghost.localBounds = _source.localBounds;
            _ghost.updateWhenOffscreen = false;
            _ghost.shadowCastingMode = ShadowCastingMode.Off;
            _ghost.receiveShadows = false;
            _ghost.lightProbeUsage = LightProbeUsage.Off;
            _ghost.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _ghost.sharedMaterial = _material;
            _ghost.enabled = false;

            SyncBlendShapes();
        }

        private void LateUpdate()
        {
            if (_ghost == null || _material == null || _view == null || _target == null)
                return;

            SyncBlendShapes();

            if (WantsSilhouette() && IsBlocked())
                _holdUntil = Time.time + _holdSeconds;

            var wanted = _holdUntil > Time.time ? 1f : 0f;
            var step = Time.deltaTime / Mathf.Max(0.02f, _fadeSeconds);
            _strength = Mathf.MoveTowards(_strength, wanted, step);

            if (_strength > 0.001f)
            {
                if (!_ghost.enabled)
                    _ghost.enabled = true;

                var color = _color;
                color.a *= _strength;
                _material.SetColor(ColorId, color);

                if (!_logged)
                {
                    _logged = true;
                    Debug.Log("[PlayerSilhouette] силуэт показан", this);
                }
            }
            else if (_ghost.enabled)
            {
                _ghost.enabled = false;
                if (_logged)
                {
                    _logged = false;
                    Debug.Log("[PlayerSilhouette] силуэт скрыт", this);
                }
            }
        }

        /// <summary>
        /// Силуэт нужен только в третьем лице: в первом камера стоит в голове, а на байке
        /// корпус байка сам закрывает пробные точки и заливал наездника цианом.
        /// </summary>
        private bool WantsSilhouette()
        {
            return _rig == null || _rig.Mode == CameraMode.ThirdPerson;
        }

        /// <summary>Есть ли между камерой и игроком хоть один перекрывающий рендерер.</summary>
        private bool IsBlocked()
        {
            CollectSamples();
            var origin = _view.transform.position;

            for (var i = 0; i < _points.Length; i++)
            {
                var delta = _points[i] - origin;
                var distance = delta.magnitude;
                if (distance < 0.05f)
                    continue;

                var count = Physics.RaycastNonAlloc(
                    origin, delta / distance, _hits, distance, _occluders,
                    QueryTriggerInteraction.Ignore);

                for (var h = 0; h < count; h++)
                {
                    var hit = _hits[h];
                    if (hit.collider == null)
                        continue;
                    if (_target != null && hit.collider.transform.IsChildOf(_target))
                        continue;

                    return true;
                }
            }

            return false;
        }

        private void CollectSamples()
        {
            var center = _target.position + _targetOffset;
            _points[0] = center;
            _points[1] = center + Vector3.up * (_bodyRadius * 1.2f);
            _points[2] = center + Vector3.down * (_targetOffset.y * 0.85f);
            _points[3] = center + _view.transform.right * _bodyRadius;
            _points[4] = center - _view.transform.right * _bodyRadius;
        }

        private void SyncBlendShapes()
        {
            if (_ghost == null || _source == null || _source.sharedMesh == null)
                return;

            var mesh = _source.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
                _ghost.SetBlendShapeWeight(i, _source.GetBlendShapeWeight(i));
        }

        private void Warn(string reason)
        {
            if (_warned)
                return;

            _warned = true;
            Debug.LogWarning($"PlayerSilhouette: {reason}", this);
            enabled = false;
        }

        private void Disable()
        {
            if (_ghost != null)
                _ghost.enabled = false;
            _strength = 0f;
            _holdUntil = 0f;
            enabled = false;
        }

        private void OnDisable()
        {
            if (_ghost != null)
                _ghost.enabled = false;
            _strength = 0f;
        }

        private void OnDestroy()
        {
            if (_ghost != null && _ghost.gameObject != null)
                Destroy(_ghost.gameObject);
            if (_material != null)
                Destroy(_material);
        }
    }
}
