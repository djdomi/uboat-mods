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
        // "Hydrophone hears fully surfaced / fast": see HydroSame below (no per-tick overwrite any more).
        private readonly System.Collections.Generic.Dictionary<Hydrophone, bool> state = new System.Collections.Generic.Dictionary<Hydrophone, bool>();
        public override void Tick()
        {
            foreach (var h in Util.Find<Hydrophone>())
            {
                if (h == null) continue;
                bool want = HydroSame.Wanted(h);
                state.TryGetValue(h, out var was);
                if (want == was) continue;
                state[h] = want;
                if (want) HydroSame.Force(h); else HydroSame.Restore(h);
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

namespace UboatToolkit.Modules
{
    // Hydrophone "as good surfaced and fast as submerged and slow" (cheat CheatHydrophoneAlways, Warthog WhPassiveSame).
    // The game eases the speed penalty EVERY FRAME (Hydrophone.UpdateShipVelocityModifier) and fades the surface penalty
    // over 0.5 s. Overwriting the modifiers once per tick made the range jump between two values = the two green range
    // circles on the map flickered. The old fix patched SetShipVelocityModifiers, a 6-line method the JIT inlines, so it
    // never ran (no log line in DoMe's log 2026-11-13). Now the game's TARGET values are set to 1 in the bigger methods
    // that decide them, so the game itself settles on 1 and there is only one value.
    internal static class HydroSame
    {
        private static readonly System.Reflection.FieldInfo targetF = AccessTools.Field(typeof(Hydrophone), "shipVelocityModifierTargetValue");
        private static readonly System.Reflection.FieldInfo tweenF = AccessTools.Field(typeof(Hydrophone), "rangeModifierTween");
        private static readonly string[] Mods = { "surfaceRangeModifier", "surfaceGroupRangeModifier", "shipVelocityRangeModifier", "shipVelocityGroupRangeModifier" };
        private static bool logged;

        internal static bool Wanted(Hydrophone h)
        {
            if (h == null || !Util.IsPlayers(h)) return false;
            if (Settings.B("CheatHydrophoneAlways")) return true;
            return Warthog.WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame");
        }

        internal static void Force(Hydrophone h)
        {
            try
            {
                if (targetF != null) targetF.SetValue(h, 1f);
                // The game's range tween is NOT aborted any more: the map draws the green range circles from it (gone since 0.2.54).
                var t = Traverse.Create(h);
                foreach (var f in Mods) { var m = t.Field(f).GetValue<Modifier>(); if (m != null && m.Value != 1f) m.Value = 1f; }
                if (!logged) { logged = true; Util.Log("Horcher: Abzug für Fahrt/aufgetaucht aufgehoben, Hörkreis fest (" + h.name + ")"); }
            }
            catch (System.Exception e) { Util.Log("Horcher: " + e.Message); }
        }

        // switched off again: let the game compute its own values
        internal static void Restore(Hydrophone h)
        {
            try
            {
                var t = Traverse.Create(h);
                t.Method("ValidateVelocityModifier").GetValue();
                if (h.ParentEntity is UBOAT.Game.Scene.Entities.PlayableSubmarine) t.Method("SurfaceRangeModifierParameter_Changed", new[] { typeof(Parameter) }, new object[] { null }).GetValue();
            }
            catch (System.Exception e) { Util.Log("Horcher zurück: " + e.Message); }
        }
    }

    [HarmonyPatch]
    internal static class HydroSame_Patches
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "ValidateVelocityModifier", "UpdateShipVelocityModifier", "Submarine_SubmergeStateChanged", "SurfaceRangeModifierParameter_Changed", "OnSandboxRepresentationSet" })
            {
                var m = AccessTools.Method(typeof(Hydrophone), n);
                if (m != null) yield return m; else Util.Log("Horcher: Spiel-Methode " + n + " fehlt");
            }
        }
        private static void Postfix(Hydrophone __instance) { if (HydroSame.Wanted(__instance)) HydroSame.Force(__instance); }
    }
}
