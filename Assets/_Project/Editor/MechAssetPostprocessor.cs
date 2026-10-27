using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Настройки импорта ездовых ходунов FrostSpace.
    ///
    /// Blender отдаёт три take (`idle_loop`, `walk_loop`, `run_loop`), а состояния
    /// Animator ищут клип по имени, как у роботов и животных: `Idle` / `Walk` / `Run`.
    /// Границы кадров берутся из самого FBX (`defaultClipAnimations` в
    /// `OnPreprocessAnimation`) — у каждого ходуна своя длина шага
    /// (26…38 кадров), и константы в коде здесь устарели бы на первой же пересборке.
    ///
    /// Цвет материала FBX хранит в sRGB, а URP в Linear ждёт линейный: без
    /// конвертации ходуны выглядят выбеленными (та же причина, что у роботов).
    /// </summary>
    public sealed class MechAssetPostprocessor : AssetPostprocessor
    {
        private const string MechFolder = "/Content/Models/Mechs/";

        // Версия меняется вместе с логикой импорта: Unity переимпортирует
        // ходунов, иначе старый кэш оставит у них настройки прежней версии.
        private static readonly uint Version = 1;

        public override uint GetVersion()
        {
            return Version;
        }

        private void OnPreprocessModel()
        {
            if (!IsMech(assetPath))
                return;

            var importer = assetImporter as ModelImporter;
            if (importer == null)
                return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        /// <summary>
        /// Список take-клипов доступен только на этом шаге: в `OnPreprocessModel`
        /// `defaultClipAnimations` ещё пуст (та же ловушка, что у животных).
        /// </summary>
        private void OnPreprocessAnimation()
        {
            if (!IsMech(assetPath))
                return;

            var importer = assetImporter as ModelImporter;
            if (importer == null)
                return;

            importer.clipAnimations = BuildClips(importer.defaultClipAnimations);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsMech(assetPath))
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;

                    FixColor(material, "_BaseColor");
                    FixColor(material, "_Color");
                }
            }
        }

        /// <summary>
        /// Оставляет три клипа ходьбы: имя клипа — роль, кадры и порядок берутся
        /// у импортёра, включается зацикливание. Лишние take отбрасываются.
        /// </summary>
        private static ModelImporterClipAnimation[] BuildClips(
            ModelImporterClipAnimation[] defaults)
        {
            var kept = 0;
            foreach (var source in defaults)
            {
                if (ClipName(source.takeName) != null)
                    kept++;
            }

            var clips = new ModelImporterClipAnimation[kept];
            var index = 0;
            foreach (var source in defaults)
            {
                var name = ClipName(source.takeName);
                if (name == null)
                    continue;

                clips[index] = new ModelImporterClipAnimation
                {
                    name = name,
                    takeName = source.takeName,
                    firstFrame = source.firstFrame,
                    lastFrame = source.lastFrame,
                    loopTime = true,
                };
                index++;
            }

            return clips;
        }

        /// <summary>Take из Blender — роль клипа; всё лишнее отбрасывается.</summary>
        private static string ClipName(string takeName)
        {
            if (takeName.StartsWith("idle"))
                return "Idle";
            if (takeName.StartsWith("walk"))
                return "Walk";
            if (takeName.StartsWith("run"))
                return "Run";
            return null;
        }

        private static void FixColor(Material material, string property)
        {
            if (!material.HasProperty(property))
                return;

            var color = material.GetColor(property);
            material.SetColor(property, color.linear);
        }

        private static bool IsMech(string path)
        {
            return path.Replace('\\', '/').Contains(MechFolder);
        }
    }
}
