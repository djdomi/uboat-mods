using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Time compression ignores fishing boats and friendly (green) ships, and
    // "back to water": if the boat is stuck on land anyway, it is put back at the last safe spot at sea.
    public class Traffic : ModuleBase
    {
        private Vector2 lastSafe;
        private bool hasSafe;
        private float stuckSince = -1f;

        // true = this group should not stop time compression.
        public static bool Ignore(SandboxGroup g)
        {
            if (g == null) return false;
            if (Settings.B("IgnoreFishingBoats") && g.Type == "Fishing Boat") return true;
            var me = Util.PlayerShip?.SandboxEntity?.Country;
            if (g.Country == null || me == null) return false;
            var rel = g.Country.GetRelationWith(me);
            if (Settings.B("IgnoreAlliedShips") && rel == Country.Relation.Ally) return true;
            if (Settings.B("IgnoreNeutralShips") && rel == Country.Relation.Neutral) return true;
            return false;
        }

        // For the mod's own contact stop: true if all ships of this contact are ignored.
        public static bool Ignore(IContactInfo c)
        {
            var list = new List<SingularContactInfo>();
            if (c is SingularContactInfo s) list.Add(s);
            else if (c is CompoundContactInfo cc)
                list.AddRange(Traverse.Create(cc).Field("entityInfos").GetValue<List<SingularContactInfo>>() ?? new List<SingularContactInfo>());
            if (list.Count == 0) return false;
            foreach (var x in list)
            {
                var e = Traverse.Create(x).Field("entity").GetValue<SandboxEntity>();
                if (!Ignore(e?.Group)) return false;
            }
            return true;
        }

        public override void Tick()
        {
            var w = Game.Wolfpack;
            var ship = Util.PlayerShip;
            if (w == null || ship.Docked) { stuckSince = -1f; return; }

            // Remember the last spot with at least 1 km water to the coast.
            if (w.LandDistance > 1f) { lastSafe = w.Position; hasSafe = true; }

            var o = Settings.Get("BackToWaterNow");
            if (o != null && o.On) { o.Value = 0; BackToWater("von Hand"); return; }

            if (!Settings.B("BackToWater")) { stuckSince = -1f; return; }
            bool engineOn = ship.ActiveEngine != null && ship.ActiveEngine.GearIndex != 0;
            bool stuck = w.LandDistance < 0.3f && engineOn && Mathf.Abs(ship.AverageVelocity) < 0.3f;
            if (!stuck) { stuckSince = -1f; return; }
            if (stuckSince < 0f) { stuckSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - stuckSince >= Settings.F("BackToWaterSeconds")) BackToWater("automatisch");
        }

        private void BackToWater(string how)
        {
            stuckSince = -1f;
            if (!hasSafe) { ToolkitRunner.ShowNotice("Keine sichere Stelle gemerkt", 4f); return; }
            var w = Game.Wolfpack;
            var tc = Game.TimeCompression;
            if (tc != null) tc.TimeCompression = false;
            w.SetPosition(lastSafe, true, true);
            ToolkitRunner.ShowNotice("Zurück ins Wasser (" + how + ")", 6f);
        }
    }

    // The game's own time compression stop on a new sighting: undo it for ignored groups.
    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "PlayerWolfpack_ObservationAdded")]
    internal static class Traffic_ObservationPatch
    {
        private static void Prefix(out bool __state)
        {
            __state = Game.TimeCompression?.TimeCompression ?? false;
        }

        private static void Postfix(GroupObservation observation, bool __state)
        {
            var tc = Game.TimeCompression;
            if (!__state || tc == null || tc.TimeCompression) return;
            if (Traffic.Ignore(observation?.Group)) tc.TimeCompression = true;
        }
    }
}
