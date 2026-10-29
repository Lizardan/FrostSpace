namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Событие участника сцены для <see cref="QuestLog"/>. Директор создаёт его
    /// напрямую, подписок и глобальной шины нет (ADR 0027).
    /// </summary>
    public readonly struct QuestEvent
    {
        public QuestEvent(QuestObjectiveKind kind, string targetId, int amount = 1)
        {
            Kind = kind;
            TargetId = targetId;
            Amount = amount;
        }

        public QuestObjectiveKind Kind { get; }

        public string TargetId { get; }

        public int Amount { get; }
    }
}
