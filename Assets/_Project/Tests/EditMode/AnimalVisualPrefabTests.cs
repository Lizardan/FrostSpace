using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет границу «визуальный префаб — сценовая логика» и контракт
    /// FBX-клипов робо-животных. Ожидания повторяют
    /// <c>Tools/Blender/animal_manifest.json</c> — манифест генератора
    /// <c>Tools/Blender/build_animals.py</c>. Тесты читают ассеты, ничего не меняя.
    /// </summary>
    public sealed class AnimalVisualPrefabTests
    {
        private const string ModelFolder = "Assets/_Project/Content/Models/RobotAnimals";
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/RobotAnimals";

        /// <summary>Ожидание по виду: длина клипов в кадрах и целевой габарит.</summary>
        private sealed class Expectation
        {
            public string Species;
            public int IdleFrames;
            public int MoveFrames;
            public float Target;
            public string Metric;
        }

        /// <summary>
        /// Список животных берётся из папки префабов: партия расширяется и
        /// сокращается без правки теста. Имя префаба <c>RA_X_Model</c> задаёт
        /// вид <c>X</c>.
        /// </summary>
        private static string[] Species()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("_Model.prefab"))
                .Select(path => System.IO.Path.GetFileNameWithoutExtension(path)
                    .Replace("RA_", string.Empty)
                    .Replace("_Model", string.Empty))
                .OrderBy(name => name)
                .ToArray();
        }

        private static Expectation[] Expectations()
        {
            return new[]
            {
                Expect("Wolf", 39, 39, 1.15f, "height"),
                Expect("Cat", 39, 40, 0.62f, "height"),
                Expect("Dog", 39, 39, 0.90f, "height"),
                Expect("Cow", 149, 40, 1.50f, "height"),
                Expect("Horse", 149, 32, 2.10f, "height"),
                Expect("Zebra", 149, 32, 1.60f, "height"),
                Expect("Llama", 149, 26, 1.80f, "height"),
                Expect("Pig", 149, 35, 0.85f, "height"),
                Expect("Sheep", 149, 26, 0.95f, "height"),
                Expect("Pug", 149, 35, 0.45f, "height"),
                Expect("Eagle", 39, 29, 1.80f, "width"),
                Expect("Piranha", 38, 38, 0.70f, "length"),
                Expect("Spider", 60, 30, 0.55f, "height"),
            };
        }

        private static Expectation Expect(
            string species, int idleFrames, int moveFrames, float target, string metric)
        {
            return new Expectation
            {
                Species = species,
                IdleFrames = idleFrames,
                MoveFrames = moveFrames,
                Target = target,
                Metric = metric,
            };
        }

        private static string PrefabPath(string species)
        {
            return PrefabFolder + "/" + species + "/RA_" + species + "_Model.prefab";
        }

        private static string ModelPath(string species)
        {
            return ModelFolder + "/" + species + "/RA_" + species + ".fbx";
        }

        private static Dictionary<string, AnimationClip> Clips(string species)
        {
            var path = ModelPath(species);
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview"))
                .GroupBy(clip => clip.name)
                .ToDictionary(group => group.Key, group => group.First());
        }

        /// <summary>Ось габарита: как у генератора — width=X, length=Z, height=Y.</summary>
        private static int MetricAxis(string metric)
        {
            switch (metric)
            {
                case "width": return 0;
                case "length": return 2;
                default: return 1;
            }
        }

        [Test]
        public void AnimalFolder_MatchesManifest()
        {
            var expected = Expectations().Select(item => item.Species).OrderBy(n => n);
            CollectionAssert.AreEqual(expected, Species(), "Состав префабов разошёлся с манифестом");
        }

        [Test]
        public void VisualPrefab_HasAnimatorWithoutGameplayLogic()
        {
            foreach (var species in Species())
            {
                var path = PrefabPath(species);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "Нет визуального префаба: " + path);

                var animator = prefab.GetComponent<Animator>();
                Assert.IsNotNull(animator, path + ": нет Animator");
                Assert.IsNotNull(
                    animator.runtimeAnimatorController, path + ": не назначен controller");
                Assert.IsFalse(animator.applyRootMotion, path + ": включён Root Motion");

                Assert.IsNull(
                    prefab.GetComponent<CharacterController>(),
                    path + ": на визуале лишний CharacterController");
                Assert.IsNull(
                    prefab.GetComponent("NpcController"),
                    path + ": на визуале не должно быть игровой логики");
                Assert.IsNull(
                    prefab.GetComponent("AirPatrolController"),
                    path + ": на визуале не должно быть игровой логики");
                Assert.AreEqual("Untagged", prefab.tag, path + ": визуалу назначен тег");
            }
        }

        [Test]
        public void VisualPrefab_ControllerHasIdleAndMove()
        {
            foreach (var species in Species())
            {
                var path = PrefabPath(species);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = prefab.GetComponent<Animator>();
                var controller = animator.runtimeAnimatorController as AnimatorController;
                Assert.IsNotNull(controller, path + ": controller не AnimatorController");

                var stateNames = controller.layers[0].stateMachine.states
                    .Select(state => state.state.name)
                    .ToArray();
                CollectionAssert.Contains(stateNames, "Idle", path + ": нет состояния Idle");
                CollectionAssert.Contains(stateNames, "Move", path + ": нет состояния Move");

                var parameterNames = controller.parameters
                    .Select(parameter => parameter.name)
                    .ToArray();
                CollectionAssert.Contains(parameterNames, "Moving", path + ": нет параметра Moving");
            }
        }

        [Test]
        public void Model_HasIdleAndMoveClipsInPlace()
        {
            foreach (var expectation in Expectations())
            {
                var path = ModelPath(expectation.Species);
                var clips = Clips(expectation.Species);

                Assert.IsTrue(clips.ContainsKey("Idle"), path + ": нет клипа Idle");
                Assert.IsTrue(clips.ContainsKey("Move"), path + ": нет клипа Move");

                foreach (var pair in new[]
                         {
                             new KeyValuePair<string, int>("Idle", expectation.IdleFrames),
                             new KeyValuePair<string, int>("Move", expectation.MoveFrames),
                         })
                {
                    var clip = clips[pair.Key];
                    var label = path + ": клип " + pair.Key;

                    Assert.IsTrue(
                        AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                        label + " не зациклен");

                    // Длина клипа в кадрах: у пачек разный fps, поэтому кадры,
                    // а не секунды — как в манифесте.
                    Assert.AreEqual(
                        pair.Value, clip.length * clip.frameRate, 0.5f,
                        label + ": длина разошлась с манифестом");

                    // Инплейс: у корневых кривых нет хода (горизонтального root
                    // motion ноль). Клип крутит только кости.
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.path.Contains("/"))
                            continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.keys.Length < 2)
                            continue;
                        var low = curve.keys.Min(key => key.value);
                        var high = curve.keys.Max(key => key.value);
                        Assert.Less(
                            high - low, 0.001f,
                            label + ": корневая кривая " + binding.propertyName + " едет");
                    }
                }
            }
        }

        /// <summary>
        /// Регрессия на «два шага, потом ноги стоят»: в цикле Move нет длинного
        /// провала движения. У фермерских исходников Quaternius `Walk` вдвое
        /// длиннее цикла — вторая половина это стояние на месте (линейный дрейф
        /// ~0.0002 на кадр); генератор срезает такой хвост
        /// (<c>animal_lib._static_tail</c>).
        ///
        /// Движение считается покадрово (кривые оцениваются в каждый кадр), а не
        /// по ключам: у процедурных клипов (паук, пиранья) ключи разрежены, и по
        /// ключам кадр без ключа выглядел бы паузой.
        /// </summary>
        [Test]
        public void MoveClip_HasNoStaticStretch()
        {
            const float StaticShare = 0.02f;

            foreach (var species in Species())
            {
                var path = ModelPath(species);
                var clip = Clips(species)["Move"];
                var frames = Mathf.RoundToInt(clip.length * clip.frameRate);
                Assert.Greater(frames, 2, path + ": клип Move слишком короткий");

                var curves = AnimationUtility.GetCurveBindings(clip)
                    .Select(binding => AnimationUtility.GetEditorCurve(clip, binding))
                    .Where(curve => curve != null)
                    .ToArray();

                var motion = new float[frames + 1];
                var previous = new float[curves.Length];
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = frame / clip.frameRate;
                    var sum = 0f;
                    for (var i = 0; i < curves.Length; i++)
                    {
                        var value = curves[i].Evaluate(time);
                        if (frame > 0)
                            sum += Mathf.Abs(value - previous[i]);
                        previous[i] = value;
                    }

                    motion[frame] = sum;
                }

                var total = motion.Skip(1).Sum();
                Assert.Greater(total, 0.001f, path + ": клип Move без движения");

                var mean = total / frames;
                var longest = 0;
                var run = 0;
                for (var frame = 1; frame <= frames; frame++)
                {
                    if (motion[frame] >= mean * StaticShare)
                    {
                        run = 0;
                        continue;
                    }

                    run++;
                    longest = Mathf.Max(longest, run);
                }

                // Короткое затишье (задержка лапы) допустимо, серия кадров
                // «стоит на месте» — нет.
                var allowed = Mathf.Max(2, frames / 10);
                Assert.LessOrEqual(
                    longest, allowed,
                    path + ": клип Move стоит " + longest + " кадров из " + frames);
            }
        }

        [Test]
        public void VisualPrefab_HasManifestSize()
        {
            foreach (var expectation in Expectations())
            {
                var path = PrefabPath(expectation.Species);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = Object.Instantiate(prefab);
                try
                {
                    var low = Vector3.one * float.MaxValue;
                    var high = Vector3.one * float.MinValue;
                    var meshes = 0;
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                    {
                        var mesh = MeshOf(renderer);
                        if (mesh == null)
                            continue;

                        meshes++;

                        // AABB меша в bind-позе, приведённый в локальное
                        // пространство префаба (без зашитого рыскания): та же
                        // величина, что генератор меряет в Blender. Renderer.bounds
                        // у SkinnedMeshRenderer в edit mode врёт (бокс по костям).
                        var bounds = mesh.bounds;
                        var toRoot = instance.transform.worldToLocalMatrix
                            * renderer.transform.localToWorldMatrix;
                        for (var corner = 0; corner < 8; corner++)
                        {
                            var point = new Vector3(
                                (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                                (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                                (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                            var local = toRoot.MultiplyPoint3x4(point);
                            low = Vector3.Min(low, local);
                            high = Vector3.Max(high, local);
                        }
                    }

                    Assert.Greater(meshes, 0, path + ": нет мешей");
                    var size = Vector3.Scale(high - low, instance.transform.lossyScale);
                    var axis = MetricAxis(expectation.Metric);
                    Assert.AreEqual(
                        expectation.Target, size[axis], expectation.Target * 0.02f + 0.005f,
                        path + ": габарит " + expectation.Metric + " " + size.ToString("F2"));
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
                return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
