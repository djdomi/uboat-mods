using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Periscope/UZO zoom without stutter: overlay textures stay loaded (up to a memory cap),
    // overlays seen before are preloaded in the background after a load, the UZO zoom dropdown of
    // a realistic-UZO mod refreshes the overlay instead of restarting the view, slow loads are logged.
    internal static class ScopeCfg
    {
        public static bool On => Settings.B("FixScopeStall");
        public static bool Retain => On && Settings.B("ScopeRetain");
        public static bool Preload => Settings.B("ScopePreload");
        public static bool ReplaceUzo => Settings.B("ScopeUzoRefresh");
        public static float MaxMb => Mathf.Max(0f, Settings.F("ScopeMaxMb"));
        public static float LogSlowMs => Settings.B("ScopeLog") ? Mathf.Max(0f, Settings.F("ScopeLogSlowMs")) : float.MaxValue;
        public static void Log(string m) { if (Settings.B("ScopeLog")) Util.Log("Sehrohr-Ruckeln: " + m); }
    }

    internal static class ScopeClock
    {
        public static long Now() => LoadClock.Now();
        public static double MsSince(long t) => LoadClock.Seconds(LoadClock.Now() - t) * 1000.0;
    }

    [HarmonyPatch(typeof(PeriscopeIME), "ValidateOverlay")]
    internal static class ScopeRetainer
    {
        public static readonly object Referent = new object();
        private static FieldInfo[] textureFields;
        private static FieldInfo resourceField, assetField;
        private static MethodInfo addUsage, removeUsage;
        private static readonly Dictionary<object, long> retained = new Dictionary<object, long>();
        private static long retainedBytes;
        private static bool capLogged, errorLogged, inited, lastRetain;
        public static PeriscopeIME LastIme;

        private static void Init()
        {
            if (inited) return;
            inited = true;
            var found = new List<FieldInfo>();
            foreach (var f in typeof(PeriscopeIME).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var t = f.FieldType;
                if (t.IsGenericType && t.GetGenericTypeDefinition().Name == "Resource`1" && t.GetGenericArguments()[0] == typeof(Texture2D)) found.Add(f);
            }
            textureFields = found.ToArray();
            if (textureFields.Length > 0)
            {
                var rt = textureFields[0].FieldType;
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                resourceField = rt.GetField("resource", any);
                assetField = rt.GetField("asset", any);
                if (resourceField != null)
                {
                    var iface = resourceField.FieldType;
                    addUsage = iface.GetMethod("AddUsage", new[] { typeof(object), typeof(bool) });
                    removeUsage = iface.GetMethod("RemoveUsage", new[] { typeof(object) });
                }
            }
            ScopeCfg.Log("Overlay-Felder gefunden: " + textureFields.Length + (addUsage == null || removeUsage == null ? " - Festhalten nicht möglich" : ""));
        }

        public static void Adopt(object resource) { if (resource != null && !retained.ContainsKey(resource)) retained[resource] = 0; }

        private static void Prefix(out long __state) { __state = ScopeClock.Now(); }

        private static void Postfix(PeriscopeIME __instance, long __state)
        {
            try
            {
                double ms = ScopeClock.MsSince(__state);
                Init();
                LastIme = __instance;
                bool retain = ScopeCfg.Retain;
                if (lastRetain && !retain) ReleaseAll("ausgeschaltet");
                lastRetain = retain;
                if (!ScopeCfg.On) return;
                if (retain) Retain(__instance);
                if (ms >= ScopeCfg.LogSlowMs)
                    ScopeCfg.Log("Overlay (" + (__instance.Periscope != null ? __instance.Periscope.Name : "?") + ") brauchte " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms.");
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Sehrohr-Ruckeln: Fehler (einmalig) " + e); } }
        }

        private static void Retain(PeriscopeIME ime)
        {
            if (addUsage == null || resourceField == null) return;
            foreach (var tf in textureFields)
            {
                object boxed = tf.GetValue(ime);
                if (boxed == null) continue;
                object res = resourceField.GetValue(boxed);
                if (res == null) continue;
                bool known = retained.ContainsKey(res);
                Texture tex = null;
                long bytes = 0;
                if (!known)
                {
                    tex = assetField != null ? assetField.GetValue(boxed) as Texture : null;
                    bytes = EstimateBytes(tex);
                    if ((retainedBytes + bytes) / (1024.0 * 1024.0) > ScopeCfg.MaxMb)
                    {
                        if (!capLogged) { capLogged = true; ScopeCfg.Log("Obergrenze erreicht (" + Mb(retainedBytes) + " MB) - weitere Overlays wie im Spiel."); }
                        continue;
                    }
                }
                addUsage.Invoke(res, new object[] { Referent, false });
                ScopePreloader.Remember(ScopeResources.PathOf(res));
                if (!known)
                {
                    retained[res] = bytes; retainedBytes += bytes;
                    ScopeCfg.Log("halte Overlay geladen: " + Describe(tex) + ", ~" + Mb(bytes) + " MB (gesamt ~" + Mb(retainedBytes) + " MB, " + retained.Count + " Texturen).");
                }
            }
        }

        public static void ReleaseAll(string why)
        {
            if (removeUsage == null || retained.Count == 0) return;
            int n = retained.Count;
            foreach (var r in retained.Keys) { try { removeUsage.Invoke(r, new object[] { Referent }); } catch { } }
            retained.Clear(); retainedBytes = 0; capLogged = false;
            ScopeCfg.Log(n + " Overlay-Textur(en) freigegeben (" + why + ").");
        }

        private static string Describe(Texture t)
        {
            if (t == null) return "(noch nicht geladen)";
            var t2 = t as Texture2D;
            return "'" + t.name + "' " + t.width + "x" + t.height + (t2 != null ? " " + t2.format : "");
        }

        private static long EstimateBytes(Texture t)
        {
            if (t == null) return 0;
            double bpp = 4.0;
            if (t is Texture2D t2)
            {
                switch (t2.format)
                {
                    case TextureFormat.DXT1: case TextureFormat.BC4: bpp = 0.5; break;
                    case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC6H: case TextureFormat.BC7: case TextureFormat.Alpha8: case TextureFormat.R8: bpp = 1.0; break;
                    case TextureFormat.RGBAHalf: bpp = 8.0; break;
                    case TextureFormat.RGBAFloat: bpp = 16.0; break;
                }
                if (t2.mipmapCount > 1) bpp *= 4.0 / 3.0;
            }
            return (long)((double)t.width * t.height * bpp);
        }

        private static string Mb(long b) => (b / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
    }

    // Remembers every overlay ever kept (file toolkit-scope-preload.txt) and loads them in the
    // background 20 s after a career is ready, one every 0.25 s.
    internal static class ScopePreloader
    {
        private static readonly List<string> paths = new List<string>();
        private static readonly HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<object> requested = new List<object>();
        private static bool dirty, inited;
        public static int Count => paths.Count;
        public static int RequestedCount => requested.Count;
        private static string FilePath => Path.Combine(Settings.Folder, "toolkit-scope-preload.txt");

        public static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                if (File.Exists(FilePath))
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim().TrimStart('﻿');
                        if (line.Length == 0 || line[0] == '#') continue;
                        if (known.Add(line)) paths.Add(line);
                    }
                ScopeCfg.Log(paths.Count + " Overlay(s) für Vorladen gemerkt.");
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Vorladen nicht möglich: " + e.Message); }
        }

        public static void Remember(string path)
        {
            if (string.IsNullOrEmpty(path) || !known.Add(path)) return;
            paths.Add(path); dirty = true;
        }

        public static void FlushIfDirty()
        {
            if (!dirty) return;
            dirty = false;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                var lines = new List<string> { "# Sehrohr-Overlays, die im Hintergrund vorgeladen werden. Wird automatisch gefüllt;", "# Datei löschen = alles vergessen." };
                lines.AddRange(paths);
                File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Vorlade-Liste nicht speicherbar: " + e.Message); }
        }

        public static void Request(int i)
        {
            var r = ScopeResources.RetrieveAsync(paths[i], ScopeRetainer.Referent);
            if (r == null) return;
            requested.Add(r);
            ScopeRetainer.Adopt(r);
        }

        public static int LoadedCount() { int n = 0; foreach (var r in requested) if (ScopeResources.IsLoaded(r)) n++; return n; }
    }

    public class ScopePreloadHost : ModuleBase
    {
        private float nextCheck, nextFlush, readySince = -1f, nextRequest, verifyAt = -1f;
        private int next;
        private bool done;

        public override void Frame()
        {
            ScopePreloader.Init();
            float now = Time.unscaledTime;
            if (now >= nextFlush) { nextFlush = now + 6.7f; ScopePreloader.FlushIfDirty(); }
            if (verifyAt > 0f && now >= verifyAt) { verifyAt = -1f; ScopeCfg.Log("Vorladen: " + ScopePreloader.LoadedCount() + "/" + ScopePreloader.RequestedCount + " Overlays geladen."); }
            if (done || !ScopeCfg.Retain || !ScopeCfg.Preload || ScopePreloader.Count == 0) return;
            if (readySince < 0f)
            {
                if (now < nextCheck) return;
                nextCheck = now + 3f;
                if (Util.PlayerShip == null || ScopeResources.IsLoading()) return;
                readySince = now;
                return;
            }
            if (now - readySince < 20f || now < nextRequest) return;
            if (next >= ScopePreloader.Count || !ScopeResources.Ready)
            {
                done = true; verifyAt = now + 30f;
                ScopeCfg.Log("Vorladen angefordert für " + ScopePreloader.RequestedCount + " Overlay(s)" + (ScopeResources.Ready ? "." : " - Ressourcen-Schnittstelle fehlt."));
                return;
            }
            ScopePreloader.Request(next++);
            nextRequest = now + 0.25f;
        }
    }

    internal static class ScopeResources
    {
        private static bool tried;
        private static object manager;
        private static MethodInfo retrieveAsync;
        private static PropertyInfo isLoadingP, pathP, isLoadedP;

        public static bool Ready { get { Init(); return manager != null && retrieveAsync != null; } }

        public static string PathOf(object r) { Init(); if (r == null || pathP == null) return null; try { return pathP.GetValue(r, null) as string; } catch { return null; } }
        public static bool IsLoaded(object r) { Init(); if (r == null || isLoadedP == null) return false; try { return (bool)isLoadedP.GetValue(r, null); } catch { return false; } }
        public static bool IsLoading() { Init(); if (manager == null || isLoadingP == null) return false; try { return (bool)isLoadingP.GetValue(manager, null); } catch { return false; } }

        public static object RetrieveAsync(string path, object referent)
        {
            if (!Ready) return null;
            try { return retrieveAsync.Invoke(manager, new object[] { path, referent, null, false }); }
            catch (Exception e) { ScopeCfg.Log("Vorladen von '" + path + "' fehlgeschlagen: " + e.Message); return null; }
        }

        private static void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var iface = AccessTools.TypeByName("DWS.Common.Resources.IResource");
                if (iface != null) { pathP = iface.GetProperty("Path"); isLoadedP = iface.GetProperty("IsLoaded"); }
                var mt = AccessTools.TypeByName("DWS.Common.Resources.ResourceManager");
                if (mt == null) return;
                isLoadingP = mt.GetProperty("IsLoading", inst);
                foreach (var m in typeof(ScriptableObjectSingleton).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.Name == "LoadSingleton" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { manager = m.MakeGenericMethod(mt).Invoke(null, null); break; }
                foreach (var m in mt.GetMethods(inst))
                    if (m.Name == "RetrieveAssetAsync" && m.GetParameters().Length == 4) { retrieveAsync = m.IsGenericMethodDefinition ? m.MakeGenericMethod(typeof(Texture2D)) : m; break; }
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Ressourcen-Schnittstelle fehlt: " + e.Message); }
        }
    }

    // A realistic-UZO mod restarts the whole view on its zoom dropdown; refresh the overlay instead.
    [HarmonyPatch]
    internal static class ScopeUzoZoom
    {
        private static MethodInfo validate;
        private static bool lastDirect, errorLogged;
        private static int direct, restarts;

        private static MethodBase Target()
        {
            var t = AccessTools.TypeByName("RealisticUZOSight.PeriscopeUIPatches+OnZoomSelectedPeriscopeUIPatch");
            return t == null ? null : AccessTools.Method(t, "Postfix");
        }

        private static bool Prepare() { validate = AccessTools.Method(typeof(PeriscopeIME), "ValidateOverlay"); return validate != null && Target() != null; }
        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(object[] __args, out long __state)
        {
            __state = ScopeClock.Now();
            lastDirect = false;
            try
            {
                if (!ScopeCfg.On || !ScopeCfg.ReplaceUzo) return true;
                var main = __args != null && __args.Length > 0 ? __args[0] as MainCamera : null;
                if (main == null || main.GenericController == null) return true;
                var ctl = main.GenericController.ExternalController as PeriscopeController;
                var scope = ctl != null ? ctl.Periscope : null;
                var ime = ScopeRetainer.LastIme;
                if (scope == null || ime == null || !ime.isActiveAndEnabled || ime.Periscope != scope) return true;
                validate.Invoke(ime, null);
                lastDirect = true;
                return false;
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Sehrohr-Ruckeln: UZO-Ersatz fehlgeschlagen, nehme Original (einmalig): " + e); } return true; }
        }

        private static void Postfix(long __state)
        {
            double ms = ScopeClock.MsSince(__state);
            if (lastDirect) direct++; else restarts++;
            ScopeCfg.Log("UZO-Zoom: " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms über " + (lastDirect ? "Overlay-Auffrischen" : "Neustart der Ansicht") + " (Sitzung: " + direct + " Auffrischen, " + restarts + " Neustarts).");
        }
    }

    // Logs synchronous asset-bundle opens (explains any stall that is left).
    [HarmonyPatch]
    internal static class ScopeBundleTimer
    {
        private static readonly HashSet<string> opened = new HashSet<string>(StringComparer.Ordinal);
        private static int count;
        private static double total;

        private static MethodBase Target()
        {
            var t = AccessTools.TypeByName("DWS.Common.Resources.AssetBundleResource");
            return t == null ? null : AccessTools.Method(t, "LoadAsset", Type.EmptyTypes);
        }

        private static bool Prepare() => Target() != null;
        private static MethodBase TargetMethod() => Target();
        private static void Prefix(out long __state) { __state = ScopeClock.Now(); }

        private static void Postfix(object __instance, long __state)
        {
            try
            {
                double ms = ScopeClock.MsSince(__state);
                string path = ScopeResources.PathOf(__instance) ?? "?";
                bool again = !opened.Add(path);
                count++; total += ms;
                if (ScopeCfg.On && (ms >= ScopeCfg.LogSlowMs || (again && Settings.B("ScopeLog"))))
                    ScopeCfg.Log("Asset-Bundle " + (again ? "ERNEUT" : "") + " geöffnet: " + path + " in " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms (Sitzung: " + count + ", " + total.ToString("0", CultureInfo.InvariantCulture) + " ms).");
            }
            catch { }
        }
    }
}
