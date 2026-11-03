// Снять с префаб-инстансов override, которые ничего не меняют.
//
// Зачем: `WorldResidency` гасит клетки через `SetActive(false)` в рантайме.
// Если сцену сохранить во время прогона, Unity записывает состояние активности
// как override инстанса. После этого клетка в сохранённой сцене закреплена
// показанной, и при любой синхронизации префаба override может вернуть её в
// кадр — то есть резидентность в редакторе и в рантайме начнут врать друг
// другу. 2026-11-03 в сцене `Frontier` висели ровно такие два override:
// `m_IsActive` на корне чанка депо и `m_Enabled` на его рендерере, оба со
// значением 1, совпадающим с префабом.
//
// Скрипт снимает ТОЛЬКО те override, значение которых совпадает со значением
// в исходном префабе. Ничего другого он не трогает: позиции, слои, имена и
// любые другие override остаются как есть.
//
// Запуск:
//     python Tools/Unity/run_snippet.py Tools/Unity/clean_redundant_overrides.cs

var sb = new System.Text.StringBuilder();

if (UnityEditor.EditorApplication.isPlaying) return "редактор в Play — сначала останови";

var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
int removed = 0;
int checkedMods = 0;

foreach (var root in scene.GetRootGameObjects())
{
    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        var go = t.gameObject;

        // 1. Активность объекта.
        var goMods = UnityEditor.PrefabUtility.GetPropertyModifications(go);
        if (goMods != null && goMods.Length > 0)
        {
            var srcGo = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (srcGo != null && srcGo.activeSelf)
            {
                var kept = new System.Collections.Generic.List<UnityEditor.PropertyModification>();
                for (int i = 0; i < goMods.Length; i++)
                {
                    checkedMods++;
                    if (goMods[i].propertyPath == "m_IsActive" && goMods[i].value == "1")
                    {
                        removed++;
                        sb.AppendLine(string.Format("снят m_IsActive: {0} (значение 1 совпадает с префабом)", go.name));
                        continue;
                    }
                    kept.Add(goMods[i]);
                }
                if (kept.Count != goMods.Length)
                    UnityEditor.PrefabUtility.SetPropertyModifications(go, kept.ToArray());
            }
        }

        // 2. Включённость компонентов.
        var comps = go.GetComponents<UnityEngine.Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var comp = comps[i];
            if (comp == null) continue;
            var srcComp = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(comp);
            if (srcComp == null) continue;
            var behaviour = comp as UnityEngine.Behaviour;
            var srcBehaviour = srcComp as UnityEngine.Behaviour;
            if (behaviour == null || srcBehaviour == null || !srcBehaviour.enabled) continue;

            var compMods = UnityEditor.PrefabUtility.GetPropertyModifications(comp);
            if (compMods == null || compMods.Length == 0) continue;

            var keptC = new System.Collections.Generic.List<UnityEditor.PropertyModification>();
            for (int k = 0; k < compMods.Length; k++)
            {
                checkedMods++;
                if (compMods[k].propertyPath == "m_Enabled" && compMods[k].value == "1")
                {
                    removed++;
                    sb.AppendLine(string.Format("снят m_Enabled: {0} на {1} (значение 1 совпадает с префабом)",
                        comp.GetType().Name, go.name));
                    continue;
                }
                keptC.Add(compMods[k]);
            }
            if (keptC.Count != compMods.Length)
                UnityEditor.PrefabUtility.SetPropertyModifications(comp, keptC.ToArray());
        }
    }
}

if (removed > 0)
{
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    sb.AppendLine("сцена сохранена=" + saved);
}
else
{
    sb.AppendLine("лишних override не найдено — сцену не трогал");
}
sb.AppendLine(string.Format("снято override={0} просмотрено={1}", removed, checkedMods));
return sb.ToString();
