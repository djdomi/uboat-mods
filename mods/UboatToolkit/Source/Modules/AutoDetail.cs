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

        public override void Frame()
        {
            if (original < 0f) original = Time.maximumDeltaTime;
            bool want = Settings.B("ExplosionAuto") && ExplosionLimit_Patch.Fps() < Settings.F("ExplosionTargetFps") * 0.5f;
            if (want == limited) return;
            limited = want;
            Time.maximumDeltaTime = want ? Mathf.Min(original, 0.1f) : original;
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
            int every = load < 0.25f ? 4 : load < 0.5f ? 3 : 2;
            if (__originalMethod.Name == "UpdateNearbyGroups") step++;
            return step % every == 0;
        }
    }
}
