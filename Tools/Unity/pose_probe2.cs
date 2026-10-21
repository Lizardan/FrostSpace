using System.Reflection;
using UnityEngine;
using FrostSpace.Features.Ride;
using FrostSpace.Features.View;

namespace FrostSpaceTools
{
    public static class PoseProbe2
    {
        public static string Execute()
        {
            var player = GameObject.Find("Player");
            var seat = FindDeep(GameObject.Find("Hoverbike_A").transform, "Seat");
            var pelvis = FindDeep(player.transform, "pelvis");
            var handR = FindDeep(player.transform, "hand_r");
            var handL = FindDeep(player.transform, "hand_l");
            var indexR = FindDeep(player.transform, "index_01_r");
            var kneeR = FindDeep(player.transform, "calf_r");
            var footR = FindDeep(player.transform, "foot_r");
            var pose = player.GetComponent<RiderPose>();

            return "seat=" + seat.position.ToString("F2") +
                   "\nroot=" + player.transform.position.ToString("F2") +
                   "\npelvis=" + pelvis.position.ToString("F2") +
                   "\nkneeR=" + kneeR.position.ToString("F2") +
                   "\nfootR=" + footR.position.ToString("F2") +
                   "\nhandR=" + handR.position.ToString("F2") +
                   "\nhandL=" + handL.position.ToString("F2") +
                   "\nindexR=" + indexR.position.ToString("F2") +
                   "\nweight=" + pose.Weight.ToString("F2");
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
