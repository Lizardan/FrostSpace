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
    ///
    /// В облаке сборку запускает game-ci, и он же проверяет её по stdout: нужен
    /// блок «Build results» и строка <c>Build succeeded!</c> (см. <see cref="ReportResult"/>).
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

                // Скорость сборки в CI. Замер прогона 39414342112 (сборка 594 с):
                // «Postprocess built player» — 519 с, из них ~110 с Brotli и ~400 с
                // компиляция IL2CPP. Дефолты WebGL (OptimizeSpeed + Minimal) выбраны
                // ради рантайма и дают максимум машинного кода на входе компилятора.
                //
                // OptimizeSize = «Faster (smaller) builds»: меньше машинного кода —
                // короче компиляция и меньше .wasm. Для веба разница в рантайме
                // незаметна, зато файл легче качать.
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,
                    Il2CppCodeGeneration.OptimizeSize);
                // Medium вместо дефолтного Minimal: неиспользуемый managed-код
                // (пакеты URP/Input System/Cinemachine) не превращается в C++.
                // Рефлексии в проекте нет, так что вырезать безопасно; если что-то
                // отвалится в рантайме — сначала вернуть Minimal, а не искать баг.
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL,
                    ManagedStrippingLevel.Medium);
                // Release, а не Master: Master компилируется заметно дольше, а выигрыш
                // в скорости на таком объёме кода не стоит времени сборки.
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL,
                    Il2CppCompilerConfiguration.Release);

                // В проекте ровно один уровень качества (ADR 0008), так что это
                // просто страховка: m_PerPlatformDefaultQuality не перезаписывается
                // при удалении уровней, и старый индекс 1 для WebGL остаётся в файле.
                // В батче активная цель — WebGL, поэтому запись идёт именно в неё.
                QualitySettings.SetQualityLevel(0, true);

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
                ReportSummary(summary);
                ReportResult(ok);
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

        /// <summary>
        /// Печатает блок «Build results» в формате, который разбирает game-ci
        /// (<c>UnityBuilderAction.Reporting.StdOutReporter.ReportSummary</c>).
        /// Свой buildMethod заменяет штатный скрипт билдера, поэтому контракт
        /// вывода приходится повторять руками.
        /// </summary>
        private static void ReportSummary(BuildSummary summary)
        {
            var eol = Environment.NewLine;
            Console.WriteLine(
                eol +
                "###########################" + eol +
                "#      Build results      #" + eol +
                "###########################" + eol +
                eol +
                "Duration: " + summary.totalTime + eol +
                "Warnings: " + summary.totalWarnings + eol +
                "Errors: " + summary.totalErrors + eol +
                "Size: " + summary.totalSize + " bytes" + eol +
                eol);
        }

        /// <summary>
        /// Маркер результата для game-ci: CLI считает сборку успешной, если в выводе
        /// есть строка <c>Build succeeded!</c> (или блок «Build results» с
        /// <c>Errors: 0</c>). Без него зелёная сборка падает с
        /// «There was an error building the project» — так и случился прогон
        /// 39409178978 после перехода на unity-builder@v6.
        /// </summary>
        private static void ReportResult(bool ok)
        {
            Console.WriteLine(ok ? "Build succeeded!" : "Build failed!");
        }
    }
}
#endif
