using System;
using UnityEngine;

namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Одна цель квеста. Id стабилен и уникален внутри квеста; targetId — id
    /// собеседника, тип предмета или зоны, которые закрывают цель.
    /// </summary>
    [Serializable]
    public sealed class QuestObjective
    {
        [SerializeField] private string _id;
        [SerializeField] private string _text;
        [SerializeField] private QuestObjectiveKind _kind;
        [Tooltip("Id собеседника, типа предмета или зоны")]
        [SerializeField] private string _targetId;
        [Min(1)]
        [SerializeField] private int _required = 1;

        public QuestObjective()
        {
        }

        public QuestObjective(
            string id,
            string text,
            QuestObjectiveKind kind,
            string targetId,
            int required = 1)
        {
            _id = id;
            _text = text;
            _kind = kind;
            _targetId = targetId;
            _required = required;
        }

        public string Id => _id;

        public string Text => _text;

        public QuestObjectiveKind Kind => _kind;

        public string TargetId => _targetId;

        public int Required => _required;

        /// <summary>Меняет текст цели; используется авторингом и миграцией данных.</summary>
        public void SetText(string text) => _text = text;
    }
}
