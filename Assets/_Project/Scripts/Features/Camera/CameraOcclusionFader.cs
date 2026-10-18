using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Прячет геометрию, которая закрывает игрока от камеры.
    ///
    /// Каждый кадр из камеры в несколько точек игрока летят лучи, и всё, что
    /// попало на отрезок, плавно растворяется, а когда перестаёт мешать —
    /// возвращается. Отдельных правил для крыши или навеса не нужно: система
    /// сама находит, что загораживает персонажа — крышу дома, балки, деревья.
    ///
    /// Составные сооружения (дом) описаны <see cref="CameraOcclusionFadeGroup"/>:
    /// перекрытие любой их части скрывает весь набор, иначе на месте стены
    /// оставались бы висеть окна.
    ///
    /// Материалы на время фейда подменяются прозрачными копиями (URP Lit:
    /// Surface = Transparent); копии создаются лениво, по одной на рендерер,
    /// поэтому общие ассеты палитры не трогаются. Когда альфа дошла до нуля,
    /// рендерер выключается целиком; коллайдеры и физика не меняются.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CameraOcclusionFader : MonoBehaviour
    {
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Ссылки")]
        [Tooltip("Камера, из которой смотрим на игрока")]
        [SerializeField] private Camera _view;
        [Tooltip("Игрок, которого не должна закрывать геометрия")]
        [SerializeField] private Transform _target;
        [Tooltip("Точка на игроке, вокруг которой строятся пробы")]
        [SerializeField] private Vector3 _targetOffset = new Vector3(0f, 0.95f, 0f);
        [Tooltip("Разброс пробных лучей по телу игрока, м")]
        [SerializeField] private float _bodyRadius = 0.45f;

        [Header("Что скрывать")]
        [Tooltip("Слои, которые могут растворяться (игрок и NPC исключены)")]
        [SerializeField] private LayerMask _occluders = ~0;

        [Header("Фейд")]
        [Tooltip("Время растворения, с")]
        [SerializeField] private float _fadeSeconds = 0.25f;
        [Tooltip("Пауза перед возвращением, с")]
        [SerializeField] private float _holdSeconds = 0.3f;

        private sealed class Entry
        {
            public Renderer Renderer;
            public Material[] Original;
            public Material[] Fade;
            public float Visibility = 1f;
            public float HoldUntil;
            public bool Begun;
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
        private readonly List<Renderer> _lost = new();
        private readonly Vector3[] _points = new Vector3[5];

        private void LateUpdate()
        {
            if (_view == null || _target == null)
                return;

            CollectSamples();
            var origin = _view.transform.position;
            _blocking.Clear();

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
                        continue;
                    }

                    var parts = target.Group.Renderers;
                    for (var p = 0; p < parts.Length; p++)
                    {
                        if (parts[p] != null)
                            _blocking.Add(parts[p]);
                    }
                }
            }

            var now = Time.time;
            foreach (var renderer in _blocking)
                GetEntry(renderer).HoldUntil = now + _holdSeconds;

            var step = Time.deltaTime / Mathf.Max(0.05f, _fadeSeconds);
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry.Renderer == null)
                {
                    _lost.Add(pair.Key);
                    continue;
                }

                var wanted = entry.HoldUntil > now ? 0f : 1f;
                if (Mathf.Approximately(entry.Visibility, wanted))
                    continue;

                if (!entry.Begun && entry.Visibility >= 1f)
                {
                    Begin(entry);
                    entry.Begun = true;
                }

                entry.Visibility = Mathf.MoveTowards(entry.Visibility, wanted, step);
                Apply(entry, entry.Visibility);

                if (entry.Visibility <= 0f)
                {
                    entry.Renderer.enabled = false;
                }
                else
                {
                    entry.Renderer.enabled = true;
                    if (wanted >= 1f && entry.Visibility >= 1f)
                    {
                        End(entry);
                        entry.Begun = false;
                    }
                }
            }

            foreach (var renderer in _lost)
                _entries.Remove(renderer);
            _lost.Clear();
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
            var fade = new Material[original.Length];
            for (var i = 0; i < original.Length; i++)
                fade[i] = ToTransparent(original[i]);

            entry = new Entry
            {
                Renderer = renderer,
                Original = original,
                Fade = fade
            };
            _entries.Add(renderer, entry);
            return entry;
        }

        private static void Begin(Entry entry)
        {
            entry.Renderer.sharedMaterials = entry.Fade;
            entry.Renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void Apply(Entry entry, float visibility)
        {
            foreach (var material in entry.Fade)
            {
                if (material == null || !material.HasProperty(BaseColorId))
                    continue;
                var color = material.GetColor(BaseColorId);
                color.a = visibility;
                material.SetColor(BaseColorId, color);
            }
        }

        private static void End(Entry entry)
        {
            entry.Renderer.enabled = true;
            entry.Renderer.sharedMaterials = entry.Original;
            entry.Renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>Копия материала палитры, настроенная на прозрачность URP Lit.</summary>
        private static Material ToTransparent(Material source)
        {
            if (source == null)
                return null;

            var copy = new Material(source) { name = source.name + " (fade)" };
            if (copy.HasProperty(SurfaceId))
            {
                copy.SetFloat(SurfaceId, 1f);
                copy.SetFloat(BlendId, 0f);
                copy.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                copy.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                copy.SetFloat(ZWriteId, 0f);
                copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                copy.DisableKeyword("_ALPHATEST_ON");
                copy.renderQueue = (int)RenderQueue.Transparent;
                copy.SetShaderPassEnabled("ShadowCaster", false);
            }

            return copy;
        }
    }
}
