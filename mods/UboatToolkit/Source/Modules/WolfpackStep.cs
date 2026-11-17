using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // 0.4.8 (DoMe 2026-11-17: optimise the game). Measured with the hotspot finder: SandboxPlayerWolfpack.FixedUpdate
    // was the most expensive method of the game (about 4.6 s per minute). In every physics step it searches all ship
    // groups around the boat twice (near groups, far "impostor" groups), compares the lists and checks the coast
    // distance. With low FPS the game runs 2-3 physics steps per frame, so the same search ran 2-3 times per frame
    // although the boat moved a few centimetres. Now the two searches run in the first physics step of a frame only.
    // The boat's own position update still runs in every step. Each minute the log shows calls, time and savings.
    public class WolfpackStep : ModuleBase
    {
        internal static int lastFrame = -1, Ran, Saved;
        internal static bool SkipNow;
        internal static double Seconds;
        private float nextLog;

        public override void Tick()
        {
            if (Time.unscaledTime < nextLog) return;
            if (nextLog > 0f && Ran + Saved > 0)
                Util.Log("Nahbereich-Suche: " + Ran + " mal gelaufen (" + (Seconds * 1000.0).ToString("0") + " ms), " + Saved + " mal gespart (zweiter/dritter Physik-Schritt im selben Bild)");
            nextLog = Time.unscaledTime + 60f; Ran = 0; Saved = 0; Seconds = 0;
        }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "UpdateNearbyGroups")]
    internal static class WolfpackStep_Near
    {
        private static bool Prefix(out double __state)
        {
            __state = 0;
            bool skip = Settings.B("WolfpackOncePerFrame") && Time.frameCount == WolfpackStep.lastFrame;
            WolfpackStep.SkipNow = skip;
            if (skip) { WolfpackStep.Saved++; return false; }
            WolfpackStep.lastFrame = Time.frameCount;
            WolfpackStep.Ran++;
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }
        private static void Finalizer(double __state) { if (__state > 0) WolfpackStep.Seconds += Time.realtimeSinceStartupAsDouble - __state; }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "UpdateImpostorGroups")]
    internal static class WolfpackStep_Far
    {
        private static bool Prefix(out double __state)
        {
            __state = 0;
            if (WolfpackStep.SkipNow) return false;
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }
        private static void Finalizer(double __state) { if (__state > 0) WolfpackStep.Seconds += Time.realtimeSinceStartupAsDouble - __state; }
    }
}
