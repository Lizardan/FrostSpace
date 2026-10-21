using UnityEngine;

namespace FrostSpaceTools
{
    public static class BoneProbe
    {
        public static string Execute()
        {
            var player = GameObject.Find("Player");
            var all = player.GetComponentsInChildren<Transform>(true);
            var report = "count=" + all.Length;
            foreach (var name in new[] { "pelvis", "upperarm_r", "hand_r", "spine_01", "thigh_l" })
            {
                var found = false;
                foreach (var t in all)
                {
                    if (t.name == name)
                    {
                        found = true;
                        break;
                    }
                }

                report += "\n" + name + "=" + found;
            }

            report += "\nfirst5=";
            for (var i = 0; i < Mathf.Min(5, all.Length); i++)
                report += all[i].name + ",";

            return report;
        }
    }
}
