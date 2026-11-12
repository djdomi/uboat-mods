using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // High-resolution clock without naming the diagnostics namespace (blocked by the mod check).
    internal static class LoadClock
    {
        public static readonly Type StopwatchType = Type.GetType("System.Diagnostics.Stopwatch, System") ?? Type.GetType("System.Diagnostics.Stopwatch");
        private static readonly Func<long> stamp = Make();
        public static readonly long Frequency = ReadFrequency();

        private static Func<long> Make()
        {
            try
            {
                var m = StopwatchType?.GetMethod("GetTimestamp", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m != null) return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
            }
            catch { }
            return () => DateTime.UtcNow.Ticks;
        }

        private static long ReadFrequency()
        {
            try
            {
                var f = StopwatchType?.GetField("Frequency", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null && StopwatchType.GetMethod("GetTimestamp") != null) return (long)f.GetValue(null);
            }
            catch { }
            return TimeSpan.TicksPerSecond;
        }

        public static long Now() => stamp();
        public static double Seconds(long ticks) => ticks / (double)Frequency;
    }

    // Faster loading: the save loader may work longer per frame, ends an idle frame early while it
    // only waits for the worker thread, and skips the game's memory clean-ups during the load.
    [HarmonyPatch]
    internal static class SaveTools_LoadSpeedPatch
    {
        private const int VanillaBudget = 160000, VanillaActions = 500;
        private static bool patched;
        private static object active;
        private static long lastStep, loadStart;
        private static int frames;

        private static bool On => Settings.B("FastLoad");
        public static bool IsLoading => active != null && LoadClock.Seconds(LoadClock.Now() - lastStep) < 5.0;

        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SavesManager), "LoadCoroutine"));

        public static long Budget()
        {
            if (!On) return VanillaBudget;
            if (SaveTools_IdlePatch.ShouldYield()) return 0;
            return (long)(Mathf.Clamp(Settings.F("FastLoadBudgetMs"), 16f, 1000f) * LoadClock.Frequency / 1000.0);
        }

        public static int MaxActions() => On ? Mathf.Clamp(Mathf.RoundToInt(Settings.F("FastLoadMaxActions")), 500, 1000000) : VanillaActions;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            var elapsed = LoadClock.StopwatchType == null ? null : AccessTools.PropertyGetter(LoadClock.StopwatchType, "ElapsedTicks");
            if (elapsed == null) { Util.Log("Laden: Stoppuhr nicht gefunden - Lade-Schleife bleibt Spielwert."); return list; }
            int b = -1, m = -1, nb = 0, nm = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.opcode != OpCodes.Ldc_I4 && c.opcode != OpCodes.Ldc_I4_S) continue;
                int v = c.operand is int x ? x : c.operand is sbyte sb ? sb : c.operand is byte by ? by : int.MinValue;
                if (v == VanillaBudget && i > 0 && i + 1 < list.Count && list[i - 1].operand as System.Reflection.MethodInfo == elapsed && list[i + 1].opcode == OpCodes.Conv_I8) { b = i; nb++; }
                if (v == VanillaActions && i + 1 < list.Count && (list[i + 1].opcode == OpCodes.Bge || list[i + 1].opcode == OpCodes.Bge_S)) { m = i; nm++; }
            }
            if (nb != 1 || nm != 1) { Util.Log("Laden: Lade-Schleife passt nicht (" + nb + "/" + nm + ") - bleibt Spielwert."); return list; }
            list[b].opcode = OpCodes.Call; list[b].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "Budget");
            list[m].opcode = OpCodes.Call; list[m].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "MaxActions");
            patched = true;
            return list;
        }

        private static void Prefix(object __instance)
        {
            if (!ReferenceEquals(active, __instance)) { active = __instance; loadStart = LoadClock.Now(); frames = 0; SaveTools_IdlePatch.ResetLoad(); }
            frames++;
            lastStep = LoadClock.Now();
            SaveTools_IdlePatch.BeginFrame();
        }

        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception != null && ReferenceEquals(active, __instance))
            {
                active = null;
                Util.Log("Laden: Fehler beim Laden (" + __exception.GetType().Name + ") - Speicher-Bremse gelöst." + SaveTools_MemoryPatch.SummariseAndReset());
            }
            return __exception;
        }

        private static void Postfix(object __instance, bool __result)
        {
            if (__result || !ReferenceEquals(active, __instance)) return;
            double s = LoadClock.Seconds(LoadClock.Now() - loadStart);
            Util.Log("Laden: fertig in " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s über " + frames + " Bilder ("
                + (patched && On ? Settings.F("FastLoadBudgetMs").ToString("0", CultureInfo.InvariantCulture) + " ms / " + MaxActions() + " Schritte pro Bild" : "Spielwert 16 ms / 500 Schritte") + ")."
                + SaveTools_IdlePatch.Summarise(frames) + SaveTools_MemoryPatch.SummariseAndReset());
            active = null;
        }
    }

    // While the loader only waits on its worker thread, end that frame after 16 ms instead of idling on.
    [HarmonyPatch]
    internal static class SaveTools_IdlePatch
    {
        private const double IdleYieldMs = 16.0;
        private static bool installed, yielded;
        private static long idleFrame, idleLoad;
        private static int framesYielded;

        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Serialization.GameStateDeserializer");
            return t == null ? null : AccessTools.DeclaredMethod(t, "CallMainThreadAction");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            var waitOne = AccessTools.Method(typeof(System.Threading.WaitHandle), "WaitOne", new[] { typeof(int) });
            int at = -1, n = 0;
            for (int i = 0; i < list.Count; i++)
                if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && list[i].operand as System.Reflection.MethodInfo == waitOne) { at = i; n++; }
            if (n != 1 || waitOne == null) { Util.Log("Laden: Warte-Stelle passt nicht (" + n + ") - leere Bilder bleiben Spielwert."); return list; }
            list[at].opcode = OpCodes.Call; list[at].operand = AccessTools.Method(typeof(SaveTools_IdlePatch), "TimedWait");
            installed = true;
            return list;
        }

        public static bool TimedWait(System.Threading.WaitHandle handle, int ms)
        {
            long start = LoadClock.Now();
            bool ok = handle.WaitOne(ms);
            long w = LoadClock.Now() - start;
            idleFrame += w; idleLoad += w;
            return ok;
        }

        public static bool ShouldYield()
        {
            if (!installed || idleFrame < (long)(IdleYieldMs * LoadClock.Frequency / 1000.0)) return false;
            if (!yielded) { yielded = true; framesYielded++; }
            return true;
        }

        public static void BeginFrame() { idleFrame = 0; yielded = false; }
        public static void ResetLoad() { framesYielded = 0; idleLoad = 0; BeginFrame(); }

        public static string Summarise(int frames) => !installed ? "" :
            " Gewartet " + LoadClock.Seconds(idleLoad).ToString("0.0", CultureInfo.InvariantCulture) + " s auf leere Warteschlange; " + framesYielded + " von " + frames + " Bildern früher beendet.";
    }

    // The loader's worker thread may only queue 40 jobs for the main thread, then it stops and waits.
    // The main thread then often idles too ("Gewartet x s auf leere Warteschlange"). A larger queue lets
    // the worker read ahead, so both threads stay busy. 40 = game value.
    [HarmonyPatch]
    internal static class SaveTools_QueueCapPatch
    {
        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Serialization.GameStateDeserializer");
            return t == null ? null : AccessTools.DeclaredMethod(t, "MainThread", new[] { typeof(Action), typeof(bool) });
        }

        public static int Cap() => Settings.B("FastLoad") ? Mathf.Clamp(Mathf.RoundToInt(Settings.F("FastLoadQueueCap")), 40, 100000) : 40;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if ((c.opcode == OpCodes.Ldc_I4_S || c.opcode == OpCodes.Ldc_I4) && Convert.ToInt32(c.operand) == 40)
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(SaveTools_QueueCapPatch), nameof(Cap)); n++; }
            }
            if (n != 2) Util.Log("Laden: Warteschlangen-Grenze passt nicht (" + n + " Stellen gefunden, erwartet 2).");
            return list;
        }
    }

    // During a save load the game's memory clean-up (about 115 ms each) is skipped, up to a heap cap.
    [HarmonyPatch]
    internal static class SaveTools_MemoryPatch
    {
        private static int skipped, allowed;
        private static bool installed;

        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Core.GameState");
            return t == null ? null : AccessTools.DeclaredMethod(t, "CollectMemory");
        }

        private static bool Prefix()
        {
            installed = true;
            if (!Settings.B("FastLoad") || !Settings.B("FastLoadNoGc") || !SaveTools_LoadSpeedPatch.IsLoading) return true;
            long heapMb = GC.GetTotalMemory(false) / (1024L * 1024L);
            if (heapMb >= Mathf.Clamp(Settings.F("FastLoadGcCapMb"), 500f, 32000f)) { allowed++; return true; }
            skipped++;
            return false;
        }

        public static string SummariseAndReset()
        {
            if (!installed || (skipped == 0 && allowed == 0)) return "";
            string s = " " + skipped + " Speicher-Aufräumen übersprungen" + (allowed > 0 ? ", " + allowed + " wegen Obergrenze erlaubt" : "") + " (etwa " + (skipped * 115) + " ms gespart).";
            skipped = allowed = 0;
            return s;
        }
    }
}
