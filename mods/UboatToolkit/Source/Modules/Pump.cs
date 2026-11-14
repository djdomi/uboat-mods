using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #38: pump switches on by itself when there is water in the boat.
    // Cheat #38: strong pump (x power), works at any depth, boat can never fill up with water.
    public class Pump : ModuleBase
    {
        public override void Tick()
        {
            if (!Settings.B("AutoPump")) return;
            var ship = Util.PlayerShip;
            foreach (var p in Util.Find<TrimPump>())
                if (Util.IsPlayers(p) && !p.enabled && (p.IsWaterLeft || Pump_Patch.InteriorWater(ship) > 0.1))
                {
                    p.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
                    Util.Log("Pumpe automatisch an");
                }
        }

        public override void Frame()
        {
            if (!Settings.B("CheatNeverFlood")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            foreach (var c in ship.Interior.Compartments)
                if (c != null && c.Water > 0.0) c.Water = 0.0;
        }
    }

    // Extra pumping on top of the game's own (only while the pump runs).
    [HarmonyPatch(typeof(TrimPump), "Update")]
    internal static class Pump_Patch
    {
        public static double InteriorWater(PlayerShip ship)
        {
            double w = 0;
            foreach (var c in ship.Interior.Compartments) if (c != null) w += c.Water;
            return w;
        }

        private static void Postfix(TrimPump __instance)
        {
            float power = Settings.F("CheatPumpPower");
            bool anyDepth = Settings.B("CheatPumpAnyDepth");
            if (power <= 1f && !anyDepth) return;
            if (!Util.IsPlayers(__instance)) return;

            var t = Traverse.Create(__instance);
            var sub = t.Field("submarine").GetValue<PlayableSubmarine>();
            var lps = t.Field("litresPerSecond").GetValue<Parameter>();
            var pressure = t.Field("litresPerSecondPressureModifier").GetValue<Modifier>();
            var tanks = t.Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
            var gt = Traverse.Create(typeof(TrimPump)).Field("gameTime").GetValue<UBOAT.Game.Core.Time.GameTime>();
            if (sub == null || lps == null || tanks == null || gt == null) return;

            float baseLps = lps.GetValueExcludingModifier(pressure);
            float dt = Time.deltaTime * gt.ArtificialTimeScale;
            foreach (var ct in tanks)
            {
                float gameDepth = 1f - Mathf.Clamp01(sub.DeckDepth / ct.MaxDepth);
                float ourDepth = anyDepth ? 1f : gameDepth;
                double extra = baseLps * ct.PerformanceModifier * (ourDepth * Mathf.Max(1f, power) - gameDepth) * dt;
                if (extra <= 0) continue;
                if (extra > ct.Tank.Water) extra = ct.Tank.Water;
                ct.Tank.Water -= extra;
            }
        }
    }
}
