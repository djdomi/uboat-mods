using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Dive angle: the game tilts the boat by at most "verticalSteersInclination" degrees (depth steers fully over).
    // Setting DiveAngleMax replaces this value for the player's boat (0 = game value). Not on Type XXX when the Warthog mod brings its own.
    public class DiveAngle : ModuleBase
    {
        private static readonly AccessTools.FieldRef<PlayableSubmarine, float> Incl = AccessTools.FieldRefAccess<PlayableSubmarine, float>("verticalSteersInclination");
        private PlayableSubmarine boat; private float original = float.NaN, applied = float.NaN, lastTarget = float.NaN;

        public override void Tick()
        {
            var sub = Util.PlayerShip as PlayableSubmarine;
            if (sub == null || Incl == null) return;
            if (sub != boat) { boat = sub; original = Incl(sub); applied = float.NaN; }
            if (Util.WarthogModOwns(Util.PlayerShip)) return;
            // log every change of the ordered depth (to trace "forced dives")
            float td = sub.TargetDepth;
            if (float.IsNaN(lastTarget) || Mathf.Abs(td - lastTarget) > 1f) { if (!float.IsNaN(lastTarget)) Util.Log("Zieltiefe geändert: " + lastTarget.ToString("F0") + " -> " + td.ToString("F0") + " m (Preset " + sub.DepthPreset + ", Tiefe " + sub.Depth.ToString("F1") + ")"); lastTarget = td; }
            float want = Settings.F("DiveAngleMax");
            // steep angle only while really diving to a depth; surfaced / near the surface the game value (no nose-down dips)
            bool diving = td > 5f;
            float v = want <= 0f || !diving ? original : Mathf.Clamp(want, 1f, 45f);
            if (v == applied) return;
            Incl(sub) = v; applied = v;
            Util.Log("Tauchwinkel: " + v.ToString("0") + "° (Spiel " + original.ToString("0") + "°)");
        }
    }
}
