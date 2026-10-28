namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Состояние одного разговора: текущий узел и переходы. UI и NPC в сессию не
    /// входят, поэтому граф проверяется в EditMode без сцены.
    ///
    /// <see cref="Advance"/> и <see cref="Choose"/> возвращают <c>true</c>, только
    /// если разговор перешёл на другой узел. Конец диалога, битая ссылка и
    /// недопустимый выбор дают <c>false</c>; после них <see cref="IsFinished"/> истинно.
    /// </summary>
    public sealed class DialogueSession
    {
        private readonly DialogueAsset _asset;
        private DialogueNode _current;

        public DialogueSession(DialogueAsset asset)
        {
            _asset = asset;
            _current = asset != null ? asset.StartNode : null;
        }

        /// <summary>Ассет, по которому идёт разговор.</summary>
        public DialogueAsset Asset => _asset;

        /// <summary>Текущая реплика; <c>null</c> — разговор закончен.</summary>
        public DialogueNode Current => _current;

        /// <summary>У текущей реплики есть варианты ответа.</summary>
        public bool HasChoices => _current != null && _current.Choices.Count > 0;

        /// <summary>Разговор закончен: показывать больше нечего.</summary>
        public bool IsFinished => _current == null;

        /// <summary>
        /// Переход по <c>NextId</c>, когда вариантов нет. Для узла с вариантами
        /// возвращает <c>false</c> и ничего не меняет.
        /// </summary>
        public bool Advance()
        {
            if (_current == null || HasChoices)
                return false;

            return MoveTo(_current.NextId);
        }

        /// <summary>
        /// Выбор варианта по индексу. Индекс вне диапазона ничего не меняет.
        /// </summary>
        public bool Choose(int index)
        {
            if (_current == null || !HasChoices)
                return false;
            if (index < 0 || index >= _current.Choices.Count)
                return false;

            return MoveTo(_current.Choices[index].NextId);
        }

        private bool MoveTo(string id)
        {
            _current = _asset != null ? _asset.Find(id) : null;
            return _current != null;
        }
    }
}
