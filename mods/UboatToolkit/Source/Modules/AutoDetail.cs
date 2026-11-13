using UnityEngine;

namespace UboatToolkit.Modules
{
    // Automatic detail (setting ExplosionAuto): at low FPS Unity runs MORE physics steps per frame to catch up
    // (FixedUpdate of the contact handling, ships, rudders ...), which makes the next frame even slower.
    // Below half the target FPS the catch-up per frame is limited (max. 0.1 s game time per frame); above, the game value is back.
    public class AutoDetail : ModuleBase
    {
        private float original = -1f;
        private bool limited;
        private float nextSwitch;

        public override void Frame()
        {
            if (original < 0f) original = Time.maximumDeltaTime;
            // Hysteresis: limit below 50 % of target FPS, release only above 70 %, and switch at most every 10 s.
            float f = ExplosionLimit_Patch.Fps(), target = Settings.F("ExplosionTargetFps");
            bool want = Settings.B("ExplosionAuto") && (limited ? f < target * 0.7f : f < target * 0.5f);
            if (want == limited || Time.unscaledTime < nextSwitch) return;
            nextSwitch = Time.unscaledTime + 10f;
            limited = want;
            Time.maximumDeltaTime = want ? Mathf.Min(original, 0.1f) : original;
            ToolkitRunner.ShowNotice(want ? "Auto-Detail: viel los (" + Mathf.RoundToInt(f) + " FPS) - Effekte, Stimmen + Physik werden gespart" : "Auto-Detail: wieder volle Details", 4f);
            Util.Log("Auto-Detail: Physik-Aufholen " + (want ? "begrenzt (0,1 s pro Bild)" : "normal (" + original.ToString("0.00") + " s)"));
        }
    }
}

namespace UboatToolkit.Modules
{
    // The game checks ALL nearby ship groups (enter/leave, group update) in every physics step (SandboxPlayerWolfpack.FixedUpdate,
    // measured ~10 s per minute with ~100 ships). Under the target FPS this check runs only every 2nd-4th step;
    // groups are still found, just a few hundredths of a second later.
    [HarmonyLib.HarmonyPatch]
    internal static class AutoDetail_NearbyGroupsPatch
    {
        private static int step;
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var t = typeof(UBOAT.Game.Sandbox.SandboxPlayerWolfpack);
            var a = HarmonyLib.AccessTools.Method(t, "UpdateNearbyGroups"); if (a != null) yield return a;
            var b = HarmonyLib.AccessTools.Method(t, "UpdateImpostorGroups"); if (b != null) yield return b;
        }
        private static bool Prefix(System.Reflection.MethodBase __originalMethod)
        {
            if (!Settings.B("ExplosionAuto")) return true;
            float load = ExplosionLimit_Patch.Fps() / UnityEngine.Mathf.Max(1f, Settings.F("ExplosionTargetFps"));
            if (load >= 1f) return true;
            // the slower, the rarer: 40 FPS target at 5 FPS = every 8th step (max. 10)
            int every = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(1f / UnityEngine.Mathf.Max(0.01f, load)), 2, 10);
            if (__originalMethod.Name == "UpdateNearbyGroups") step++;
            return step % every == 0;
        }
    }
}

namespace UboatToolkit.Modules
{
    // DoMe's idea: far ships only "virtual". The game already has this: groups outside the simulation distance are not
    // spawned as real ships (they stay on the map/sensors as sandbox groups). Under the target FPS this distance is
    // reduced step by step (down to AutoVirtualMin), so far groups go virtual; when FPS recover it goes back to the game value.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.Data.GameplaySettings), "SimulationDistanceMultiplier", HarmonyLib.MethodType.Getter)]
    internal static class AutoDetail_VirtualShipsPatch
    {
        private static float smooth = 1f, lastT = -1f;
        private static void Postfix(ref float __result)
        {
            if (!Settings.B("ExplosionAuto") || !Settings.B("AutoVirtualShips")) return;
            float t = UnityEngine.Time.unscaledTime;
            if (t != lastT)
            {
                lastT = t;
                float load = UnityEngine.Mathf.Clamp01(ExplosionLimit_Patch.Fps() / UnityEngine.Mathf.Max(1f, Settings.F("ExplosionTargetFps")));
                float want = UnityEngine.Mathf.Lerp(UnityEngine.Mathf.Clamp(Settings.F("AutoVirtualMin"), 0.2f, 1f), 1f, load);
                smooth = UnityEngine.Mathf.MoveTowards(smooth, want, 0.02f * UnityEngine.Time.unscaledDeltaTime);   // slow: 0.1 per 5 s, no flicker
            }
            __result *= smooth;
        }
    }
}
