using System;
using FrostSpace.Features.Carry;
using FrostSpace.Features.Character;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Progression;
using UnityEngine;

namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Сценовый владелец прогресса: создаёт <see cref="QuestLog"/>, восстанавливает
    /// его из сохранения, переводит в него явные события диалога и слотов и пишет
    /// прогресс обратно (ADR 0031). Глобальной шины нет; ссылки заданы
    /// в инспекторе (ADR 0027).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-5)]
    public sealed class QuestDirector : MonoBehaviour
    {
        [SerializeField] private QuestAsset _quest;
        [SerializeField] private DialogueController _dialogue;
        [SerializeField] private CarrySlot[] _slots = Array.Empty<CarrySlot>();

        [Tooltip("R13: после погрузки получает второй диалог")]
        [SerializeField] private DialogueSpeaker _speaker;
        [Tooltip("Диалог похвалы, который включается после четвёртого слота")]
        [SerializeField] private DialogueAsset _completedDialogue;

        [Header("Награда")]
        [Tooltip("Опыт игрока; пусто — награда не начисляется")]
        [SerializeField] private PlayerExperience _experience;
        [SerializeField] private int _completionExperience = 250;

        private QuestLog _log;
        private bool _restoredCompleted;

        /// <summary>Прогресс сцены; <c>null</c>, если ассет не назначен.</summary>
        public QuestLog Log => _log;

        public QuestAsset Quest => _quest;

        /// <summary>Квест перешёл в <c>Completed</c>; событие приходит один раз.</summary>
        public event Action<QuestLog> QuestCompleted;

        private void Awake()
        {
            _log = _quest != null ? _quest.CreateLog() : null;

            if (_quest == null)
                Debug.LogError("QuestDirector: не назначен QuestAsset", this);
            if (_dialogue == null)
                Debug.LogError("QuestDirector: не назначен DialogueController", this);

            Restore();
        }

        /// <summary>
        /// Завершённый квест из сохранения сообщаем после всех <c>OnEnable</c>:
        /// финал вагона подписывается на <see cref="QuestCompleted"/> и должен
        /// услышать событие, иначе «Продолжить» после сдачи квеста ведёт в тупик.
        /// Опыт при восстановлении не начисляется — он уже был выдан в прошлой сессии.
        /// </summary>
        private void Start()
        {
            if (!_restoredCompleted)
                return;

            _restoredCompleted = false;
            QuestCompleted?.Invoke(_log);
        }

        /// <summary>
        /// Поднимает прогресс из ключа `FrostSpace.Save`, если сохранение наше:
        /// тот же квест и читаемое состояние. Чужие сохранения игнорируются.
        /// </summary>
        private void Restore()
        {
            if (_log == null || _quest == null)
                return;
            if (!ProgressSave.TryLoad(out var data))
                return;
            if (!string.Equals(data.questId, _quest.QuestId, StringComparison.Ordinal))
                return;
            if (!ProgressSave.TryGetState(data, out var state))
                return;
            if (!_log.Restore(new QuestSnapshot(state, data.objective, data.progress)))
                return;

            _restoredCompleted = _log.IsCompleted;

            // После восстановления сцена стоит в исходной расстановке, а счётчик
            // уже частично закрыт (ADR 0031): добирать груз игрок начинает с того
            // места, где остановился, — это допустимое упрощение демо.

            // Финальная цель — разговор: после перезапуска R13 должен получить
            // диалог похвалы, иначе CompleteQuest из вступления недостижим.
            if (_log.CurrentObjective != null &&
                _log.CurrentObjective.Kind == QuestObjectiveKind.Talk &&
                _log.ObjectiveIndex == _log.ObjectiveCount - 1 &&
                _speaker != null &&
                _completedDialogue != null)
            {
                _speaker.SetDialogue(_completedDialogue);
            }
        }

        /// <summary>Пишет текущий прогресс в сохранение; неактивный квест не пишется.</summary>
        private void Persist()
        {
            if (_log == null || _quest == null || _log.State == QuestState.Inactive)
                return;

            var characterId = CharacterSelection.Current != null
                ? CharacterSelection.Current.Id
                : CharacterSelection.SavedId;
            var scene = gameObject.scene.name;
            ProgressSave.Write(scene, characterId, _quest.QuestId, _log.Snapshot());
        }

        private void OnEnable()
        {
            if (_dialogue != null)
                _dialogue.ActionInvoked += OnDialogueAction;

            if (_slots == null)
                return;

            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                    _slots[i].Placed += OnPlaced;
            }
        }

        private void OnDisable()
        {
            if (_dialogue != null)
                _dialogue.ActionInvoked -= OnDialogueAction;

            if (_slots == null)
                return;

            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                    _slots[i].Placed -= OnPlaced;
            }
        }

        private void OnDialogueAction(DialogueAction action, string questId)
        {
            if (_log == null || action == DialogueAction.None)
                return;
            if (!string.IsNullOrWhiteSpace(questId) &&
                !string.Equals(questId, _quest.QuestId, StringComparison.Ordinal))
            {
                return;
            }

            switch (action)
            {
                case DialogueAction.AcceptQuest:
                    if (!_log.Activate())
                        return;
                    CompleteTalkObjective();
                    Persist();
                    break;

                case DialogueAction.CompleteQuest:
                    CompleteTalkObjective();
                    break;
            }
        }

        /// <summary>
        /// Разговор, в котором показан узел действия, и есть цель Talk: закрываем
        /// её сразу. Для Accept это цель «поговорить», для Complete — финальная.
        /// </summary>
        private void CompleteTalkObjective()
        {
            var objective = _log?.CurrentObjective;
            if (objective == null || objective.Kind != QuestObjectiveKind.Talk)
                return;

            ApplyQuestEvent(new QuestEvent(QuestObjectiveKind.Talk, objective.TargetId));
        }

        private void OnPlaced(CarrySlot slot, Carryable item)
        {
            if (_log == null || item == null)
                return;

            var previousObjective = _log.ObjectiveIndex;
            ApplyQuestEvent(new QuestEvent(QuestObjectiveKind.Deliver, item.ItemType));

            if (_log.ObjectiveIndex != previousObjective &&
                _log.CurrentObjective != null &&
                _log.CurrentObjective.Kind == QuestObjectiveKind.Talk &&
                _speaker != null &&
                _completedDialogue != null)
            {
                _speaker.SetDialogue(_completedDialogue);
            }
        }

        /// <summary>
        /// Применяет событие и один раз начисляет награду в момент перехода
        /// квеста в <c>Completed</c>.
        /// </summary>
        private void ApplyQuestEvent(QuestEvent questEvent)
        {
            if (_log == null)
                return;

            var wasCompleted = _log.IsCompleted;
            _log.Apply(questEvent);
            Persist();
            if (wasCompleted || !_log.IsCompleted)
                return;

            _experience?.AddExperience(_completionExperience);
            QuestCompleted?.Invoke(_log);
        }
    }
}
