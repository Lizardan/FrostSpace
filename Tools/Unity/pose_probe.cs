using System.Reflection;
using UnityEngine;
using FrostSpace.Features.Ride;
using FrostSpace.Features.View;

namespace FrostSpaceTools
{
    public static class PoseProbe
    {
        public static string Execute(string mode)
        {
            var bikeGo = GameObject.Find("Hoverbike_A");
            var bike = bikeGo != null ? bikeGo.GetComponent<Hoverbike>() : null;
            var rig = Object.FindFirstObjectByType<PlayerCameraRig>();

            switch (mode)
            {
                case "mount":
                    if (bike == null)
                        return "FAIL: no bike";
                    bike.Mount();
                    return "mounted=" + bike.IsMounted;

                case "dismount":
                    if (bike == null)
                        return "FAIL: no bike";
                    bike.Dismount();
                    return "mounted=" + bike.IsMounted;

                case "pitch_up":
                    SetPitch(rig, -30f);
                    return "pitch=-30";

                case "pitch_down":
                    SetPitch(rig, 65f);
                    return "pitch=65";

                case "pitch_mid":
                    SetPitch(rig, 10f);
                    return "pitch=10";

                case "report":
                {
                    var player = GameObject.Find("Player");
                    var pose = player != null ? player.GetComponent<RiderPose>() : null;
                    var pelvis = FindDeep(player != null ? player.transform : null, "pelvis");
                    var handR = FindDeep(player != null ? player.transform : null, "hand_r");
                    var hip = pelvis != null ? pelvis.position : Vector3.zero;
                    var hand = handR != null ? handR.position : Vector3.zero;
                    var bikePos = bike != null ? bike.transform.position : Vector3.zero;
                    return "riding=" + (pose != null && pose.IsRiding) +
                           " weight=" + (pose != null ? pose.Weight : -1f) +
                           " hips=" + hip.ToString("F2") +
                           " handR=" + hand.ToString("F2") +
                           " bike=" + bikePos.ToString("F2");
                }
            }

            return "unknown mode";
        }

        private static void SetPitch(PlayerCameraRig rig, float value)
        {
            if (rig == null)
                return;
            var field = typeof(PlayerCameraRig).GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(rig, value);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
                return null;
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
