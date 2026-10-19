using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Вырезает вокруг игрока круглое «окно», когда геометрия закрывает его от камеры.
    ///
    /// Каждый кадр из камеры в несколько точек игрока летят лучи. Рендереры, в которые
    /// попали лучи, получают материал с шейдером <c>FrostSpace/VFX/OcclusionReveal</c>.
    /// Шейдер вырезает не объект целиком, а только круг на экране вокруг персонажа,
    /// причём лишь те пиксели, что ближе к камере, чем игрок: пол под ногами и дальняя
    /// стена остаются. Край круга ступенчатый, распадается кубическими ячейками, по
    /// кромке идёт холодное свечение.
    ///
    /// Круг считается в экранном пространстве намеренно: крыша дома висит в 4–5 м над
    /// игроком и в мировую сферу вокруг него не попадает, а на экране всегда
    /// проецируется вокруг персонажа.
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
        [Tooltip("Камера, из которой смотрим на игрока")]
        [SerializeField] private Camera _view;
        [Tooltip("Игрок, которого не должна закрывать геометрия")]
        [SerializeField] private Transform _target;
        [Tooltip("Точка на игроке, вокруг которой строится вырез")]
        [SerializeField] private Vector3 _targetOffset = new(0f, 0.95f, 0f);
        [Tooltip("Разброс пробных лучей по телу игрока, м")]
        [SerializeField] private float _bodyRadius = 0.45f;

        [Header("Что скрывать")]
        [Tooltip("Слои, которые могут вырезаться (игрок и NPC исключены)")]
        [SerializeField] private LayerMask _occluders = ~0;
        [Tooltip("Шейдер FrostSpace/VFX/OcclusionReveal")]
        [SerializeField] private Shader _revealShader;

        [Header("Вырез")]
        [Tooltip("Радиус круга в метрах на глубине игрока")]
        [SerializeField] private float _apertureRadius = 1.8f;
        [Tooltip("Доля радиуса, занятая ступенчатым градиентом")]
        [SerializeField, Range(0.01f, 1f)] private float _edgeSoftness = 0.4f;
        [Tooltip("Размер кубической ячейки растворения, м")]
        [SerializeField] private float _cellSize = 0.24f;
        [Tooltip("Насколько ячейки растворяются вразнобой")]
        [SerializeField, Range(0f, 1f)] private float _cellJitter = 0.55f;
        [Tooltip("Размер экранного пикселя выреза, px")]
        [SerializeField] private float _pixelBlock = 5f;
        [Tooltip("Запас над полом, ниже которого геометрия не вырезается, м")]
        [SerializeField] private float _floorMargin = 0.3f;
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
        private readonly Vector3[] _points = new Vector3[5];
        private readonly Vector3[] _hitPoints = new Vector3[5];
        private readonly Vector3[] _hitNormals = new Vector3[5];
        private int _hitCount;
        private bool _warnedNoShader;

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

            UpdateGlobals();
            CollectSamples();

            var origin = _view.transform.position;
            _blocking.Clear();
            _hitCount = 0;

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
                    var target = Resolve(_hits[h].collider);
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
                        _hitPoints[_hitCount] = _hits[h].point;
                        _hitNormals[_hitCount] = _hits[h].normal;
                        _hitCount++;
                    }
                }
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
        private void UpdateGlobals()
        {
            var world = _target.position + _targetOffset;
            var viewport = _view.WorldToViewportPoint(world);
            var eyeDepth = Mathf.Max(0.01f, viewport.z);
            var radius = ScreenAperture.RadiusFraction(
                _apertureRadius, eyeDepth, _view.fieldOfView);
            var pixelStep = ScreenAperture.PixelStep(Screen.height, _pixelBlock);
            var aspect = ScreenAperture.Aspect(Screen.width, Screen.height);

            Shader.SetGlobalVector(
                RevealCenterId, new Vector4(viewport.x, viewport.y, radius, eyeDepth));
            Shader.SetGlobalVector(
                RevealSetupId,
                new Vector4(aspect, Mathf.Max(pixelStep, 1e-5f), Mathf.Max(0.01f, _cellSize), Time.time));
            Shader.SetGlobalFloat(RevealFloorYId, _target.position.y + _floorMargin);
            Shader.SetGlobalColor(EdgeColorId, _edgeColor);
            Shader.SetGlobalFloat(EdgeIntensityId, _edgeIntensity);
            Shader.SetGlobalFloat(EdgeSoftnessId, _edgeSoftness);
            Shader.SetGlobalFloat(CellJitterId, _cellJitter);
        }

        /// <summary>Пять пробных точек: центр, голова, ноги и бока игрока.</summary>
        private void CollectSamples()
        {
            var center = _target.position + _targetOffset;
            _points[0] = center;
            _points[1] = center + Vector3.up * (_bodyRadius * 1.2f);
            _points[2] = center + Vector3.down * (_targetOffset.y * 0.85f);
            _points[3] = center + _view.transform.right * _bodyRadius;
            _points[4] = center - _view.transform.right * _bodyRadius;
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
