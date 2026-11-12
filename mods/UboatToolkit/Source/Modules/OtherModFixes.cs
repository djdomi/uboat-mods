using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.Characters;
using UBOAT.Game.Scene.Effects;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UBOAT.Game.UI.MainMenu;

namespace UboatToolkit.Modules
{
    // Fix for the "Supply Price Editor" mod: it searched the whole scene for the main menu every
    // frame, even at sea. Now it only searches while the main menu is loaded, at most once a second.
    [HarmonyPatch]
    internal static class SupplyPriceMenu_Patch
    {
        private static MainMenu cached;
        private static float nextSearch;
        private static bool errorLogged;

        private static MethodBase Target()
        {
            var t = AccessTools.TypeByName("SupplyPriceEditor.MainMenuContext");
            var m = t == null ? null : AccessTools.Method(t, "FindMainMenu", Type.EmptyTypes);
            return m != null && m.ReturnType == typeof(MainMenu) ? m : null;
        }

        private static bool Prepare()
        {
            bool ok = Target() != null;
            Util.Log("Preis-Editor-Fix: " + (ok ? "aktiv (Hauptmenü-Suche nur im Hauptmenü, höchstens 1x pro Sekunde)." : "Supply Price Editor nicht gefunden - nichts zu tun."));
            return ok;
        }

        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(ref MainMenu __result)
        {
            try
            {
                if (!Settings.B("FixSupplyPriceMenu")) return true;
                if (!SceneManager.GetSceneByName("Main Menu").isLoaded) { cached = null; __result = null; return false; }
                if (cached != null && cached.isActiveAndEnabled && cached.gameObject.scene.isLoaded) { __result = cached; return false; }
                float now = Time.unscaledTime;
                if (now < nextSearch) { __result = cached; return false; }
                nextSearch = now + 1f;
                return true;
            }
            catch (Exception e)
            {
                if (!errorLogged) { errorLogged = true; Util.Log("Preis-Editor-Fix: Fehler, nehme Original-Suche (einmalig): " + e.Message); }
                return true;
            }
        }

        private static void Postfix(MainMenu __result) { cached = __result; }
    }

    // Fix for the "Realistic Uniforms Revival" mod: it rebuilt a crewman's textures again and again
    // although nothing on him changed. Those own rebuilds are skipped when renderers, meshes and
    // materials are unchanged; rebuilds that other mods ask for afterwards are never touched.
    internal static class UniformFix
    {
        public sealed class Pending
        {
            public object Character;
            public Renderer[] Renderers;
            public Material[][] Before;
            public Mesh[] Meshes;
            public int[] Verts, Subs;
        }

        public static Pending pending;
        public static object afterBody;
        public static FieldInfo redirectCharacter;
        public static int skipped, passed, foreign, skippedR, passedR, foreignR, nextReport;
        private static bool errorLogged;

        public static MethodBase Apply()
        {
            var rt = AccessTools.TypeByName("RealisticUniformsRevival.RealisticUniformClothesRuntime");
            var rs = AccessTools.TypeByName("RealisticUniformsRevival.RedirectState");
            redirectCharacter = rs == null ? null : AccessTools.Field(rs, "Character");
            var m = rt == null ? null : AccessTools.Method(rt, "ApplyMaterialToExistingWearable");
            return m != null && redirectCharacter != null ? m : null;
        }

        public static bool On => Settings.B("FixUniformRebuild");

        public static Mesh MeshOf(Renderer r)
        {
            if (r == null) return null;
            if (r is SkinnedMeshRenderer sk) return sk.sharedMesh;
            var f = r.GetComponent<MeshFilter>();
            return f == null ? null : f.sharedMesh;
        }

        public static bool Unchanged(Pending p)
        {
            if (p.Character is Component body)
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (r != null && Array.IndexOf(p.Renderers, r) < 0) return false;
            for (int i = 0; i < p.Renderers.Length; i++)
            {
                var r = p.Renderers[i];
                var was = p.Before[i];
                if (r == null || was == null) return false;
                var mesh = MeshOf(r);
                if (!ReferenceEquals(mesh, p.Meshes[i])) return false;
                if (mesh != null && (mesh.vertexCount != p.Verts[i] || mesh.subMeshCount != p.Subs[i])) return false;
                var now = r.sharedMaterials;
                if (now.Length != was.Length) return false;
                for (int j = 0; j < now.Length; j++) if (!ReferenceEquals(now[j], was[j])) return false;
            }
            return true;
        }

