using System;
using UnityEngine;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Вариант ответа игрока. Пустой <c>NextId</c> означает «конец диалога».
    /// Класс сериализуется внутри <see cref="DialogueNode"/>, поэтому поля — простые
    /// строки, без словарей и интерфейсов.
    /// </summary>
    [Serializable]
    public sealed class DialogueChoice
    {
        [SerializeField] private string _text;
        [Tooltip("Id узла-ответа. Пусто — конец диалога.")]
        [SerializeField] private string _nextId;

        public DialogueChoice()
        {
        }

        public DialogueChoice(string text, string nextId)
        {
            _text = text;
            _nextId = nextId;
        }

        /// <summary>Текст варианта в панели.</summary>
        public string Text => _text;

        /// <summary>Куда ведёт вариант; пусто — разговор заканчивается.</summary>
        public string NextId => _nextId;

        public void SetText(string text) => _text = text;

        public void SetNextId(string nextId) => _nextId = nextId;
    }
}
