using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Better crew at the guns and at the hydrophone (QoL, not cheats: the game's own mechanics, just tuned).
    //
    // Guns (only your own boat, only when the crew aims, never when you aim yourself):
    //  * Ranging in: the game raises the hit chance a little with every shot ("einschießen").
    //    GunnerRangingSpeed multiplies that step.
    //  * New target: the game throws most of the ranging away (keeps 55 %). GunnerKeepRanging sets how much stays.
    //  * Target not seen with the eyes (night, only heard / radar): the game adds extra spread.
    //    GunnerNightPenalty sets how much of that extra spread is used (100 % = game).
    // Hydrophone (only while a crewman sits at the listening room):
    //  * HorcherRange: hears ships further away, so more and earlier reports.
    //  * HorcherBearing: contacts on the map get a more exact bearing.
    public class CrewSkills : ModuleBase
    {
        internal const string ModName = "Toolkit Horcher";
        internal static bool HydrophoneManned;
        private static readonly List<Hydrophone> boosted = new List<Hydrophone>();
        private float lastRange = -1f;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            float pct = Mathf.Max(0f, Settings.F("HorcherRange"));
            bool manned = false;
            Hydrophone hydro = null;
            if (ship != null)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null || !(act.AssociatedEquipment is ListeningRoom lr) || !Util.IsPlayers(lr)) continue;
                    hydro = lr.Hydrophone;
                    manned = act.Users.Count > 0;
                    break;
                }
            HydrophoneManned = manned;

            bool want = manned && hydro != null && pct > 0f;
            // Drop the bonus when nobody listens, the setting changed or the hydrophone is another one.
            if (boosted.Count > 0 && (!want || pct != lastRange || !boosted.Contains(hydro)))
            {
                foreach (var h in boosted)
                    if (h != null) { h.DetectionRange.RemoveScaleModifiers(ModName); h.GroupDetectionRange.RemoveScaleModifiers(ModName); }
                boosted.Clear();
            }
            if (want && boosted.Count == 0)
            {
                float f = 1f + pct / 100f;
                hydro.DetectionRange.AddScaleModifier(ModName).Value = f;
                hydro.GroupDetectionRange.AddScaleModifier(ModName).Value = f;
                boosted.Add(hydro);
                lastRange = pct;
                if (Settings.B("CrewSkillLog")) Util.Log("Horcher: Reichweite x" + f.ToString("0.00") + " (Horcher besetzt)");
            }
        }
    }

    // More exact bearings while the hydrophone is manned.
    [HarmonyPatch(typeof(SingularContactInfo), "BearingInaccuracy", MethodType.Setter)]
    internal static class CrewSkills_BearingPatch
    {
        private static void Prefix(ref float value)
        {
            if (!CrewSkills.HydrophoneManned) return;
            float pct = Mathf.Clamp(Settings.F("HorcherBearing"), 0f, 90f);
            if (pct > 0f) value *= 1f - pct / 100f;
        }
    }

    // Gun ranging: bigger step per shot.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class CrewSkills_RangingPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Gun __instance, bool ___manualAim, float ___currentAccuracy, out float __state)
        {
            __state = -1f;
            if (!___manualAim && Util.IsPlayers(__instance)) __state = ___currentAccuracy;
        }

        private static void Postfix(Gun __instance, ref float ___currentAccuracy, float __state)
        {
            if (__state < 0f) return;
            float mul = Settings.F("GunnerRangingSpeed");
            float step = ___currentAccuracy - __state;
            if (mul > 1f && step > 0f) ___currentAccuracy = Mathf.Min(1f, __state + step * mul);
        }

        // The extra spread when the target is not seen with the eyes (constant 0.022 in Gun.Fire).
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var get = AccessTools.Method(typeof(CrewSkills_RangingPatch), nameof(NightPenalty));
            int n = 0;
            foreach (var ci in code)
            {
                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && Mathf.Abs(f - 0.022f) < 1e-6f)
                {
                    var call = new CodeInstruction(OpCodes.Call, get);
                    call.labels.AddRange(ci.labels);
                    call.blocks.AddRange(ci.blocks);
                    yield return call;
                    n++;
                    continue;
                }
                yield return ci;
            }
            if (n != 1) Util.Log("Schützen: Nacht-Streuung im Spiel nicht gefunden (" + n + "), bleibt wie im Spiel");
        }

        internal static float NightPenalty()
        {
            // Weapons_FirePatch.Current is set only while one of OUR guns fires.
            if (Weapons_FirePatch.Current == null) return 0.022f;
            if (Settings.B("CheatPerfectAim")) return 0f;
            return 0.022f * Mathf.Max(0f, Settings.F("GunnerNightPenalty")) / 100f;
        }
    }

    // New target: keep more of the ranging.
    [HarmonyPatch(typeof(Gun), "TargetOverride", MethodType.Setter)]
    internal static class CrewSkills_TargetPatch
    {
        private static void Prefix(Gun __instance, float ___currentAccuracy, Entity ___targetOverride, Entity value, out float __state)
        {
            __state = (___targetOverride != null && ___targetOverride != value && Util.IsPlayers(__instance)) ? ___currentAccuracy : -1f;
        }

        private static void Postfix(ref float ___currentAccuracy, float __state)
        {
            if (__state < 0f) return;
            float keep = Mathf.Clamp(Settings.F("GunnerKeepRanging"), 0f, 100f) / 100f;
            ___currentAccuracy = Mathf.Max(___currentAccuracy, __state * keep);
        }
    }
}
