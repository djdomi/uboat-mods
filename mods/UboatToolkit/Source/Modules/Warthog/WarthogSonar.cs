using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Three sonar arrays on U-1337: bow, middle, stern.
    // Passive (all arrays together): 360°, always listening even without a man in the listening room,
    // as good surfaced and at high speed as submerged and slow, range slider, alarm on every new contact.
    // Active (Toolkit "ActiveSonar" ping): bow array covers the front half (forward + down),
    // stern array the rear half (aft + down). Switch "Ping verrät uns": off = the ping does not make us louder.
    public class WarthogSonar : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip);
        private readonly HashSet<int> known = new HashSet<int>();
        private Modifier energy;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool passive = On && Settings.B("WhPassive") && (Settings.B("WhSonarBow") || Settings.B("WhSonarMid") || Settings.B("WhSonarStern"));
            foreach (var h in Util.Find<Hydrophone>())
            if (h != null && h.DetectionRange != null)
            {
                var t = Traverse.Create(h);
                // 360°: the arc check uses |angle| <= arc, so 190° with fade covers every direction
                var arc = t.Field("hydrophoneDetectionArc").GetValue<Parameter>();
                if (arc != null) { var m = arc.GetScaleModifier("Warthog 360", true); float baseArc = arc.GetValueExcludingModifier("Warthog 360"); m.Value = passive && baseArc > 1f ? Mathf.Max(1f, 190f / baseArc) : 1f; }
                // range slider
                h.DetectionRange.GetScaleModifier("Warthog Range", true).Value = passive ? Settings.F("WhPassiveRange") : 1f;
                h.GroupDetectionRange.GetScaleModifier("Warthog Range", true).Value = passive ? Settings.F("WhPassiveRange") : 1f;
                // surfaced / fast = as good as submerged / slow: done where the game sets these values (HydroSame in SensorCheats.cs)
                // always on: no operator needed
                if (passive && !h.enabled) h.SetDisableFlag(DeviceDisableFlag.Unmanned, false);
            }

            // battery for the arrays (mode from the power menu)
            if (ship == null) return;   // 0.3.58: NRE without a boat (port, menus)
            if (ship.Energy != null)
            {
                if (energy == null) energy = ship.Energy.Gain.AddDeltaModifier("Warthog Sonar");
                int arrays = (Settings.B("WhSonarBow") ? 1 : 0) + (Settings.B("WhSonarMid") ? 1 : 0) + (Settings.B("WhSonarStern") ? 1 : 0);
                float use = passive ? WarthogPower.Apply("WarthogSonar", arrays * 0.00002f) : 0f;
                if (energy.Value != -use) energy.Value = -use;
            }

            if (passive && Settings.B("WhPassiveAlarm")) Alarm(ship);
            else known.Clear();
        }

        // New-contact alarm: everything the hydrophones would now hear for sure, that we did not hear before.
        private void Alarm(PlayerShip ship)
        {
            var hs = Util.Find<Hydrophone>();
            if (hs.Length == 0) return;
            var h = hs[0];
            // the game's own list of all active ships/observers (no FindObjectsOfType: that cost 40-70 ms every 15 s)
            var obs = Observator.observators;
            var now = new HashSet<int>();
            for (int i = obs.Count - 1; i >= 0; i--)
            {
                var s = obs[i] as Ship;
                if (s == null || s == ship || s is Aircraft) continue;
                float d = Vector3.Distance(ship.transform.position, s.transform.position);
                if (d > h.DetectionRange.Value) continue;
                float chance = h.GetSpotChance(s, d, true, true, out _, out _);
                if (chance < 0.5f) continue;
                int id = s.GetInstanceID();
                now.Add(id);
                if (!known.Contains(id))
                {
                    Vector3 to = s.transform.position - ship.transform.position;
                    int brg = Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
                    ToolkitRunner.ShowNotice("Sonar: neuer Kontakt, Peilung " + brg.ToString("000") + "°, " + (d / 1000f).ToString("0.0") + " km", 6f);
                }
            }
            known.Clear(); known.UnionWith(now);
        }
    }

    // passive: the hydrophone keeps working without an operator
    [HarmonyPatch(typeof(Device), nameof(Device.SetDisableFlag))]
    internal static class WarthogSonar_UnmannedPatch
    {
        private static void Prefix(Device __instance, DeviceDisableFlag disableFlag, ref bool state)
        {
            if (state && disableFlag == DeviceDisableFlag.Unmanned && __instance is Hydrophone && WarthogSonar.On && Settings.B("WhPassive") && Util.IsPlayers(__instance))
                state = false;
        }
    }

    // passive: exact values (distance, speed, course) also surfaced
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class WarthogSonar_ExactPatch
    {
        private static void Postfix(Hydrophone __instance, ref bool __result)
        {
            if (__result && WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame") && Util.IsPlayers(__instance)) __result = false;
        }
    }

    // active: bow array = front half, stern array = rear half (both = all around)
    [HarmonyPatch(typeof(ActiveSonar), nameof(ActiveSonar.InArc))]
    internal static class WarthogSonar_ArcPatch
    {
        private static bool Prefix(PlayerShip ship, Entity e, ref bool __result)
        {
            if (!WarthogSonar.On || ship == null || e == null) return true;
            Vector3 to = e.transform.position - ship.transform.position;
            float rel = Mathf.Abs(Mathf.DeltaAngle(ship.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            __result = (Settings.B("WhSonarBow") && rel <= 95f) || (Settings.B("WhSonarStern") && rel >= 85f);
            return false;
        }
    }

    // active: "Ping verrät uns" off = the ping does not make us louder
    [HarmonyPatch(typeof(ActiveSonar), nameof(ActiveSonar.Tick))]
    internal static class WarthogSonar_QuietPingPatch
    {
        private static void Postfix(ActiveSonar __instance)
        {
            if (!WarthogSonar.On || Settings.B("WhPingReveals")) return;
            var loud = Traverse.Create(__instance).Field("loud").GetValue<Modifier>();
            if (loud != null && loud.Value != 0f) loud.Value = 0f;
        }
    }
}
