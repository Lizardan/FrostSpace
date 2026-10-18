using System.Linq;
using FrostSpace.Features.Locomotion;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет границу «визуальный префаб — сценовая логика» и контракт
    /// FBX-клипов роботов. Тесты читают ассеты, ничего не меняя.
    /// </summary>
    public sealed class RobotVisualPrefabTests
    {
        private const string ModelFolder = "Assets/_Project/Content/Models/Robots";
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Robots";

        private static readonly string[] Robots =
        {
            "R1_PolarMechanic", "R2_Furnace", "R3_Scout", "R4_Hauler", "R5_Ranger",
        };

        private static readonly string[] States = { "Idle", "Walk", "Sprint" };

        [Test]
        public void VisualPrefab_HasAnimatorWithoutGameplayLogic()
        {
            foreach (var robot in Robots)
            {
                var path = PrefabFolder + "/" + robot + "_Model.prefab";
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
                    prefab.GetComponent<RobotLocomotion>(),
                    path + ": на визуале не должно быть игровой логики");
                Assert.AreEqual("Untagged", prefab.tag, path + ": визуалу назначен тег");
            }
        }

        [Test]
        public void VisualPrefab_ControllerHasLocomotionStates()
        {
            foreach (var robot in Robots)
            {
                var path = PrefabFolder + "/" + robot + "_Model.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "Нет визуального префаба: " + path);

                var animator = prefab.GetComponent<Animator>();
                var controller = animator.runtimeAnimatorController as AnimatorController;
                Assert.IsNotNull(controller, path + ": controller не AnimatorController");

                var stateNames = controller.layers[0].stateMachine.states
                    .Select(state => state.state.name)
                    .ToArray();
                foreach (var state in States)
                    CollectionAssert.Contains(stateNames, state, path + ": нет состояния " + state);

                var parameterNames = controller.parameters
                    .Select(parameter => parameter.name)
                    .ToArray();
                CollectionAssert.Contains(parameterNames, "Moving", path + ": нет Moving");
                CollectionAssert.Contains(parameterNames, "Sprinting", path + ": нет Sprinting");
            }
        }

        [Test]
        public void RobotModel_HasThreeInPlaceClips()
        {
            foreach (var robot in Robots)
            {
                var path = ModelFolder + "/" + robot + ".fbx";
                var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview"))
                    .GroupBy(clip => clip.name)
                    .ToDictionary(group => group.Key, group => group.First());

                Assert.IsTrue(clips.ContainsKey("Idle"), path + ": нет клипа Idle");
                Assert.IsTrue(clips.ContainsKey("Walk"), path + ": нет клипа Walk");
                Assert.IsTrue(clips.ContainsKey("Sprint"), path + ": нет клипа Sprint");

                Assert.AreEqual(2.5f, clips["Idle"].length, 0.02f, path + ": длина Idle");
                Assert.AreEqual(40f / 30f, clips["Walk"].length, 0.02f, path + ": длина Walk");
                Assert.AreEqual(20f / 30f, clips["Sprint"].length, 0.02f, path + ": длина Sprint");

                foreach (var state in States)
                    Assert.IsFalse(
                        clips[state].hasRootCurves,
                        path + ": клип " + state + " должен быть in-place");
            }
        }
    }
}
