using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities.NavMesh;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using UnityEngine.AI;

namespace UboatToolkit.Modules.Warthog
{
    // Second conning-tower hatch + side doors of U-1337 (built into the boat, see WarthogBuilder.AddSecondHatchAndDoors).
    // The game knows only ONE exterior hatch (PlayerShip.ExteriorHatch): diving auto-close, flooding, air and the crew's
    // "close the hatch" job all use it. So hatch 2 simply follows hatch 1:
    //   - hatch 2 has the same lock state as hatch 1 (opens/closes with it, also the automatic close when diving)
    //   - its flooding hole is driven by hatch 2 itself (open hatch under water = water in, like hatch 1)
    //   - the side doors are walk-through links deck <-> tower; their flooding holes open exactly as far as hatch 1
    //   - the crew's path finder sees both ladders and both doors and takes the shortest way
    // Switch "WhSecondHatch" off: hatch 2 stays shut, its ladders and the doors are disabled -> original single-hatch game.
    public class WarthogHatches : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhSecondHatch");
        private Hatch hatch2;
        private readonly List<OffMeshLink> links = new List<OffMeshLink>();
        private readonly List<ExternalPressure> doorHoles = new List<ExternalPressure>();
        private object cachedFor;
        private bool lastOn = true;

        private void Collect()
        {
            var ship = Util.PlayerShip;
            if (cachedFor == (object)ship) return;
            cachedFor = ship; hatch2 = null; links.Clear(); doorHoles.Clear();
            if (ship == null) return;
            foreach (var h in ship.GetComponentsInChildren<Hatch>(true)) if (h.name == "Exterior Hatch 2") hatch2 = h;
            var interior = ship.Interior != null ? ((Component)ship.Interior).transform : null;
            if (interior == null) return;
            foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true))
                if (l.name.Contains("(Hatch 2)") || l.name.StartsWith("Side Door")) links.Add(l);
            foreach (var p in interior.GetComponentsInChildren<ExternalPressure>(true))
                if (p.transform.parent != null && p.transform.parent.name.StartsWith("Side Door")) doorHoles.Add(p);
            Util.Log("[Warthog] hatch: found hatch 2 = " + (hatch2 != null) + ", links " + links.Count + ", door holes " + doorHoles.Count);
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect();
            var main = ship.ExteriorHatch;
            bool on = On;
            if (on != lastOn) { Util.Log("[Warthog] hatch: second hatch + side doors " + (on ? "ON" : "OFF")); lastOn = on; }
            foreach (var l in links) if (l != null && l.activated != on) l.activated = on;
            if (hatch2 != null && main != null)
            {
                bool want = on ? main.Locked : true;
                if (hatch2.Locked != want) { hatch2.Locked = want; Util.Log("[Warthog] hatch: hatch 2 " + (want ? "closed" : "opened") + " (follows hatch 1)"); }
            }
            float open = on && main != null ? main.Openess : 0f;
            foreach (var p in doorHoles) if (p != null && p.Openess != open) p.Openess = open;
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
