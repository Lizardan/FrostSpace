using System;
using System.Collections.Generic;
using FrostSpace.Features.Character;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Menu;
using FrostSpace.Features.Story;
using FrostSpace.Features.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrostSpace.Features.Inventory
{
    /// <summary>
    /// Окно экипировки и инвентаря по клавише <c>I</c>.
    ///
    /// Слева — блок с трёхмерным превью выбранного корпуса, внизу этого блока два
    /// слота: левый под сонар, правый под карту. Справа сверху — описание предмета,
    /// на который навели, справа снизу — сетка ячеек. В сетке живёт только
    /// оборудование: аккумуляторы остаются в переноске и сюда не ложатся.
    ///
    /// Геометрия и плотность квадратов взяты из игровых образцов: ячейка 56 px при
    /// зазоре 6 px, слот 72 px, панель 1180×720 — как панель паузы. Больше — и окно
    /// начинает съедать экран, как это было в первом макете.
    ///
    /// Открытое окно блокирует персонажа и камеру тем же набором, что и диалог
    /// (<see cref="Dialogue.DialogueController"/>), и отпускает курсор: игрок ходит
    /// мышью по слотам. Порядок снятия блокировки строго обратный порядку установки,
    /// иначе остаётся «застрявший» ввод.
    ///
    /// Всё состояние живёт в <see cref="EquipmentBoard"/>; экран только перекрашивает
    /// мишени по факту, а не по намерению, поэтому отказ модели не может выглядеть
    /// как состоявшееся перетаскивание.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class EquipmentScreen : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Снаряжение игрока: выдача, размещение, событие изменений")]
        [SerializeField] private PlayerEquipment _equipment;
        [Tooltip("Актёр игрока: превью его корпуса и блокировка ходьбы")]
        [SerializeField] private RobotLocomotion _locomotion;
        [Tooltip("Биндер визуала: от него берётся выбранный корпус")]
        [SerializeField] private PlayerVisualBinder _binder;
        [Tooltip("Риг камеры: пауза обзора и захват курсора")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Меню паузы: на время окна выключается, чтобы Esc не открыл его")]
        [SerializeField] private PauseMenuScreen _pause;
        [Tooltip("Ассет ввода; пусто — InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Шрифт")]
        [Tooltip("Кириллический TMP-ассет; пусто — шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Палитра")]
        [SerializeField] private Color _accent = new(0.302f, 0.902f, 1f, 1f);
        [SerializeField] private Color _text = new(0.72f, 0.86f, 0.92f, 1f);
        [SerializeField] private Color _dim = new(0.30f, 0.42f, 0.50f, 1f);
        [SerializeField] private Color _warm = new(1f, 0.72f, 0.42f, 1f);
        [SerializeField] private Color _panelColor = new(0.016f, 0.026f, 0.038f, 0.96f);
        [SerializeField] private Color _shadeColor = new(0.008f, 0.016f, 0.026f, 0.78f);

        // Геометрия окна в референсных пикселях 1920×1080.
        private const float PanelWidth = 1180f;
        private const float PanelHeight = 720f;
        private const float LeftPaneWidth = 380f;
        private const float PaneHeight = 660f;
        private const float RightPaneLeft = 400f;
        private const float DescriptionHeight = 236f;
        private const float GridPaneTop = 256f;
        private const float SlotSize = 72f;
        private const float SlotTop = 540f;
        private const float CellSize = 56f;
        private const float CellGap = 6f;
        private const int Columns = 8;
        private const int PreviewSlot = 7;   // рендер-сцена превью уведена под мир

        private GameObject _root;
        private CanvasGroup _group;
        private RawImage _previewImage;
        private CharacterCardPreview _preview;
        private TextMeshProUGUI _description;
        private readonly List<EquipmentTarget> _targets = new List<EquipmentTarget>();
        private readonly Dictionary<EquipmentKind, EquipmentTarget> _slots =
            new Dictionary<EquipmentKind, EquipmentTarget>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        private Image _ghost;
        private EquipmentTarget _dragSource;
        private EquipmentTarget _dropTarget;
        private EquipmentTarget _hovered;
        private EquipmentKind? _focus;
        private InputActionMap _playerMap;
        private bool _pauseSuspended;
        private bool _restorePause;

        /// <summary>Окно открыто.</summary>
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (_equipment == null)
                _equipment = GetComponentInParent<PlayerEquipment>();
            if (_locomotion == null)
                _locomotion = GetComponentInParent<RobotLocomotion>();
            if (_binder == null)
                _binder = GetComponentInParent<PlayerVisualBinder>();
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_pause == null)
                _pause = FindAnyObjectByType<PauseMenuScreen>();

            var source = _actions != null ? _actions : InputSystem.actions;
            _playerMap = source != null ? source.FindActionMap("Player", false) : null;

            EnsureEventSystem();
            Build();
            Hide();
        }

        private void OnEnable()
        {
            // Экран обязан зеркалить модель, а не только свои собственные действия:
            // без подписки снятая вне окна карта оставляла иконку в слоте.
            if (_equipment != null)
                _equipment.Changed += OnEquipmentChanged;
        }

        private void OnDisable()
        {
            if (_equipment != null)
                _equipment.Changed -= OnEquipmentChanged;

            if (IsOpen)
                Close();
        }

        private void OnDestroy()
        {
            // Domain reload выключен: всё, что создано кодом, обязано убирать себя само.
            if (_preview != null)
                _preview.Clear();

            for (var i = 0; i < _sprites.Count; i++)
                if (_sprites[i] != null)
                    Destroy(_sprites[i]);
            for (var i = 0; i < _textures.Count; i++)
                if (_textures[i] != null)
                    Destroy(_textures[i]);

            _sprites.Clear();
            _textures.Clear();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (IsOpen)
            {
                if (keyboard.iKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
                    Close();
                return;
            }

            if (keyboard.iKey.wasPressedThisFrame)
                TryOpen();
        }

        private void LateUpdate()
        {
            // Пауза возвращается после кадра закрытия: её Update уже прошёл, поэтому
            // Esc, которым закрыли окно, не откроет меню паузы тем же нажатием.
            if (!_restorePause)
                return;

            _restorePause = false;
            if (_pause != null)
                _pause.enabled = true;
        }

        /// <summary>
        /// Открывает окно, если это возможно: во время диалога, меню паузы и
        /// заставки прибытия — нельзя.
        /// </summary>
        public void TryOpen()
        {
            if (IsOpen || _equipment == null)
                return;
            if (_rig != null && _rig.IsPaused)
                return;
            if (_pause != null && _pause.IsPaused)
                return;

            Open();
        }

        public void Open()
        {
            if (IsOpen)
                return;

            IsOpen = true;
            _focus = null;
            _pauseSuspended = _pause != null && _pause.enabled;
            if (_pause != null)
                _pause.enabled = false;

            _locomotion?.SetControlLock(true);
            _playerMap?.Disable();
            if (_rig != null)
                _rig.SetPaused(true);

            ShowPreview();
            Refresh();
            _group.alpha = 1f;
            _root.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            _dragSource = null;
            _dropTarget = null;
            _hovered = null;

            if (_preview != null)
                _preview.Clear();

            if (_rig != null)
                _rig.SetPaused(false);
            _playerMap?.Enable();
            _locomotion?.SetControlLock(false);

            if (_pauseSuspended && _pause != null)
            {
                _pause.enabled = true;
                _pauseSuspended = false;
            }

            _restorePause = _pause != null;
            Hide();
        }

        /// <summary>
        /// Модель изменилась — в том числе вне окна (выдача диалогом, тест, другой
        /// экран). Открытое окно перерисовывается, закрытое просто дождётся открытия.
        /// </summary>
        private void OnEquipmentChanged()
        {
            if (IsOpen)
                Refresh();
        }

        private void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        // ------------------------------------------------------------------ состояние

        /// <summary>Чем занят слот или ячейка — по модели, а не по картинке.</summary>
        public EquipmentKind? ItemAt(EquipmentTarget target)
        {
            if (target == null || _equipment == null)
                return null;

            var board = _equipment.Board;
            return target.Kind == EquipmentTargetKind.Slot
                ? board.IsEquipped(target.SlotKind) ? target.SlotKind : null
                : board.ItemAt(target.CellIndex);
        }

        private void Refresh()
        {
            if (_equipment == null)
                return;

            var board = _equipment.Board;

            for (var i = 0; i < _targets.Count; i++)
                Paint(_targets[i], board);

            PaintDescription();
        }

        private void Paint(EquipmentTarget target, EquipmentBoard board)
        {
            var item = ItemAt(target);
            var dragging = _dragSource != null;
            var canDrop = dragging && CanAccept(_dragSource, target);

            if (target.Icon != null)
            {
                target.Icon.enabled = item.HasValue;
                if (item.HasValue)
                    target.Icon.sprite = IconFor(item.Value);
            }

            if (target.Background != null)
            {
                var color = item.HasValue ? _accent : _dim;
                var alpha = item.HasValue ? 0.62f : 0.24f;

                if (canDrop)
                {
                    color = _warm;
                    alpha = 0.85f;
                }

                if (target == _dragSource)
                    alpha *= 0.35f;

                target.Background.color = new Color(color.r, color.g, color.b, alpha);
            }

            if (target.Label != null && target.Kind == EquipmentTargetKind.Slot)
            {
                var granted = board.Has(target.SlotKind);
                target.Label.text = EquipmentInfo.ShortTitle(target.SlotKind);
                target.Label.color = !granted
                    ? new Color(_dim.r, _dim.g, _dim.b, 0.6f)
                    : item.HasValue ? _accent : _warm;
            }
        }

        private void PaintDescription()
        {
            if (_description == null || _equipment == null)
                return;

            var kind = _focus;
            if (!kind.HasValue)
            {
                _description.text =
                    "<size=18>ЭКИПИРОВКА</size>\n\n" +
                    "Наведи на слот или ячейку, чтобы прочитать предмет.\n" +
                    "Перетащи мышью: в слот — надеть, из слота — снять.";
                return;
            }

            var value = kind.Value;
            var board = _equipment.Board;
            var equipped = board.IsEquipped(value);
            var inGrid = board.Has(value) && !equipped;
            var state = !board.Has(value)
                ? "<color=#4D6B80>ЕЩЁ НЕ ВЫДАНО</color>"
                : equipped
                    ? $"<color=#4DE5EA>НАДЕТО · РАБОТАЕТ</color>"
                    : $"<color=#FFB86B>СНЯТО · {NotWorking(value)}</color>";

            var key = EquipmentInfo.KeyHint(value);
            var body = EquipmentInfo.Description(value);
            if (!string.IsNullOrEmpty(key))
                body += $"\n\nКлавиша: <color=#4DE5EA>{key}</color>";

            _description.text =
                $"<size=13><color=#4D6B80>{EquipmentInfo.KindLabel(value).ToUpperInvariant()}</color></size>\n" +
                $"<size=24><b>{EquipmentInfo.Title(value)}</b></size>\n" +
                $"{body}\n\n{state}" +
                (inGrid ? "\n<size=13>Лежит в сетке — перетащи в свой слот.</size>" : string.Empty);
        }

        private static string NotWorking(EquipmentKind kind)
        {
            return kind == EquipmentKind.Scanner ? "Q МОЛЧИТ" : "МИНИКАРТЫ НЕТ";
        }

        // ------------------------------------------------------------------ перетаскивание

        private static bool CanAccept(EquipmentTarget source, EquipmentTarget dest)
        {
            if (source == null || dest == null || source == dest)
                return false;

            var item = source.Item;
            if (!item.HasValue)
                return false;

            if (dest.Kind == EquipmentTargetKind.Slot)
                return dest.SlotKind == item.Value && !dest.Item.HasValue;

            return dest.Item.HasValue
                ? source.Kind == EquipmentTargetKind.Cell
                : true;
        }

        public void BeginDrag(EquipmentTarget source)
        {
            if (source == null || !source.Item.HasValue)
                return;

            _dragSource = source;
            _focus = source.Item;

            if (_ghost != null)
            {
                _ghost.sprite = IconFor(source.Item.Value);
                _ghost.enabled = true;
            }

            Refresh();
        }

        public void MoveDrag(PointerEventData data)
        {
            if (_dragSource == null)
                return;

            if (_ghost != null)
                _ghost.rectTransform.anchoredPosition = data.position;

            var found = Pick(data);
            if (found == _hovered)
                return;

            _hovered = found;
            Refresh();
        }

        public void MarkDropTarget(EquipmentTarget target) => _dropTarget = target;

        public void EndDrag(EquipmentTarget source)
        {
            if (_dragSource == null)
                return;

            var dest = _dropTarget ?? _hovered;
            var from = _dragSource;
            _dragSource = null;
            _dropTarget = null;
            _hovered = null;

            if (_ghost != null)
                _ghost.enabled = false;

            if (dest == null || dest == from || !CanAccept(from, dest))
            {
                Refresh();
                return;
            }

            var item = from.Item;
            if (!item.HasValue)
            {
                Refresh();
                return;
            }

            // Разложение решает модель. Экран ничего не «предполагает»: если ход
            // отвергнут, перерисовка вернёт прежнее состояние само.
            if (dest.Kind == EquipmentTargetKind.Slot)
                _equipment.SetEquipped(item.Value, true);
            else
                _equipment.MoveToCell(item.Value, dest.CellIndex);

            _focus = item;
            Refresh();
        }

        private EquipmentTarget Pick(PointerEventData data)
        {
            if (data.pointerCurrentRaycast.gameObject == null)
                return null;

            var found = data.pointerCurrentRaycast.gameObject.GetComponentInParent<EquipmentTarget>();
            return CanAccept(_dragSource, found) ? found : null;
        }

        /// <summary>
        /// Описание липкое: панель показывает последний наведённый предмет и не
        /// гаснет, когда курсор уходит в сторону. Экран читает состояние, а не
        /// следит за наведением, поэтому «пусто» здесь означало бы «ничего не видно».
        /// </summary>
        public void ShowDescription(EquipmentTarget target)
        {
            if (_dragSource != null)
                return;

            var item = target != null ? target.Item : null;
            if (!item.HasValue && target != null && target.Kind == EquipmentTargetKind.Slot)
                item = target.SlotKind;

            if (item == _focus)
                return;

            _focus = item;
            PaintDescription();
        }

        // ------------------------------------------------------------------ сборка

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private void Build()
        {
            _root = new GameObject("EquipmentCanvas", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Между диалогом (40) и паузой (50): окно снаряжения — модальное,
            // но разговор важнее, а меню паузы и так выключено под окном.
            canvas.sortingOrder = 45;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();
            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = true;
            _group.blocksRaycasts = true;

            var shade = NewImage(_root.transform, "Shade", _shadeColor);
            Stretch(shade.rectTransform);

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRect.anchoredPosition = Vector2.zero;

            BuildLeftPane(panel.transform);
            BuildDescription(panel.transform);
            BuildGrid(panel.transform);

            _ghost = new GameObject("DragGhost", typeof(RectTransform)).AddComponent<Image>();
            _ghost.transform.SetParent(_root.transform, false);
            _ghost.rectTransform.anchorMin = new Vector2(0f, 1f);
            _ghost.rectTransform.anchorMax = new Vector2(0f, 1f);
            _ghost.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _ghost.rectTransform.sizeDelta = new Vector2(CellSize, CellSize);
            _ghost.color = new Color(_warm.r, _warm.g, _warm.b, 0.85f);
            _ghost.preserveAspect = true;
            _ghost.raycastTarget = false;
            _ghost.enabled = false;
            _ghost.rectTransform.SetAsLastSibling();

            NewText(_root.transform, "Hint", "I  ·  ESC — ЗАКРЫТЬ", 15f,
                new Color(_dim.r, _dim.g, _dim.b, 0.9f), TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(-48f, -40f), new Vector2(420f, 26f), 8f);
        }

        private void BuildLeftPane(Transform parent)
        {
            var pane = NewPane(parent, Vector2.zero, new Vector2(LeftPaneWidth, PaneHeight));

            NewText(pane.transform, "Title", "КУРЬЕР", 15f, _accent, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(200f, 22f), 8f);

            var previewObject = new GameObject("Preview", typeof(RectTransform));
            previewObject.transform.SetParent(pane.transform, false);
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.anchorMin = new Vector2(0f, 1f);
            previewRect.anchorMax = new Vector2(0f, 1f);
            previewRect.pivot = new Vector2(0f, 1f);
            previewRect.sizeDelta = new Vector2(340f, 470f);
            previewRect.anchoredPosition = new Vector2(20f, -44f);

            _previewImage = previewObject.AddComponent<RawImage>();
            _previewImage.raycastTarget = false;
            _preview = previewObject.AddComponent<CharacterCardPreview>();
            _preview.Configure(true, new Vector2Int(340, 470), 30f, 1f);

            var scan = BuildSlot(pane.transform, EquipmentKind.Scanner, 74f);
            var map = BuildSlot(pane.transform, EquipmentKind.Map, 234f);
            _slots[EquipmentKind.Scanner] = scan;
            _slots[EquipmentKind.Map] = map;
        }

        private EquipmentTarget BuildSlot(Transform parent, EquipmentKind kind, float x)
        {
            var go = new GameObject("Slot_" + kind, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(SlotSize, SlotSize);
            rect.anchoredPosition = new Vector2(x, -SlotTop);

            var background = go.AddComponent<Image>();
            background.sprite = CellSprite;
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            var icon = NewImage(go.transform, "Icon", Color.white);
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(40f, 40f);
            iconRect.anchoredPosition = Vector2.zero;
            icon.sprite = IconFor(kind);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var label = NewText(go.transform, "Label", string.Empty, 12f, _dim,
                TextAlignmentOptions.Top, new Vector2(0.5f, 0f), new Vector2(0f, -6f),
                new Vector2(140f, 18f), 4f);

            var target = go.AddComponent<EquipmentTarget>();
            target.Setup(this, EquipmentTargetKind.Slot, kind, -1, background, icon, label);
            _targets.Add(target);
            return target;
        }

        private void BuildDescription(Transform parent)
        {
            var width = PanelWidth - RightPaneLeft;
            var pane = NewPane(parent, new Vector2(RightPaneLeft, 0f),
                new Vector2(width, DescriptionHeight));

            NewText(pane.transform, "Title", "ОПИСАНИЕ", 15f, _accent, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(240f, 22f), 8f);

            _description = NewText(pane.transform, "Text", string.Empty, 16f, _text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(16f, -52f),
                new Vector2(width - 32f, DescriptionHeight - 68f), 1f);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.raycastTarget = false;
        }

        private void BuildGrid(Transform parent)
        {
            var cells = _equipment != null ? _equipment.Board.CellCount : Columns * 3;
            var rows = Mathf.Max(1, Mathf.CeilToInt(cells / (float)Columns));
            var width = PanelWidth - RightPaneLeft;
            var height = PaneHeight - GridPaneTop;

            var pane = NewPane(parent, new Vector2(RightPaneLeft, GridPaneTop),
                new Vector2(width, height));

            NewText(pane.transform, "Title", "ИНВЕНТАРЬ", 15f, _accent, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(240f, 22f), 8f);

            NewText(pane.transform, "Note", "ОБОРУДОВАНИЕ · АККУМУЛЯТОРЫ НЕСУТ В РУКАХ", 12f,
                new Color(_dim.r, _dim.g, _dim.b, 0.85f), TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(420f, 18f), 3f);

            var holder = new GameObject("Cells", typeof(RectTransform));
            holder.transform.SetParent(pane.transform, false);
            var holderRect = (RectTransform)holder.transform;
            holderRect.anchorMin = new Vector2(0f, 1f);
            holderRect.anchorMax = new Vector2(0f, 1f);
            holderRect.pivot = new Vector2(0f, 1f);
            holderRect.sizeDelta = new Vector2(
                Columns * CellSize + (Columns - 1) * CellGap,
                rows * CellSize + (rows - 1) * CellGap);
            holderRect.anchoredPosition = new Vector2(16f, -46f);

            for (var i = 0; i < cells; i++)
            {
                var column = i % Columns;
                var row = i / Columns;

                var go = new GameObject("Cell_" + i, typeof(RectTransform));
                go.transform.SetParent(holderRect, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(CellSize, CellSize);
                rect.anchoredPosition = new Vector2(
                    column * (CellSize + CellGap), -row * (CellSize + CellGap));

                var background = go.AddComponent<Image>();
                background.sprite = CellSprite;
                background.type = Image.Type.Sliced;
                background.raycastTarget = true;

                var icon = NewImage(go.transform, "Icon", Color.white);
                var iconRect = icon.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(34f, 34f);
                iconRect.anchoredPosition = Vector2.zero;
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                var target = go.AddComponent<EquipmentTarget>();
                target.Setup(this, EquipmentTargetKind.Cell, default, i, background, icon, null);
                _targets.Add(target);
            }
        }

        private void ShowPreview()
        {
            var profile = _binder != null ? _binder.Profile : null;
            if (profile == null || _preview == null)
                return;

            _preview.Show(profile, _previewImage, PreviewSlot);
        }

        // ------------------------------------------------------------------ материалы

        private Sprite CellSprite
        {
            get
            {
                const string name = "EquipmentCell";
                var existing = FindSprite(name);
                if (existing != null)
                    return existing;

                var texture = Paint(64, 64, name, (x, y, w, h) =>
                {
                    var edge = Mathf.Min(x, y, w - 1 - x, h - 1 - y);
                    var alpha = edge < 2 ? 1f : edge < 4 ? 0.45f : 0.12f;
                    return new Color(1f, 1f, 1f, alpha);
                });

                var sprite = Sprite.Create(
                    texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(5f, 5f, 5f, 5f));
                sprite.name = name;
                _sprites.Add(sprite);
                return sprite;
            }
        }

        private Sprite IconFor(EquipmentKind kind)
        {
            var name = "EquipmentIcon_" + kind;
            var existing = FindSprite(name);
            if (existing != null)
                return existing;

            var texture = Paint(64, 64, name, (x, y, w, h) =>
            {
                var cx = (w - 1) * 0.5f;
                var cy = (h - 1) * 0.5f;
                var dx = x - cx;
                var dy = y - cy;
                var radius = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx);
                var alpha = 0f;

                if (kind == EquipmentKind.Scanner)
                {
                    // Центр и две пары дуг: сонар расходится в обе стороны.
                    if (radius < 4f)
                        alpha = 1f;
                    else if (radius > 12f && radius < 15f && Mathf.Abs(Mathf.Cos(angle)) > 0.55f)
                        alpha = 0.95f;
                    else if (radius > 22f && radius < 25f && Mathf.Abs(Mathf.Cos(angle)) > 0.62f)
                        alpha = 0.55f;
                }
                else
                {
                    // Диск карты, внутреннее кольцо и метка цели.
                    if (Mathf.Abs(radius - 26f) < 2f)
                        alpha = 1f;
                    else if (Mathf.Abs(radius - 14f) < 1.6f)
                        alpha = 0.5f;
                    else if (Mathf.Sqrt((dx - 11f) * (dx - 11f) + (dy - 11f) * (dy - 11f)) < 4f)
                        alpha = 1f;
                }

                return new Color(1f, 1f, 1f, alpha);
            });

            var sprite = Sprite.Create(
                texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private Texture2D Paint(int width, int height, string name, Func<int, int, int, int, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    texture.SetPixel(x, y, pixel(x, y, width, height));

            texture.Apply();
            _textures.Add(texture);
            return texture;
        }

        private Sprite FindSprite(string name)
        {
            for (var i = 0; i < _sprites.Count; i++)
                if (_sprites[i] != null && _sprites[i].name == name)
                    return _sprites[i];
            return null;
        }

        // ------------------------------------------------------------------ конструкторы UI

        private GameObject NewPane(Transform parent, Vector2 topLeft, Vector2 size)
        {
            var pane = new GameObject("Pane", typeof(RectTransform));
            pane.transform.SetParent(parent, false);
            var rect = (RectTransform)pane.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            // Якорь и пивот в верхнем левом углу: положительный Y поднимает блок
            // над панелью, а не опускает. Сетка уезжала за верх экрана именно так.
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);

            var background = pane.AddComponent<Image>();
            background.color = new Color(_panelColor.r, _panelColor.g, _panelColor.b, 0.55f);
            background.raycastTarget = false;

            var edge = NewImage(pane.transform, "EdgeLine", _accent);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 2f);
            edgeRect.anchoredPosition = Vector2.zero;

            Corner(pane.transform, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Corner(pane.transform, new Vector2(1f, 0f), new Vector2(1f, 0f));
            return pane;
        }

        private void Corner(Transform parent, Vector2 anchor, Vector2 pivot)
        {
            var horizontal = NewImage(parent, "Corner", _accent);
            var rect = horizontal.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(18f, 1f);
            rect.anchoredPosition = Vector2.zero;

            var vertical = NewImage(parent, "Corner", _accent);
            var vrect = vertical.rectTransform;
            vrect.anchorMin = anchor;
            vrect.anchorMax = anchor;
            vrect.pivot = pivot;
            vrect.sizeDelta = new Vector2(1f, 18f);
            vrect.anchoredPosition = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI NewText(
            Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 position,
            Vector2 boxSize, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(
                anchor.x <= 0f ? 0f : anchor.x >= 1f ? 1f : 0.5f,
                anchor.y <= 0f ? 0f : anchor.y >= 1f ? 1f : 0.5f);
            rect.sizeDelta = boxSize;
            rect.anchoredPosition = position;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }
    }
}
