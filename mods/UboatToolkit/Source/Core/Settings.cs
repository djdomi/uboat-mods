using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UboatToolkit
{
    // Optional INI file. The mod works without it and then uses the defaults below.
    // Location: <persistent data>/UboatToolkit.ini  (same folder as the game's "Mods" folder)
    public static class Settings
    {
        // ---- QoL ----
        public static bool AwashGuns = true;          // #24 crew may use deck gun + AA while running deck awash

        private static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string FilePath => Path.Combine(Application.persistentDataPath, "UboatToolkit.ini");

        public static void Load()
        {
            values.Clear();
            try
            {
                if (!File.Exists(FilePath))
                {
                    WriteDefaults();
                    return;
                }
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e)
            {
                Debug.Log(ToolkitLoader.Tag + " could not read INI, using defaults.");
                Debug.LogException(e);
            }

            AwashGuns = GetBool("AwashGuns", AwashGuns);
        }

        private static bool GetBool(string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var v)) return fallback;
            v = v.ToLowerInvariant();
            if (v == "1" || v == "true" || v == "on" || v == "ja") return true;
            if (v == "0" || v == "false" || v == "off" || v == "nein") return false;
            return fallback;
        }

        public static float GetFloat(string key, float fallback)
        {
            return values.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
        }

        private static void WriteDefaults()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "; UBOAT Toolkit settings. 1 = on, 0 = off.",
                    "[QoL]",
                    "; Crew may use deck gun and AA while running deck awash.",
                    "AwashGuns=" + (AwashGuns ? "1" : "0"),
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
