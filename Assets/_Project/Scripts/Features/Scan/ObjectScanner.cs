using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FrostSpace.Features.View;

namespace FrostSpace.Features.Scan
{
    /// <summary>Что сканер прочитал с объекта: имя, категория, точка подписи и дистанция.</summary>
    public readonly struct ScanResult
    {
        public readonly string Name;
        public readonly string Category;
        public readonly Vector3 Anchor;
        public readonly float Distance;

        public ScanResult(string name, string category, Vector3 anchor, float distance)
        {
            Name = name;
            Category = category;
            Anchor = anchor;
            Distance = distance;
        }
    }

    /// <summary>
    /// Сканер — эффект модуля <see cref="ViewModule.Scanner"/>. Пока удерживается ПКМ,
    /// из перекрестия летит луч; найденный объект получает собственный дубль геометрии
    /// с материалом <c>FrostSpace/VFX/ScanHighlight</c>: контур, кромка и полоса скана
    /// ложатся поверх всей модели, а исходные материалы объекта не трогаются, поэтому
    /// он остаётся в палитре уровня. HUD рисует подпись с именем.
    ///
    /// Дубль, а не дополнительный слот материала: лишние слоты сверх числа подмешей
    /// Unity не рисует, поэтому у односекционного меша контур не появлялся вовсе.
    /// Материал один на все цели: смена цели переключает видимость дублей, а не
    /// перенастраивает материалы.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(190)]
    public sealed class ObjectScanner : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int ScanBandId = Shader.PropertyToID("_ScanBand");

