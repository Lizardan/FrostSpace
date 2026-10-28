using System.Collections.Generic;
using FrostSpace.Features.Npc;
using FrostSpace.Features.Scan;
using UnityEngine;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Маркер разговариваемого NPC на сценовом актёре (не на визуальном префабе —
    /// ADR 0004). Держит ассет диалога и останавливает патруль на время разговора.
    ///
    /// Имя и категория берутся у <see cref="ScanTarget"/>: подпись диалога и подпись
    /// сканера говорят об одном роботе одно и то же. Если маркера нет, имя чистится
    /// так же, как это делает сканер.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueSpeaker : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Патрульный NPC; пусто — ищется на этом объекте")]
        [SerializeField] private NpcController _npc;
        [Tooltip("Диалог робота")]
        [SerializeField] private DialogueAsset _dialogue;

        private bool _usable;

        /// <summary>Диалог робота.</summary>
        public DialogueAsset Dialogue => _dialogue;

        /// <summary>Разговор уже идёт: второй раз не открываем.</summary>
        public bool IsTalking { get; private set; }

        /// <summary>Имя в шапке панели.</summary>
        public string DisplayName { get; private set; } = "UNKNOWN";

        /// <summary>Категория в шапке панели.</summary>
        public string Category { get; private set; } = "NPC";

        /// <summary>
        /// С роботом можно поговорить: диалог назначен, граф без ошибок, разговор
        /// ещё не начат. Предупреждения (циклы, длинные реплики) не блокируют.
        /// </summary>
        public bool CanTalk => _usable && !IsTalking;

        private void Awake()
        {
            if (_npc == null)
                _npc = GetComponent<NpcController>();

            var target = ScanTarget.Find(transform);
            if (target == null)
                target = GetComponentInChildren<ScanTarget>(true);

            if (target != null)
            {
                DisplayName = target.DisplayName;
                Category = target.Category;
            }
            else
            {
                DisplayName = ScanTarget.Sanitize(name);
                Category = "NPC";
            }

            _usable = false;
            if (_dialogue == null)
                return;

            var errors = new List<string>();
            _dialogue.CollectProblems(errors, null);
            _usable = errors.Count == 0;
            if (!_usable)
            {
                Debug.LogError(
                    $"DialogueSpeaker: диалог «{_dialogue.name}» содержит ошибки — {errors[0]}",
                    this);
            }
        }

        /// <summary>Останавливает NPC и фиксирует собеседника.</summary>
        public void BeginConversation(Transform player)
        {
            if (!CanTalk || player == null)
                return;

            IsTalking = true;
            _npc?.EnterConversation(player);
        }

        /// <summary>Возвращает NPC на маршрут.</summary>
        public void EndConversation()
        {
            if (!IsTalking)
                return;

            IsTalking = false;
            _npc?.EndConversation();
        }
    }
}
