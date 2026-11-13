using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // One bilge pump per compartment, each with a float switch.
    // The game has only ONE pump (TrimPump, control room) for the whole boat, and it gets weaker with depth.
    // U-1337 has a pump in every compartment: as soon as the water in a compartment passes the float level,
    // that compartment's pump runs by itself and pumps the water overboard until it is dry.
    // Strength: per pump "WhPumpRate" m³/s; with "WhPumpDepthScale" it gets STRONGER with depth.
    // Uses battery while running. Does not touch the game's TrimPump (Toolkit "Pump" keeps working).
    public class WarthogPumps : ModuleBase
    {
        internal static readonly bool[] Running = new bool[32];
        internal static int RunningCount;
        private Modifier energy;
        private static UBOAT.Game.Core.Time.GameTime gameTime;

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            bool on = WarthogSettings.Active(ship) && Settings.B("WhPumps");
            var comps = ship.Interior != null ? ship.Interior.Compartments : null;
            RunningCount = 0;
            if (on && comps != null)
            {
                if (gameTime == null) gameTime = Traverse.Create(typeof(TrimPump)).Field("gameTime").GetValue<UBOAT.Game.Core.Time.GameTime>();
                float timeScale = gameTime != null ? gameTime.ArtificialTimeScale : 1f;
                float dt = Time.deltaTime * timeScale;
                float start = Settings.F("WhPumpStart");
                float rate = Settings.F("WhPumpRate");
                // stronger with depth: x1 at the surface, x2 at 100 m, x3 at 200 m
                float depthK = Settings.B("WhPumpDepthScale") ? 1f + Mathf.Max(0f, ship.DeckDepth) / 100f : 1f;
                for (int i = 0; i < comps.Length && i < Running.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) { Running[i] = false; continue; }
                    // float switch with hysteresis: on above "start", off when (almost) dry
                    if (!Running[i] && c.Water > start) Running[i] = true;
                    else if (Running[i] && c.Water <= 0.001) Running[i] = false;
                    if (!Running[i]) continue;
                    RunningCount++;
                    double take = rate * depthK * dt;
                    if (take > c.Water) take = c.Water;
                    c.Water -= take;
                }
            }
            else for (int i = 0; i < Running.Length; i++) Running[i] = false;

            // battery use of the running pumps
            if (ship.Energy == null) return;
            if (energy == null) energy = ship.Energy.Gain.AddDeltaModifier("Warthog Pumps");
            float use = WarthogPower.Apply("WarthogPumps", RunningCount * Settings.F("WhPumpEnergy") * 0.0001f);
            if (energy.Value != -use) energy.Value = -use;
        }
    }
}
