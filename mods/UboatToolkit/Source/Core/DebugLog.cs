using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UboatToolkit
{
    // Test logging: everything the mod does and every error goes into
    // ...\UBOAT\UboatToolkit\logs\toolkit-<date>.log  (one file per day, max 20 MB).
    // Also writes all settings at start and every time they are saved, plus game state every minute.
    public static class DebugLog
    {
        private const long MaxBytes = 20L * 1024 * 1024;
        private static string path;
        private static bool full;

        private static bool On => Settings.Get("DebugLog") == null || Settings.B("DebugLog");

        public static void Write(string level, string text)
        {
            if (!On || full) return;
            try
            {
                if (path == null)
                {
                    string dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "toolkit-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                }
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) { full = true; return; }
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " [" + level + "] " + text + "\n");
            }
            catch { /* logging must never break the game */ }
        }

        public static void DumpSettings(string why)
        {
            Write("SETTINGS", why + ": " + string.Join(", ", Settings.All.Select(o => o.Key + "=" + (o.IsBool ? (o.On ? "an" : "aus") : o.Value.ToString("0.###")))));
        }

        public static void DumpState()
        {
            try
            {
                var ship = Util.PlayerShip;
                if (ship == null) { Write("STATE", "kein Boot geladen"); return; }
                string res = string.Join(", ", new[] { "Fuel", "Energy", "Oxygen" }.Select(n =>
                {
                    var r = ship.GetResource(n);
                    return r == null ? n + "=?" : n + "=" + ((float)r.Amount).ToString("0.###");
                }));
                Write("STATE", "Tiefe=" + ship.TargetDepth.ToString("0.0") + " Alarm=" + ship.Alarmed + " Gejagt=" + ship.IsHunted +
                               " Hafen=" + ship.Docked + " " + res + " FPS=" + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0"));
            }
            catch (Exception e) { Write("ERROR", "DumpState: " + e.Message); }
        }
    }
}
