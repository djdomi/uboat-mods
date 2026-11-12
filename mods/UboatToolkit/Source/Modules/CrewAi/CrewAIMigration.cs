// Crew-KI (Teil des UBOAT Toolkit): einmalige Übernahme alter Werte.
// Wer die Crew-KI vorher als einzelnen Mod oder im großen Patch benutzt hat, behält seine Werte:
// beim ersten Start mit dem Toolkit werden sie aus der alten Datei in die UboatToolkit.ini übernommen.
// Danach nie wieder (Merker-Datei Crew-KI-uebernommen.txt im Toolkit-Ordner).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class CrewAIMigration
    {
        internal const string Prefix = "CrewAIOverhaul.";
        internal const string OldPrefix = "Crew_AI_Overhaul.";
        internal const string OwnFileName = "CrewAIOverhaul.cfg";
        internal const string PatchFileName = "DustysCommunityPatch.cfg";
        internal const string MarkerName = "Crew-KI-uebernommen.txt";

        // Newest source wins: the patch's own key, then its "Other Mods" key, then the standalone file.
        internal static Dictionary<string, string> Plan(IDictionary<string, string> patchCfg,
                                                       IDictionary<string, string> ownCfg,
                                                       IEnumerable<SettingRow> rows)
        {
            var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SettingRow r in rows)
            {
                string v;
                if ((patchCfg != null && patchCfg.TryGetValue(Prefix + r.Key, out v)) ||
                    (patchCfg != null && patchCfg.TryGetValue(OldPrefix + r.Key, out v)) ||
                    (ownCfg != null && ownCfg.TryGetValue(r.Key, out v)))
                    plan[r.ToolkitKey] = Normalise(v);
            }
            return plan;
        }

        // "Key: value" and "Key=value" lines; '#' and ';' start a comment line.
        internal static Dictionary<string, string> ParseFile(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines)
            {
                string line = raw == null ? "" : raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int colon = line.IndexOf(':');
                int eq = line.IndexOf('=');
                int sep = colon <= 0 ? eq : (eq <= 0 ? colon : Math.Min(colon, eq));
                if (sep <= 0) continue;
                d[line.Substring(0, sep).Trim()] = line.Substring(sep + 1).Trim();
            }
            return d;
        }

        private static string Normalise(string v)
        {
            string s = (v ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "on" || s == "yes" || s == "1") return "true";
            if (s == "false" || s == "off" || s == "no" || s == "0") return "false";
            return (v ?? "").Trim();
        }

        private static Dictionary<string, string> ReadFirst(string name)
        {
            string root = Application.persistentDataPath;
            string[] places = { Path.Combine(root, name), Path.Combine(Path.Combine(root, "ModData"), name),
                                Path.Combine(Path.Combine(root, "Mods"), name) };
            foreach (string p in places)
                if (File.Exists(p)) return ParseFile(File.ReadAllLines(p, Encoding.UTF8));
            return null;
        }

        internal static string RunOnce(IList<SettingRow> rows)
        {
            string marker = Path.Combine(UboatToolkit.Settings.Folder, MarkerName);
            if (File.Exists(marker)) return "";
            try
            {
                Dictionary<string, string> patch = ReadFirst(PatchFileName);
                Dictionary<string, string> own = ReadFirst(OwnFileName);
                Dictionary<string, string> plan = Plan(patch, own, rows);
                int applied = 0;
                foreach (KeyValuePair<string, string> kv in plan)
                {
                    var o = UboatToolkit.Settings.Get(kv.Key);
                    if (o == null) continue;
                    if (o.IsBool) { if (kv.Value == "true") o.Value = 1; else if (kv.Value == "false") o.Value = 0; else continue; }
                    else
                    {
                        float f;
                        if (!float.TryParse(kv.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                        o.Value = Mathf.Clamp(f, o.Min, o.Max);
                    }
                    applied++;
                }
                if (applied > 0) UboatToolkit.Settings.Save();
                Directory.CreateDirectory(UboatToolkit.Settings.Folder);
                File.WriteAllText(marker, "Alte Crew-KI-Werte übernommen: " + applied + " (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")\n");
                return applied + " alte Wert(e) übernommen (" + (patch == null ? "keine " + PatchFileName : patch.Count + " Zeilen in " + PatchFileName) + ", " +
                       (own == null ? "keine " + OwnFileName : own.Count + " Zeilen in " + OwnFileName) + "); ";
            }
            catch (Exception e)
            {
                return "Übernahme fehlgeschlagen (" + e.GetType().Name + ": " + e.Message + "), es gelten die INI-Werte; ";
            }
        }
    }
}
