using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Missions;

namespace UboatToolkit.Modules
{
    // Cheat (DoMe 2026-11-16: "detect cheat dauerhaft aktiv, Missionsziel direkt anfahren"): keeps contacts on the map,
    // the same way the game's console command "Detect" does it, but every 3 s instead of 12 times a second,
    // also after loading, and without the console. 1 = only targets of missions, 2 = every group.
    public class DetectCheat : ModuleBase
    {
        private float next;
        private int lastCount = -1;
        public override void Tick()
        {
            int mode = Mathf.RoundToInt(Settings.F("CheatDetect"));
            if (mode <= 0 || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 3f;
            var w = Game.Wolfpack;
            var sb = Traverse.Create(typeof(UBOAT.Game.UI.DevConsole.DevConsoleCommands)).Field("sandbox").GetValue<Sandbox>();
            if (w == null || sb == null) return;
            float km = Settings.F("CheatDetectKm");
            float max2 = km > 0f ? km * km : float.MaxValue;
            List<SandboxGroup> groups = sb.GetGroupsDirect();
            int n = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (!g || g.IsGroupPublic || g == w) continue;
                if (g.DistanceToPlayerSquared > max2) continue;
                if (mode == 1 && !IsTarget(g)) continue;
                w.AddObservation(g, GroupDetectionMethod.NotSpecified).AddKnownData(GroupData.All, 0f, true);
                n++;
            }
            if (n != lastCount) { lastCount = n; Util.Log("[Kontakte] " + n + " Gruppen auf der Karte gehalten (Modus " + mode + ")"); }
        }

        private static bool IsTarget(SandboxGroup g)
        {
            var es = g.Entities;
            for (int i = 0; i < es.Count; i++)
            {
                var st = es[i]?.Statuses;
                if (st == null) continue;
                for (int j = 0; j < st.Count; j++) if (st[j] is IMissionTarget) return true;
            }
            return false;
        }
    }
}
