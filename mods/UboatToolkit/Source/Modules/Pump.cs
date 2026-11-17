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
            var tw = Settings.Get("CheatPumpTestWater");
            if (tw != null && tw.On) { tw.Value = 0; Util.Safe("Testwasser", TestWater); }
            if (!Settings.B("AutoPump")) return;
            var ship = Util.PlayerShip;
            foreach (var p in Util.Find<TrimPump>())
                if (Util.IsPlayers(p) && !p.enabled && Pump_Patch.InteriorWater(ship) > 0.1)   // 0.4.0: only water in the rooms counts, not trim/hull tanks
                {
                    p.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
                    Util.Log("Pumpe automatisch an");
                }
        }

        // DoMe 2026-11-17: "put 1-10 m³ water into the boat, to test the pumps". Water goes into the compartment the
        // camera is in (else the control room, else the first one), at most up to the compartment's size.
        private static void TestWater()
        {
            var ship = Util.PlayerShip;
            if (ship == null || ship.Interior == null) { ToolkitRunner.ShowNotice("Testwasser: kein Boot", 3f); return; }
            float m3 = Mathf.Clamp(Settings.F("CheatPumpTestWaterM3"), 1f, 10f);
            UBOAT.Game.Scene.Items.Compartment c = null;
            var cam = Camera.main;
            if (cam != null) { try { c = ship.Interior.FindContainingCompartment(cam.transform.position); } catch { } }
            if (c == null)
                foreach (var x in ship.Interior.Compartments)
                    if (x != null && (x.name.IndexOf("Control", System.StringComparison.OrdinalIgnoreCase) >= 0 || x.name.IndexOf("Zentrale", System.StringComparison.OrdinalIgnoreCase) >= 0)) { c = x; break; }
            if (c == null && ship.Interior.Compartments.Length > 0) c = ship.Interior.Compartments[0];
            if (c == null) { ToolkitRunner.ShowNotice("Testwasser: kein Raum gefunden", 3f); return; }
            double before = c.Water;
            double after = System.Math.Min((double)c.Capacity, before + m3);
            c.Water = after;
            string note = Settings.B("CheatNeverFlood") ? " (Achtung: \"Boot kann nie volllaufen\" ist an, das Wasser verschwindet sofort)" : "";
            Util.Log("Testwasser: " + (after - before).ToString("0.0") + " m³ in " + c.name + " (vorher " + before.ToString("0.0") + ", jetzt " + after.ToString("0.0") + " m³)" + note);
            ToolkitRunner.ShowNotice("Testwasser: " + (after - before).ToString("0.0") + " m³ in " + c.name + note, 5f);
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
                // 0.4.0 (DoMe: "das Boot flutet übertrieben"): the pump is also connected to tanks that are not rooms
                // (trim / compensating / hull tanks). Emptying those x50 at depth made the boat's depth keeping flood
                // them again at once. The toolkit now only adds pumping for water standing in the ROOMS of the boat.
                if (ct == null || ct.Tank == null) continue;
                if (!(ct.Tank is UBOAT.Game.Scene.Items.Compartment))
                {
                    if (otherLogged.Count < 20 && otherLogged.Add(ct.Tank.name)) Util.Log("Pumpe: Tank \"" + ct.Tank.name + "\" ist kein Raum, Toolkit pumpt ihn nicht zusätzlich (Wasser " + ct.Tank.Water.ToString("0.00") + " von " + ct.Tank.Capacity.ToString("0.00") + ")");
                    continue;
                }
                float gameDepth = 1f - Mathf.Clamp01(sub.DeckDepth / ct.MaxDepth);
                float ourDepth = anyDepth ? 1f : gameDepth;
                gameRate += baseLps * ct.PerformanceModifier * gameDepth;
                ourRate += baseLps * ct.PerformanceModifier * ourDepth * Mathf.Max(1f, power);
                double extra = baseLps * ct.PerformanceModifier * (ourDepth * Mathf.Max(1f, power) - gameDepth) * dt;
                if (extra <= 0) continue;
                if (extra > ct.Tank.Water) extra = ct.Tank.Water;
                ct.Tank.Water -= extra;
                removed += extra;
            }
            samples++;
            LastDepth = sub.DeckDepth;
            LastGameRate = (float)(gameRate / samples); LastOurRate = (float)(ourRate / samples);
            if (Time.unscaledTime >= nextLog)
            {
                if (removed > 0)
                    Util.Log("Pumpe: Tiefe " + sub.DeckDepth.ToString("0") + " m, Spiel allein " + (LastGameRate * 1000f).ToString("0.0") + " l/s, mit Toolkit " + (LastOurRate * 1000f).ToString("0.0")
                        + " l/s (Pumpenleistung x" + Mathf.Max(1f, power).ToString("0.#") + (anyDepth ? ", volle Leistung in jeder Tiefe" : "") + "), in der letzten Minute zusätzlich " + (removed * 1000.0).ToString("0") + " l abgepumpt, Wasser in den Räumen jetzt " + (InteriorWater(Util.PlayerShip) * 1000.0).ToString("0") + " l");
                nextLog = Time.unscaledTime + 60f; removed = 0; gameRate = ourRate = 0; samples = 0;
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> otherLogged = new System.Collections.Generic.HashSet<string>();
        private static double removed, gameRate, ourRate; private static int samples; private static float nextLog;
        internal static float LastDepth, LastGameRate, LastOurRate;
    }

    // 0.3.94 (DoMe: the pump tooltip still says reduced/red at depth, so he doubts the cheat works): with the pump
    // cheats on, the game's red/orange pressure line is replaced by the real toolkit rate.
    [HarmonyPatch(typeof(TrimPump), nameof(TrimPump.GetStateText))]
    internal static class Pump_TooltipPatch
    {
        private static void Postfix(TrimPump __instance, ref string __result)
        {
            try
            {
                float power = Settings.F("CheatPumpPower");
                bool anyDepth = Settings.B("CheatPumpAnyDepth");
                if ((power <= 1f && !anyDepth) || __result == null || !Util.IsPlayers(__instance)) return;
                int cut = __result.IndexOf("<line-height=50%>");
                string head = cut >= 0 ? __result.Substring(0, cut) : __result;
                string line = "Toolkit: Pumpenleistung x" + Mathf.Max(1f, power).ToString("0.#") + (anyDepth ? ", volle Leistung in jeder Tiefe" : ", Tiefe bremst wie im Spiel");
                if (Pump_Patch.LastOurRate > 0f) line += " (" + (Pump_Patch.LastOurRate * 1000f).ToString("0") + " l/s statt " + (Pump_Patch.LastGameRate * 1000f).ToString("0") + " l/s)";
                __result = head + "<line-height=50%>\n<line-height=100%>\n<color=#7CFC00>" + line + "</color>";
            }
            catch { }
        }
    }
}
