using System;
using System.IO;
using FrostSpace.Features.Ride;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Обновляет префабы ходунов по <c>Tools/Blender/mech_manifest.json</c>.
    ///
    /// Числа посадки, габарита и скоростей рождаются в Blender: их считает
    /// <c>build_mechs.py</c> по фактическим костям и по замеру скорости земли в
    /// клипе. В Unity они не выдумываются — префаб только раскладывает их по
    /// якорям и компонентам. Раньше это делалось разовым сниппетом, и после
    /// пересборки модели числа в префабе устаревали молча (тест
    /// <c>MechVisualPrefabTests</c> ловил расхождение уже постфактум).
    ///
    /// Скрипт идемпотентен: он не создаёт объекты и не меняет структуру, а
    /// переписывает числа, поэтому его можно гонять после каждой сборки ассетов.
    /// Формулы — те же, что были в исходной сборке префабов (капсула 0.9 роста,
    /// орбита камеры 3.5 роста), они проверены на всех четырёх ходунах.
    /// </summary>
    public static class MechPrefabUpdater
    {
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Ride";
        private const string ManifestPath = "Tools/Blender/mech_manifest.json";

        /// <summary>Капсула: доля роста и радиус, м. Ниже роста — выхлопы не в счёт.</summary>
        private const float CapsuleShare = 0.9f;
        private const float CapsuleRadius = 0.55f;

        /// <summary>Орбита камеры: радиус и высота центра в долях роста.</summary>
        private const float OrbitShare = 3.5f;
        private const float LookShare = 0.72f;

        [Serializable]
        private sealed class Entry
        {
            public string unit;
            public string name;
            public float height;
            public float walk_speed;
            public float run_speed;
            public Cockpit cockpit;
            public Speeds clip_speed;
        }

        [Serializable]
        private sealed class Cockpit
        {
            public float[] seat;
            public float[] seat_point;
            public float grips;
            public float[] grip;
            public float[] foot;
            public float foot_half;
        }

        [Serializable]
        private sealed class Speeds
        {
            public float walk;
            public float run;
        }

        [Serializable]
        private sealed class Wrapper
        {
            public Entry[] items;
        }

        [MenuItem("FrostSpace/Ходуны/Обновить префабы из манифеста")]
        public static void UpdateAll()
        {
            var entries = Read();
            var updated = 0;
            foreach (var entry in entries)
            {
                var path = PrefabFolder + "/" + entry.name + "_Model.prefab";
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[Mech] нет префаба " + path);
                    continue;
                }

                Apply(path, entry);
                updated++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Mech] префабы обновлены по манифесту: " + updated);
        }

        private static Entry[] Read()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException("нет манифеста сборки ходунов", ManifestPath);

            // JsonUtility не умеет массив в корне — оборачиваем в объект.
            var json = "{\"items\":" + File.ReadAllText(ManifestPath) + "}";
            var wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper == null || wrapper.items == null || wrapper.items.Length == 0)
                throw new InvalidDataException("манифест пуст: " + ManifestPath);
            return wrapper.items;
        }

        private static void Apply(string path, Entry entry)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var height = entry.height;

                var capsule = root.GetComponent<CharacterController>();
                if (capsule != null)
                {
                    capsule.height = height * CapsuleShare;
                    capsule.radius = CapsuleRadius;
                    capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                }

                var profile = root.GetComponent<RideCameraProfile>();
                if (profile != null)
                {
                    var serialized = new SerializedObject(profile);
                    serialized.FindProperty("_distance").floatValue = height * OrbitShare;
                    serialized.FindProperty("_lookHeight").floatValue = height * LookShare;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var mech = root.GetComponent<Mech>();
                if (mech != null)
                {
                    var serialized = new SerializedObject(mech);
                    // Скорость контроллера равна скорости земли в клипе: тогда
                    // Animator.speed остаётся около 1 и подошва не скользит.
                    serialized.FindProperty("_walkSpeed").floatValue = entry.walk_speed;
                    serialized.FindProperty("_runSpeed").floatValue = entry.run_speed;
                    serialized.FindProperty("_walkClipSpeed").floatValue = entry.clip_speed.walk;
                    serialized.FindProperty("_runClipSpeed").floatValue = entry.clip_speed.run;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                // Якоря: манифест в осях Blender (x вбок, y вперёд-назад, z вверх),
                // Unity — (x вбок, y вверх, z вперёд), поэтому z = −y.
                var seat = entry.cockpit.seat;
                var seatWorld = new Vector3(seat[0], seat[2], -seat[1]);
                Place(root.transform, "Seat", seatWorld);

                var point = entry.cockpit.seat_point;
                Place(root.transform, "Seat Point",
                    new Vector3(0f, point[2] - seat[2], 0f));

                var grip = entry.cockpit.grip;
                var gripLocal = new Vector3(0f, grip[1] - seat[2], -grip[0] + seat[1]);
                Place(root.transform, "Grip L",
                    gripLocal + new Vector3(-entry.cockpit.grips, 0f, 0f));
                Place(root.transform, "Grip R",
                    gripLocal + new Vector3(entry.cockpit.grips, 0f, 0f));

                var foot = entry.cockpit.foot;
                var footLocal = new Vector3(0f, foot[1] - seat[2], -foot[0] + seat[1]);
                Place(root.transform, "Foot L",
                    footLocal + new Vector3(-entry.cockpit.foot_half, 0f, 0f));
                Place(root.transform, "Foot R",
                    footLocal + new Vector3(entry.cockpit.foot_half, 0f, 0f));

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Mech] " + entry.name + ": рост " + height.ToString("0.00") +
                          ", шаг " + entry.walk_speed.ToString("0.00") + " м/с");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Ставит якорь по локальной позиции относительно его родителя. Якоря
        /// живут детьми <c>Seat</c>, и манифест даёт их смещения именно от
        /// сиденья, поэтому вычитать позицию родителя не нужно — значения уже в
        /// его осях.
        /// </summary>
        private static void Place(Transform root, string name, Vector3 local)
        {
            var anchor = FindDeep(root, name);
            if (anchor == null)
            {
                Debug.LogWarning("[Mech] " + root.name + ": нет якоря " + name);
                return;
            }

            anchor.localPosition = local;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
