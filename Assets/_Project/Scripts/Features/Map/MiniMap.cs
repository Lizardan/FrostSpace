using System.Collections.Generic;
using FrostSpace.Features.Inventory;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Story;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Круглая миникарта в правом верхнем углу: тёмный диск-«радар», на нём
    /// контуры путей, тёплые узлы, руины, маркер цели и стрелка взгляда.
    /// Появляется только после того, как карта выдана диалогом (<c>DP-03</c>), —
    /// до этого её нет вовсе, а не «пустой кружок» (бит <c>RD-05</c>).
    ///
    /// Строится кодом, как весь интерфейс проекта. Диск — процедурная текстура:
    /// спрайтов в проекте нет, а без тёмного фона прибор исчезает на снегу,
    /// который занимает большую часть кадра. Кольца и обод запечены в ту же
    /// текстуру, поэтому весь фон — один элемент и один батч.
    ///
    /// Ориентация — на север: +Z мира смотрит вверх экрана. Карта не вращается
    /// вместе с камерой, поэтому направление показывает стрелка взгляда, и
    /// «не врёт о направлении» из критерия проверяется геометрией, а не ощущением.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class MiniMap : MonoBehaviour
    {
        private const int BezelTicks = 24;
        private const int DiscSize = 256;
        private const float MapRadius = 140f;
        private const float BezelInner = 134f;
        private const float BezelOuter = 150f;
        private const float MinSegmentPixels = 2f;
        private const float LineThickness = 2f;
        private const float RailThickness = 3f;

        [Header("Данные")]
        [Tooltip("world_layout.json (ADR 0036). Без него карта не строится.")]
        [SerializeField] private TextAsset _layout;

        [Tooltip("Снаряжение игрока: до выдачи карты миникарты нет")]
        [SerializeField] private PlayerEquipment _equipment;

        [Tooltip("Игрок; пусто — ищется в сцене")]
        [SerializeField] private Transform _player;

        [Tooltip("Взгляд камеры; пусто — Main Camera")]
        [SerializeField] private Transform _view;

        [Header("Показ")]
        [Tooltip("Дальность карты в метрах. 900 м — три отрезка тумана по 358 м")]
        [SerializeField] private float _radiusMeters = 900f;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _rim = new(0.302f, 0.902f, 1f, 0.45f);
        [SerializeField] private Color _road = new(0.62f, 0.78f, 0.88f, 0.55f);
        [SerializeField] private Color _rail = new(0.72f, 0.86f, 0.94f, 0.9f);
        [SerializeField] private Color _dim = new(0.34f, 0.46f, 0.54f, 0.9f);

        /// <summary>Тон диска-радара: тёмный и холодный, чтобы циан читался на снегу.</summary>
        [SerializeField] private Color _backdrop = new(0.016f, 0.075f, 0.105f, 0.82f);

        private Texture2D _discTexture;
        private Sprite _discSprite;

        private readonly List<RectTransform> _segments = new();
        private readonly List<Image> _segmentImages = new();

        private WorldLayout _world;
        private GameObject _root;
        private RectTransform _map;
        private RectTransform _needle;
        private RectTransform _goalMark;
        private Image _goalImage;
        private readonly List<RectTransform> _nodeMarks = new();
        private readonly List<RectTransform> _ruinMarks = new();
        private bool _goalClamped;

        private void Awake()
        {
            if (_equipment == null)
                _equipment = FindAnyObjectByType<PlayerEquipment>();
            if (_player == null)
            {
                // Ходовая часть есть только на игроке: по ней его и находим,
                // вместо тега, который может стоять на ком угодно.
                var body = FindAnyObjectByType<RobotLocomotion>();
                _player = body != null ? body.transform : null;
            }

            if (!WorldLayout.TryParse(_layout, out _world))
            {
                _world = null;
                return;
            }

            Build();
        }

        private void LateUpdate()
        {
            if (_root == null || _world == null)
                return;

            var shown = _equipment != null && _equipment.IsEquipped(EquipmentKind.Map) &&
                        _player != null;
            if (_root.activeSelf != shown)
                _root.SetActive(shown);

            if (!shown)
                return;

            Draw();
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root);

            // Domain reload выключен: процедурную текстуру надо убирать самим,
            // иначе каждый вход в Play оставляет ещё одну в памяти.
            if (_discSprite != null)
                Destroy(_discSprite);
            if (_discTexture != null)
                Destroy(_discTexture);
        }

        private void Draw()
        {
            // Игрок приходит из осей сцены, а весь файл — в осях раскладки.
            // Без перевода карта центрировалась бы не на игроке.
            var center = _world.ToWorld(new Vector2(_player.position.x,
                _player.position.z));
            var index = 0;

            for (var l = 0; l < _world.Lines.Count; l++)
            {
                var line = _world.Lines[l];
                var thickness = line.Kind == WorldLineKind.Rail
                    ? RailThickness : LineThickness;
                var color = line.Kind == WorldLineKind.Rail ? _rail : _road;

                for (var p = 0; p + 1 < line.Points.Length; p++)
                {
                    var from = MiniMapProjection.Project(line.Points[p], center,
                        _radiusMeters, MapRadius);
                    var to = MiniMapProjection.Project(line.Points[p + 1], center,
                        _radiusMeters, MapRadius);

                    // Отрезок за дальностью обрезается по кругу, а не рисуется
                    // целиком: иначе рельс вылезает за обод и прибор превращается
                    // в царапину по углу экрана (проверено кадром в Play Mode).
                    if (!MiniMapProjection.TryClipToDisc(from, to, MapRadius,
                        out var clippedFrom, out var clippedTo))
                    {
                        continue;
                    }

                    if (MiniMapProjection.SegmentLength(clippedFrom, clippedTo) <
                        MinSegmentPixels)
                    {
                        continue;
                    }

                    if (index >= _segments.Count)
                        break;

                    PlaceSegment(_segments[index], _segmentImages[index], clippedFrom,
                        clippedTo, thickness, color);
                    index++;
                }
            }

            for (; index < _segments.Count; index++)
            {
                _segments[index].gameObject.SetActive(false);
            }

            PlaceMarks(_nodeMarks, _world.Nodes, center);
            PlaceRuinMarks(center);
            PlaceGoal(center);
            PlacePlayer();
        }

        /// <summary>Тёплые узлы — квадратики, холодные — тусклые и вдвое меньше.</summary>
        private void PlaceMarks(List<RectTransform> marks,
            IReadOnlyList<WorldNode> nodes, Vector2 center)
        {
            for (var i = 0; i < marks.Count; i++)
            {
                if (i >= nodes.Count)
                {
                    marks[i].gameObject.SetActive(false);
                    continue;
                }

                var point = MiniMapProjection.Project(nodes[i].Position, center,
                    _radiusMeters, MapRadius);
                var warm = nodes[i].Warm;
                var size = warm ? 9f : 5f;
                marks[i].gameObject.SetActive(
                    point.sqrMagnitude <= MapRadius * MapRadius);
                marks[i].anchoredPosition = point;
                marks[i].sizeDelta = new Vector2(size, size);

                var image = marks[i].GetComponent<Image>();
                image.color = warm ? _accent : _dim;
            }
        }

        private void PlaceRuinMarks(Vector2 center)
        {
            for (var i = 0; i < _ruinMarks.Count; i++)
            {
                if (i >= _world.Ruins.Count)
                {
                    _ruinMarks[i].gameObject.SetActive(false);
                    continue;
                }

                var ruin = _world.Ruins[i];
                var point = MiniMapProjection.Project(ruin.Position, center,
                    _radiusMeters, MapRadius);
                _ruinMarks[i].gameObject.SetActive(
                    point.sqrMagnitude <= MapRadius * MapRadius);
                _ruinMarks[i].anchoredPosition = point;
            }
        }

        /// <summary>
        /// Маркер цели: станция. Вне дальности прижимается к ободу и становится
        /// крупнее — «видно, что туда», но не «вот тут рядом» (RD-05).
        /// </summary>
        private void PlaceGoal(Vector2 center)
        {
            var point = MiniMapProjection.Project(_world.Station, center,
                _radiusMeters, MapRadius);
            var clamped = MiniMapProjection.ClampToRim(ref point, MapRadius - 8f);

            if (clamped != _goalClamped)
            {
                _goalClamped = clamped;
                _goalImage.color = clamped ? _accent : _rim;
            }

            _goalMark.anchoredPosition = point;
            _goalMark.sizeDelta = clamped
                ? new Vector2(14f, 14f)
                : new Vector2(11f, 11f);
        }

        private void PlacePlayer()
        {
            var view = _view != null
                ? _view
                : (Camera.main != null ? Camera.main.transform : null);
            if (view == null)
                return;

            // Карта смотрит на север, поэтому мировое направление (x, z) — это
            // экранное (x, y) без переворота: тот же знак угла, что у отрезков.
            var forward = view.forward;
            _needle.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(forward.z, forward.x) * Mathf.Rad2Deg);
        }

        private static void PlaceSegment(RectTransform rect, Image image,
            Vector2 from, Vector2 to, float thickness, Color color)
        {
            var delta = to - from;
            var length = delta.magnitude;

            rect.gameObject.SetActive(true);
            rect.anchoredPosition = from;
            rect.sizeDelta = new Vector2(length, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            image.color = color;
        }

        private void Build()
        {
            _root = new GameObject("MiniMap Canvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Между трекером квеста (20) и экраном диалога (40): карта — часть
            // HUD, но разговор важнее карты.
            canvas.sortingOrder = 25;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var group = _root.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // Правый верхний угол: трекер квеста занимает левый, полоса опыта —
            // низ. Отступ равен внешнему радиусу обода плюс запас, иначе круг
            // срезается углом экрана (проверено первым кадром в Play Mode).
            _map = NewRect((RectTransform)_root.transform, "Map",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-(BezelOuter + 24f), -(BezelOuter + 24f)),
                new Vector2(BezelOuter * 2f, BezelOuter * 2f));

            BuildBackdrop();
            BuildBezel();
            BuildSegments();
            BuildMarks();
            BuildPlayer();

            _root.SetActive(false);
        }

        /// <summary>
        /// Диск-«радар» под прибором: тёмный, с двумя дальними кольцами и ярким
        /// ободом. Без него на снегу не видно ни линий, ни маркеров — снег
        /// занимает большую часть кадра, а светлый прибор на светлом фоне
        /// исчезает.
        /// </summary>
        private void BuildBackdrop()
        {
            _discTexture = BuildDiscTexture();
            _discSprite = Sprite.Create(_discTexture,
                new Rect(0f, 0f, DiscSize, DiscSize), new Vector2(0.5f, 0.5f), 100f);

            var disc = NewRect(_map, "Disc",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(BezelOuter * 2f, BezelOuter * 2f));
            var image = disc.gameObject.AddComponent<Image>();
            image.sprite = _discSprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.color = _backdrop;
        }

        /// <summary>
        /// Процедурный диск: яркость канала RGB несёт кольца (их видно под
        /// затемнением), альфа — форму с мягким краем. Спрайтов в проекте нет,
        /// а один такой элемент дешевле, чем гирлянда четырёхугольников, и
        /// остаётся одним батчем.
        /// </summary>
        private static Texture2D BuildDiscTexture()
        {
            var texture = new Texture2D(DiscSize, DiscSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[DiscSize * DiscSize];
            var half = DiscSize * 0.5f;

            for (var y = 0; y < DiscSize; y++)
            {
                for (var x = 0; x < DiscSize; x++)
                {
                    var dx = (x + 0.5f - half) / half;
                    var dy = (y + 0.5f - half) / half;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);

                    var alpha = r <= 1f ? Mathf.Clamp01((1f - r) / 0.05f) : 0f;

                    // Яркость канала — то, что остаётся после затемнения
                    // `Image.color`: тело 0.34 против колец 0.62 давало разницу
                    // в полтора тёмных шага, и на кадре кольца не было видно
                    // вовсе. Тело уходит в 0.20, кольца и обод — в 1.0.
                    var value = 0.20f;
                    if (Mathf.Abs(r - 0.34f) < 0.02f || Mathf.Abs(r - 0.67f) < 0.02f)
                        value = 1f;
                    if (r > 0.95f)
                        value = 1f;

                    var b = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
                    var a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
                    pixels[y * DiscSize + x] = new Color32(b, b, b, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.name = "MiniMapDisc";
            return texture;
        }

        /// <summary>Обод из 24 насечек: круг читается прибором, а не подложкой.</summary>
        private void BuildBezel()
        {
            for (var i = 0; i < BezelTicks; i++)
            {
                var angle = i * 360f / BezelTicks;
                var radians = angle * Mathf.Deg2Rad;
                var middle = (BezelInner + BezelOuter) * 0.5f;
                var position = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * middle;

                var tick = NewRect(_map, "Tick_" + i,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), position,
                    new Vector2(BezelOuter - BezelInner, i % 6 == 0 ? 3f : 1.5f));
                tick.localRotation = Quaternion.Euler(0f, 0f, angle);

                var image = tick.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = i % 6 == 0 ? _accent : _rim;
            }
        }

        private void BuildSegments()
        {
            var total = 0;
            for (var l = 0; l < _world.Lines.Count; l++)
            {
                total += _world.Lines[l].Points.Length;
            }

            for (var i = 0; i < total; i++)
            {
                var rect = NewRect(_map, "Segment_" + i,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1f, LineThickness));
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = _road;
                rect.gameObject.SetActive(false);
                _segments.Add(rect);
                _segmentImages.Add(image);
            }
        }

        private void BuildMarks()
        {
            for (var i = 0; i < _world.Nodes.Count; i++)
            {
                var rect = NewRect(_map, "Node_" + _world.Nodes[i].Key,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f));
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = _accent;
                rect.gameObject.SetActive(false);
                _nodeMarks.Add(rect);
            }

            for (var i = 0; i < _world.Ruins.Count; i++)
            {
                var rect = NewRect(_map, "Ruin_" + _world.Ruins[i].Key,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5f, 5f));
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = _dim;
                rect.gameObject.SetActive(false);
                _ruinMarks.Add(rect);
            }

            _goalMark = NewRect(_map, "Goal",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
            _goalImage = _goalMark.gameObject.AddComponent<Image>();
            _goalImage.raycastTarget = false;
            _goalImage.color = _rim;
            // Квадрат под 45°: точка, которая не похожа ни на один другой маркер.
            _goalMark.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private void BuildPlayer()
        {
            // Игрок всегда в центре: карта центрирована на нём, поэтому точка
            // статична и ей нужен только один объект.
            var playerMark = NewRect(_map, "Player",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
            var dot = playerMark.gameObject.AddComponent<Image>();
            dot.raycastTarget = false;
            dot.color = _accent;

            _needle = NewRect(_map, "Heading",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 2.5f));
            var needle = _needle.gameObject.AddComponent<Image>();
            needle.raycastTarget = false;
            needle.color = _accent;
        }

        private static RectTransform NewRect(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
