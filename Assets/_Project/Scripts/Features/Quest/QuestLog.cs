using System;
using UnityEngine;

namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Прогресс одного квеста. Обычный C#-класс без Unity: цели идут строго по
    /// порядку, событие следующей цели игнорируется, пока не закрыта текущая.
    /// Поэтому шаги проверяются в EditMode без сцены (ADR 0027).
    /// </summary>
    public sealed class QuestLog
    {
        private readonly QuestAsset _quest;
        private int _objectiveIndex;
        private int _progress;

        public QuestLog(QuestAsset quest)
        {
            _quest = quest;
        }

        /// <summary>Ассет, по которому идёт прогресс; может быть <c>null</c>.</summary>
        public QuestAsset Quest => _quest;

        public QuestState State { get; private set; } = QuestState.Inactive;

        public int ObjectiveIndex => _objectiveIndex;

        public int Progress => _progress;

        public int ObjectiveCount => _quest != null ? _quest.Objectives.Count : 0;

        public bool IsActive => State == QuestState.Active;

        public bool IsCompleted => State == QuestState.Completed;

        /// <summary>Текущая цель; <c>null</c> до старта и после завершения.</summary>
        public QuestObjective CurrentObjective
        {
            get
            {
                if (State != QuestState.Active || _quest == null)
                    return null;

                var objectives = _quest.Objectives;
                return _objectiveIndex >= 0 && _objectiveIndex < objectives.Count
                    ? objectives[_objectiveIndex]
                    : null;
            }
        }

        /// <summary>
        /// Переводит квест из <c>Inactive</c> в <c>Active</c>. Повторный вызов,
        /// квест без целей и квест с пустой первой целью ничего не меняют.
        /// </summary>
        public bool Activate()
        {
            if (State != QuestState.Inactive || ObjectiveCount == 0)
                return false;

            if (_quest.Objectives[0] == null)
                return false;

            State = QuestState.Active;
            _objectiveIndex = 0;
            _progress = 0;
            return true;
        }

        /// <summary>
        /// Применяет событие к текущей цели. Несовпадающий тип, targetId или
        /// неположительное количество игнорируются. Возвращает <c>true</c>, если
        /// прогресс изменился.
        /// </summary>
        public bool Apply(QuestEvent questEvent)
        {
            if (State != QuestState.Active || questEvent.Amount <= 0)
                return false;

            var objective = CurrentObjective;
            if (objective == null)
                return false;
            if (objective.Kind != questEvent.Kind)
                return false;
            if (!string.Equals(objective.TargetId, questEvent.TargetId, StringComparison.Ordinal))
                return false;

            var required = Mathf.Max(1, objective.Required);
            _progress = Mathf.Min(required, _progress + questEvent.Amount);
            if (_progress < required)
                return true;

            _objectiveIndex++;
            _progress = 0;
            if (_objectiveIndex >= ObjectiveCount)
            {
                _objectiveIndex = ObjectiveCount;
                State = QuestState.Completed;
            }

            return true;
        }

        /// <summary>Снимок текущего прогресса для сохранения и тестов.</summary>
        public QuestSnapshot Snapshot() => new(State, _objectiveIndex, _progress);

        /// <summary>
        /// Восстанавливает прогресс из сохранения (ADR 0031). Принимает только
        /// активный квест с существующей целью и завершённый; неактивный снимок
        /// и индекс вне списка отвергаются, чтобы сцена не стартовала сломанной.
        /// Возвращает <c>true</c>, если состояние изменилось.
        /// </summary>
        public bool Restore(QuestSnapshot snapshot)
        {
            if (_quest == null || ObjectiveCount == 0)
                return false;

            switch (snapshot.State)
            {
                case QuestState.Completed:
                    State = QuestState.Completed;
                    _objectiveIndex = ObjectiveCount;
                    _progress = 0;
                    return true;

                case QuestState.Active:
                    if (snapshot.ObjectiveIndex < 0 || snapshot.ObjectiveIndex >= ObjectiveCount)
                        return false;
                    if (_quest.Objectives[snapshot.ObjectiveIndex] == null)
                        return false;

                    State = QuestState.Active;
                    _objectiveIndex = snapshot.ObjectiveIndex;
                    _progress = Mathf.Clamp(
                        snapshot.Progress, 0, Mathf.Max(1, CurrentObjective.Required));
                    return true;

                default:
                    return false;
            }
        }
    }
}
