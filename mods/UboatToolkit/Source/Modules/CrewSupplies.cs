using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Air, food and packing when the crew is bigger than normal:
    // - Air: a full crew always reaches the same dive time (default 11 h), whatever the crew limit is.
    //   Fewer men aboard = longer. Optional: prisoners shorten it (up to x % with a full prisoner hold).
    //   Absorbers / snorkel still add on top.
    // - Food: a bigger crew limit does not empty the stores faster (eats like the normal crew, or like x men).
    // - Tight packing skill: every man with it adds +x to the stack size of all stackable items
    //   (instead of the game's one extra slot).
    public class CrewSupplies : ModuleBase
    {
        private const string FoodMod = "Toolkit Food Norm";
        private Kitchen kitchen;
        private int lastPack = int.MinValue;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            Util.Safe("Luft", () => AirNorm.Apply(ship));
            Util.Safe("Essen", Food);
            Util.Safe("Packen", Pack);
        }

        internal static int MaxCrew()
        {
            var c = Util.Career;
            return c == null ? 0 : Mathf.RoundToInt(c.SailorsOnBoardLimit.Value + c.OfficersOnBoardLimit.Value);
        }

        // Normal crew = limits without our own "more officers" bonus.
        internal static int NormalCrew()
        {
            var c = Util.Career;
            if (c == null) return 0;
            var m = c.OfficersOnBoardLimit.GetDeltaModifier("Toolkit Officers", false);
            int v = Mathf.RoundToInt(c.SailorsOnBoardLimit.Value + c.OfficersOnBoardLimit.Value - (m != null ? m.Value : 0f));
            return v > 0 ? v : MaxCrew();
        }

        private void Food()
        {
            if (kitchen == null) kitchen = UnityEngine.Object.FindObjectOfType<Kitchen>();
            if (kitchen == null) return;
            var rate = kitchen.FoodConsumptionRate;
            var mod = rate.GetScaleModifier(FoodMod, true);
            float scale = 1f;
            if (Settings.B("FoodByCrew"))
            {
                int max = MaxCrew();
                int refCrew = Mathf.RoundToInt(Settings.F("FoodRefCrew"));
                if (refCrew <= 0) refCrew = NormalCrew();
                if (max > 0 && refCrew > 0) scale = Mathf.Min(1f, (float)refCrew / max);
            }
            if (Math.Abs(mod.Value - scale) > 1e-6f) mod.Value = scale;
        }

        private static void Inventory_Stack(int delta) { StackData.PackBonus = delta; StackData.Apply(); }

        private void Pack()
        {
            int per = Mathf.RoundToInt(Settings.F("TightPackPerMan"));
            int holders = 0;
            if (per > 0)
                foreach (var crew in Util.Find<PlayerCrew>(30f))
                    if (crew != null && crew.Characters != null)
                        foreach (var pc in crew.Characters)
                            if (pc != null && pc.Data != null && pc.Data.HasStatus(typeof(UBOAT.Game.Scene.Characters.Skills.TightPacking))) holders++;
            int delta = per * holders;
            if (delta == lastPack) return;
            lastPack = delta;
            Inventory_Stack(delta);
            if (holders > 0) Util.Log("Dichtes Packen: " + holders + " Mann, Stapel +" + delta);
        }
    }

    internal static class AirNorm
    {
        private static bool wasOn;
        private static readonly System.Reflection.MethodInfo vanilla = AccessTools.Method(typeof(PlayerShip), "ValidateOxygenBreathModifier");
        internal static void Apply(PlayerShip ship)
        {
            if (ship == null) return;
            if (!Settings.B("AirByCrew"))
            {
                if (wasOn) { wasOn = false; vanilla?.Invoke(ship, null); }   // back to the game's own value
                return;
            }
            wasOn = true;
            double t = Mathf.Max(0.25f, Settings.F("AirHours")) * 3600.0;
            var crew = Util.Find<PlayerCrew>(30f);
            PlayerCrew pc = null; foreach (var c in crew) { pc = c; break; }
            if (pc == null) return;
            if (Settings.B("AirPrisoners"))
            {
                int pris = pc.SurvivorCount;
                var career = Util.Career;
                float cap = career != null ? career.SurvivorsOnBoardLimit.Value : 0f;
                if (pris > 0)
                {
                    float frac = cap > 0f ? Mathf.Min(1f, pris / cap) : 1f;
                    t = Math.Max(60.0, t * (1.0 - Settings.F("AirPrisonerPenalty") / 100.0 * frac));
                }
            }
            int n = pc.Count, max = CrewSupplies.MaxCrew();
            if (n <= 0 || max <= 0) return;
            if (n > max) max = n;
            var mod = Traverse.Create(ship).Field("oxygenBreathModifier").GetValue<Modifier>();
            if (mod == null) return;
            float drain = (float)(-(1.0 / t) * ((double)n / max));
            if (Math.Abs(mod.Value - drain) > 1e-10f) mod.Value = drain;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ValidateOxygenBreathModifier")]
    internal static class AirNorm_Patch
    {
        private static void Postfix(PlayerShip __instance) { try { AirNorm.Apply(__instance); } catch { } }
    }

    // The skill now means deeper stacks, so the game's extra slot is not added.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.TightPacking), "ValidateAdditionalSlotPresence")]
    internal static class TightPack_NoSlot
    {
        private static bool Prefix() => Settings.F("TightPackPerMan") <= 0f;
    }
}
