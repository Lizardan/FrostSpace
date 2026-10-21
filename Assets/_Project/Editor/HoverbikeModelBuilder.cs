using UnityEditor;
using UnityEngine;
using FrostSpace.Features.Ride;
using FrostSpace.Features.Scan;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Собирает префаб <c>Hoverbike_Model</c> вокруг FBX из Blender
    /// (<c>Tools/Blender/build_hoverbike.py</c>): модель становится дочерним
    /// объектом <c>Visual</c>, а корень получает игровые компоненты.
    ///
    /// Префаб собирается скриптом, а не руками в сцене: STYLE.md запрещает
    /// одноразовые правки, поэтому результат воспроизводим. Конвенция имени
    /// та же, что у роботов (<c>R1_PolarMechanic_Model</c>): файл и корневой
    /// объект называются одинаково, мир ставит инстанс в сцену.
    /// </summary>
    public static class HoverbikeModelBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Ride";
        private const string PrefabPath = PrefabFolder + "/Hoverbike_Model.prefab";
        private const string ModelPath =
            "Assets/_Project/Content/Models/Level/Hoverbike.fbx";
        private const string MaterialFolder = "Assets/_Project/Content/Materials/VFX";
        private const string CushionMaterialPath = MaterialFolder + "/HoverCushion.mat";
        private const string CushionShaderName = "FrostSpace/VFX/HoverCushion";

        /// <summary>Высота посадки от корня модели, м: верх седла в Blender-модели.</summary>
        private const float SeatHeight = 0.92f;

        [MenuItem("FrostSpace/Ride/Build Hoverbike Model")]
        public static void BuildFromFbx()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError(
                    $"HoverbikeModelBuilder: нет модели {ModelPath}. " +
                    "Соберите её: Tools/Blender/build_hoverbike.py");
                return;
            }

            EnsureFolder("Assets/_Project/Content/Materials", "VFX");
            EnsureFolder("Assets/_Project/Content/Prefabs", "Ride");

            var cushionMaterial = LoadOrCreateCushionMaterial();
            if (cushionMaterial == null)
            {
                Debug.LogError(
                    $"HoverbikeModelBuilder: не найден шейдер {CushionShaderName}");
                return;
            }

            var root = new GameObject("Hoverbike_Model");
            try
            {
                var controller = root.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 0.5f, 0f);
                controller.height = 1.05f;
                controller.radius = 0.55f;
                controller.stepOffset = 0.25f;
                controller.slopeLimit = 50f;
                controller.skinWidth = 0.04f;

                // Модель приходит с нулями в трансформе: высоты и зазор до земли
                // уже заданы в Blender, повторно поднимать её не нужно.
                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                modelInstance.name = "Visual";
                var visual = modelInstance.transform;
                visual.SetParent(root.transform, false);
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;

                var cushion = Part(root.transform, "Cushion", PrimitiveType.Quad,
                    new Vector3(0f, 0.03f, 0f), new Vector3(1.25f, 1f, 2.15f),
                    new Vector3(90f, 0f, 0f), cushionMaterial);
                cushion.GetComponent<MeshRenderer>().shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                cushion.GetComponent<MeshRenderer>().receiveShadows = false;

                var seat = new GameObject("Seat").transform;
                seat.SetParent(root.transform, false);
                seat.localPosition = new Vector3(0f, SeatHeight, -0.30f);

                var exitPoint = new GameObject("ExitPoint").transform;
                exitPoint.SetParent(root.transform, false);
                exitPoint.localPosition = new Vector3(1.9f, 0.05f, -0.4f);

                var lamp = new GameObject("EngineGlow").AddComponent<Light>();
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                lamp.type = LightType.Point;
                lamp.color = new Color(0.302f, 0.902f, 1f);
                lamp.range = 5f;
                lamp.intensity = 1.4f;
                lamp.shadows = LightShadows.None;

                var bike = root.AddComponent<Hoverbike>();
                var glow = root.AddComponent<HoverbikeGlow>();
                var scan = root.AddComponent<ScanTarget>();

                SetReference(glow, "_bike", bike);
                SetReference(glow, "_visual", visual);
                SetReference(glow, "_cushion", cushion.GetComponent<MeshRenderer>());
                SetReference(glow, "_engineLight", lamp);
                SetReference(bike, "_seat", seat);
                SetReference(bike, "_exit", exitPoint);
                SetString(scan, "_displayName", "HOVERBIKE");
                SetString(scan, "_category", "VEHICLE");

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[HoverbikeModelBuilder] префаб собран: {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject Part(
            Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;

            // Коллайдеры деталей не нужны: корпус целиком описывает CharacterController
            // на корне, а лишние коллайдеры ловили бы игрока на подходах к байку.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            return go;
        }

        private static Material LoadOrCreateCushionMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CushionMaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find(CushionShaderName);
            if (shader == null)
                return null;

            material = new Material(shader) { name = "HoverCushion" };
            AssetDatabase.CreateAsset(material, CushionMaterialPath);
            return material;
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{leaf}"))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"HoverbikeModelBuilder: нет поля {field} у {target.name}");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"HoverbikeModelBuilder: нет поля {field} у {target.name}");
                return;
            }

            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
