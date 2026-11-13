using System.Collections.Generic;
using System.Linq;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Fire-control room of U-1337: 4 stations built into the room (WarthogBuilder.BuildFireControlRoom):
    //   "Fire Control 105 Fore"   -> the two forward 10.5 cm turrets
    //   "Fire Control 105 Aft"    -> the two aft 10.5 cm turrets
    //   "Fire Control Quad Flak"  -> the two 37 mm quad mounts on the sponsons
    //   "Fire Control Tower Flak" -> the two MG C30 on the tower
    // With "WhRemoteGuns" the guns are worked from here (no man at the gun). With "WhFireControlCrew" a gun group is only
    // ready when a crew member works at its station; otherwise the remote guns are always ready.
    public class WarthogFireControl : ModuleBase
    {
        internal static readonly string[] Stations = { "Fire Control 105 Fore", "Fire Control 105 Aft", "Fire Control Quad Flak", "Fire Control Tower Flak" };
        internal static readonly string[] Labels = { "10,5 cm vorn", "10,5 cm achtern", "Vierlingsflak", "Turmflak" };
        internal static readonly Dictionary<string, Interactable> Found = new Dictionary<string, Interactable>();
        private static object cachedFor;
        internal static string Status = "";

        private static void Collect(PlayerShip ship)
        {
            if (cachedFor == (object)ship) return;
            cachedFor = ship; Found.Clear();
            if (ship == null || ship.Interior == null) return;
            foreach (var i in ((Component)ship.Interior).GetComponentsInChildren<Interactable>(true))
                if (Stations.Contains(i.name) && !Found.ContainsKey(i.name)) Found[i.name] = i;
            Util.Log("[Warthog] fire control: " + Found.Count + " of 4 stations found");
        }

        // which station works this gun
        internal static int GroupOf(Gun g, PlayerShip ship)
        {
            if (g == null || ship == null) return -1;
            float cal = g.Calibre.Value;
            if (g.name.Contains("MG C30") || (cal > 0f && cal < 30f)) return 3;
            if (WarthogWeapons.IsFlak(g)) return 2;
            float z = ship.transform.InverseTransformPoint(g.transform.position).z;
            return z >= 0f ? 0 : 1;
        }

        internal static bool Manned(int group)
        {
            if (group < 0 || group >= Stations.Length) return true;
            if (!Found.TryGetValue(Stations[group], out var st) || st == null) return true;   // no station built -> never block
            return Util.SafeGet(() => st.ReservingCharacters.Count > 0, true);
        }

        // gun may be worked remotely right now
        internal static bool Ready(Gun g)
        {
            var ship = Util.PlayerShip;
            if (!Settings.B("WhFireControlCrew")) return true;
            Collect(ship);
            return Manned(GroupOf(g, ship));
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect(ship);
            var parts = new List<string>();
            for (int i = 0; i < Stations.Length; i++)
                parts.Add(Labels[i] + ": " + (!Found.ContainsKey(Stations[i]) ? "fehlt" : Manned(i) ? (Settings.B("WhFireControlCrew") ? "besetzt" : "bereit") : "unbesetzt"));
            Status = string.Join("  |  ", parts);
        }
    }
}