        public static void Report()
        {
            int now = Environment.TickCount;
            if (unchecked(now - nextReport) < 0) return;
            nextReport = now + 60000;
            int s = skipped - skippedR, q = passed - passedR, f = foreign - foreignR;
            if (s == 0 && q == 0 && f == 0) return;
            skippedR = skipped; passedR = passed; foreignR = foreign;
            if (Settings.B("UniformLog")) Util.Log("Uniform-Fix: letzte Minute " + s + " unnötige Neuaufbauten übersprungen, " + q + " durchgelassen, " + f + " von anderen Mods unberührt. Sitzung: " + skipped + "/" + passed + "/" + foreign + ".");
        }

        public static void Error(Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Uniform-Fix: Fehler, Spielverhalten (einmalig): " + e); } }
    }

    [HarmonyPatch]
    internal static class UniformFix_Apply
    {
        private static bool Prepare() => UniformFix.Apply() != null;
        private static MethodBase TargetMethod() => UniformFix.Apply();

        private static void Prefix(object[] __args)
        {
            UniformFix.pending = null;
            UniformFix.afterBody = null;
            try
            {
                if (!UniformFix.On || __args == null || __args.Length < 2) return;
                var wearable = __args[1] as Component;
                object character = __args[0] == null ? null : UniformFix.redirectCharacter.GetValue(__args[0]);
                if (wearable == null || character == null) return;
                var set = new List<Renderer>();
                if (character is Component body) foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (r != null) set.Add(r);
                var lods = wearable.GetComponent<LODReferences>();
                var levels = lods != null ? lods.GetLODsDirect() : null;
                if (levels != null)
                    foreach (var lv in levels)
                    {
                        if (lv == null || lv.renderers == null) continue;
                        foreach (var r in lv.renderers) if (r != null && !set.Contains(r)) set.Add(r);
                    }
                foreach (var r in wearable.GetComponentsInChildren<Renderer>(true)) if (r != null && !set.Contains(r)) set.Add(r);
                var rs = set.ToArray();
                var p = new UniformFix.Pending { Character = character, Renderers = rs, Before = new Material[rs.Length][], Meshes = new Mesh[rs.Length], Verts = new int[rs.Length], Subs = new int[rs.Length] };
                for (int i = 0; i < rs.Length; i++)
                {
                    p.Before[i] = rs[i] != null ? rs[i].sharedMaterials : null;
                    p.Meshes[i] = UniformFix.MeshOf(rs[i]);
                    p.Verts[i] = p.Meshes[i] != null ? p.Meshes[i].vertexCount : -1;
                    p.Subs[i] = p.Meshes[i] != null ? p.Meshes[i].subMeshCount : -1;
                }
                UniformFix.pending = p;
            }
            catch (Exception e) { UniformFix.pending = null; UniformFix.Error(e); }
        }

        // Runs first among postfixes: from here on rebuild requests come from other mods.
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("officersandsailors.nzo.compat")]
        private static void Postfix()
        {
            var p = UniformFix.pending;
            UniformFix.pending = null;
            UniformFix.afterBody = p != null ? p.Character : null;
        }

        private static Exception Finalizer(Exception __exception)
        {
            UniformFix.pending = null;
            UniformFix.afterBody = null;
            UniformFix.Report();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class UniformFix_Dirty
    {
        private static bool Prepare() => UniformFix.Apply() != null;
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Character), "SetLODLevelsDirty", Type.EmptyTypes);

        private static bool Prefix(object __instance)
        {
            var p = UniformFix.pending;
            if (p == null)
            {
                if (UniformFix.afterBody != null && ReferenceEquals(UniformFix.afterBody, __instance)) UniformFix.foreign++;
                return true;
            }
            if (!ReferenceEquals(p.Character, __instance)) return true;
            try { if (UniformFix.Unchanged(p)) { UniformFix.skipped++; return false; } }
            catch (Exception e) { UniformFix.Error(e); }
            UniformFix.passed++;
            return true;
        }
    }

    // Companion fixes for the "Projekt Bifrost" mod: its gauges are updated at a few Hz instead of
    // every frame (list in toolkit-bifrost-throttle.txt, "Typ = Hz"), its depth-charge tally no longer
    // throws on every detonation when its chalkboard never bound, plus a UI path report in the log.
    internal static class BifrostCfg
    {
        private static readonly string[] DefaultTypes = { "ThermoPlath", "Uboat.Mods.Moonphase.Mapper.SmoothRotator", "IndicatorHandlerEM2", "IndicatorHandler", "Uboat.Mods.Moonphase.Mapper.Thermo_outer", "Uboat.Mods.Moonphase.Mapper.hgWet" };
        private static readonly float[] DefaultHz = { 10f, 30f, 10f, 10f, 10f, 10f };
        private static List<KeyValuePair<string, float>> list;
        public static bool On => Settings.B("FixBifrost");
        private static string FilePath => System.IO.Path.Combine(Settings.Folder, "toolkit-bifrost-throttle.txt");