        [Header("Ссылки")]
        [Tooltip("Камера, из которой смотрим")]
        [SerializeField] private Camera _view;
        [Tooltip("Риг камеры: скан живёт только в третьем лице при удержании ПКМ")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Модули HUD: скан работает, только когда выбран Scanner")]
        [SerializeField] private ViewModuleSwitch _modules;
        [Tooltip("Шейдер FrostSpace/VFX/ScanHighlight")]
        [SerializeField] private Shader _shader;
        [Tooltip("Конус сканирования из головы: создаётся сам, если поле пустое")]
        [SerializeField] private ScanCone _cone;

        [Header("Сканирование")]
        [Tooltip("Что можно сканировать (слой Player исключить)")]
        [SerializeField] private LayerMask _targets = ~0;
        [Tooltip("Дальность луча сканера, м")]
        [SerializeField] private float _maxDistance = 40f;
        [Tooltip("Сколько секунд идёт полоса скана")]
        [SerializeField] private float _scanSeconds = 0.6f;
        [Tooltip("За сколько секунд контур гаснет после отпускания")]
        [SerializeField] private float _fadeSeconds = 0.16f;
        [Tooltip("Сколько рендереров максимум красить: защита от сканирования целого террейна")]
        [SerializeField] private int _maxRenderers = 48;
        [Tooltip("Полуразмер квадратной области скана в долях экрана: 0.14 ≈ 9° при поле зрения 60°")]
        [SerializeField, Range(0.02f, 0.5f)] private float _scanAreaHalf = 0.14f;
        [Tooltip("Сколько лучей по стороне квадрата области: 5 — это 25 лучей за кадр")]
        [SerializeField, Range(1, 9)] private int _scanSamples = 5;

        [Header("Цифровой слой")]
        [Tooltip("Цвет скана — циан цифрового слоя #4DE6FF")]
        [SerializeField] private Color _scanColor = new(0.302f, 0.902f, 1f, 1f);
        [Tooltip("Толщина контура в пикселях экрана: не зависит от дистанции до объекта")]
        [SerializeField] private float _outlineWidth = 2.6f;

        private sealed class Entry
        {
            public Renderer Overlay;
            public bool Wanted;
        }

        private readonly Dictionary<Renderer, Entry> _entries = new();
        private readonly List<Renderer> _wanted = new(64);
        private readonly List<Renderer> _dead = new(8);
        private Material _material;
        private float _strength;
        private float _progress;
        private bool _announced;
        private bool _hadTarget;
        private ScanResult _current;
        private float _boundsMin;
        private float _boundsMax;
        private Bounds _bounds;

        /// <summary>Скан идёт или контур ещё виден.</summary>
        public bool IsVisible => _strength > 0.01f;

        /// <summary>Есть ли цель под перекрестием прямо сейчас.</summary>
        public bool HasTarget { get; private set; }

        /// <summary>Что прочитано с цели.</summary>
        public ScanResult Current => _current;

        /// <summary>Прогресс полосы скана 0..1.</summary>
        public float Progress => _progress;

        /// <summary>Яркость контура 0..1 — HUD гасит подпись вместе с ним.</summary>
        public float Strength => _strength;

        /// <summary>Мировые границы цели: по ним HUD рисует квадратную рамку.</summary>
        public Bounds TargetBounds => _bounds;

        /// <summary>
        /// Камера скана. Ищется один раз; если камера пересоздана — ищется заново,
        /// но не каждый кадр: обращение к <c>Camera.main</c> — это поиск по тегу.
        /// </summary>
        private Camera ResolveView()
        {
            if (_view == null)
                _view = Camera.main;

            return _view;
        }

        private void Awake()
        {
            if (_view == null)
                _view = Camera.main;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_modules == null)
                _modules = FindAnyObjectByType<ViewModuleSwitch>();

            if (_shader == null)
                _shader = Shader.Find("FrostSpace/VFX/ScanHighlight");

            if (_shader == null)
            {
                Debug.LogError("[ObjectScanner] не найден шейдер FrostSpace/VFX/ScanHighlight", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            var active = _rig != null && _rig.IsAiming &&
                         (_modules == null || _modules.Current == ViewModule.Scanner);

            // Объявленные до ветвления: короткое замыкание выше не даёт компилятору
            // гарантий, что out-параметры инициализируются внутри вызова.
            var hasTarget = false;
            var renderers = new List<Renderer>(4);
            var result = default(ScanResult);
            if (active)
                hasTarget = TryResolveTarget(out renderers, out result);

            ReleaseWanted();
            if (hasTarget)
            {
                _wanted.AddRange(renderers);
                if (!_hadTarget || result.Name != _current.Name)
                {
                    _progress = 0f;
                    _announced = false;
                }

                _current = result;
            }
            else if (_hadTarget)
            {
                Debug.Log("[ObjectScanner] пусто", this);
                _progress = 0f;
                _announced = false;
            }

            HasTarget = hasTarget;
            _hadTarget = hasTarget;

            var wantedStrength = hasTarget ? 1f : 0f;
            _strength = Mathf.MoveTowards(
                _strength, wantedStrength, Time.deltaTime / Mathf.Max(0.01f, _fadeSeconds));

            if (hasTarget)
            {
                _progress = Mathf.Min(1f, _progress + Time.deltaTime / Mathf.Max(0.01f, _scanSeconds));
                if (_progress >= 1f && !_announced)
                {
                    _announced = true;
                    Debug.Log($"[ObjectScanner] скан {_current.Name} на {_current.Distance:0.#} м", this);
                }
            }

            ApplyOverlays();
            PushShaderState();
            UpdateCone();
        }

        private void OnDestroy()
        {
            foreach (var pair in _entries)
            {
                if (pair.Value.Overlay != null)
                    Destroy(pair.Value.Overlay.gameObject);
            }

            _entries.Clear();

            if (_material != null)
                Destroy(_material);
        }

        /// <summary>
        /// Луч из перекрестия и сбор рендереров цели. Если у объекта есть
        /// <see cref="ScanTarget"/>, обводится вся его модель — иначе только рендерер
        /// под лучом, чтобы случайно не обвести пол-уровня.
        /// </summary>
        /// <summary>
        /// Цель внутри квадратной области скана. Квадрат накрывается сеткой лучей: объект
        /// ловится даже когда перекрестие смотрит мимо, а каждая точка проверяется на перекрытие
        /// отдельно, поэтому сквозь стену цель не прочитается. Приоритет у ближайшего объекта со
        /// <see cref="ScanTarget"/>; если такого нет — читается ближайшая геометрия под лучами.
        /// </summary>
        private bool TryResolveTarget(out List<Renderer> renderers, out ScanResult result)
        {
            renderers = new List<Renderer>(4);
            result = default;

            if (_view == null)
                return false;

            var samples = Mathf.Clamp(_scanSamples, 1, 9);
            var half = Mathf.Clamp(_scanAreaHalf, 0.01f, 0.5f);

            // Простая геометрия читается только под самим перекрестием: иначе широкий
            // квадрат цеплял бы снег и стены и подсказка висела бы постоянно.
            var centerDistance = float.MaxValue;
            var centerHit = default(RaycastHit);
            var targetDistance = float.MaxValue;
            var targetHit = default(RaycastHit);
            ScanTarget target = null;

            for (var x = 0; x < samples; x++)
            {
                for (var y = 0; y < samples; y++)
                {
                    // Крайние точки лежат на границе квадрата, центральная — в перекрестии.
                    var u = samples == 1 ? 0.5f : 0.5f + half * (x / (samples - 1f) * 2f - 1f);
                    var v = samples == 1 ? 0.5f : 0.5f + half * (y / (samples - 1f) * 2f - 1f);

                    var ray = _view.ViewportPointToRay(new Vector3(u, v, 0f));
                    if (!Physics.Raycast(ray, out var hit, _maxDistance, _targets, QueryTriggerInteraction.Ignore))
                        continue;

                    if (Mathf.Abs(u - 0.5f) < 1e-4f && Mathf.Abs(v - 0.5f) < 1e-4f)
                    {
                        centerDistance = hit.distance;
                        centerHit = hit;
                    }

                    var candidate = ScanTarget.Find(hit.collider.transform);
                    if (candidate == null || hit.distance >= targetDistance)
                        continue;

                    targetDistance = hit.distance;
                    targetHit = hit;
                    target = candidate;
                }
            }

            if (target == null && centerDistance == float.MaxValue)
                return false;

            var hitInfo = target != null ? targetHit : centerHit;

            if (target != null)
            {
                target.GetComponentsInChildren(true, renderers);
            }
            else
            {
                var renderer = hitInfo.collider.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = hitInfo.collider.GetComponentInChildren<Renderer>();
                if (renderer != null)
                    renderers.Add(renderer);
            }

            if (renderers.Count == 0)
                return false;

            if (renderers.Count > _maxRenderers)
                renderers.RemoveRange(_maxRenderers, renderers.Count - _maxRenderers);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);

            _bounds = bounds;
            _boundsMin = bounds.min.y;
            _boundsMax = bounds.max.y;

            var display = target != null ? target.DisplayName : ScanTarget.Sanitize(hitInfo.collider.gameObject.name);
            var category = target != null ? target.Category : "OBJECT";
            result = new ScanResult(display, category, bounds.center, hitInfo.distance);
            return true;
        }

        private void ReleaseWanted()
        {
            foreach (var pair in _entries)
                pair.Value.Wanted = false;
        }

        /// <summary>Показывает дубли только у текущей цели и гасит их, когда скан отпущен.</summary>
        private void ApplyOverlays()
        {
            if (_shader == null)
                return;

            foreach (var renderer in _wanted)
            {
                if (renderer == null)
                    continue;

                if (!_entries.TryGetValue(renderer, out var entry))
                {
                    entry = CreateEntry(renderer);
                    if (entry == null)
                        continue;

                    _entries[renderer] = entry;
                }

                entry.Wanted = true;
                if (entry.Overlay != null && !entry.Overlay.gameObject.activeSelf)
                    entry.Overlay.gameObject.SetActive(true);
            }

            _wanted.Clear();

            var visible = _strength > 0f;
            _dead.Clear();

            foreach (var pair in _entries)
            {
                var overlay = pair.Value.Overlay;
                if (overlay == null)
                    continue;

                // Источник мог исчезнуть вместе с объектом уровня — тогда дубль не нужен.
                if (pair.Key == null)
                {
                    Destroy(overlay.gameObject);
                    _dead.Add(pair.Key);
                    continue;
                }

                var show = visible && pair.Value.Wanted;
                if (overlay.gameObject.activeSelf != show)
                    overlay.gameObject.SetActive(show);
            }

            for (var i = 0; i < _dead.Count; i++)
                _entries.Remove(_dead[i]);
        }

        private Entry CreateEntry(Renderer source)
        {
            if (_material == null)
            {
                _material = new Material(_shader)
                {
                    name = "ScanHighlight (runtime)",
                    hideFlags = HideFlags.DontSave
                };
                _material.SetColor(ScanColorId, _scanColor);
                _material.SetFloat(OutlineWidthId, _outlineWidth);
            }

            var overlay = CreateOverlay(source);
            return overlay == null ? null : new Entry { Overlay = overlay };
        }

        /// <summary>
        /// Дубль геометрии цели с материалом скана. Дубль повторяет трансформ источника
        /// кадр в кадр: у меша геометрия лежит в локальных осях рендерера, у скиннинга
        /// берутся те же кости, поэтому деформация совпадает и обводка не отстаёт.
        /// </summary>
        private Renderer CreateOverlay(Renderer source)
        {
            var go = new GameObject("Scan Overlay") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(source.transform, false);

            switch (source)
            {
                case SkinnedMeshRenderer skin when skin.sharedMesh != null:
                {
                    var clone = go.AddComponent<SkinnedMeshRenderer>();
                    clone.sharedMesh = skin.sharedMesh;
                    clone.bones = skin.bones;
                    clone.rootBone = skin.rootBone;
                    clone.localBounds = skin.localBounds;
                    clone.updateWhenOffscreen = true;
                    clone.shadowCastingMode = ShadowCastingMode.Off;
                    clone.receiveShadows = false;
                    clone.sharedMaterial = _material;
                    return clone;
                }

                case MeshRenderer mesh when mesh.TryGetComponent(out MeshFilter filter):
                {
                    var meshFilter = go.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = filter.sharedMesh;
                    var clone = go.AddComponent<MeshRenderer>();
                    clone.shadowCastingMode = ShadowCastingMode.Off;
                    clone.receiveShadows = false;
                    clone.sharedMaterial = _material;
                    return clone;
                }

                default:
                    Destroy(go);
                    return null;
            }
        }

        private void PushShaderState()
        {
            if (_material == null)
                return;

            _material.SetFloat(StrengthId, _strength);
            Shader.SetGlobalVector(ScanBandId, new Vector4(_boundsMin, _boundsMax, _progress, 0f));
        }
        /// <summary>
        /// Конус сканирования из головы: направление берётся на точку прицела, а длина —
        /// до неё же. Поэтому луч упирается ровно в то, что читает сканер, и не уходит в
        /// пустоту, когда цель близко. Яркость и прогресс общие с контуром объекта.
        /// </summary>
        private void UpdateCone()
        {
            if (_cone == null)
            {
                var go = new GameObject("Scan Cone") { hideFlags = HideFlags.DontSave };
                _cone = go.AddComponent<ScanCone>();
            }

            var view = ResolveView();
            var origin = _rig != null && _rig.HeadBone != null
                ? _rig.HeadBone.position
                : view != null ? view.transform.position : transform.position;

            var direction = view != null ? view.transform.forward : transform.forward;
            var length = _maxDistance;

            // Точка прицела точнее оси камеры: конус идёт от головы, а не по оси объектива.
            if (_rig != null && _rig.TryGetAim(out var point, out var distance) && distance > 0.01f)
            {
                direction = point - origin;
                length = direction.magnitude;
            }

            _cone.SetState(_strength > 0.001f, origin, direction, length, _strength, _progress);
        }

    }
}
