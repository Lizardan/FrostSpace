// Запрос компиляции скриптов: редактор не перечитывает .cs, пока окно не
// получило фокус, а тесты тогда идут на старой сборке и падают на числах,
// которые уже исправлены.
//
//   UNITY_MCP_PORT=6401 python Tools/Unity/run_snippet.py Tools/Unity/request_compile.cs
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
return "RequestScriptCompilation отправлен: редактор пересобирает сборки";
