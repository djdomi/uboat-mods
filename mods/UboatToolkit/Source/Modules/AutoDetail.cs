using UnityEngine;

namespace UboatToolkit.Modules
{
    // Automatic detail (setting ExplosionAuto): at low FPS Unity runs MORE physics steps per frame to catch up
    // (FixedUpdate of the contact handling, ships, rudders ...), which makes the next frame even slower.
    // Below half the target FPS the catch-up per frame is limited (max. 0.1 s game time per frame); above, the game value is back.
    public class AutoDetail : ModuleBase
    {
        private bool limited;
        private float nextSwitch;
        private static UBOAT.Game.Core.Time.GameTime gameTime;
        private static float gameStep = -1f;   // the game's own physics step (1/75 s)
        private float nextCensus;

        // 0.3.69 physics rate (DoMe: get performance out of the engine). Measured: with 48 ships the physics took
        // 149 of 224 ms per frame. The game runs physics 75 times per second and up to 5 steps per frame.
        // "PhysicsHz" lowers the rate through the game's own GameTime.FixedDeltaTime (50 = the step the game itself
        // uses at 2x time compression). Only set at normal speed; time compression keeps the game's own logic.
        public override void Tick()
        {
            if (gameTime == null) gameTime = Object.FindObjectOfType<UBOAT.Game.Core.Time.GameTime>();
            if (gameTime == null) return;
            if (gameStep < 0f) gameStep = gameTime.FixedDeltaTime;
            float hz = Settings.F("PhysicsHz");
            float want = hz >= 20f ? Mathf.Max(gameStep, 1f / hz) : gameStep;
            // time compression: back to the game's own step (the game multiplies it there itself); pause: leave alone
            if (Time.timeScale < 0.99f) return;
            if (Time.timeScale > 1.01f) want = gameStep;
            if (Mathf.Abs(gameTime.FixedDeltaTime - want) > 0.00001f && !SaveTools_LoadSpeedPatch.IsLoading)
            {
                gameTime.FixedDeltaTime = want;
                Util.Log("Physik-Takt: " + (1f / want).ToString("0") + " pro Sekunde (Spiel: " + (1f / gameStep).ToString("0") + ")");
            }
            // Census for the next tuning step: what is in the physics scene when it is slow (max. every 2 min).
            float f = ExplosionLimit_Patch.Fps();
            if (f < Settings.F("ExplosionTargetFps") * 0.5f && Time.unscaledTime >= nextCensus && Time.timeScale > 0f)
            {
                nextCensus = Time.unscaledTime + 120f;
                Util.Safe("Physik-Zählung", () => Census(f));
            }
        }

        private static void Census(float fps)
        {
            int rb = 0, dyn = 0, awake = 0, col = 0, mesh = 0, concave = 0, trig = 0;
            foreach (var r in Object.FindObjectsOfType<Rigidbody>()) { rb++; if (!r.isKinematic) { dyn++; if (!r.IsSleeping()) awake++; } }
            foreach (var c in Object.FindObjectsOfType<Collider>())
            {
                col++; if (c.isTrigger) trig++;
                var m = c as MeshCollider; if (m != null) { mesh++; if (!m.convex) concave++; }
            }
            int ships = Object.FindObjectsOfType<UBOAT.Game.Scene.Entities.Ship>().Length;
            Util.Log("Physik-Zählung: FPS " + fps.ToString("0") + ", Schiffe " + ships + ", Körper " + rb + " (beweglich " + dyn + ", wach " + awake + "), Kollider " + col
                + " (Auslöser " + trig + ", Netz " + mesh + ", davon hohl " + concave + "), Takt " + (1f / Time.fixedDeltaTime).ToString("0") + "/s, max. Schritte/Bild "
                + (Time.maximumDeltaTime / Time.fixedDeltaTime).ToString("0.#") + ", Löser " + Physics.defaultSolverIterations + "/" + Physics.defaultSolverVelocityIterations
                + ", autoSync " + Physics.autoSyncTransforms + ", Zeitraffer " + Time.timeScale.ToString("0.#"));
        }

        public override void Frame()
        {
            // 0.3.69: the old limit (0.1 s) was ABOVE the game's own value (5 steps = 0.067 s), so it never did anything.
            // Now: while limited, at most "PhysicsMaxSteps" physics steps per frame (the game re-sets its value on
            // every speed change, so this is checked every frame).
            if (limited)
            {
                float cap = Time.fixedDeltaTime * Mathf.Clamp(Settings.F("PhysicsMaxSteps"), 1f, 5f);
                if (Time.maximumDeltaTime > cap + 0.00001f) Time.maximumDeltaTime = cap;
            }
            // Hysteresis: limit below 50 % of target FPS, release only above 70 %, and switch at most every 10 s.
            float f = ExplosionLimit_Patch.Fps(), target = Settings.F("ExplosionTargetFps");
            bool want = Settings.B("ExplosionAuto") && (limited ? f < target * 0.7f : f < target * 0.5f);
            if (want == limited || Time.unscaledTime < nextSwitch) return;
            nextSwitch = Time.unscaledTime + 10f;
            limited = want;
            // release: the game's own setter recalculates step + catch-up limit for the current speed
            if (!want && gameTime != null) gameTime.FixedDeltaTime = gameTime.FixedDeltaTime;
            ToolkitRunner.ShowNotice(want ? "Auto-Detail: viel los (" + Mathf.RoundToInt(f) + " FPS) - Effekte, Stimmen + Physik werden gespart" : "Auto-Detail: wieder volle Details", 4f);
            Util.Log("Auto-Detail: Physik-Aufholen " + (want ? "begrenzt (" + Settings.F("PhysicsMaxSteps").ToString("0") + " Schritte pro Bild)" : "normal (5 Schritte)"));
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
