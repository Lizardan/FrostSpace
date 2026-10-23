using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Настройки импорта роботов FrostSpace.
    ///
    /// FBX из Blender отдаёт три take-клипа без флага loop и цвет материала в
    /// sRGB. URP в Linear ждёт линейный цвет, поэтому без конвертации роботы
    /// выглядят выбеленными. Настройки задаются здесь, а не руками в инспекторе,
    /// чтобы переимпорт модели не терял результат.
    /// </summary>
    public sealed class RobotAssetPostprocessor : AssetPostprocessor
    {
        private const string RobotFolder = "/Content/Models/Robots/";

        private static readonly ModelImporterClipAnimation[] Clips =
        {
            new ModelImporterClipAnimation
            {
                name = "Walk", takeName = "walk_loop",
                firstFrame = 0, lastFrame = 40, loopTime = true,
            },
            new ModelImporterClipAnimation
            {
                name = "Idle", takeName = "idle_loop",
                firstFrame = 0, lastFrame = 75, loopTime = true,
            },
            new ModelImporterClipAnimation
            {
                name = "Sprint", takeName = "sprint_loop",
                firstFrame = 0, lastFrame = 20, loopTime = true,
            },
        };

        /// <summary>
        /// Прыжок есть только у модели игрока: NPC не прыгают, а лишние клипы в
        /// каждом FBX раздували бы импорт. Клипы того же ActionForge-пака.
        /// </summary>
        private static readonly ModelImporterClipAnimation[] JumpClips =
        {
            new ModelImporterClipAnimation
            {
                name = "JumpStart", takeName = "jump_start",
                firstFrame = 0, lastFrame = 40, loopTime = false,
            },
            new ModelImporterClipAnimation
            {
                name = "JumpLoop", takeName = "jump_loop",
                firstFrame = 0, lastFrame = 75, loopTime = true,
            },
            new ModelImporterClipAnimation
            {
                name = "JumpLand", takeName = "jump_land",
                firstFrame = 0, lastFrame = 38, loopTime = false,
            },
        };

        /// <summary>
        /// Переноска ящика есть только у складского носильщика: клип добавлен в
        /// FBX сборкой `build_robots.py --carry`, остальным роботам он не нужен.
        /// </summary>
        private static readonly ModelImporterClipAnimation[] CarryClips =
        {
            new ModelImporterClipAnimation
            {
                name = "CarryWalk", takeName = "walk_carry_loop",
                firstFrame = 0, lastFrame = 60, loopTime = true,
            },
        };

        private const string PlayerRobot = "R1_PolarMechanic";
        private const string CarrierRobot = "R18_Forge";

        private void OnPreprocessModel()
        {
            if (!IsRobot(assetPath))
                return;

            var importer = assetImporter as ModelImporter;
            if (importer == null)
                return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            var name = Path.GetFileNameWithoutExtension(assetPath);
            var clips = new List<ModelImporterClipAnimation>(Clips);
            if (name == PlayerRobot)
                clips.AddRange(JumpClips);
            if (name == CarrierRobot)
                clips.AddRange(CarryClips);

            importer.clipAnimations = CloneClips(clips);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsRobot(assetPath))
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

        private static void FixColor(Material material, string property)
        {
            if (!material.HasProperty(property))
                return;

            var color = material.GetColor(property);
            material.SetColor(property, color.linear);
        }

        private static ModelImporterClipAnimation[] CloneClips(
            IReadOnlyList<ModelImporterClipAnimation> sourceClips)
        {
            var copy = new ModelImporterClipAnimation[sourceClips.Count];
            for (var i = 0; i < sourceClips.Count; i++)
            {
                var source = sourceClips[i];
                copy[i] = new ModelImporterClipAnimation
                {
                    name = source.name,
                    takeName = source.takeName,
                    firstFrame = source.firstFrame,
                    lastFrame = source.lastFrame,
                    loopTime = source.loopTime,
                };
            }

            return copy;
        }

        private static bool IsRobot(string path)
        {
            return path.Replace('\\', '/').Contains(RobotFolder);
        }
    }
}
