using System.Reflection;
using UnityEngine;
using FrostSpace.Features.Ride;

namespace FrostSpaceTools
{
    public static class PoseStateProbe
    {
        public static string Execute()
        {
            var poses = Object.FindObjectsByType<RiderPose>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var report = "poses=" + poses.Length;
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var pose in poses)
            {
                report += "\n@" + pose.gameObject.name;
                foreach (var field in new[] { "_pelvis", "_spine", "_neck", "_upperArmR", "_handR", "_thighL" })
                {
                    var value = typeof(RiderPose).GetField(field, flags)?.GetValue(pose) as Object;
                    report += " " + field + "=" + (value != null);
                }
            }

            return report;
        }
    }
}
