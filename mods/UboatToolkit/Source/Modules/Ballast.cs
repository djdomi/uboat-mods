using System;
using UnityEngine;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // QoL #23 ballast (all in one, own version):
    //  - surfaced: the diesel exhaust slowly blows the rest of the water out of the ballast tanks,
    //    so the boat rides fully surfaced without using compressed air;
    //  - diving: the ballast tanks flood faster.
    // Works on the same tank values the game itself sets (e.g. during quick travel).
    public class Ballast : ModuleBase
    {
        private float lastTime = -1f;

        private static readonly System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Physics.Tank, double> lastWater = new System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Physics.Tank, double>();
        private static int helped; private static float nextLog;
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
                    if (t != null && t.Water > 0.0)
                    {
                        // 0.4.22 (option audit): the place of the water is filled with air (surface pressure 1 bar), else
                        // the tank was left at low pressure and sucked water back in.
                        double before = t.Water;
                        t.Water = Math.Max(0.0, t.Water - t.Capacity * rate * dt);
                        double free = t.Capacity - t.Water - t.Fuel;
                        if (t.Air < free) t.Air = Math.Min(free, t.Air + (before - t.Water));
                    }
            }

            float fast = Settings.F("BallastFastDive");
            if (diving && fast > 1f)
            {
                double extra = 0.05 * (fast - 1f); // share of the tank per second on top of the game's own flooding
                // 0.4.22 (option audit): only tanks the game itself is flooding right now (water rose since the last look).
                // Before, every tank was filled to the brim whenever the ordered depth was > 1 m: the quick-dive tank was
                // refilled at periscope depth, fuel bunkers overfilled and the tank air was squeezed to nothing.
                foreach (var t in tanks)
                {
                    if (t == null) continue;
                    double was; bool known = lastWater.TryGetValue(t, out was);
                    double room = t.Capacity - t.Fuel - t.Air;              // free space that is not fuel and not air
                    if (known && t.Water > was + 1e-6 && t.Water < room)
                    {
                        t.Water = Math.Min(room, t.Water + t.Capacity * extra * dt);
                        helped++;
                    }
                    lastWater[t] = t.Water;
                }
                if (helped > 0 && Time.unscaledTime > nextLog) { nextLog = Time.unscaledTime + 30f; Util.Log("Schnelltauchen: " + helped + " mal eine Tauchzelle beschleunigt, die das Spiel gerade flutet (x" + fast.ToString("0.#") + ")"); helped = 0; }
            }
            else lastWater.Clear();
        }
    }
}
