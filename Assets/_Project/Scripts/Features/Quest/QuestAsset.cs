using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Данные квеста: стабильный id, title для HUD и упорядоченный список целей.
    /// Прогресс живёт не здесь, а в <see cref="QuestLog"/> на сцене (ADR 0027).
    /// </summary>
    [CreateAssetMenu(
        fileName = "Quest",
        menuName = "FrostSpace/Quest",
        order = 0)]
    public sealed class QuestAsset : ScriptableObject
    {
        [SerializeField] private string _questId;
        [SerializeField] private string _title;
        [TextArea(2, 4)]
        [SerializeField] private string _description;
        [SerializeField] private List<QuestObjective> _objectives = new();

        public string QuestId => _questId;

        public string Title => _title;

        public string Description => _description;

        public IReadOnlyList<QuestObjective> Objectives => _objectives;

        public QuestLog CreateLog() => new(this);

        /// <summary>Заполняет идентичность квеста; используется авторингом и тестами.</summary>
        public void SetIdentity(string questId, string title, string description = "")
        {
            _questId = questId;
            _title = title;
            _description = description;
        }

        /// <summary>Добавляет цель в конец очереди.</summary>
        public void AddObjective(QuestObjective objective)
        {
            if (objective != null)
                _objectives.Add(objective);
        }

        /// <summary>
        /// Проверка данных для инспектора, меню и тестов. Ошибки не дают квесту
        /// стартовать; предупреждений пока нет.
        /// </summary>
        public void CollectProblems(List<string> errors, List<string> warnings)
        {
            errors ??= new List<string>();
            warnings ??= new List<string>();

            if (string.IsNullOrWhiteSpace(_questId))
                errors.Add("Пустой questId.");

            if (_objectives.Count == 0)
            {
                errors.Add("В квесте нет ни одной цели.");
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _objectives.Count; i++)
            {
                var objective = _objectives[i];
                var label = objective != null && !string.IsNullOrWhiteSpace(objective.Id)
                    ? objective.Id
                    : $"#{i + 1}";

                if (objective == null)
                {
                    errors.Add($"Цель #{i + 1}: пустая запись списка.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(objective.Id))
                    errors.Add($"Цель {label}: пустой id.");
                else if (!ids.Add(objective.Id))
                    errors.Add($"Цель {label}: id повторяется.");

                if (string.IsNullOrWhiteSpace(objective.Text))
                    errors.Add($"Цель {label}: пустой текст.");
                if (string.IsNullOrWhiteSpace(objective.TargetId))
                    errors.Add($"Цель {label}: пустой targetId.");
                if (objective.Required <= 0)
                    errors.Add($"Цель {label}: required должен быть больше нуля.");
            }
        }

        private void OnValidate()
        {
            // Временные ассеты (тесты, превью) не должны засорять Console.
            if ((hideFlags & HideFlags.DontSave) != 0)
                return;
            if (string.IsNullOrWhiteSpace(_questId) && _objectives.Count == 0)
                return;

            var errors = new List<string>();
            CollectProblems(errors, null);
            for (var i = 0; i < errors.Count; i++)
                Debug.LogError($"Квест «{name}»: {errors[i]}", this);
        }
    }
}
