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

            // Undetectable: seen, heard (sonar + hydrophones) and radar-detected at almost zero range.
            float f = Settings.B("CheatUndetectable") ? 0.001f : 1f;
            var t = Traverse.Create(ship);
            foreach (var field in new[] { "visualDetectability", "sonarDetectability", "radarDetectorDetectability", "hydroacousticDetectorsRangeScale" })
                Util.ScaleParameter(t.Field(field).GetValue<Parameter>(), f);

            if (Settings.B("CheatFullFuel")) Fill(ship.Fuel);
            if (Settings.B("CheatFullBattery")) Fill(ship.Energy);
            if (Settings.B("CheatFullAir")) Fill(ship.Oxygen);
            if (Settings.B("CheatFullDiscipline"))
            {
                Fill(ship.Discipline);
                if (ship.Stress != null) ship.Stress.Amount = 0;
            }

            if (Settings.B("CheatCrewNeverTired"))
            {
                if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
                if (crew != null && crew.Characters != null)
                    foreach (var c in crew.Characters) if (c != null) c.Energy = 1f;
            }

            float money = Settings.F("CheatMoney"), rep = Settings.F("CheatReputation");
            if (money > 0f || rep > 0f)
            {
                if (career == null) career = Object.FindObjectOfType<PlayerCareer>();
                if (career != null)
                {
                    if (money > 0f && career.Budget < money) Util.SetField(career, "budget", Mathf.RoundToInt(money));
                    if (rep > 0f && career.ReputationPoints < rep) career.ReputationPoints = Mathf.RoundToInt(rep);
                }
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
}
