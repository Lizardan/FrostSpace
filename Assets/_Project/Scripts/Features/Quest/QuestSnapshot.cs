namespace FrostSpace.Features.Quest
{
    /// <summary>Снимок прогресса квеста для сохранения и тестов.</summary>
    public readonly struct QuestSnapshot
    {
        public QuestSnapshot(QuestState state, int objectiveIndex, int progress)
        {
            State = state;
            ObjectiveIndex = objectiveIndex;
            Progress = progress;
        }

        public QuestState State { get; }

        public int ObjectiveIndex { get; }

        public int Progress { get; }
    }
}
