using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;

namespace UboatToolkit.Modules
{
    // Cheats:
    // - Radio reception at any depth.
    // - Hydrophone hears fully when surfaced and at high speed.
    // - Exact target data (speed, course, distance) instead of rough estimates.
    // - Hull armour: damage to your boat and crew is reduced (explosions, shells, torpedoes, depth charges, ramming).
    public class SensorCheats : ModuleBase
    {
        public override void Tick()
        {
            if (!Settings.B("CheatHydrophoneAlways")) return;
            foreach (var h in Util.Find<Hydrophone>())
            {
                if (h == null) continue;
                var t = Traverse.Create(h);
                foreach (var f in new[] { "surfaceRangeModifier", "surfaceGroupRangeModifier", "shipVelocityRangeModifier", "shipVelocityGroupRangeModifier" })
                {
                    var m = t.Field(f).GetValue<Modifier>();
                    if (m != null && m.Value < 1f) m.Value = 1f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Antenna), "LowFrequencyUpdate")]
    internal static class Sensor_RadioDepthPatch
    {
        private static void Postfix(Antenna __instance)
        {
            if (Settings.B("CheatRadioAnyDepth") && __instance.ReceiveRangeDepthModifier != null)
                __instance.ReceiveRangeDepthModifier.Value = 1f;
        }
    }

    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class Sensor_ExactTargetsPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Settings.B("CheatExactTargets")) __result = false;
        }
    }

    [HarmonyPatch(typeof(DamageUtility), nameof(DamageUtility.ApplyDamage))]
    internal static class Sensor_ArmourPatch
    {
        private static void Prefix(Entity appliedBy, Vector3 position, float damageRadius, ref float damage, ref float crewDamage, ref float fireChance, Ship target)
        {
            float hull = Settings.F("CheatHullArmour");
            bool crew = Settings.B("CheatCrewProtection");
            if (hull >= 1f && !crew) return;
            var ship = Util.PlayerShip;
            if (ship == null || appliedBy == ship) return;
            if (target != null && target != ship) return;
            if ((position - ship.transform.position).magnitude > 80f + damageRadius) return;
            if (hull < 1f) { damage *= hull; fireChance *= hull; }
            if (crew) crewDamage = 0f;
        }
    }
}
