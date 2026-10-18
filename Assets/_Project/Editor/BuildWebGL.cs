#if UNITY_EDITOR
using System;
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
    /// </summary>
    public static class BuildWebGL
    {
        private const string OutputPath = "Builds/WebGL";

        public static void PerformBuild()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("BuildWebGL: в Build Settings нет включённых сцен");
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
            Debug.Log(string.Format(
                "BuildWebGL: result={0} size={1} MB errors={2} time={3}",
                summary.result, summary.totalSize / (1024 * 1024),
                summary.totalErrors, summary.totalTime));

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