        public static List<KeyValuePair<string, float>> Throttles()
        {
            if (list != null) return list;
            list = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < DefaultTypes.Length; i++) list.Add(new KeyValuePair<string, float>(DefaultTypes[i], DefaultHz[i]));
            try
            {
                if (!System.IO.File.Exists(FilePath))
                {
                    System.IO.Directory.CreateDirectory(Settings.Folder);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# Bifrost-Anzeigen: wie oft pro Sekunde sie neu gerechnet werden (0 = jedes Bild).");
                    sb.AppendLine("# Zeile: Typname = Hz. Neue Zeilen für weitere Anzeigen sind erlaubt. Wirkt nach Spiel-Neustart.");
                    for (int i = 0; i < DefaultTypes.Length; i++) sb.AppendLine(DefaultTypes[i] + " = " + DefaultHz[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    System.IO.File.WriteAllText(FilePath, sb.ToString());
                }
                foreach (var raw in System.IO.File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string type = line.Substring(0, eq).Trim();
                    if (!float.TryParse(line.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hz)) continue;
                    hz = Mathf.Clamp(hz, 0f, 120f);
                    int at = list.FindIndex(kv => string.Equals(kv.Key, type, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) list[at] = new KeyValuePair<string, float>(type, hz); else list.Add(new KeyValuePair<string, float>(type, hz));
                }
            }
            catch (Exception e) { Util.Log("Bifrost-Fix: Liste nicht lesbar, nehme Standard: " + e.Message); }
            return list;
        }
    }

    [HarmonyPatch]
    internal static class Bifrost_Throttle
    {
        private static readonly Dictionary<int, float> nextDue = new Dictionary<int, float>();
        private static readonly Dictionary<Type, float> interval = new Dictionary<Type, float>();
        public static long Skipped, Ran;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var missing = new List<string>();
            foreach (var kv in BifrostCfg.Throttles())
            {
                var t = AccessTools.TypeByName(kv.Key);
                var u = t == null ? null : AccessTools.DeclaredMethod(t, "Update");
                if (u == null) { missing.Add(kv.Key); continue; }
                interval[t] = kv.Value <= 0f ? 0f : 1f / kv.Value;
                yield return u;
            }
            if (missing.Count > 0 && interval.Count > 0) Util.Log("Bifrost-Fix: nicht gefunden (übersprungen): " + string.Join(", ", missing.ToArray()));
        }

        private static bool Prepare()
        {
            foreach (var kv in BifrostCfg.Throttles()) if (AccessTools.TypeByName(kv.Key) != null) return true;
            return false;
        }

        private static bool Prefix(MonoBehaviour __instance)
        {
            try
            {
                if (!BifrostCfg.On || __instance == null) return true;
                if (!interval.TryGetValue(__instance.GetType(), out float iv) || iv <= 0f) return true;
                int id = __instance.GetInstanceID();
                float now = Time.unscaledTime;
                if (nextDue.TryGetValue(id, out float due) && now < due) { Skipped++; return false; }
                if (nextDue.Count >= 256 && !nextDue.ContainsKey(id)) nextDue.Clear();   // forget destroyed instances
                nextDue[id] = now + iv;
                Ran++;
                return true;
            }
            catch { return true; }
        }

        private static void Cleanup()
        {
            if (interval.Count == 0) return;
            Util.Log("Bifrost-Fix: " + interval.Count + " Anzeige(n) gedrosselt.");
            Application.quitting += () =>
            {
                long all = Skipped + Ran;
                if (all == 0) return;
                Util.Log("Bifrost-Fix: Sitzung " + Skipped + " von " + all + " Anzeige-Updates gespart (" + (Skipped * 100.0 / all).ToString("0.0") + " %)" + (Bifrost_DepthCharge.Suppressed > 0 ? ", " + Bifrost_DepthCharge.Suppressed + " Wasserbomben-Fehler verhindert" : "") + ".");
            };
        }
    }

    [HarmonyPatch]
    internal static class Bifrost_DepthCharge
    {
        private static FieldInfo chalk;
        private static bool logged;
        public static int Suppressed;

        private static MethodBase Target()
        {
            var t = AccessTools.TypeByName("UBOAT.Mods.Moonphase.Main");
            if (t == null) return null;
            chalk = AccessTools.Field(t, "chalkHandler");
            var m = AccessTools.DeclaredMethod(t, "depthChargesDetonatedDuringCurrentEncounterChanged");
            return chalk != null ? m : null;
        }

