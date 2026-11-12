using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using HarmonyLib;

namespace UboatToolkit.Modules
{
    // Voice language: at start, puts the chosen voice language back if the game reset it to the
    // interface language, clears the game's "apply default voice" flag, and repairs a "latest version"
    // entry that no longer exists (it would stamp new saves as unloadable). Writes settings.ubt only if needed.
    internal static class VoiceRepair
    {
        private static readonly string[] Langs = { null, "German", "English" };

        public static void Run()
        {
            if (!Settings.B("KeepVoiceLanguage")) return;
            Type st = AccessTools.TypeByName("UBOAT.Game.Core.Data.UserSettings");
            if (st == null) { Util.Log("Sprache: UserSettings nicht gefunden"); return; }
            object settings;
            try { settings = ScriptableObjectSingleton.LoadSingleton(st); } catch (Exception e) { Util.Log("Sprache: Einstellungen nicht ladbar: " + e.Message); return; }
            if (settings == null) { Util.Log("Sprache: noch keine Einstellungen - nur die Sperre ist aktiv"); return; }
            bool changed = false;
            var flag = AccessTools.Field(st, "applyDefaultVoiceLanguage");
            if (flag != null && flag.FieldType == typeof(bool) && (bool)flag.GetValue(settings)) { flag.SetValue(settings, false); changed = true; Util.Log("Sprache: Spiel-Rücksetzung war vorgemerkt - gelöscht"); }
            object gameplay = AccessTools.Property(st, "GameplaySettings")?.GetValue(settings, null) ?? AccessTools.Field(st, "gameplaySettings")?.GetValue(settings);
            if (gameplay == null) { Util.Log("Sprache: Spiel-Einstellungen nicht erreichbar"); return; }
            var gt = gameplay.GetType();
            int idx = (int)Math.Round(Settings.F("VoiceLang"));
            string want = idx > 0 && idx < Langs.Length ? Langs[idx] : null;
            var voice = AccessTools.Field(gt, "voiceLanguage");
            if (voice != null && want != null)
            {
                string cur = voice.GetValue(gameplay) as string;
                if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) { voice.SetValue(gameplay, want); changed = true; Util.Log("Sprache: Stimme war \"" + (cur ?? "leer") + "\" - zurück auf \"" + want + "\""); }
            }
            if (Settings.B("VoiceRepairVersion")) changed |= RepairVersion(st, gameplay, gt);
            if (!changed) return;
            var write = AccessTools.Method(st, "WriteToHDD");
            if (write != null) { write.Invoke(settings, null); Util.Log("Sprache: Einstellungen gespeichert"); }
        }

        private static bool RepairVersion(Type st, object gameplay, Type gt)
        {
            var vf = AccessTools.Field(gt, "latestVersion");
            if (vf == null) return false;
            string stored = vf.GetValue(gameplay) as string;
            var versions = Changelog(st);
            if (versions == null || versions.Count == 0) return false;
            if (!string.IsNullOrEmpty(stored) && versions.Contains(stored)) return false;
            string newest = versions[versions.Count - 1];
            vf.SetValue(gameplay, newest);
            Util.Log("Sprache: gespeicherte Version \"" + (stored ?? "leer") + "\" fehlt in der Versionsliste - auf \"" + newest + "\" gesetzt (sonst gelten neue Spielstände als nicht ladbar)");
            return true;
        }

        private static List<string> Changelog(Type st)
        {
            try
            {
                object gd = AccessTools.Field(st, "gameData")?.GetValue(null);
                if (gd == null) return null;
                var rows = AccessTools.Method(gd.GetType(), "GetDataRows", new[] { typeof(string) })?.Invoke(gd, new object[] { "General/Changelogs" }) as Array;
                if (rows == null || rows.Length == 0) return null;
                var result = new List<string>(rows.Length);
                MethodInfo gs = null;
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    if (gs == null) { gs = AccessTools.Method(row.GetType(), "GetString", new[] { typeof(int) }); if (gs == null) return null; }
                    if (gs.Invoke(row, new object[] { 0 }) is string v && v.Length > 0) result.Add(v);
                }
                return result;
            }
            catch (Exception e) { Util.Log("Sprache: Versionsliste nicht lesbar: " + e.Message); return null; }
        }
    }
}
