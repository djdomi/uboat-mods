using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Gun gyro (only your own boat's guns).
    //  * Crew aims (GunGyroCrew): the game turns a gun very slowly (5 % speed) for the last 8 degrees
    //    to the target. In waves the boat rolls faster than that, so the barrel never settles.
    //    With the gyro the gun follows those small corrections at once; big turns to a new target
    //    keep the crew's normal turning speed.
    //  * You aim yourself (GunGyroManual): the game keeps the gun fixed to the boat, so the boat's
    //    roll moves your aim point. With the gyro the gun keeps pointing at the same spot in the world
    //    (the boat's own movement is taken out), and only your mouse moves it.
    [HarmonyPatch(typeof(Gun), "UpdateAnglesGradually")]
    internal static class GunGyro_Patch
    {
        private const float FineZone = 8f; // the game's own slow fine-tracking zone
        private static readonly AccessTools.FieldRef<Gun, Transform> Origin = AccessTools.FieldRefAccess<Gun, Transform>("originTransform");

        private class Hold { public Vector3 World; public Vector2 Set; }
        private static readonly Dictionary<int, Hold> holds = new Dictionary<int, Hold>();

        private static Vector3 Dir(Vector2 a) => Quaternion.Euler(a.x, a.y, 0f) * Vector3.forward;

        private static Vector2 Angles(Vector3 d)
        {
            float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            return new Vector2(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        private static void Prefix(Gun __instance, bool ___manualAim, ref Vector2 ___angles, ref Vector2 ___targetAngles)
        {
            if (!Util.IsPlayers(__instance)) return;
            int id = __instance.GetInstanceID();

            if (!___manualAim)
            {
                holds.Remove(id);
                if (!Settings.B("GunGyroCrew")) return;
                float dy = Mathf.DeltaAngle(___angles.y, ___targetAngles.y), dx = ___targetAngles.x - ___angles.x;
                if (dx * dx + dy * dy < FineZone * FineZone) ___angles = ___targetAngles;
                return;
            }

            if (!Settings.B("GunGyroManual")) { holds.Remove(id); return; }
            var origin = Origin(__instance);
            if (origin == null) return;
            Hold h;
            if (!holds.TryGetValue(id, out h) || (___targetAngles - h.Set).sqrMagnitude > 1e-6f)
            {
                // First frame or you moved the mouse: remember where the gun now points in the world.
                if (h == null) { h = new Hold(); holds[id] = h; }
                h.World = origin.TransformDirection(Dir(___targetAngles));
                h.Set = ___targetAngles;
                return;
            }
            // Boat moved, mouse did not: turn the gun so it still points at the same world spot.
            Vector2 want = Angles(origin.InverseTransformDirection(h.World));
            __instance.SetAngles(want);          // keeps the gun's own turning/elevation limits
            h.Set = ___targetAngles;
        }
    }
}
