using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Диалог как граф реплик. Ассет — только данные: переходы хранятся по стабильным
    /// строковым id, поэтому перестановка узлов ничего не ломает. Ошибки и
    /// предупреждения собирает <see cref="CollectProblems"/>; её вызывают инспектор,
    /// меню проверки и EditMode-тесты.
    ///
    /// Методы добавления/удаления узлов — часть авторинга: инспектор пишет через них,
    /// чтобы удаление переводило ссылки на узел в «Конец», а дубликат получал новый id.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Dialogue",
        menuName = "FrostSpace/Dialogue",
        order = 0)]
    public sealed class DialogueAsset : ScriptableObject
    {
        /// <summary>Столько вариантов ответа помещается в панель.</summary>
        public const int MaxChoices = 4;

        /// <summary>Длина реплики, после которой инспектор предупреждает о переполнении.</summary>
        public const int LongLineWarning = 300;

        [Tooltip("С какого узла начинается разговор")]
        [SerializeField] private string _startId;
        [SerializeField] private List<DialogueNode> _nodes = new();

        /// <summary>Id стартового узла.</summary>
        public string StartId => _startId;

        /// <summary>Узлы в порядке, заданном автором.</summary>
        public IReadOnlyList<DialogueNode> Nodes => _nodes;

        /// <summary>Стартовый узел или <c>null</c>, если id пуст/не найден.</summary>
        public DialogueNode StartNode => Find(_startId);

        /// <summary>Узел по id; <c>null</c> для пустой строки и неизвестного id.</summary>
        public DialogueNode Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] != null && _nodes[i].Id == id)
                    return _nodes[i];
            }

            return null;
        }

        /// <summary>Новая runtime-сессия этого диалога.</summary>
        public DialogueSession CreateSession() => new(this);

        /// <summary>
        /// Новый узел в конце. Первый узел автоматически становится стартовым.
        /// </summary>
        public DialogueNode AddNode(string line = "")
        {
            var node = new DialogueNode(NewId(), line);
            _nodes.Add(node);
            if (string.IsNullOrWhiteSpace(_startId))
                _startId = node.Id;
            return node;
        }

        /// <summary>Копия узла сразу после оригинала; id всегда новый.</summary>
        public DialogueNode DuplicateNode(int index)
        {
            if (index < 0 || index >= _nodes.Count || _nodes[index] == null)
                return null;

            var copy = _nodes[index].Clone(NewId());
            _nodes.Insert(index + 1, copy);
            return copy;
        }

        /// <summary>
        /// Удаляет узел и переводит все ссылки на него в «Конец». Стартовым становится
        /// первый оставшийся узел; если узлов не осталось — старт пуст.
        /// </summary>
        public void RemoveNode(int index)
        {
            if (index < 0 || index >= _nodes.Count)
                return;

            var removed = _nodes[index];
            _nodes.RemoveAt(index);

            if (removed != null)
            {
                for (var i = 0; i < _nodes.Count; i++)
                    _nodes[i]?.Forget(removed.Id);

                if (_startId == removed.Id)
                    _startId = _nodes.Count > 0 ? _nodes[0].Id : string.Empty;
            }
        }

        /// <summary>Назначает стартовый узел; <c>null</c> очищает старт.</summary>
        public void SetStart(DialogueNode node)
        {
            _startId = node != null ? node.Id : string.Empty;
        }

        /// <summary>
        /// Полная проверка графа. Ошибки не дают диалогу открыться, предупреждения
        /// допустимы: циклы и длинные реплики — авторское решение.
        /// </summary>
        public void CollectProblems(List<string> errors, List<string> warnings)
        {
            errors ??= new List<string>();
            warnings ??= new List<string>();

            if (_nodes.Count == 0)
            {
                errors.Add("В диалоге нет ни одного узла.");
                return;
            }

            var byId = new Dictionary<string, DialogueNode>();
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null)
                {
                    errors.Add($"Узел #{i + 1}: пустая запись списка.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    errors.Add($"Узел #{i + 1}: пустой id.");
                    continue;
                }

                if (byId.ContainsKey(node.Id))
                    errors.Add($"Узел {node.Id}: id повторяется.");
                else
                    byId.Add(node.Id, node);
            }

            if (string.IsNullOrWhiteSpace(_startId))
                errors.Add("Не выбран стартовый узел.");
            else if (!byId.ContainsKey(_startId))
                errors.Add($"Стартовый узел {_startId} не найден.");

            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null)
                    continue;

                var label = string.IsNullOrWhiteSpace(node.Id) ? $"#{i + 1}" : node.Id;

                if (string.IsNullOrWhiteSpace(node.Line))
                    errors.Add($"Узел {label}: пустая реплика.");
                else if (node.Line.Length > LongLineWarning)
                    warnings.Add($"Узел {label}: реплика длиннее {LongLineWarning} символов.");

                if (node.Choices.Count > MaxChoices)
                {
                    errors.Add(
                        $"Узел {label}: {node.Choices.Count} вариантов, максимум {MaxChoices}.");
                }

                for (var c = 0; c < node.Choices.Count; c++)
                {
                    var choice = node.Choices[c];
                    if (choice == null)
                    {
                        errors.Add($"Узел {label}, вариант #{c + 1}: пустая запись.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(choice.Text))
                        errors.Add($"Узел {label}, вариант #{c + 1}: пустой текст.");

                    if (!string.IsNullOrWhiteSpace(choice.NextId) &&
                        !byId.ContainsKey(choice.NextId))
                    {
                        errors.Add(
                            $"Узел {label}, вариант #{c + 1}: переход {choice.NextId} не найден.");
                    }
                }

                if (node.Choices.Count == 0 &&
                    !string.IsNullOrWhiteSpace(node.NextId) &&
                    !byId.ContainsKey(node.NextId))
                {
                    errors.Add($"Узел {label}: переход {node.NextId} не найден.");
                }
            }

            if (errors.Count > 0)
                return;

            CheckReachability(byId, warnings);
        }

        /// <summary>
        /// Обход от старта: узлы вне обхода — предупреждение; отсутствие хотя бы
        /// одного достижимого конца — тоже (любой путь зациклен).
        /// </summary>
        private void CheckReachability(
            Dictionary<string, DialogueNode> byId, List<string> warnings)
        {
            var reachable = new HashSet<string>();
            var stack = new Stack<DialogueNode>();
            var start = Find(_startId);
            if (start != null)
                stack.Push(start);

            var hasEnd = false;
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node == null || !reachable.Add(node.Id))
                    continue;

                if (node.IsTerminal)
                {
                    hasEnd = true;
                    continue;
                }

                if (node.Choices.Count == 0)
                {
                    if (byId.TryGetValue(node.NextId, out var next))
                        stack.Push(next);
                    continue;
                }

                for (var i = 0; i < node.Choices.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(node.Choices[i].NextId))
                        hasEnd = true;
                    else if (byId.TryGetValue(node.Choices[i].NextId, out var next))
                        stack.Push(next);
                }
            }

            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node != null && !reachable.Contains(node.Id))
                    warnings.Add($"Узел {node.Id} недостижим из старта.");
            }

            if (!hasEnd)
                warnings.Add("В диалоге нет достижимого конца: любой путь зациклен.");
        }

        /// <summary>Свободный id вида node_N: старые id не переиспользуются молча.</summary>
        private string NewId()
        {
            for (var n = 1; ; n++)
            {
                var id = $"node_{n}";
                if (Find(id) == null)
                    return id;
            }
        }
    }
}
