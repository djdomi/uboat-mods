using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Course plotting: the game switches to the next waypoint only AFTER passing the current one, so the boat
    // overshoots and turns in a wide arc behind the point. Now the player's boat starts turning early
    // (lead distance = turn radius * tan(half the turn angle)), so it rounds the corner on the line. World units = km.
    [HarmonyPatch(typeof(EntityRoute), "Update", new[] { typeof(Vector2) })]
    internal static class RouteLeadPatch
    {
        private static readonly AccessTools.FieldRef<EntityRoute, Entity> EntityF = AccessTools.FieldRefAccess<EntityRoute, Entity>("entity");
        private static readonly AccessTools.FieldRef<EntityRoute, List<Vector2>> PointsF = AccessTools.FieldRefAccess<EntityRoute, List<Vector2>>("waypoints");

        private static void Prefix(EntityRoute __instance, Vector2 position)
        {
            if (!Settings.B("RouteLead")) return;
            var ship = Util.PlayerShip;
            if (ship == null || !ReferenceEquals(EntityF(__instance), ship)) return;
            var pts = PointsF(__instance);
            if (pts == null || pts.Count < 2) return;   // last point: drive to it exactly
            Vector2 toWp = pts[0] - position, next = pts[1] - pts[0];
            float dist = toWp.magnitude;
            if (dist < 1e-4f || next.sqrMagnitude < 1e-8f) return;
            float angle = Vector2.Angle(toWp, next);      // 0 = straight on, 90 = right angle
            if (angle < 5f) return;
            float radiusKm = Mathf.Max(0.05f, Settings.F("RouteLeadRadius") / 1000f);
            float lead = Mathf.Min(radiusKm * Mathf.Tan(Mathf.Min(angle, 170f) * 0.5f * Mathf.Deg2Rad), next.magnitude * 0.5f);
            if (dist <= lead) __instance.RemoveAtSilent(0);
        }
    }
}
