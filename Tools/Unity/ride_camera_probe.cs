using System.Reflection;
using UnityEngine;
using FrostSpace.Features.View;

namespace FrostSpaceTools
{
    public static class RideCameraProbe
    {
        public static string Execute(string payload)
        {
            var rig = Object.FindFirstObjectByType<PlayerCameraRig>();
            if (rig == null)
                return "FAIL: no rig";

            var parts = payload.Split(',');
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            typeof(PlayerCameraRig).GetField("_rideHeight", flags)?.SetValue(rig, float.Parse(parts[0], culture));
            typeof(PlayerCameraRig).GetField("_rideDistance", flags)?.SetValue(rig, float.Parse(parts[1], culture));
            typeof(PlayerCameraRig).GetField("_ridePitch", flags)?.SetValue(rig, float.Parse(parts[2], culture));
            return "ride camera " + payload;
        }
    }
}
