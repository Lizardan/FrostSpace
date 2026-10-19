using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Настройки импорта робо-животных FrostSpace.
    ///
    /// Blender отдаёт два take (`idle_loop`, `move_loop`); клипам нужны имена
    /// `Idle` и `Move` — состояния Animator ищут клип по имени, как у роботов.
    /// Кадровый диапазон берётся из самого FBX (`defaultClipAnimations` в
    /// `OnPreprocessAnimation`): у каждого вида своя длина клипа, константы в
    /// коде здесь быстро устаревают. Цвет материала FBX хранит в sRGB, а URP
    /// в Linear ждёт линейный — конвертация как у роботов, чтобы переимпорт не
    /// терял результат.
    /// </summary>
    public sealed class AnimalAssetPostprocessor : AssetPostprocessor
    {
        private const string AnimalFolder = "/Content/Models/RobotAnimals/";

        // Версия меняется вместе с логикой импорта: Unity переимпортирует
        // животных, иначе старый кэш оставит у них настройки прежней версии.
        private static readonly uint Version = 1;

        public override uint GetVersion()
        {
            return Version;
        }

        private void OnPreprocessModel()
        {
            if (!IsAnimal(assetPath))
                return;

            var importer = assetImporter as ModelImporter;
            if (importer == null)
                return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        /// <summary>
        /// Список take-клипов доступен только на этом шаге: в OnPreprocessModel
        /// `defaultClipAnimations` ещё пуст.
        /// </summary>
        private void OnPreprocessAnimation()
        {
            if (!IsAnimal(assetPath))
                return;

            var importer = assetImporter as ModelImporter;
            if (importer == null)
                return;

            importer.clipAnimations = BuildClips(importer.defaultClipAnimations);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsAnimal(assetPath))
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
        /// Оставляет только клипы `Idle` и `Move`: имя клипа — роль, границы
        /// кадров и порядок берутся у импортёра, включается зацикливание.
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
            if (takeName.StartsWith("move"))
                return "Move";
            return null;
        }

        private static void FixColor(Material material, string property)
        {
            if (!material.HasProperty(property))
                return;

            var color = material.GetColor(property);
            material.SetColor(property, color.linear);
        }

        private static bool IsAnimal(string path)
        {
            return path.Replace('\\', '/').Contains(AnimalFolder);
        }
    }
}
