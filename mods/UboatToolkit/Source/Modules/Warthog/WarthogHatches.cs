using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Entities.NavMesh;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using UnityEngine.AI;

namespace UboatToolkit.Modules.Warthog
{
    // Hatches ("Luken") of U-1337 as one system instead of the game's single exterior hatch:
    //   Turmluk 1          the game's own exterior hatch (PlayerShip.ExteriorHatch)
    //   Turmluk 2          second hatch on the second tower drum, own ladders + flooding hole (built in)
    //   Seitentür Bb/Stb   doors deck <-> tower, walk-through links + flooding hole each (built in)
    //   Kombüsenluk        the game's cargo-loading hatch above the galley (if the boat has one as a hatch)
    //   Torpedoluk vorn/achtern  torpedo-loading hatches of the torpedo rooms (their leak point = flooding when open)
    // Each hatch has its own state. "Luken einzeln steuern" off: all follow Turmluk 1 (opened/closed together).
    // Diving / alarm: whenever the game wants the tower hatch shut (ShouldExteriorHatchBeClosed) all others are shut too,
    // so the game's dive checks (which look at the tower hatch) never dive with another hatch open; fresh air comes in
    // through every open hatch.
    // Open hatch under water = water into ITS room (own flooding hole). "Alle Luken benutzbar" off = game's single hatch.
    public class WarthogHatches : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhHatchAll");
        internal static bool Single => Settings.B("WhHatchSingle");

        internal class Luke
        {
            public string Name; public Hatch Hatch; public List<OffMeshLink> Links = new List<OffMeshLink>();
            public List<ExternalPressure> Holes = new List<ExternalPressure>(); public bool Open; public bool Manual;
            public bool IsMain;
            public bool IsOpen => Hatch != null ? !Hatch.Locked : Open;
        }
        internal static readonly List<Luke> All = new List<Luke>();
        private static object cachedFor;
        private bool lastOn = true;
        private object debounceShip; private bool rawMain, stableMain; private float rawSince;

