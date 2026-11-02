using System.IO;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Импорт звуков прибытия: моно, 44.1 кГц, петли — сжатые в памяти,
    /// короткие удары — PCM с предзагрузкой. Без этого WebGL держит лишнюю
    /// память и декодирует клип в первом кадре.
    /// </summary>
    public sealed class AudioArrivalImportPostprocessor : AssetPostprocessor
    {
        private const string Folder = "/Content/Audio/Arrival/";

        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains(Folder))
                return;

            var importer = (AudioImporter)assetImporter;
            var loop = IsLoop(path);

            importer.forceToMono = true;
            importer.loadInBackground = false;

            var settings = importer.defaultSampleSettings;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;
            settings.loadType = loop
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = loop
                ? AudioCompressionFormat.Vorbis
                : AudioCompressionFormat.PCM;
            settings.preloadAudioData = !loop;
            settings.quality = 0.6f;

            importer.defaultSampleSettings = settings;
        }

        private static bool IsLoop(string path)
        {
            return Path.GetFileNameWithoutExtension(path).EndsWith("_loop");
        }

        public override uint GetVersion()
        {
            return 2;
        }
    }
}
