using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrostSpace.Features.Locomotion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Собирает prefab робота из FBX: AnimatorController с Idle/Walk/Sprint,
    /// CharacterController и компонент движения. Повторный запуск пересоздаёт
    /// prefab и контроллер, поэтому импорт воспроизводим.
    /// </summary>
    public static class RobotPrefabBuilder
    {
        private const string ModelFolder = "Assets/_Project/Content/Models/Robots";
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Robots";
        private const string ControllerFolder = "Assets/_Project/Content/Animation/Controllers";
        private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";

        private static readonly string[] Robots =
        {
            "R1_PolarMechanic", "R2_Furnace", "R3_Scout",
            "R4_Hauler", "R5_Ranger",
        };

        [MenuItem("FrostSpace/Robots/Rebuild Robot Prefabs")]
        public static void RebuildAll()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(ControllerFolder);
            foreach (var robot in Robots)
                Build(robot);

            AssetDatabase.SaveAssets();
            Debug.Log("RobotPrefabBuilder: собрано prefab'ов — " + Robots.Length);
        }

        private static void Build(string robot)
        {
            var modelPath = ModelFolder + "/" + robot + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError("RobotPrefabBuilder: нет модели " + modelPath);
                return;
            }

            var clips = LoadClips(modelPath);
            if (!clips.TryGetValue("Idle", out var idle) ||
                !clips.TryGetValue("Walk", out var walk) ||
                !clips.TryGetValue("Sprint", out var sprint))
            {
                Debug.LogError("RobotPrefabBuilder: у " + robot + " нет трёх клипов");
                return;
            }

            var controller = BuildController(robot, idle, walk, sprint);
            var root = new GameObject(robot);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);

            // Animator живёт на корне модели: пути клипов начинаются с Armature.
            // Импортёр с avatarSetup=CreateFromThisModel уже добавил Animator.
            var animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.avatar = LoadAvatar(modelPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var controllerComponent = root.AddComponent<CharacterController>();
            controllerComponent.height = 1.9f;
            controllerComponent.radius = 0.42f;
            controllerComponent.center = new Vector3(0f, 0.95f, 0f);
            controllerComponent.skinWidth = 0.04f;
            controllerComponent.stepOffset = 0.35f;
            controllerComponent.slopeLimit = 45f;

            var locomotion = root.AddComponent<RobotLocomotion>();
            var serialized = new SerializedObject(locomotion);
            serialized.FindProperty("_actions").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (robot == "R1_PolarMechanic")
                root.tag = "Player";

            var prefabPath = PrefabFolder + "/" + robot + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        private static AnimatorController BuildController(
            string robot, AnimationClip idle, AnimationClip walk, AnimationClip sprint)
        {
            var path = ControllerFolder + "/" + robot + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Sprinting", AnimatorControllerParameterType.Bool);

            var machine = controller.layers[0].stateMachine;
            var idleState = machine.AddState("Idle", new Vector3(280f, 60f, 0f));
            var walkState = machine.AddState("Walk", new Vector3(520f, 60f, 0f));
            var sprintState = machine.AddState("Sprint", new Vector3(760f, 60f, 0f));
            idleState.motion = idle;
            walkState.motion = walk;
            sprintState.motion = sprint;
            machine.defaultState = idleState;

            AddTransition(idleState, walkState, "Moving", true);
            AddTransition(walkState, idleState, "Moving", false);
            AddTransition(walkState, sprintState, "Sprinting", true);
            AddTransition(sprintState, walkState, "Sprinting", false);
            AddTransition(sprintState, idleState, "Moving", false);
            return controller;
        }

        private static void AddTransition(
            AnimatorState from, AnimatorState to, string parameter, bool value)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.AddCondition(
                value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static Dictionary<string, AnimationClip> LoadClips(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview"))
                .ToDictionary(clip => clip.name, clip => clip);
        }

        private static Avatar LoadAvatar(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
