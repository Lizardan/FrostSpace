using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Круговой вырез в геометрии по перекрестию прицела.
    ///
    /// Работает в третьем лице, пока удерживается ПКМ. Из камеры в точку прицела
    /// и по краям круга летят лучи; рендереры, в которые они попали, получают материал
    /// с шейдером <c>FrostSpace/VFX/OcclusionReveal</c>. Шейдер вырезает круг в экранном
    /// пространстве вокруг перекрестия и только те пиксели, что ближе к камере, чем точка
    /// прицела: сквозь дерево или стену видно то, во что целимся. Край круга ступенчатый,
    /// распадается кубическими ячейками, по кромке идёт холодное свечение.
    ///
    /// Выбора модуля больше нет: ленты APERTURE/SCANNER на HUD не существует, поэтому
    /// просвет — безусловный эффект ПКМ, а сканирование переехало на Q
    /// (<c>ObjectScanner</c>) и работает независимо от кнопки прицела.
    ///
    /// Спокойный режим больше ничего не прорезает: крыша дома остаётся целой, а игрока
    /// за перекрытием показывает силуэт (<see cref="PlayerSilhouette"/>). Поэтому глубина
    /// отсечки здесь берётся из прицельного луча, а ограничение по высоте пола снято.
    ///
    /// Материалы подменяются копиями (общие палитровые ассеты не трогаются) и
    /// возвращаются, когда сила эффекта дошла до нуля. Рендереры и тени не выключаются.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CameraOcclusionFader : MonoBehaviour
    {
        private static readonly int RevealStrengthId = Shader.PropertyToID("_RevealStrength");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeIntensityId = Shader.PropertyToID("_EdgeIntensity");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int CellJitterId = Shader.PropertyToID("_CellJitter");
        private static readonly int RevealCenterId = Shader.PropertyToID("_RevealCenter");
        private static readonly int RevealSetupId = Shader.PropertyToID("_RevealSetup");
        private static readonly int RevealFloorYId = Shader.PropertyToID("_RevealFloorY");

        [Header("Ссылки")]
        [Tooltip("Камера, из которой смотрим")]
        [SerializeField] private Camera _view;
        [Tooltip("Риг камеры: вырез живёт только в режиме прицела")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Игрок — его коллайдеры не считаются перекрытием")]
        [SerializeField] private Transform _target;

        [Header("Что скрывать")]
        [Tooltip("Слои, которые могут вырезаться (слой Player исключить)")]
        [SerializeField] private LayerMask _occluders = ~0;
        [Tooltip("Шейдер FrostSpace/VFX/OcclusionReveal")]
        [SerializeField] private Shader _revealShader;

        [Header("Круг в перекрестии")]
        [Tooltip("Радиус круга в метрах на глубине точки прицела")]
        [SerializeField] private float _apertureRadius = 0.9f;
        [Tooltip("Максимальная дальность прицельного луча, м")]
        [SerializeField] private float _maxAimDistance = 60f;
        [Tooltip("Насколько глубоко за точку прицела режется геометрия, м: стена прорезается насквозь")]
        [SerializeField] private float _penetration = 2.5f;
        [Tooltip("Запас над полом под игроком, м: пол и снег не вырезаются")]
        [SerializeField] private float _floorMargin = 0.15f;
        [Tooltip("Доля радиуса, занятая ступенчатым градиентом")]
        [SerializeField, Range(0.01f, 1f)] private float _edgeSoftness = 0.4f;
        [Tooltip("Размер кубической ячейки растворения, м")]
        [SerializeField] private float _cellSize = 0.24f;
        [Tooltip("Насколько ячейки растворяются вразнобой")]
        [SerializeField, Range(0f, 1f)] private float _cellJitter = 0.55f;
        [Tooltip("Размер экранного пикселя выреза, px")]
        [SerializeField] private float _pixelBlock = 5f;
        [SerializeField] private Color _edgeColor = new(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private float _edgeIntensity = 4f;

        [Header("Переход")]
        [Tooltip("Время появления и возврата, с")]
        [SerializeField] private float _fadeSeconds = 0.22f;
        [Tooltip("Пауза перед возвратом, с")]
        [SerializeField] private float _holdSeconds = 0.15f;

        [Header("Осколки")]
        [SerializeField] private OcclusionRevealDebris _debris;
        [Tooltip("Кубических осколков на одно новое перекрытие")]
        [SerializeField] private int _burstPerHit = 5;

        private sealed class Entry
        {
            public Renderer Renderer;
            public Material[] Original;
            public Material[] Reveal;
            public float Progress;
            public float HoldUntil;
            public bool Active;
        }

        /// <summary>Разбор коллайдера: сам рендерер и группа, если он в неё входит.</summary>
        private sealed class Target
        {
            public Renderer Renderer;
            public CameraOcclusionFadeGroup Group;
        }

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private readonly Dictionary<Renderer, Entry> _entries = new();
        private readonly Dictionary<EntityId, Target> _targets = new();
        private readonly HashSet<Renderer> _blocking = new();
        private readonly HashSet<Renderer> _wasBlocking = new();
        private readonly List<Renderer> _lost = new();
        private readonly Vector3[] _points = new Vector3[13];
        private readonly Vector3[] _hitPoints = new Vector3[13];
        private readonly Vector3[] _hitNormals = new Vector3[13];
        private int _hitCount;
        private bool _warnedNoShader;

        private void Awake()
        {
            if (_view == null)
                _view = GetComponent<Camera>();
            if (_view == null)
                _view = Camera.main;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
        }

        private void LateUpdate()
        {
            if (_view == null || _target == null)
                return;

            if (_revealShader == null)
            {
                if (!_warnedNoShader)
                {
                    _warnedNoShader = true;
                    Debug.LogWarning(
                        "CameraOcclusionFader: не назначен шейдер FrostSpace/VFX/OcclusionReveal",
                        this);
                }

                return;
            }

            // Просвет — эффект ПКМ в третьем лице. В первом лице и на байке он
            // не работает, поэтому круг там не рисуется вовсе.
            var aiming = _rig != null && _rig.IsAiming;
            var distance = _maxAimDistance;
            var point = _view.transform.position + _view.transform.forward * _maxAimDistance;

            // Неба в перекрестии вырезать нечего: без попадания круга нет вообще.
            if (aiming)
                aiming = _rig.TryGetAim(out point, out distance);

            UpdateGlobals(aiming, distance);

            _blocking.Clear();
            _hitCount = 0;

            if (aiming)
            {
                CollectSamples(point);
                TraceOccluders();
            }

            EmitDebris();
            _wasBlocking.Clear();
            _wasBlocking.UnionWith(_blocking);

            var now = Time.time;
            foreach (var renderer in _blocking)
            {
                var entry = GetEntry(renderer);
                if (entry != null)
                    entry.HoldUntil = now + _holdSeconds;
            }

            UpdateEntries(now);

            foreach (var renderer in _lost)
                _entries.Remove(renderer);
            _lost.Clear();
        }

        /// <summary>Глобальные параметры кадра: центр и радиус круга, пиксели, ячейки.</summary>
        private void UpdateGlobals(bool aiming, float distance)
        {
            var depth = Mathf.Max(0.5f, distance);
            var radius = aiming
                ? ScreenAperture.RadiusFraction(_apertureRadius, depth, _view.fieldOfView)
                : 0f;
            var pixelStep = ScreenAperture.PixelStep(Screen.height, _pixelBlock);
            var aspect = ScreenAperture.Aspect(Screen.width, Screen.height);

            // Центр — перекрестие: круг всегда там, куда смотрит игрок. Глубина отсечки —
            // точка прицела плюс заглубление: без него режется только то, что ближе цели,
            // и стена в перекрестии остаётся целой — круга как будто нет.
            var cutDepth = aiming ? depth + _penetration : depth;
            Shader.SetGlobalVector(
                RevealCenterId, new Vector4(0.5f, 0.5f, radius, cutDepth - 0.03f));
            Shader.SetGlobalVector(
                RevealSetupId,
                new Vector4(aspect, Mathf.Max(pixelStep, 1e-5f), Mathf.Max(0.01f, _cellSize), Time.time));

            // Пол и снег под игроком не вырезаются: иначе в земле появляется дыра.
            var floorY = _target != null ? _target.position.y + _floorMargin : -10000f;
            Shader.SetGlobalFloat(RevealFloorYId, floorY);
            Shader.SetGlobalColor(EdgeColorId, _edgeColor);
            Shader.SetGlobalFloat(EdgeIntensityId, _edgeIntensity);
            Shader.SetGlobalFloat(EdgeSoftnessId, _edgeSoftness);
            Shader.SetGlobalFloat(CellJitterId, _cellJitter);
        }

        /// <summary>
        /// Тринадцать проб: центр, полрадиуса и кольцо. Пяти лучей не хватало: рендерер,
        /// пересекающий круг углом, не попадал ни в один луч и оставался целым —
        /// вырез выглядел прорезанным только частично.
        /// </summary>
        private void CollectSamples(Vector3 point)
        {
            var right = _view.transform.right * _apertureRadius;
            var up = _view.transform.up * _apertureRadius;
            const float diagonal = 0.7071068f;

            _points[0] = point;
            _points[1] = point + up * 0.5f;
            _points[2] = point - up * 0.5f;
            _points[3] = point + right * 0.5f;
            _points[4] = point - right * 0.5f;
            _points[5] = point + up;
            _points[6] = point - up;
            _points[7] = point + right;
            _points[8] = point - right;
            _points[9] = point + (right + up) * diagonal;
            _points[10] = point + (right - up) * diagonal;
            _points[11] = point - (right + up) * diagonal;
            _points[12] = point - (right - up) * diagonal;
        }

        /// <summary>Лучи в точки прицела: кто перекрывает круг, тот получает шейдер выреза.</summary>
        private void TraceOccluders()
        {
            var origin = _view.transform.position;

            for (var i = 0; i < _points.Length; i++)
            {
                var delta = _points[i] - origin;
                var distance = delta.magnitude;
                if (distance < 0.01f)
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

                    var target = Resolve(hit.collider);
                    if (target == null)
                        continue;

                    if (target.Group == null)
                    {
                        _blocking.Add(target.Renderer);
                    }
                    else
                    {
                        var parts = target.Group.Renderers;
                        for (var p = 0; p < parts.Length; p++)
                        {
                            if (parts[p] != null)
                                _blocking.Add(parts[p]);
                        }
                    }

                    if (_hitCount < _hitPoints.Length)
                    {
                        _hitPoints[_hitCount] = hit.point;
                        _hitNormals[_hitCount] = hit.normal;
                        _hitCount++;
                    }
                }
            }
        }

        private void EmitDebris()
        {
            if (_debris == null || _hitCount == 0)
                return;

            var isNew = false;
            foreach (var renderer in _blocking)
            {
                if (!_wasBlocking.Contains(renderer))
                {
                    isNew = true;
                    break;
                }
            }

            if (!isNew)
                return;

            for (var i = 0; i < _hitCount; i++)
                _debris.Burst(_hitPoints[i], _hitNormals[i], _burstPerHit);
        }

        private void UpdateEntries(float now)
        {
            var step = Time.deltaTime / Mathf.Max(0.02f, _fadeSeconds);

            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry.Renderer == null)
                {
                    _lost.Add(pair.Key);
                    continue;
                }

                var wanted = entry.HoldUntil > now ? 1f : 0f;
                if (Mathf.Approximately(entry.Progress, wanted) && !entry.Active)
                    continue;

                if (Mathf.Approximately(entry.Progress, wanted) && wanted >= 1f)
                {
                    Apply(entry, 1f);
                    continue;
                }

                entry.Progress = Mathf.MoveTowards(entry.Progress, wanted, step);
                var strength = entry.Progress * entry.Progress * (3f - 2f * entry.Progress);

                if (entry.Progress > 0f)
                {
                    if (!entry.Active)
                    {
                        Begin(entry);
                        entry.Active = true;
                    }

                    Apply(entry, strength);
                }
                else if (entry.Active)
                {
                    Apply(entry, 0f);
                    End(entry);
                }
            }
        }

        /// <summary>Рендерер и группа по коллайдеру, с кэшем на время игры.</summary>
        private Target Resolve(Collider collider)
        {
            var id = collider.GetEntityId();
            if (_targets.TryGetValue(id, out var cached))
            {
                if (cached == null)
                    return null;
                if (cached.Renderer != null)
                    return cached;
                // рендерер уничтожен — разбираем заново
            }

            var renderer = collider.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                _targets[id] = null;
                return null;
            }

            var target = new Target
            {
                Renderer = renderer,
                Group = renderer.GetComponentInParent<CameraOcclusionFadeGroup>()
            };
            _targets[id] = target;
            return target;
        }

        private Entry GetEntry(Renderer renderer)
        {
            if (_entries.TryGetValue(renderer, out var entry))
                return entry;

            var original = renderer.sharedMaterials;
            var reveal = new Material[original.Length];
            for (var i = 0; i < original.Length; i++)
                reveal[i] = CreateRevealMaterial(original[i]);

            entry = new Entry
            {
                Renderer = renderer,
                Original = original,
                Reveal = reveal,
                Progress = 0f
            };
            _entries.Add(renderer, entry);
            return entry;
        }

        private Material CreateRevealMaterial(Material source)
        {
            if (source == null)
                return null;

            var copy = new Material(_revealShader) { name = source.name + " (reveal)" };
            copy.CopyPropertiesFromMaterial(source);
            if (source.HasProperty(CullId))
                copy.SetFloat(CullId, source.GetFloat(CullId));

            if (source.IsKeywordEnabled("_EMISSION"))
                copy.EnableKeyword("_EMISSION");
            else
                copy.DisableKeyword("_EMISSION");

            copy.SetFloat(RevealStrengthId, 0f);
            copy.renderQueue = (int)RenderQueue.Geometry;
            return copy;
        }

        private void Begin(Entry entry)
        {
            entry.Renderer.sharedMaterials = entry.Reveal;
        }

        private void End(Entry entry)
        {
            entry.Renderer.sharedMaterials = entry.Original;
            entry.Active = false;
        }

        private static void Apply(Entry entry, float strength)
        {
            foreach (var material in entry.Reveal)
            {
                if (material != null)
                    material.SetFloat(RevealStrengthId, strength);
            }
        }

        private void OnDisable()
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Active && entry.Renderer != null)
                    End(entry);
            }

            _entries.Clear();
            _targets.Clear();
            _blocking.Clear();
            _wasBlocking.Clear();
            _lost.Clear();
        }
    }
}
