#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Сборка WebGL для CI. Вызывается только из командной строки:
    /// <code>
    /// Unity.exe -quit -batchmode -projectPath . -buildTarget WebGL \
    ///   -executeMethod FrostSpace.Editor.BuildWebGL.PerformBuild
    /// </code>
    /// Меню намеренно нет (ADR 0005): сборка не правит контент, но и не то, что жмут
    /// руками в редакторе — её запускает workflow.
    ///
    /// Результат пишется в <c>Builds/webgl-result.json</c> и читается workflow:
    /// Unity.exe на Windows — лончер, он завершается сразу с кодом 1, поэтому
    /// дождаться сборки по коду возврата нельзя.
    /// </summary>
    public static class BuildWebGL
    {
        private const string OutputPath = "Builds/WebGL";
        private const string ResultPath = "Builds/webgl-result.json";

        public static void PerformBuild()
        {
            try
            {
                var scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray();
                if (scenes.Length == 0)
                {
                    WriteResult(false, 0, 0, "в Build Settings нет включённых сцен");
                    EditorApplication.Exit(1);
                    return;
                }

                // WebGL в проекте — статический хостинг без настройки заголовков
                // (GitHub Pages): Brotli + decompression fallback, иначе браузер
                // не распакует .unityweb.
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL,
                    ScriptingImplementation.IL2CPP);
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
                PlayerSettings.WebGL.dataCaching = true;
                // Свой шаблон: канвас на всё окно, без футера Unity с кнопкой
                // полноэкранного режима (Assets/WebGLTemplates/FrostSpace).
                PlayerSettings.WebGL.template = "PROJECT:FrostSpace";

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = OutputPath,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                var ok = summary.result == BuildResult.Succeeded;
                Debug.Log(string.Format(
                    "BuildWebGL: result={0} size={1} MB errors={2} time={3}",
                    summary.result, summary.totalSize / (1024 * 1024),
                    summary.totalErrors, summary.totalTime));
                WriteResult(ok, summary.totalErrors, summary.totalSize, string.Empty);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                WriteResult(false, 1, 0, exception.Message);
                EditorApplication.Exit(1);
            }
        }

        private static void WriteResult(bool ok, int errors, ulong size, string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath) ?? "Builds");
            var json = string.Format(
                "{{\"result\":\"{0}\",\"errors\":{1},\"sizeBytes\":{2},\"message\":\"{3}\"}}",
                ok ? "Succeeded" : "Failed", errors, size,
                (message ?? string.Empty)
                    .Replace("\\", "/").Replace("\"", "'")
                    .Replace("\r", " ").Replace("\n", " "));
            File.WriteAllText(ResultPath, json);
            Debug.Log("BuildWebGL: " + json);
        }
    }
}
#endif
