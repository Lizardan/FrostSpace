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
        private const float SeatHeight = 0.88f;

        /// <summary>Середина седла по продольной оси, м (Blender Y = 0.35 → Unity −0.35).</summary>
        private const float SeatY = -0.35f;

        /// <summary>Наклон корпуса наездника: на ховербайке он лежит на баке.</summary>
        private const float RiderLean = 42f;

        /// <summary>Доля наклона, которую забирает шея, чтобы взгляд остался на дороге.</summary>
        private const float RiderNeck = 0.45f;

        /// <summary>
        /// Боковой вынос колена: на ховербайке колени поджаты к корпусу,
        /// иначе ноги расходятся почти на ширину выносных поплавков.
        /// При −0.15 колено всё ещё выходило наружу ступни (0.315 против 0.24),
        /// при −0.30 оно встаёт на 0.218 — чуть внутрь подножки.
        /// </summary>
        private const float RiderKneeOut = -0.30f;

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

                // Плоскость подушки лежит в XY и повёрнута на 90° вокруг X:
                // длина по днищу — это локальная Y, а не Z. Масштаб по Z
                // растягивал пустоту, и подушка читалась круглой точкой.
                var cushion = Part(root.transform, "Cushion", PrimitiveType.Quad,
                    new Vector3(0f, 0.04f, 0f), new Vector3(1.15f, 2.30f, 1f),
                    new Vector3(90f, 0f, 0f), cushionMaterial);
                cushion.GetComponent<MeshRenderer>().shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                cushion.GetComponent<MeshRenderer>().receiveShadows = false;

                // Якоря позы — внутри Visual, а не на корне: модель качается
                // и кренится, и наездник обязан двигаться вместе с ней.
                var seat = new GameObject("Seat").transform;
                seat.SetParent(visual, false);
                seat.localPosition = new Vector3(0f, SeatHeight, SeatY);

                // Таз наездника: чуть выше подушки и по её середине. Отдельный якорь,
                // а не расчёт от сиденья, — чтобы посадку задавал префаб, а не код.
                var seatPoint = new GameObject("Seat Point").transform;
                seatPoint.SetParent(visual, false);
                seatPoint.localPosition = new Vector3(0f, SeatHeight + 0.08f, SeatY);

                // Точки хвата и подножек: Blender (x, y, z) → Unity (x, z, −y).
                // Рукоятки на высоте 1.05, ступни — на задних подножках за седлом.
                Anchor(visual, "Grip L", new Vector3(-0.33f, 1.05f, 0.47f));
                Anchor(visual, "Grip R", new Vector3(0.33f, 1.05f, 0.47f));
                Anchor(visual, "Foot L", new Vector3(-0.24f, 0.66f, -0.55f));
                Anchor(visual, "Foot R", new Vector3(0.24f, 0.66f, -0.55f));

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
                var cockpit = root.AddComponent<RiderCockpit>();

                SetReference(glow, "_bike", bike);
                SetReference(glow, "_visual", visual);
                SetReference(glow, "_cushion", cushion.GetComponent<MeshRenderer>());
                SetReference(glow, "_engineLight", lamp);
                SetReference(bike, "_seat", seat);
                SetReference(bike, "_exit", exitPoint);
                SetReference(cockpit, "_seatPoint", seatPoint);
                SetFloat(cockpit, "_leanAngle", RiderLean);
                SetFloat(cockpit, "_neckShare", RiderNeck);
                SetFloat(cockpit, "_kneeOut", RiderKneeOut);
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

        /// <summary>Пустой якорь в локальных координатах корня: точка позы наездника.</summary>
        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"HoverbikeModelBuilder: нет поля {field} у {target.name}");
                return;
            }

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
