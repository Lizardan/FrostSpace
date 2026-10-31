using System;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Scan;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Снаряжение курьера: карта и сканер выдаются диалогом (DP-03) и больше
    /// нигде не включаются. Флаги живут на игроке, а не в глобальной шине;
    /// выдача идемпотентна, поэтому повторный разговор ничего не дублирует.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Контроллер диалога: действия GrantScanner/GrantMap")]
        [SerializeField] private DialogueController _dialogue;
        [Tooltip("Сканер камеры: до выдачи молчит")]
        [SerializeField] private ObjectScanner _scanner;

        /// <summary>Сканер выдан.</summary>
        public bool HasScanner { get; private set; }

        /// <summary>Карта местности выдана.</summary>
        public bool HasMap { get; private set; }

        /// <summary>Набор снаряжения изменился.</summary>
        public event Action Changed;

        private void Awake()
        {
            if (_dialogue == null)
                _dialogue = GetComponent<DialogueController>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();
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
            switch (action)
            {
                case DialogueAction.GrantScanner:
                    if (HasScanner)
                        return;
                    HasScanner = true;
                    _scanner?.SetAvailable(true);
                    break;

                case DialogueAction.GrantMap:
                    if (HasMap)
                        return;
                    HasMap = true;
                    break;

                default:
                    return;
            }

            Changed?.Invoke();
        }
    }
}
