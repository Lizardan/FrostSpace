using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using FrostSpace.Features.Ride;
using FrostSpace.Features.Hud;

namespace FrostSpaceTools
{
    public static class FixWiring
    {
        public static string Execute()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions == null)
                return "FAIL: action asset not found";

            foreach (var name in new[] { "Hoverbike_A", "Hoverbike_B" })
            {
                var go = GameObject.Find(name);
                if (go == null)
                    return "FAIL: " + name + " not found";

                var bike = go.GetComponent<Hoverbike>();
                if (bike == null)
                    return "FAIL: Hoverbike component missing on " + name;

                var so = new SerializedObject(bike);
                so.FindProperty("_actions").objectReferenceValue = actions;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bike);
            }

            var hudGo = GameObject.Find("HUD");
            if (hudGo == null)
                return "FAIL: HUD not found";

            var hud = hudGo.GetComponent<PlayerHud>();
            var hso = new SerializedObject(hud);
            hso.FindProperty("_labelLift").floatValue = 68f;
            hso.FindProperty("_labelArm").floatValue = 150f;
            hso.FindProperty("_labelNameSize").floatValue = 34f;
            hso.FindProperty("_labelCategorySize").floatValue = 20f;
            hso.FindProperty("_labelLineWidth").floatValue = 3f;
            hso.FindProperty("_labelOutline").floatValue = 0.24f;
            hso.FindProperty("_labelOutlineColor").colorValue = new Color(0.02f, 0.05f, 0.07f, 1f);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);

            var player = GameObject.Find("Player");
            var pose = player != null ? player.GetComponent<RiderPose>() : null;
            if (pose == null)
                return "FAIL: RiderPose missing on Player";

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            return "ok: actions on both bikes, HUD label, RiderPose present";
        }
    }
}
