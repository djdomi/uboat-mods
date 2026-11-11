using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Performance logger (plan: /uboat/logger-mod-plan.md). Off by default.
    // Writes one small CSV per session to <persistent data>/UboatToolkit/logs (max. about 10 MB):
    //  1. scene load times, 2. frames slower than 100 ms, 3. memory + GC every 5 min,
    //  4. object counts every 5 min, 5. hotspot finder (F11): time spent in the game's Update methods, top 20 per minute.
    public class PerfLogger : ModuleBase
    {
        private const long MaxBytes = 10L * 1024 * 1024;
        private static string file;
        private static long written;
        private static double sceneLoadStart;
        private float nextMemory, nextObjects;
        private bool hooked;

        // hotspot finder
        private static readonly Dictionary<MethodBase, double> timeSpent = new Dictionary<MethodBase, double>();
        private static readonly List<MethodBase> patched = new List<MethodBase>();
        private static bool hotspotsOn;
        private float nextHotspotDump;

        private static void Write(string kind, string a, string b = "", string c = "")
        {
            if (!Settings.B("PerfLogger")) return;
            try
            {
                if (file == null)
                {
                    var dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "perf-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
                    File.WriteAllText(file, "time;realtime_s;kind;value1;value2;value3\n");
                }
                if (written > MaxBytes) return;
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + ";" + Time.realtimeSinceStartup.ToString("0.00", CultureInfo.InvariantCulture) + ";" + kind + ";" + a + ";" + b + ";" + c + "\n";
                written += line.Length;
                File.AppendAllText(file, line);
            }
            catch (Exception e) { Util.Log("logger: " + e.Message); }
        }

        public override void Frame()
        {
            if (!hooked)
            {
                hooked = true;
                SceneManager.sceneLoaded += (s, m) =>
                {
                    double took = Time.realtimeSinceStartupAsDouble - sceneLoadStart;
                    Write("scene_loaded", s.name, sceneLoadStart > 0 ? took.ToString("0.00", CultureInfo.InvariantCulture) + " s since previous scene change" : "");
                    sceneLoadStart = Time.realtimeSinceStartupAsDouble;
                };
                SceneManager.sceneUnloaded += s => { Write("scene_unloaded", s.name); sceneLoadStart = Time.realtimeSinceStartupAsDouble; };
            }
            if (!Settings.B("PerfLogger")) { if (hotspotsOn) SetHotspots(false); return; }

            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > 100f) Write("slow_frame", ms.ToString("0", CultureInfo.InvariantCulture) + " ms");

            float now = Time.unscaledTime;
            if (now >= nextMemory)
            {
                nextMemory = now + 300f;
                Write("memory", "mono_MB=" + (Profiler.GetMonoUsedSizeLong() / 1048576),
                    "reserved_MB=" + (Profiler.GetTotalReservedMemoryLong() / 1048576),
                    "gc_runs=" + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2));
            }
            if (now >= nextObjects)
            {
                nextObjects = now + 300f;
                Write("objects", "gameobjects=" + Resources.FindObjectsOfTypeAll<GameObject>().Length,
                    "textures=" + Resources.FindObjectsOfTypeAll<Texture>().Length + " meshes=" + Resources.FindObjectsOfTypeAll<Mesh>().Length,
                    "sounds=" + Resources.FindObjectsOfTypeAll<AudioClip>().Length);
            }

            if (Settings.B("PerfHotspots") && Keys.Down("KeyHotspots")) SetHotspots(!hotspotsOn);
            if (!Settings.B("PerfHotspots") && hotspotsOn) SetHotspots(false);
            if (hotspotsOn && now >= nextHotspotDump)
            {
                nextHotspotDump = now + 60f;
                int rank = 0;
                foreach (var kv in timeSpent.OrderByDescending(k => k.Value).Take(20))
                    Write("hotspot", (++rank).ToString(), kv.Key.DeclaringType?.Name + "." + kv.Key.Name, (kv.Value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ms/min");
                timeSpent.Clear();
            }
        }

        private void SetHotspots(bool on)
        {
            var h = ToolkitLoader.Harmony;
            var pre = new HarmonyMethod(AccessTools.Method(typeof(PerfLogger), nameof(Before)));
            var post = new HarmonyMethod(AccessTools.Method(typeof(PerfLogger), nameof(After)));
            if (on)
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(typeof(PlayerShip).Assembly))
                {
                    if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsGenericTypeDefinition) continue;
                    foreach (var name in new[] { "Update", "LateUpdate", "FixedUpdate" })
                    {
                        var m = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
                        if (m == null || m.IsAbstract || m.GetMethodBody() == null) continue;
                        try { h.Patch(m, pre, post); patched.Add(m); } catch { }
                    }
                }
                nextHotspotDump = Time.unscaledTime + 60f;
                Write("hotspots", "on", patched.Count + " methods measured");
            }
            else
            {
                foreach (var m in patched)
                {
                    try { h.Unpatch(m, pre.method); h.Unpatch(m, post.method); } catch { }
                }
                patched.Clear();
                timeSpent.Clear();
                Write("hotspots", "off");
            }
            hotspotsOn = on;
        }

        private static void Before(out double __state) { __state = Time.realtimeSinceStartupAsDouble; }

        private static void After(MethodBase __originalMethod, double __state)
        {
            double d = Time.realtimeSinceStartupAsDouble - __state;
            timeSpent.TryGetValue(__originalMethod, out var t);
            timeSpent[__originalMethod] = t + d;
        }
    }
}
