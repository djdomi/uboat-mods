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
            Write("SETTINGS", why + ": " + string.Join(", ", Settings.All.Select(o => o.Key + "=" + (o.IsKey ? o.Text : o.IsBool ? (o.On ? "an" : "aus") : o.Value.ToString("0.###")))));
        }

        // Real frames per real second since the last STATE line. Time.smoothDeltaTime is game time: under time
        // compression it showed "FPS=2" although the game ran smoothly.
        private static int lastFrames = -1; private static float lastReal;
        private static float RealFps()
        {
            int f = Time.frameCount; float t = Time.realtimeSinceStartup;
            float fps = lastFrames >= 0 && t - lastReal > 0.5f ? (f - lastFrames) / (t - lastReal) : 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            lastFrames = f; lastReal = t;
            return fps;
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
                               " Hafen=" + ship.Docked + " " + res + " FPS=" + RealFps().ToString("0") + " Zeitraffer=" + Time.timeScale.ToString("0.#"));
            }
            catch (Exception e) { Write("ERROR", "DumpState: " + e.Message); }
        }
    }
}
