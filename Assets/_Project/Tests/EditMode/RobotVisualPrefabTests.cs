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

        private static readonly string[] States = { "Idle", "Walk", "Sprint" };

        /// <summary>
        /// Список роботов берётся из папки префабов, а не из константы: партия
        /// расширяется и сокращается без правки теста. Имя префаба
        /// <c>R*_Model</c> задаёт имя модели <c>R*</c>.
        /// </summary>
        private static string[] Robots()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("_Model.prefab"))
                .Select(path => System.IO.Path.GetFileNameWithoutExtension(path)
                    .Replace("_Model", string.Empty))
                .OrderBy(name => name)
                .ToArray();
        }

        [Test]
        public void RobotFolder_IsNotEmpty()
        {
            CollectionAssert.IsNotEmpty(Robots(), "В папке префабов нет ни одного робота");
        }

        [Test]
        public void VisualPrefab_HasAnimatorWithoutGameplayLogic()
        {
            foreach (var robot in Robots())
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
            foreach (var robot in Robots())
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

        /// <summary>
        /// Прыжок есть только у модели игрока: остальные роботы — NPC, им прыжок
        /// не нужен, и лишние клипы в их FBX не появляются.
        /// </summary>
        [Test]
        public void PlayerRobot_HasJumpClipsAndStates()
        {
            const string robot = "R1_PolarMechanic";
            var modelPath = ModelFolder + "/" + robot + ".fbx";
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview"))
                .GroupBy(clip => clip.name)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var name in new[] { "JumpStart", "JumpLoop", "JumpLand" })
                Assert.IsTrue(clips.ContainsKey(name), modelPath + ": нет клипа " + name);

            Assert.AreEqual(40f / 30f, clips["JumpStart"].length, 0.02f);
            Assert.AreEqual(75f / 30f, clips["JumpLoop"].length, 0.02f);
            Assert.AreEqual(38f / 30f, clips["JumpLand"].length, 0.02f);
            Assert.IsTrue(
                AnimationUtility.GetAnimationClipSettings(clips["JumpLoop"]).loopTime,
                "JumpLoop должен зацикливаться");
            Assert.IsFalse(
                AnimationUtility.GetAnimationClipSettings(clips["JumpStart"]).loopTime,
                "JumpStart не должен зацикливаться");
            Assert.IsFalse(
                AnimationUtility.GetAnimationClipSettings(clips["JumpLand"]).loopTime,
                "JumpLand не должен зацикливаться");

            foreach (var name in new[] { "JumpStart", "JumpLoop", "JumpLand" })
                Assert.IsFalse(clips[name].hasRootCurves, "клип " + name + " должен быть in-place");

            var path = PrefabFolder + "/" + robot + "_Model.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = prefab.GetComponent<Animator>();
            var controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.IsNotNull(controller, path + ": controller не AnimatorController");

            var stateNames = controller.layers[0].stateMachine.states
                .Select(state => state.state.name)
                .ToArray();
            foreach (var state in new[] { "JumpStart", "JumpLoop", "JumpLand" })
                CollectionAssert.Contains(stateNames, state, path + ": нет состояния " + state);

            var jump = controller.parameters.FirstOrDefault(parameter => parameter.name == "Jump");
            var airborne = controller.parameters.FirstOrDefault(
                parameter => parameter.name == "Airborne");
            Assert.IsNotNull(jump, path + ": нет параметра Jump");
            Assert.AreEqual(AnimatorControllerParameterType.Trigger, jump.type);
            Assert.IsNotNull(airborne, path + ": нет параметра Airborne");
            Assert.AreEqual(AnimatorControllerParameterType.Bool, airborne.type);
        }

        [Test]
        public void RobotModel_HasThreeInPlaceClips()
        {
            foreach (var robot in Robots())
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
