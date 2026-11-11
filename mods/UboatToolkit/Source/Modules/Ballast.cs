using System;
using UnityEngine;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // QoL #23 ballast (all in one, inspired by "Proper Ballast" / Iron Coffins):
    //  - surfaced: the diesel exhaust slowly blows the rest of the water out of the ballast tanks,
    //    so the boat rides fully surfaced without using compressed air;
    //  - diving: the ballast tanks flood faster.
    // Works on the same tank values the game itself sets (e.g. during quick travel).
    public class Ballast : ModuleBase
    {
        private float lastTime = -1f;

        public override void Tick()
        {
            float now = Time.time; // game time incl. time compression
            float dt = lastTime < 0f ? 0f : Mathf.Clamp(now - lastTime, 0f, 600f);
            lastTime = now;
            if (dt <= 0f) return;

            var ship = Util.PlayerShip;
            var tanks = ship.BallastTanks;
            if (tanks == null) return;

            bool surfaced = ship.DepthPreset == DepthPreset.Surface && !ship.SubmergedOrGoingToSubmerge && ship.TargetDepth < 0.05f;
            bool diving = ship.TargetDepth > 1f && ship.DepthPreset != DepthPreset.DeckAwash;

            if (surfaced && Settings.B("BallastDieselBlow"))
            {
                double rate = Settings.F("BallastDieselBlowRate") / 100.0;
                foreach (var t in tanks)
                    if (t != null && t.Water > 0.0) t.Water = Math.Max(0.0, t.Water - t.Capacity * rate * dt);
            }

            float fast = Settings.F("BallastFastDive");
            if (diving && fast > 1f)
            {
                double extra = 0.05 * (fast - 1f); // share of the tank per second on top of the game's own flooding
                foreach (var t in tanks)
                    if (t != null && t.Water < t.Capacity) t.Water = Math.Min(t.Capacity, t.Water + t.Capacity * extra * dt);
            }
        }
    }
}
