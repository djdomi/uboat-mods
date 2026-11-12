using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Mods;

namespace UboatToolkit.Modules
{
    // Mod-cache check: the game security-checks a mod only when it loads it from its compiled-mod cache
    // (persistentDataPath/Temp) and then rejects it SILENTLY. This runs the game's own check on each cached
    // mod and clears only the ones it would reject, so they are rebuilt fresh at the next start.
    // Optional list file "toolkit-cache-clear.txt" (one DLL name per line) always clears those mods.
    internal static class CacheCheck
    {
        private const string StaleMarker = ".stale-";
        private static string ListFile => Path.Combine(Settings.Folder, "toolkit-cache-clear.txt");
        private static string VerifiedFile => Path.Combine(Settings.Folder, "toolkit-cache-checked.txt");
        private static List<string> list;
        private static string listNote = "";
        private static bool hooked;

        // ---- binding to the game's own security check ----
        private static bool bound;
        private static string unavailable;
        private static ConstructorInfo engineCtor;
        private static MethodInfo checkAssembly, reportLines, setupSecurity;
        private static PropertyInfo settingsP, restrictionsP;
        private static FieldInfo securityOptions;
        private static Dictionary<string, Mod> mods;

        private static T Need<T>(T v, string what) where T : class
        {
            if (v == null) throw new MissingMemberException(what + " nicht gefunden (Spiel-Update?)");
            return v;
        }

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            try
            {
                Type engine = Need(AccessTools.TypeByName("Trivial.CodeSecurity.CodeSecurityEngine"), "CodeSecurityEngine");
                Type restr = Need(AccessTools.TypeByName("Trivial.CodeSecurity.CodeSecurityRestrictions"), "CodeSecurityRestrictions");
                Type report = Need(AccessTools.TypeByName("Trivial.CodeSecurity.CodeSecurityReport"), "CodeSecurityReport");
                Type roslyn = Need(AccessTools.TypeByName("RoslynCSharp.RoslynCSharp"), "RoslynCSharp");
                engineCtor = Need(engine.GetConstructor(new[] { typeof(byte[]) }), "CodeSecurityEngine(byte[])");
                checkAssembly = Need(engine.GetMethod("SecurityCheckAssembly", new[] { restr, report.MakeByRefType() }), "SecurityCheckAssembly");
                reportLines = Need(report.GetMethod("GetAllLines", new[] { typeof(bool) }), "GetAllLines");
                settingsP = Need(roslyn.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static), "RoslynCSharp.Settings");
                restrictionsP = Need(roslyn.GetProperty("SecurityRestrictions", BindingFlags.Public | BindingFlags.Instance), "SecurityRestrictions");
                setupSecurity = Need(AccessTools.Method(typeof(Mod), "SetupSecurity"), "Mod.SetupSecurity");
                securityOptions = Need(AccessTools.Field(typeof(Mod), "securityOptions"), "Mod.securityOptions");
                var manager = ScriptableObjectSingleton.LoadSingleton<ModManager>();
                if (manager == null || manager.LoadedMods == null) throw new InvalidOperationException("Mod-Liste nicht verfügbar");
                mods = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
                foreach (Mod m in manager.LoadedMods)
                    if (m != null && !string.IsNullOrEmpty(m.AssemblyName)) mods[m.AssemblyName] = m;
            }
            catch (Exception e) { unavailable = e.Message; }
        }

        private static Mod ModFor(string asm) { Bind(); return mods != null && mods.TryGetValue(asm, out var m) ? m : null; }
        private static int PermissionsOf(Mod mod) { try { return Convert.ToInt32(securityOptions.GetValue(mod)); } catch { return -1; } }

        // null = passes; otherwise the first reason the game would reject it.
        private static string Violation(string dll, Mod mod)
        {
            Bind();
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            byte[] bytes;
            using (var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            }
            setupSecurity.Invoke(mod, new object[] { null });
            object rules = restrictionsP.GetValue(settingsP.GetValue(null, null), null);
            object engine = engineCtor.Invoke(new object[] { bytes });
            try
            {
                object[] args = { rules, null };
                if ((bool)checkAssembly.Invoke(engine, args)) return null;
                var lines = args[1] == null ? null : reportLines.Invoke(args[1], new object[] { false }) as string[];
                if (lines == null || lines.Length == 0) return "Prüfung nicht bestanden (keine Details)";
                return lines.Length == 1 ? lines[0] : lines.Length + " Verstöße: " + string.Join(" | ", lines);   // all of them, so a mod author can fix every one in one go
            }
            finally { (engine as IDisposable)?.Dispose(); }
        }

        // ---- the clearing ----
        public static void Install()
        {
            if (!Settings.B("CacheCheck")) return;
            Run("Start", true);
            if (!hooked) { Application.quitting += () => { if (Settings.B("CacheCheck")) Run("Beenden", false); }; hooked = true; }
        }

        private static void LoadList()
        {
            list = null; listNote = "";
            try
            {
                if (!File.Exists(ListFile)) { listNote = " (keine Liste " + Path.GetFileName(ListFile) + ")"; return; }
                var names = new List<string>();
                foreach (var raw in File.ReadAllLines(ListFile))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l[0] == '#') continue;
                    if (l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) l = l.Substring(0, l.Length - 4);
                    names.Add(l.ToLowerInvariant());
                }
                if (names.Count > 0) { list = names; listNote = " (Liste: " + string.Join(", ", names.ToArray()) + ")"; }
            }
            catch (Exception e) { listNote = " (Liste unlesbar: " + e.Message + ")"; list = null; }
        }

        private static bool Listed(string asm) => list != null && list.Contains(asm.ToLowerInvariant());

        private static string AssemblyOf(string file)
        {
            string n = Path.GetFileName(file);
            if (n.EndsWith(".dll.mdb", StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - 8);
            if (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - 4);
            return null;
        }

        private static HashSet<string> Decide(string[] files, string when, out string note)
        {
            var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool check = Settings.B("CacheCheckFirst");
            string why = null;
            if (check) { Bind(); why = unavailable; }
            if (!check || why != null)
            {
                // no check possible: clear everything (or only the listed mods if the list exists)
                foreach (var f in files) { var a = AssemblyOf(f); if (a != null && (list == null || Listed(a))) doomed.Add(a); }
                note = (check ? " (Prüfung nicht möglich: " + why + " - alles neu gebaut)" : " (Prüfung aus)") + listNote;
                return doomed;
            }
            float start = Time.realtimeSinceStartup;
            var verified = LoadVerified();
            var still = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int checkedNow = 0, known = 0, notEnabled = 0, listed = 0;
            foreach (var f in files)
            {
                if (!f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                var asm = AssemblyOf(f);
                if (asm == null) continue;
                if (Listed(asm)) { doomed.Add(asm); listed++; continue; }
                var mod = ModFor(asm);
                if (mod == null) { notEnabled++; continue; }
                // The game keeps a compiled mod even when its source files changed (or compiles while files
                // are still being copied): then old code runs. A fingerprint of all Source\*.cs is stored with
                // the check result; if the sources differ from the last check, the DLL is rebuilt.
                string srcHash = SourceHash(mod);
                string key;
                try { var fi = new FileInfo(f); key = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + PermissionsOf(mod) + "|" + srcHash; } catch { key = null; }
                if (srcHash != "-" && key != null && verified.TryGetValue(asm, out var before) && before != key && SameDll(before, key))
                {
                    doomed.Add(asm);
                    Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) passt nicht mehr zum Quellcode, wird neu gebaut.");
                    continue;
                }
                if (key != null && verified.TryGetValue(asm, out var seen) && seen == key) { still[asm] = key; known++; continue; }
                string v;
                try { v = Violation(f, mod); checkedNow++; }
                catch (Exception e)
                {
                    doomed.Add(asm);
                    Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) NICHT prüfbar, wird sicherheitshalber neu gebaut: " +
                             (e is TargetInvocationException && e.InnerException != null ? e.InnerException.Message : e.Message));
                    continue;
                }
                if (v == null) { if (key != null) still[asm] = key; continue; }
                doomed.Add(asm);
                Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) würde vom Spiel abgelehnt, wird neu gebaut: " + v);
            }
            SaveVerified(still);
            note = " (geprüft " + checkedNow + " in " + Mathf.RoundToInt((Time.realtimeSinceStartup - start) * 1000f) + " ms, " + known + " schon als gut bekannt" +
                   (listed > 0 ? ", " + listed + " aus der Liste" : "") + (notEnabled > 0 ? ", " + notEnabled + " nicht aktive Mods unberührt" : "") + ")";
            return doomed;
        }

        // Same DLL file (size + time) as at the last check, but something else (sources) changed.
        private static bool SameDll(string a, string b)
        {
            var x = a.Split('|'); var y = b.Split('|');
            return x.Length >= 2 && y.Length >= 2 && x[0] == y[0] && x[1] == y[1];
        }

        private static string SourceHash(Mod mod)
        {
            try
            {
                var src = Path.Combine(mod.Path ?? "", "Source");
                if (!Directory.Exists(src)) return "-";
                var files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    foreach (var f in files)
                    {
                        var name = System.Text.Encoding.UTF8.GetBytes(f.Substring(src.Length).ToLowerInvariant());
                        sha.TransformBlock(name, 0, name.Length, null, 0);
                        var b = File.ReadAllBytes(f);
                        sha.TransformBlock(b, 0, b.Length, null, 0);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 16);
                }
            }
            catch { return "-"; }
        }

        private static Dictionary<string, string> LoadVerified()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(VerifiedFile)) return d;
                foreach (var l in File.ReadAllLines(VerifiedFile)) { int bar = l.IndexOf('|'); if (bar > 0 && l[0] != '#') d[l.Substring(0, bar)] = l.Substring(bar + 1); }
            }
            catch { d.Clear(); }
            return d;
        }

        private static void SaveVerified(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                var lines = new List<string> { "# Mods im Cache, die die Spiel-Prüfung bestanden haben. Darf gelöscht werden." };
                foreach (var kv in d) lines.Add(kv.Key + "|" + kv.Value);
                File.WriteAllLines(VerifiedFile, lines.ToArray());
            }
            catch { }
        }

        private static void Run(string when, bool purgeStale)
        {
            try
            {
                LoadList();
                string temp = Path.Combine(Application.persistentDataPath, "Temp");
                if (!Directory.Exists(temp)) { Util.Log("Mod-Cache " + when + ": kein Temp-Ordner, nichts zu tun."); return; }
                int deleted = 0, renamed = 0, stale = 0, failed = 0, kept = 0;
                string firstError = null;
                var files = Directory.GetFiles(temp, "*", SearchOption.TopDirectoryOnly);
                if (purgeStale)
                    foreach (var f in files)
                        if (Path.GetFileName(f).IndexOf(StaleMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                            try { File.Delete(f); stale++; } catch { }
                var doomed = Decide(files, when, out string note);
                foreach (var f in files)
                {
                    var asm = AssemblyOf(f);
                    if (asm == null) continue;
                    if (!doomed.Contains(asm)) { kept++; continue; }
                    try { File.Delete(f); deleted++; }
                    catch (Exception de)
                    {
                        // locked (compiled this launch): rename, removed at the next start
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Move(f, f + StaleMarker + DateTime.Now.Ticks); renamed++; }
                        catch (Exception me) { failed++; if (firstError == null) firstError = Path.GetFileName(f) + ": löschen " + de.Message + " / umbenennen " + me.Message; }
                    }
                }
                Util.Log("Mod-Cache " + when + ": gelöscht " + deleted + ", umbenannt " + renamed + " (gesperrt, weg beim nächsten Start), alte Reste " + stale +
                         (kept > 0 ? ", BEHALTEN " + kept : "") + (failed > 0 ? ", " + failed + " FEHLER - erster: " + firstError : "") + note);
            }
            catch (Exception e) { Util.Log("Mod-Cache " + when + ": fehlgeschlagen: " + e.Message); }
        }
    }
}
