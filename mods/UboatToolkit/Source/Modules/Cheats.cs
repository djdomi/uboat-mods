using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Cheats for the player's boat (single player). Every cheat is off by default.
    public class Cheats : ModuleBase
    {
        private PlayerCrew crew;
        private PlayerCareer career;

        public override void Tick()
        {
            var ship = Util.PlayerShip;

            // Sensor range: radar, sonar, hydrophones, lookouts, radar detector see further.
            float sr = Mathf.Max(1f, Settings.F("CheatSensorRange"));
            foreach (var d in Util.Find<UBOAT.Game.Scene.Items.BaseDetector>())
            {
                float m = Util.IsPlayers(d) ? sr : 1f;   // 0.3.34: enemies keep their game range
                Util.ScaleParameter(d.DetectionRange, m);
                Util.ScaleParameter(d.GroupDetectionRange, m);
            }

            // Undetectable: seen, heard (sonar + hydrophones) and radar-detected at almost zero range.
            float f = Settings.B("CheatUndetectable") ? 0.001f : 1f;
            var t = Traverse.Create(ship);
            foreach (var field in new[] { "visualDetectability", "sonarDetectability", "radarDetectorDetectability", "hydroacousticDetectorsRangeScale" })
                Util.ScaleParameter(t.Field(field).GetValue<Parameter>(), f);
            // Also the boat's own noise (dB, heard by hydrophones) and its radar size (cm): the game's own
            // dev-console "undetectable" sets exactly these to 0. Shown in the detection tooltip too.
            bool und = Settings.B("CheatUndetectable");
            Util.Safe("Undetectable.Noise", () =>
            {
                ship.HydrophoneDetectability.GetScaleModifier(Util.ModifierName, true).Value = und ? 0f : 1f;
                ship.SandboxEntity.SignatureRadius.GetScaleModifier(Util.ModifierName, true).Value = und ? 0f : 1f;
            });

            Util.ScaleParameter(t.Field("hullCrackThreshold").GetValue<Parameter>(), Settings.B("CheatDeepDive") ? 100f : 1f);
            // The game also has a separate "hull crush" timer (HullCrushController): below the boat's CrushDepth
            // it counts down and then sets the hull to 0 = boom, even without any leaks. Push that depth far away.
            Util.Safe("DeepDive.Crush", () =>
                Util.ScaleParameter(ship.SandboxEntity.Parameters.GetParameter("CrushDepth"), Settings.B("CheatDeepDive") ? 100f : 1f));

            if (Settings.B("CheatFullFuel")) Util.Safe("FullFuel", () => FillFuel(ship));
            if (Settings.B("CheatFullBattery")) Fill(ship.Energy);
            if (Settings.B("CheatFullAir")) Fill(ship.Oxygen);
            if (Settings.B("CheatFullDiscipline"))
            {
                Fill(ship.Discipline);
                if (ship.Stress != null) ship.Stress.Amount = 0;
            }
            Util.Safe("Moral", () => NoMoraleLoss(ship));
            if (Settings.B("NoStress") && ship.Stress != null) ship.Stress.Amount = 0;

            if (Settings.B("CheatCrewNeverTired"))
            {
                if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
                if (crew != null && crew.Characters != null)
                    foreach (var c in crew.Characters) if (c != null) c.Energy = 1f;
            }

            float money = Settings.F("CheatMoney"), rep = Settings.F("CheatReputation");
            if (money > 0f || rep > 0f)
            {
                if (career == null) career = Util.Career;
                if (career != null)
                {
                    if (money > 0f && career.Budget < money) Util.SetField(career, "budget", Mathf.RoundToInt(money));
                    if (rep > 0f && career.ReputationPoints < rep) career.ReputationPoints = Mathf.RoundToInt(rep);
                }
            }
        }

        // The diesel % is computed by the game from the fuel lying in the tanks, so filling only the
        // number would be undone at once. Fill the tanks themselves.
        // Morale (game: "Discipline") with the cheat on: refilling alone left the display at e.g. "-102" (a big crew drains it
        // fast). Our own delta cancels exactly the negative part of the gain, so it shows 0 and never drops; gains stay.
        private const string MoraleMod = "UboatToolkit Moral";
        private static void NoMoraleLoss(PlayerShip ship)
        {
            var g = ship.Discipline != null ? ship.Discipline.Gain : null;
            if (g == null) return;
            var m = g.GetDeltaModifier(MoraleMod, true);
            float want = 0f;
            if (Settings.B("CheatFullDiscipline"))
            {
                float v0 = g.GetValueWithModifierOverride(m, 0f), v1 = g.GetValueWithModifierOverride(m, 1f);
                if (v0 < 0f && v1 - v0 > 1e-6f) want = -v0 / (v1 - v0);
            }
            if (Mathf.Abs(m.Value - want) > 1e-6f) m.Value = want;
        }

        private static void FillFuel(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var srcs = ship.FuelSources;
            if (srcs == null) return;
            foreach (var f in srcs)
            {
                if (f == null || f.Storage == null || f.Data == null || f.Data.Mass <= 0f) continue;
                double full = f.Storage.ItemsMassLimit / f.Data.Mass;
                if (full > 0 && f.DoubleCount < full) f.DoubleCount = full;
            }
        }

        private static void Fill(Resource r)
        {
            if (r == null || r.Capacity == null) return;
            double cap = r.Capacity.Value;
            if (cap > 0 && r.Amount < cap) r.Amount = cap;
        }
    }

    // Hull health of the player's boat never goes down.
    [HarmonyPatch(typeof(Entity), nameof(Entity.Health), MethodType.Setter)]
    internal static class Cheats_HullPatch
    {
        private static void Prefix(Entity __instance, ref float value)
        {
            if (Settings.B("CheatNoHullDamage") && __instance is PlayerShip && value < __instance.Health) value = __instance.Health;
        }
    }

    // No water pressure damage at great depth.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.ApplyWaterDamageToPlayerShip), "DoDamageTick")]
    internal static class Cheats_DeepDivePatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!Settings.B("CheatDeepDive")) return true;
            __result = 5f; // check again in 5 seconds
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    // "Crew never tired": also the game's own tired check (Fatigue skill, updated every second per man) always says rested,
    // and the man's energy is refilled right there (Tick alone missed men between ticks / new men).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.Fatigue), "UpdateFatigue")]
    internal static class Cheats_NeverTiredPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Characters.Skills.Fatigue __instance)
        {
            if (!Settings.B("CheatCrewNeverTired")) return;
            var c = __instance.Character;
            if (c != null) c.Energy = 1f;
        }
    }

    // Energy never goes down at all (DoMe: prevent the loss, not refill it): every write to Energy becomes "full".
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.PlayableCharacter), "Energy", HarmonyLib.MethodType.Setter)]
    internal static class Cheats_NoEnergyLossPatch
    {
        private static void Prefix(ref float value)
        {
            if (Settings.B("CheatCrewNeverTired")) value = 1f;
        }
    }
}
