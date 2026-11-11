using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core;

namespace UboatToolkit.Modules
{
    // Brake finder for the game's own update dispatcher (ExecutionQueue).
    // Most game logic does NOT run in MonoBehaviour.Update but as Action delegates in
    // ExecutionQueue.update / lateUpdate / fixedUpdate (decompiled com.uboat.game.dll).
    // The PerfLogger hotspot finder cannot see these. This module times each delegate.
    // Setting "PerfQueue" (off by default). Writes to <Settings.Folder>/logs/queue-*.csv:
    //  - every 60 s: top 20 delegates by time (ms per minute)
    //  - every 5 min: list sizes (growing counts = leak hint: listeners added but never removed)
    [HarmonyPatch]
    public class QueueProfiler : ModuleBase
    {
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fUpdate = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("update");
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fLate = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("lateUpdate");
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fFixed = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("fixedUpdate");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iUpdate = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentUpdateIndex");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iLate = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentLateUpdateIndex");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iFixed = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentFixedUpdateIndex");

        private static readonly Dictionary<string, double> spent = new Dictionary<string, double>();
        private static ExecutionQueue lastQueue;
        private static bool on;
        private static string file;
        private static long written;
        private float nextTop, nextSizes;

        public override void Frame()
        {
            on = Settings.B("PerfQueue");
            if (!on) { spent.Clear(); return; }
            float now = Time.unscaledTime;
            if (now >= nextTop)
            {
                if (nextTop > 0f)
                {
                    int rank = 0;
                    foreach (var kv in spent.OrderByDescending(k => k.Value).Take(20))
                        Write("queue_top", (++rank).ToString(), kv.Key, (kv.Value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ms/min");
                }
                spent.Clear();
                nextTop = now + 60f;
            }
            if (now >= nextSizes && lastQueue != null)
            {
                nextSizes = now + 300f;
                Write("queue_sizes", "update=" + fUpdate(lastQueue).Count, "late=" + fLate(lastQueue).Count, "fixed=" + fFixed(lastQueue).Count);
            }
        }

        private static void Write(string kind, string a, string b, string c)
        {
            try
            {
                if (file == null)
                {
                    var dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "queue-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
                    File.WriteAllText(file, "time;kind;value1;value2;value3\n");
                }
                if (written > 5L * 1024 * 1024) return;
                string line = DateTime.Now.ToString("HH:mm:ss") + ";" + kind + ";" + a + ";" + b + ";" + c + "\n";
                written += line.Length;
                File.AppendAllText(file, line);
            }
            catch (Exception e) { Util.Log("queue profiler: " + e.Message); }
        }

        private static string Name(Action a) =>
            a.Method.DeclaringType != null ? a.Method.DeclaringType.Name + "." + a.Method.Name : a.Method.Name;

        // Same loop as the original, plus timing. Index field is kept in sync because
        // RemoveUpdateListener() adjusts it while the loop runs.
        private static void Run(ExecutionQueue q, List<Action> list, AccessTools.FieldRef<ExecutionQueue, int> idx, int start)
        {
            idx(q) = start;
            Action action = null;
            try
            {
                while (idx(q) >= 0)
                {
                    action = list[idx(q)];
                    double t = Time.realtimeSinceStartupAsDouble;
                    action();
                    double d = Time.realtimeSinceStartupAsDouble - t;
                    string n = Name(action);
                    spent.TryGetValue(n, out var old);
                    spent[n] = old + d;
                    idx(q)--;
                }
            }
            catch (Exception ex)
            {
                if (action != null) Debug.LogError("Error in execution queue method \"" + Name(action) + "\".");
                Debug.LogException(ex);
                Run(q, list, idx, Mathf.Min(idx(q) - 1, list.Count - 1));
            }
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunUpdates"), HarmonyPrefix]
        private static bool RunUpdates(ExecutionQueue __instance, int index)
        {
            lastQueue = __instance;
            if (!on) return true;
            Run(__instance, fUpdate(__instance), iUpdate, index);
            return false;
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunLateUpdates"), HarmonyPrefix]
        private static bool RunLateUpdates(ExecutionQueue __instance, int index)
        {
            if (!on) return true;
            Run(__instance, fLate(__instance), iLate, index);
            return false;
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunFixedUpdates"), HarmonyPrefix]
        private static bool RunFixedUpdates(ExecutionQueue __instance, int index)
        {
            if (!on) return true;
            Run(__instance, fFixed(__instance), iFixed, index);
            return false;
        }
    }
}