        private static void Collect(PlayerShip ship)
        {
            if (cachedFor == (object)ship) return;
            cachedFor = ship; All.Clear();
            if (ship == null) return;
            var interior = ship.Interior != null ? ((Component)ship.Interior).transform : null;
            var main = ship.ExteriorHatch;
            All.Add(new Luke { Name = "Turmluk 1", Hatch = main, IsMain = true });
            var l2 = new Luke { Name = "Turmluk 2" };
            foreach (var h in ship.GetComponentsInChildren<Hatch>(true)) if (h.name == "Exterior Hatch 2") l2.Hatch = h;
            if (interior != null)
            {
                foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.Contains("(Hatch 2)")) l2.Links.Add(l);
                foreach (var side in new[] { ("Port", "Seitentür Bb"), ("Starboard", "Seitentür Stb") })
                {
                    var d = new Luke { Name = side.Item2 };
                    foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.StartsWith("Side Door " + side.Item1)) d.Links.Add(l);
                    foreach (var p in interior.GetComponentsInChildren<ExternalPressure>(true)) if (p.transform.parent != null && p.transform.parent.name == "Side Door " + side.Item1) d.Holes.Add(p);
                    if (d.Links.Count > 0 || d.Holes.Count > 0) All.Add(d);
                }
                // torpedo-loading hatches (leak points of the torpedo rooms)
                foreach (var room in new[] { ("Bow Torpedo Room", "Torpedoluk vorn"), ("Stern Torpedo Room", "Torpedoluk achtern") })
                {
                    var r = interior.Find(room.Item1); if (r == null) continue;
                    var th = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Torpedo Loading Hatch");
                    if (th == null) continue;
                    var t = new Luke { Name = room.Item2 };
                    t.Holes.AddRange(th.GetComponentsInChildren<ExternalPressure>(true));
                    All.Add(t);
                }
            }
            if (l2.Hatch != null) All.Insert(1, l2);
            var galley = Traverse.Create(ship).Field("cargoLoadingHatch").GetValue<Hatch>();
            if (galley != null && galley != main) All.Add(new Luke { Name = "Kombüsenluk", Hatch = galley });
            Util.Log("[Warthog] hatch: " + All.Count + " hatches: " + string.Join(", ", All.Select(x => x.Name)));
        }

        internal static bool AnyOpen(bool exceptMain)
        {
            foreach (var l in All) if ((!exceptMain || !l.IsMain) && l.IsOpen) return true;
            return false;
        }

        // F10 list: open / close one hatch (only with "Luken einzeln steuern")
        internal static void Set(Luke l, bool open)
        {
            l.Manual = true; l.Open = open;
            if (l.Hatch != null && l.Hatch.Locked == open) l.Hatch.Locked = !open;
            Util.Log("[Warthog] hatch: " + l.Name + (open ? " opened" : " closed") + " (F10)");
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect(ship);
            bool on = On;
            if (on != lastOn) { Util.Log("[Warthog] hatch: all hatches " + (on ? "ON" : "OFF (game: single hatch)")); lastOn = on; }
            var main = ship.ExteriorHatch;
            bool mustClose = Util.SafeGet(() => ship.ShouldExteriorHatchBeClosed, false);
            bool rawOpen = main != null && !main.Locked;
            // 0.3.1: the other hatches follow Turmluk 1 only once it has stayed open/closed for 1.5 s (the game opens and
            // shuts the tower hatch for a moment e.g. after switching boats -> all hatches flapped). Diving/alarm
            // (mustClose below) still closes everything at once.
            if ((object)ship != debounceShip) { debounceShip = ship; rawMain = rawOpen; stableMain = rawOpen; rawSince = Time.unscaledTime; }   // new boat: start from its current state
            if (rawOpen != rawMain) { rawMain = rawOpen; rawSince = Time.unscaledTime; }
            if (Time.unscaledTime - rawSince >= 1.5f) stableMain = rawOpen;
            bool mainOpen = stableMain;
            foreach (var l in All)
            {
                if (l.IsMain) continue;
                bool want;
                if (!on) want = false;
                else if (mustClose) { want = false; l.Manual = false; }
                else if (Single) want = l.Manual ? l.Open : mainOpen;
                else want = mainOpen;
                if (l.Hatch != null) { if (l.Hatch.Locked == want) { l.Hatch.Locked = !want; Util.Log("[Warthog] hatch: " + l.Name + (want ? " opened" : " closed") + (mustClose ? " (diving/alarm)" : "")); } }
                else if (l.Open != want) { l.Open = want; Util.Log("[Warthog] hatch: " + l.Name + (want ? " opened" : " closed") + (mustClose ? " (diving/alarm)" : "")); }
                foreach (var k in l.Links) if (k != null && k.activated != (on && want)) k.activated = on && want;
                // flooding: an open hatch lets water into its own room when the deck is under water
                float hole = want ? (l.Hatch != null ? l.Hatch.Openess : 1f) : 0f;
                foreach (var p in l.Holes) if (p != null && p.Openess != hole) p.Openess = hole;
            }
        }
    }

    // fresh air through every open hatch (game: only the tower hatch adds oxygen)
    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class WarthogHatches_AirPatch
    {
        private static readonly AccessTools.FieldRef<PlayerShip, UBOAT.Game.Core.Data.Modifier> Oxy =
            AccessTools.FieldRefAccess<PlayerShip, UBOAT.Game.Core.Data.Modifier>("oxygenExternalHatchModifier");
        private static void Postfix(PlayerShip __instance)
        {
            if (!WarthogHatches.On || !WarthogSettings.IsWarthog(__instance)) return;
            int open = 0;
            foreach (var l in WarthogHatches.All) if (!l.IsMain && l.IsOpen) open++;
            if (open == 0) return;
            var m = Oxy(__instance);
            if (m != null && Util.SafeGet(() => __instance.DeckDepth < 0.3f, false)) m.Value = 0.0005f * (1 + open);
        }
    }

    // test log: which way does the crew take
    [HarmonyPatch(typeof(NavMeshLadder), nameof(NavMeshLadder.TraverseStart))]
    internal static class WarthogHatches_LadderLog
    {
        private static void Postfix(NavMeshLadder __instance, UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator navigator)
        {
            if (!WarthogSettings.IsWarthog(Util.PlayerShip)) return;
            string n = __instance.name;
            if (!n.Contains("Observation")) return;   // only the bridge <-> tower ladders (the two hatches)
            Util.Log("[Warthog] hatch: " + (navigator != null ? navigator.name : "?") + " uses " + (n.Contains("(Hatch 2)") ? "HATCH 2" : "hatch 1") + " (" + n + ")");
        }
    }
}
