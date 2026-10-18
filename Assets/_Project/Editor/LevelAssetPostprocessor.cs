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
        private const string MaterialFolder = "Assets/_Project/Content/Materials/Level/";

        private void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains(LevelFolder))
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
            if (!assetPath.Replace('\\', '/').Contains(LevelFolder))
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
