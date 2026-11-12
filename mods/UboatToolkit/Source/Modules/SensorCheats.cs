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

namespace UboatToolkit.Modules
{
    // Own shells / flak must not damage our own boat. The game fires player shells with friendlyFire=true,
    // so a shell that bursts on our own tower or deck hurts hull, devices and crew - and the armour cheat
    // skipped those hits (appliedBy == our boat). Here our boat's parts are removed from the hit list.
    [HarmonyPatch(typeof(DamageUtility), "DoApplyDamage")]
    internal static class Sensor_OwnShellsPatch
    {
        internal static bool Active;
        private static bool logged;

        private static void Prefix(Entity appliedBy, DamageType damageType, ref float crewDamage)
        {
            Active = false;
            if (!Settings.B("CheatOwnShellsSafe") || damageType != DamageType.Projectile) return;
            var ship = Util.PlayerShip;
            if (ship == null || appliedBy != ship) return;
            Active = true;
            crewDamage = 0f;
            if (!logged) { logged = true; Util.Log("Eigene Granaten: Treffer am eigenen Boot werden ignoriert."); }
        }

        private static System.Exception Finalizer(System.Exception __exception) { Active = false; return __exception; }
    }

    [HarmonyPatch(typeof(DamageUtility), "CollectComponents")]
    internal static class Sensor_OwnShellsCollectPatch
    {
        private static readonly System.Reflection.FieldInfo compsF = AccessTools.Field(typeof(DamageUtility), "components");
        private static readonly System.Reflection.FieldInfo rendF = AccessTools.Field(typeof(DamageUtility), "damageRenderers");
        private static readonly System.Collections.Generic.List<UBOAT.Game.Scene.Effects.HullEffectsRenderer> drop = new System.Collections.Generic.List<UBOAT.Game.Scene.Effects.HullEffectsRenderer>();

        private static void Postfix()
        {
            if (!Sensor_OwnShellsPatch.Active) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            var comps = compsF != null ? compsF.GetValue(null) as System.Collections.Generic.SortedList<float, Equipment> : null;
            if (comps != null)
                for (int i = comps.Count - 1; i >= 0; i--)
                {
                    var eq = comps.Values[i];
                    if (eq != null && eq.ParentEntity == ship) comps.RemoveAt(i);
                }
            var rend = rendF != null ? rendF.GetValue(null) as System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Effects.HullEffectsRenderer, bool> : null;
            if (rend != null)
            {
                drop.Clear();
                foreach (var k in rend.Keys) if (k != null && k.ParentEntity == ship) drop.Add(k);
                for (int i = 0; i < drop.Count; i++) rend.Remove(drop[i]);
                drop.Clear();
            }
        }
    }
}