        private static bool Prepare() => Target() != null;
        private static MethodBase TargetMethod() => Target();

        private static bool Prefix()
        {
            try
            {
                if (!BifrostCfg.On || !Settings.B("FixBifrostDepthCharge")) return true;
                if (chalk.GetValue(null) as UnityEngine.Object != null) return true;
                Suppressed++;
                if (!logged) { logged = true; Util.Log("Bifrost-Fix: Kreidetafel nie verbunden - Wasserbomben-Zähler übersprungen (sonst Fehler bei jeder Detonation). Wasserbomben selbst unverändert."); }
                return false;
            }
            catch (Exception e)
            {
                if (!logged) { logged = true; Util.Log("Bifrost-Fix: Wächter fehlgeschlagen, Bifrost läuft normal: " + e.Message); }
                return true;
            }
        }
    }

    // Writes Bifrost's UI paths to the log once, 20 s after the game UI appears.
    public class BifrostProbe : ModuleBase
    {
        private float probeAt = -1f, firstTry = -1f, nextTry;
        private bool done;
        private static int moonphase;   // 0 = not checked yet, 1 = there, 2 = not there (TypeByName scans every assembly: ~350 ms)
        private static readonly string[] Paths = { "GUI/Viewport/Main UI Area", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI Alternative", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI Alternative/Timer" };

        public override void Tick()
        {
            if (done || !BifrostCfg.On || !Settings.B("BifrostUiLog")) return;
            if (moonphase == 0) moonphase = AccessTools.TypeByName("UBOAT.Mods.Moonphase.Main") != null ? 1 : 2;
            if (moonphase == 2) { done = true; return; }
            float now = Time.unscaledTime;
            // GameObject.Find searches the whole scene (300-600 ms here). It ran every second for as long as the
            // UI path was missing, i.e. all game long. Now: one try every 15 s, give up after 5 minutes.
            if (probeAt < 0f)
            {
                if (firstTry < 0f) firstTry = now;
                if (now - firstTry > 300f) { done = true; Util.Log("Bifrost-Fix: UI-Pfad nicht gefunden, Prüfung beendet"); return; }
                if (now < nextTry) return;
                nextTry = now + 15f;
                if (GameObject.Find(Paths[0]) != null) probeAt = now + 20f;
            }
            if (probeAt < 0f || now < probeAt) return;
            done = true;
            Util.Safe("Bifrost.Probe", () =>
            {
                var sb = new System.Text.StringBuilder("Bifrost-Fix: UI-Pfade:");
                foreach (var p in Paths)
                {
                    var go = GameObject.Find(p);
                    sb.Append("\n   ").Append(go != null ? "OK      " : "FEHLT   ").Append(p);
                    if (go != null) sb.Append("  (Kinder ").Append(go.transform.childCount).Append(", Text ").Append(go.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length).Append(", Bild ").Append(go.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length).Append(")");
                }
                var area = GameObject.Find(Paths[0]);
                if (area != null)
                {
                    sb.Append("\n   Kinder von 'Main UI Area':");
                    for (int i = 0; i < area.transform.childCount; i++) sb.Append("\n     ").Append(area.transform.GetChild(i).name).Append(area.transform.GetChild(i).gameObject.activeInHierarchy ? "" : "  (inaktiv)");
                }
                Util.Log(sb.ToString());
            });
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix for mods that give characters clothes which do not exist in the game (seen: "Cap NCO" from
    // "officers-and-sailors-better-ncos"). The game then writes two red errors per character and the
    // piece stays empty anyway. Here a missing accessory is simply left out, without the errors.
    [HarmonyPatch]
    internal static class MissingWearable_Patch
    {
        private static DWS.Common.Resources.ResourceManager rm;
        private static readonly HashSet<string> reported = new HashSet<string>();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Character), "LoadWearable", new[] { typeof(string), typeof(bool) });
            yield return AccessTools.Method(typeof(Character), "LoadWearableAsync", new[] { typeof(string), typeof(bool) });
        }

        private static bool Prefix(string path)
        {
            try
            {
                if (!Settings.B("FixMissingWearables") || path == null || !path.StartsWith("Character/Accessories/")) return true;
                if (rm == null) rm = Traverse.Create(typeof(Character)).Field("resourceManager").GetValue<DWS.Common.Resources.ResourceManager>();
                if (rm == null || rm.AssetExists(path)) return true;
                if (reported.Add(path)) Util.Log("Kleidung fehlt im Spiel, wird weggelassen: " + path);
                return false;   // LoadWearable then returns null, same as the game does after its error
            }
            catch { return true; }
        }
    }
}
