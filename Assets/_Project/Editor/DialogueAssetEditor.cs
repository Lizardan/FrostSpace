using System.Collections.Generic;
using FrostSpace.Features.Dialogue;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Редактор диалога: карточки узлов, стабильные id, выпадающие переходы и живая
    /// валидация. Узлы добавляются, дублируются и удаляются здесь же; удаление
    /// переводит ссылки на узел в «Конец», поэтому граф не ломается молча.
    ///
    /// Структурные правки идут через методы <see cref="DialogueAsset"/> — так они
    /// попадают и в тесты. Тексты и переходы правятся SerializedProperty, чтобы
    /// работали Undo и multi-object.
    /// </summary>
    [CustomEditor(typeof(DialogueAsset))]
    public sealed class DialogueAssetEditor : UnityEditor.Editor
    {
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();
        private SerializedProperty _startId;
        private SerializedProperty _nodes;
        private Vector2 _scroll;
        private bool _showWarnings = true;

        private void OnEnable()
        {
            _startId = serializedObject.FindProperty("_startId");
            _nodes = serializedObject.FindProperty("_nodes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var asset = (DialogueAsset)target;
            DrawHeader(asset);
            DrawStart(asset);
            DrawActions(asset);
            DrawValidation(asset);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _nodes.arraySize; i++)
                DrawNode(asset, i);
            EditorGUILayout.EndScrollView();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader(DialogueAsset asset)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("FROSTSPACE / DIALOGUE", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Узлов: {asset.Nodes.Count}    " +
                $"Старт: {(string.IsNullOrEmpty(asset.StartId) ? "—" : asset.StartId)}",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(6f);
        }

        private void DrawStart(DialogueAsset asset)
        {
            var ids = new List<string> { string.Empty };
            var labels = new List<string> { "— не выбран —" };
            for (var i = 0; i < asset.Nodes.Count; i++)
            {
                var node = asset.Nodes[i];
                if (node == null)
                    continue;

                ids.Add(node.Id);
                labels.Add($"{node.Id}  ·  {Preview(node.Line)}");
            }

            var current = ids.IndexOf(_startId.stringValue);
            if (current < 0)
                current = 0;

            var next = EditorGUILayout.Popup("Стартовый узел", current, labels.ToArray());
            if (next != current)
                _startId.stringValue = ids[next];
        }

        private void DrawActions(DialogueAsset asset)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Добавить узел"))
                AddNode(asset, "Добавить узел");

            using (new EditorGUI.DisabledScope(_nodes.arraySize == 0))
            {
                if (GUILayout.Button("Проверить"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Validate(asset);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private void DrawValidation(DialogueAsset asset)
        {
            _errors.Clear();
            _warnings.Clear();
            asset.CollectProblems(_errors, _warnings);

            if (_errors.Count > 0)
            {
                var text = _errors.Count <= 8
                    ? string.Join("\n", _errors)
                    : string.Join("\n", _errors.GetRange(0, 8)) +
                      $"\n… и ещё {_errors.Count - 8}";
                EditorGUILayout.HelpBox("Ошибки:\n" + text, MessageType.Error);
            }

            if (_warnings.Count > 0)
            {
                _showWarnings = EditorGUILayout.Foldout(_showWarnings, "Предупреждения", true);
                if (_showWarnings)
                {
                    var text = _warnings.Count <= 8
                        ? string.Join("\n", _warnings)
                        : string.Join("\n", _warnings.GetRange(0, 8)) +
                          $"\n… и ещё {_warnings.Count - 8}";
                    EditorGUILayout.HelpBox(text, MessageType.Warning);
                }
            }

            if (_errors.Count == 0 && _warnings.Count == 0)
                EditorGUILayout.HelpBox("Граф корректен.", MessageType.Info);

            EditorGUILayout.Space(4f);
        }

        private void DrawNode(DialogueAsset asset, int index)
        {
            var nodeProp = _nodes.GetArrayElementAtIndex(index);
            var id = nodeProp.FindPropertyRelative("_id").stringValue;
            var lineProp = nodeProp.FindPropertyRelative("_line");
            var nextProp = nodeProp.FindPropertyRelative("_nextId");
            var actionProp = nodeProp.FindPropertyRelative("_action");
            var questProp = nodeProp.FindPropertyRelative("_questId");
            var choicesProp = nodeProp.FindPropertyRelative("_choices");

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(id) ? $"#{index + 1}" : id,
                EditorStyles.boldLabel);
            if (asset.StartId == id)
                GUILayout.Label("СТАРТ", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(lineProp, new GUIContent("Реплика"));
            if (lineProp.stringValue.Length > DialogueAsset.LongLineWarning)
            {
                EditorGUILayout.HelpBox(
                    $"Реплика длиннее {DialogueAsset.LongLineWarning} символов: " +
                    "на панели она может обрезаться.",
                    MessageType.Warning);
            }

            DrawAction(actionProp, questProp);

            EditorGUILayout.LabelField("Варианты ответа", EditorStyles.miniBoldLabel);
            for (var c = 0; c < choicesProp.arraySize; c++)
                DrawChoice(asset, choicesProp, c);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(
                       choicesProp.arraySize >= DialogueAsset.MaxChoices))
            {
                if (GUILayout.Button(
                        $"Добавить вариант ({choicesProp.arraySize}/{DialogueAsset.MaxChoices})"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(asset, "Добавить вариант");
                    AddChoice(asset, id);
                    serializedObject.Update();
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("Ответ с новой репликой"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(asset, "Новый ответ");
                var created = asset.AddNode(string.Empty);
                FindAssetNode(asset, id)?.AddChoice("Новый ответ", created.Id);
                EditorUtility.SetDirty(asset);
                serializedObject.Update();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            if (choicesProp.arraySize == 0)
                DrawTarget(asset, nextProp, "Переход без вариантов");

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(asset.StartId == id))
            {
                if (GUILayout.Button("Сделать стартом"))
                {
                    serializedObject.ApplyModifiedProperties();
                    _startId.stringValue = id;
                }
            }

            if (GUILayout.Button("Дублировать"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(asset, "Дублировать узел");
                asset.DuplicateNode(index);
                EditorUtility.SetDirty(asset);
                serializedObject.Update();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Удалить"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(asset, "Удалить узел");
                asset.RemoveNode(index);
                EditorUtility.SetDirty(asset);
                serializedObject.Update();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        private static void DrawAction(
            SerializedProperty actionProp, SerializedProperty questProp)
        {
            EditorGUILayout.PropertyField(actionProp, new GUIContent("Действие"));
            if ((DialogueAction)actionProp.enumValueIndex == DialogueAction.None)
                return;

            EditorGUILayout.PropertyField(questProp, new GUIContent("ID квеста"));
            if (string.IsNullOrWhiteSpace(questProp.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Пусто — квест, назначенный сцене.", MessageType.None);
            }
        }

        private void DrawChoice(
            DialogueAsset asset, SerializedProperty choicesProp, int index)
        {
            var choiceProp = choicesProp.GetArrayElementAtIndex(index);
            var textProp = choiceProp.FindPropertyRelative("_text");
            var nextProp = choiceProp.FindPropertyRelative("_nextId");

            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{index + 1}.", GUILayout.Width(20f));
            EditorGUILayout.PropertyField(
                textProp, GUIContent.none, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(asset, "Удалить вариант");
                choicesProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            DrawTarget(asset, nextProp, "Ответ ведёт в");
            EditorGUILayout.EndVertical();
        }

        private void DrawTarget(
            DialogueAsset asset, SerializedProperty targetProp, string label)
        {
            var ids = new List<string> { string.Empty };
            var labels = new List<string> { "— Конец диалога —" };
            for (var i = 0; i < asset.Nodes.Count; i++)
            {
                var node = asset.Nodes[i];
                if (node == null)
                    continue;

                ids.Add(node.Id);
                labels.Add($"{node.Id}  ·  {Preview(node.Line)}");
            }

            var current = ids.IndexOf(targetProp.stringValue);
            if (current < 0)
                current = 0;

            var next = EditorGUILayout.Popup(label, current, labels.ToArray());
            if (next != current)
                targetProp.stringValue = ids[next];
        }

        private void AddNode(DialogueAsset asset, string undo)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(asset, undo);
            asset.AddNode(string.Empty);
            EditorUtility.SetDirty(asset);
            serializedObject.Update();
            GUIUtility.ExitGUI();
        }

        private static void AddChoice(DialogueAsset asset, string nodeId)
        {
            var node = FindAssetNode(asset, nodeId);
            if (node == null)
                return;

            node.AddChoice("Новый ответ", string.Empty);
            EditorUtility.SetDirty(asset);
        }

        private static DialogueNode FindAssetNode(DialogueAsset asset, string id)
        {
            for (var i = 0; i < asset.Nodes.Count; i++)
            {
                if (asset.Nodes[i] != null && asset.Nodes[i].Id == id)
                    return asset.Nodes[i];
            }

            return null;
        }

        private static void Validate(DialogueAsset asset)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            asset.CollectProblems(errors, warnings);

            foreach (var error in errors)
                Debug.LogError($"Диалог «{asset.name}»: {error}", asset);
            foreach (var warning in warnings)
                Debug.LogWarning($"Диалог «{asset.name}»: {warning}", asset);

            Debug.Log(
                $"Диалог «{asset.name}»: узлов {asset.Nodes.Count}, " +
                $"ошибок {errors.Count}, предупреждений {warnings.Count}.",
                asset);
        }

        private static string Preview(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "(пусто)";

            var trimmed = line.Trim();
            return trimmed.Length <= 42 ? trimmed : trimmed.Substring(0, 42) + "…";
        }

        /// <summary>
        /// Проверка всех диалогов проекта. Меню только читает ассеты: ничего не
        /// пересохраняет и не создаёт (ADR 0005 не нарушается).
        /// </summary>
        [MenuItem("FrostSpace/Диалоги/Проверить все")]
        private static void ValidateAll()
        {
            var guids = AssetDatabase.FindAssets("t:DialogueAsset");
            if (guids.Length == 0)
            {
                Debug.Log("[Диалоги] Ассетов DialogueAsset не найдено.");
                return;
            }

            var totalErrors = 0;
            var totalWarnings = 0;

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(path);
                if (asset == null)
                    continue;

                var errors = new List<string>();
                var warnings = new List<string>();
                asset.CollectProblems(errors, warnings);
                totalErrors += errors.Count;
                totalWarnings += warnings.Count;

                foreach (var error in errors)
                    Debug.LogError($"[Диалоги] {path}: {error}", asset);
                foreach (var warning in warnings)
                    Debug.LogWarning($"[Диалоги] {path}: {warning}", asset);
            }

            Debug.Log(
                $"[Диалоги] Проверено {guids.Length}: ошибок {totalErrors}, " +
                $"предупреждений {totalWarnings}.");
        }
    }
}
