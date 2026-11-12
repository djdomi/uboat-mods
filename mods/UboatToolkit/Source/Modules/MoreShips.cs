using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL "mehr Frachter": every merchant spawner of the game (types "Convoy" and "Transport") sends out new groups
    // every 8 h / activity. This multiplies the activity, so convoys and single freighters appear x times as often.
    // The game's own original value is remembered and restored at factor 1.
    public class MoreShips : ModuleBase
    {
        private static readonly AccessTools.FieldRef<GroupSpawner, float> Activity = AccessTools.FieldRefAccess<GroupSpawner, float>("activity");
        private readonly Dictionary<GroupSpawner, float> original = new Dictionary<GroupSpawner, float>();
        private float next, lastFactor = 1f;

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 30f;
            float f = Mathf.Max(0.1f, Settings.F("MerchantTraffic"));
            if (f == 1f && lastFactor == 1f) return;
            var sb = Traverse.Create(typeof(UBOAT.Game.UI.DevConsole.DevConsoleCommands)).Field("sandbox").GetValue<Sandbox>();
            if (sb == null || sb.GroupSpawners == null) return;
            int n = 0;
            foreach (var g in sb.GroupSpawners)
            {
                if (g == null || (g.SpawnType != "Convoy" && g.SpawnType != "Transport")) continue;
                if (!original.TryGetValue(g, out float orig)) { orig = Activity(g); original[g] = orig; }
                float want = orig * f;
                if (Activity(g) != want) { Activity(g) = want; n++; }
            }
            if (n > 0) Util.Log("Mehr Frachter: " + n + " Handels-Startpunkte auf x" + f.ToString("0.##") + " gesetzt");
            lastFactor = f;
        }
    }
}
