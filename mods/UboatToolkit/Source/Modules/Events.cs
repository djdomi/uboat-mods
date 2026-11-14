using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Messages;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Items.Statuses;

namespace UboatToolkit.Modules
{
    // - Time compression stops on damage (hull hit or new leak) and on radio messages.
    // - Speedometer + odometer (shown in the info line top left).
    // - War diary: important events with game date and position in logs/kriegstagebuch.txt.
    public class Events : ModuleBase
    {
        private float lastHealth = -1f;
        private int lastLeaks = -1;
        private Vector2 lastPos;
        private bool hasPos;
        public static float TripKm, TotalKm;
        private static GameTime gameTime;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            Damage(ship);
            Odometer(ship);
        }

        private void Damage(PlayerShip ship)
        {
            int leaks = 0;
            foreach (var e in Util.Find<LeakableEquipment>())
                if (e != null && e.HasStatus<Leaking>()) leaks++;
            float hp = ship.Health;
            bool hit = (lastHealth >= 0f && hp < lastHealth - 0.001f) || (lastLeaks >= 0 && leaks > lastLeaks);
            if (hit)
            {
                Diary("Schaden: Rumpf " + (hp * 100f).ToString("0") + " %, Lecks " + leaks);
                if (Settings.B("StopTimeOnDamage")) Game.StopTimeCompression("Schaden am Boot!");
            }
            lastHealth = hp; lastLeaks = leaks;
        }

        private void Odometer(PlayerShip ship)
        {
            var w = Game.Wolfpack;
            if (w == null) return;
            if (ship.Docked) { if (TripKm > 0f) Diary("Im Hafen. Fahrt: " + TripKm.ToString("0") + " km"); TripKm = 0f; hasPos = false; return; }
            var p = w.Position;
            if (hasPos)
            {
                float d = (p - lastPos).magnitude;
                if (d < 50f) { TripKm += d; TotalKm += d; } // bigger jumps = teleport / quick travel
            }
            lastPos = p; hasPos = true;
            if (Settings.B("Speedometer"))
                Speed = Mathf.Abs(ship.AverageVelocity) * 1.943844f;
        }

        public static float Speed;

        public static string SpeedText =>
            !Settings.B("Speedometer") ? null : Join(
                Settings.B("InfoSpeed") ? Speed.ToString("0.0") + " kn" : null,
                Settings.B("InfoTrip") ? "Fahrt: " + TripKm.ToString("0") + " km" : null,
                Settings.B("InfoTotal") ? "gesamt: " + TotalKm.ToString("0") + " km" : null);

        public static string Join(params string[] parts)
        {
            var l = new System.Collections.Generic.List<string>();
            foreach (var p in parts) if (!string.IsNullOrEmpty(p)) l.Add(p);
            return l.Count == 0 ? null : string.Join("  |  ", l);
        }

        public static void Diary(string text)
        {
            if (!Settings.B("WarDiary")) return;
            try
            {
                if (gameTime == null) gameTime = UnityEngine.Object.FindObjectOfType<GameTime>();
                string date = gameTime != null ? gameTime.CurrentDateTime.ToString("dd.MM.yyyy HH:mm") : "?";
                var w = Game.Wolfpack;
                string pos = w != null ? " [" + w.Position.x.ToString("0") + "/" + w.Position.y.ToString("0") + "]" : "";
                var dir = Path.Combine(Settings.Folder, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "kriegstagebuch.txt"), date + pos + "  " + text + Environment.NewLine);
            }
            catch (Exception e) { Util.Log("Tagebuch: " + e.Message); }
        }
    }

    // Radio message received by the player -> stop time compression + diary entry.
    [HarmonyPatch]
    internal static class Events_RadioPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(IntelMessage), typeof(Report), typeof(CustomMessage), typeof(GroupReportMessage), typeof(BasicMessage) })
            {
                var m = AccessTools.Method(t, "OnReceiveMessage", new[] { typeof(SandboxEntity) });
                if (m != null) yield return m;
            }
        }

        private static void Postfix(IMessage __instance, SandboxEntity entity)
        {
            if (entity == null || !entity.IsPlayerShip) return;
            if (__instance.Sender != null && __instance.Sender.IsPlayerShip) return;
            string who = __instance.SenderName ?? "?";
            Events.Diary("Funkspruch von " + who);
            if (Settings.B("StopTimeOnRadio")) Game.StopTimeCompression("Funkspruch von " + who);
        }
    }

    // No automatic alarm: the crew is only alerted when you order it yourself.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.StartAlarm))]
    internal static class Events_NoAutoAlarmPatch
    {
        private static bool Prefix(bool manualOrder) => manualOrder || !Settings.B("NoAutoAlarm");
    }
}
