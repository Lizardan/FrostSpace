using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Одна реплика робота с переходами. Узел либо предлагает варианты ответа
    /// (<c>Choices</c>), либо идёт дальше по <c>NextId</c>; пустой переход — конец.
    ///
    /// Id присваивается один раз при создании и не меняется при перестановке узлов:
    /// переходы хранятся строкой, а не индексом списка.
    /// </summary>
    [System.Serializable]
    public sealed class DialogueNode
    {
        [SerializeField] private string _id;
        [TextArea(2, 6)]
        [SerializeField] private string _line;
        [SerializeField] private List<DialogueChoice> _choices = new();
        [Tooltip("Переход, когда вариантов нет. Пусто — конец диалога.")]
        [SerializeField] private string _nextId;

        public DialogueNode()
        {
        }

        public DialogueNode(string id, string line)
        {
            _id = id;
            _line = line;
        }

        /// <summary>Стабильный id узла, read-only в инспекторе.</summary>
        public string Id => _id;

        /// <summary>Реплика робота.</summary>
        public string Line => _line;

        /// <summary>Варианты ответа игрока. Пусто — реплика идёт дальше сама.</summary>
        public IReadOnlyList<DialogueChoice> Choices => _choices;

        /// <summary>Переход без вариантов; для узла с вариантами не используется.</summary>
        public string NextId => _nextId;

        /// <summary>Узел завершает диалог: ни вариантов, ни перехода.</summary>
        public bool IsTerminal => _choices.Count == 0 && string.IsNullOrWhiteSpace(_nextId);

        public void SetLine(string line) => _line = line;

        public void SetNextId(string nextId) => _nextId = nextId;

        /// <summary>Добавляет вариант без ограничения: лимит проверяет валидация.</summary>
        public DialogueChoice AddChoice(string text = "", string nextId = "")
        {
            var choice = new DialogueChoice(text, nextId);
            _choices.Add(choice);
            return choice;
        }

        public void RemoveChoiceAt(int index)
        {
            if (index >= 0 && index < _choices.Count)
                _choices.RemoveAt(index);
        }

        /// <summary>Копия с новым id: строки и варианты переносятся как есть.</summary>
        internal DialogueNode Clone(string id)
        {
            var copy = new DialogueNode(id, _line);
            copy._nextId = _nextId;
            for (var i = 0; i < _choices.Count; i++)
                copy._choices.Add(new DialogueChoice(_choices[i].Text, _choices[i].NextId));
            return copy;
        }

        /// <summary>Заменяет переходы на удалённый узел концом диалога.</summary>
        internal void Forget(string removedId)
        {
            if (_nextId == removedId)
                _nextId = string.Empty;

            for (var i = 0; i < _choices.Count; i++)
            {
                if (_choices[i].NextId == removedId)
                    _choices[i].SetNextId(string.Empty);
            }
        }
    }
}
