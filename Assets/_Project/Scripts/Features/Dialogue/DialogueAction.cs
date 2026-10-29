namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Действие, которое узел диалога вызывает один раз за показ (ADR 0027).
    /// <see cref="None"/> оставляет старые диалоги без изменений.
    /// </summary>
    public enum DialogueAction
    {
        None,
        AcceptQuest,
        CompleteQuest,
        GrantScanner,
        GrantMap
    }
}
