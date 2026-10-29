using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Настройки импорта ассетов уровня FrostSpace (Models/Level).
    ///
    /// Blender-скрипты кладут в меши имена материалов, совпадающие с именами
    /// ассетов палитры (Level_Wood, Level_Window, ...). Импорт FBX создаёт
    /// собственные материалы с теми же именами; здесь они заменяются
    /// существующими ассетами палитры, чтобы уровень использовал один
    /// источник цвета. Меши уровня статические: анимация выключена, камеры и
    /// источники из файлов не импортируются, читаемость включена — она нужна
    /// MeshCollider террейна.
    /// </summary>
    public sealed class LevelAssetPostprocessor : AssetPostprocessor
    {
        private const string LevelFolder = "/Content/Models/Level/";
        private const string TrainFolder = "/Content/Models/Train/";
        private const string MaterialFolder = "Assets/_Project/Content/Materials/Level/";

        /// <summary>
        /// Папки, которые используют палитру `Level_*`: уровень и вагон.
        /// Вагон вынесен отдельно, чтобы `Prop_*` уровня и `Prop_*` вагона
        /// не пересекались в glob-шаблонах пайплайна.
        /// </summary>
        private static bool UsesLevelPalette(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains(LevelFolder) || normalized.Contains(TrainFolder);
        }

        private void OnPreprocessModel()
        {
            if (!UsesLevelPalette(assetPath))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!UsesLevelPalette(assetPath))
            {
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    var path = MaterialFolder + materials[i].name + ".mat";
                    var palette = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (palette == null || palette == materials[i])
                    {
                        continue;
                    }

                    materials[i] = palette;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }
    }
}
