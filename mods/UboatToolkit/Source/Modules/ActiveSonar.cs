using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Active sonar (ping): the game gives our boat only a passive listening device. A ping sends sound out:
    // for a few seconds every ship/boat within range (and in the arc) is found for sure and its distance,
    // speed and course are exact. Downside: during the ping our boat is much louder, enemies can hear us.
    // Manual by key, or automatically every x seconds under water (not during "quiet" alarm if wanted).
    // Needs a man in the listening room unless switched off.
    public class ActiveSonar : ModuleBase
    {
        internal static float PingUntil = -1f;
        private static float lastAuto = -9999f;
        private Modifier loud;

        internal static bool Active => Time.time < PingUntil;

        public override void Frame()
        {
            if (Settings.B("ActiveSonar") && Keys.Down("KeySonarPing")) Ping(true);
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            // louder while pinging
            if (loud == null) loud = ship.HydrophoneDetectability.AddDeltaModifier("Toolkit Sonar");
            float want = Settings.B("ActiveSonar") && Active ? Settings.F("SonarLoudness") : 0f;
            if (loud.Value != want) loud.Value = want;

            float every = Settings.F("SonarAutoSeconds");
            if (!Settings.B("ActiveSonar") || every <= 0f || !ship.SubmergedOrGoingToSubmerge) return;
            if (Settings.B("SonarAutoQuiet") && ship.Alarmed) return;
            if (Time.time - lastAuto >= every) { lastAuto = Time.time; Ping(false); }
        }

        internal static void Ping(bool manual)
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            if (Settings.B("SonarSubmergedOnly") && !ship.SubmergedOrGoingToSubmerge)
            { if (manual) ToolkitRunner.ShowNotice("Sonar: nur getaucht", 3f); return; }
            if (Settings.B("SonarNeedsOperator") && !CrewSkills.HydrophoneManned)
            { if (manual) ToolkitRunner.ShowNotice("Sonar: Horchraum ist nicht besetzt", 3f); return; }
            PingUntil = Time.time + Mathf.Max(1f, Settings.F("SonarPingSeconds"));
            lastAuto = Time.time;
            // report what is in range now
            float range = Settings.F("SonarRange");
            int n = 0; Entity near = null; float best = float.MaxValue;
            foreach (var s in Object.FindObjectsOfType<Ship>())
            {
                if (s == null || s == ship || !InArc(ship, s)) continue;
                float d = Vector3.Distance(ship.transform.position, s.transform.position);
                if (d > range) continue;
                n++;
                if (d < best) { best = d; near = s; }
            }
            string text = "Sonar-Ping: " + (n == 0 ? "nichts in " + (range / 1000f).ToString("0.0") + " km"
                : n + " Kontakt(e), nächster " + Mathf.RoundToInt(best) + " m, Peilung " + Bearing(ship, near).ToString("000") + "°");
            ToolkitRunner.ShowNotice(text, 5f);
        }

        internal static bool InArc(PlayerShip ship, Entity e)
        {
            float arc = Settings.F("SonarArc");
            if (arc >= 360f) return true;
            Vector3 to = e.transform.position - ship.transform.position;
            float rel = Mathf.Abs(Mathf.DeltaAngle(ship.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            return rel <= arc / 2f;
        }

        private static int Bearing(PlayerShip ship, Entity e)
        {
            Vector3 to = e.transform.position - ship.transform.position;
            return Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
        }
    }

    // During the ping our listening device finds everything in range for sure.
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.GetSpotChance))]
    internal static class ActiveSonar_SpotPatch
    {
        private static void Postfix(Hydrophone __instance, Entity entity, float distance, ref float __result)
        {
            if (!ActiveSonar.Active || !Settings.B("ActiveSonar") || entity == null || !(entity is Ship)) return;
            var ship = Util.PlayerShip;
            if (ship == null || __instance.ParentEntity != ship) return;
            if (distance <= Settings.F("SonarRange") && ActiveSonar.InArc(ship, entity)) __result = 1f;
        }
    }

    // ...and the values (distance, speed, course) are exact while the echo comes back.
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class ActiveSonar_ExactPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ActiveSonar.Active && Settings.B("ActiveSonar")) __result = false;
        }
    }
}
