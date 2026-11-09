using System;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Inventory;
using FrostSpace.Features.Scan;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Снаряжение курьера: карта и сканер выдаются диалогом (DP-03) и больше нигде
    /// не включаются. Флаги живут на игроке, а не в глобальной шине; выдача
    /// идемпотентна, поэтому повторный разговор ничего не дублирует.
    ///
    /// «Выдано» и «надето» — два разных состояния, и путать их нельзя. Выданное
    /// лежит на игроке всегда, надетое — только пока предмет в своём слоте.
    /// Снятое оборудование обязано молчать: снял карту — миникарты нет, снял сонар —
    /// Q не пускает волну. Иначе окно экипировки было бы картинкой, а не игрой.
    ///
    /// Разложение по состоянию держит <see cref="EquipmentBoard"/>, а здесь — только
    /// связь с выдачей, сканером и подписчиками события.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Контроллер диалога: действия GrantScanner/GrantMap")]
        [SerializeField] private DialogueController _dialogue;
        [Tooltip("Сканер камеры: работает, только когда предмет надет")]
        [SerializeField] private ObjectScanner _scanner;

        [Header("Инвентарь")]
        [Tooltip("Ячеек в сетке инвентаря. Столько же рисует экран экипировки")]
        [SerializeField] private int _cellCount = 24;

        private EquipmentBoard _board;

        /// <summary>Сканер выдан.</summary>
        public bool HasScanner => Board.Has(EquipmentKind.Scanner);

        /// <summary>Карта местности выдана.</summary>
        public bool HasMap => Board.Has(EquipmentKind.Map);

        /// <summary>Доска с размещением снаряжения. Создаётся при первом обращении.</summary>
        public EquipmentBoard Board
        {
            get
            {
                // EditMode-тесты не вызывают Awake, поэтому доска строится лениво:
                // иначе первый же вызов Apply() упал бы на null.
                if (_board == null)
                    _board = new EquipmentBoard(Mathf.Max(1, _cellCount));
                return _board;
            }
        }

        /// <summary>Набор снаряжения или его размещение изменились.</summary>
        public event Action Changed;

        private void Awake()
        {
            if (_dialogue == null)
                _dialogue = GetComponent<DialogueController>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();

            var unused = Board;
        }

        private void OnEnable()
        {
            if (_dialogue != null)
                _dialogue.ActionInvoked += OnDialogueAction;
        }

        private void OnDisable()
        {
            if (_dialogue != null)
                _dialogue.ActionInvoked -= OnDialogueAction;
        }

        private void OnDialogueAction(DialogueAction action, string questId)
        {
            Apply(action);
        }

        /// <summary>
        /// Применяет действие выдачи. Повторный вызов с тем же флагом ничего
        /// не меняет и не поднимает <see cref="Changed"/>.
        /// </summary>
        public void Apply(DialogueAction action)
        {
            if (!TryKind(action, out var kind))
                return;

            // Выданное сразу надевается: по замыслу владельца оборудование
            // «появляется там, когда нам выдают», а не ждёт в сетке.
            if (!Board.Grant(kind))
                return;

            SyncScanner();
            Changed?.Invoke();
        }

        /// <summary>Предмет выдан и может лежать в слоте или в сетке.</summary>
        public bool Has(EquipmentKind kind) => Board.Has(kind);

        /// <summary>Предмет лежит в своём слоте, то есть работает.</summary>
        public bool IsEquipped(EquipmentKind kind) => Board.IsEquipped(kind);

        /// <summary>
        /// Надевает предмет в его слот или снимает его в первую свободную ячейку.
        /// Возвращает false, если ход невозможен: предмет не выдан, состояние уже
        /// такое, или сетка забита и снимать некуда.
        /// </summary>
        public bool SetEquipped(EquipmentKind kind, bool equipped)
        {
            var board = Board;
            if (!board.Has(kind))
                return false;

            var changed = equipped ? board.Equip(kind) : UnequipToFreeCell(kind, board);
            if (!changed)
                return false;

            SyncScanner();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Перекладывает предмет в заданную ячейку и синхронизирует снаряжение.
        /// Экран вызывает этот метод, а не доску напрямую: снятый перетаскиванием
        /// сканер обязан так же отключиться, как снятый кнопкой.
        /// </summary>
        public bool MoveToCell(EquipmentKind kind, int cell)
        {
            if (!Board.MoveToCell(kind, cell))
                return false;

            SyncScanner();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Единственный источник истины для сканера: он работает тогда и только
        /// тогда, когда сонар надет.
        /// </summary>
        private void SyncScanner()
        {
            _scanner?.SetAvailable(Board.IsEquipped(EquipmentKind.Scanner));
        }

        private bool UnequipToFreeCell(EquipmentKind kind, EquipmentBoard board)
        {
            if (!board.IsEquipped(kind))
                return false;

            for (var cell = 0; cell < board.CellCount; cell++)
                if (board.MoveToCell(kind, cell))
                    return true;

            return false;
        }

        private static bool TryKind(DialogueAction action, out EquipmentKind kind)
        {
            switch (action)
            {
                case DialogueAction.GrantScanner:
                    kind = EquipmentKind.Scanner;
                    return true;

                case DialogueAction.GrantMap:
                    kind = EquipmentKind.Map;
                    return true;

                default:
                    kind = default;
                    return false;
            }
        }
    }
}
