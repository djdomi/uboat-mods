using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Shell explosions: the game spawns one explosion effect per hit (Shell.Explode). Here the effect is made smaller
    // and at most x per second are spawned. Damage is computed before and is not changed.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.Shell), "Explode")]
    internal static class ExplosionLimit_Patch
    {
        private static readonly MethodInfo Inst = AccessTools.FirstMethod(typeof(Object), m => m.Name == "Instantiate" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)?.MakeGenericMethod(typeof(GameObject));
        private static readonly MethodInfo Mine = AccessTools.Method(typeof(ExplosionLimit_Patch), nameof(Spawn));
        private static float second; private static int count; private static int skipped; private static float nextLog;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            int n = 0;
            foreach (var ci in code)
            {
                if (Inst != null && ci.Calls(Inst)) { n++; yield return new CodeInstruction(OpCodes.Call, Mine); continue; }
                yield return ci;
            }
            Util.Log("Explosionen: " + n + " Stelle(n) umgeleitet");
        }

        // Smoothed frames per second (real time).
        private static float fps = 60f, lastT;
        internal static float Fps()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (t != lastT && dt > 0f) { lastT = t; fps = Mathf.Lerp(fps, 1f / dt, 0.2f); }
            return fps;
        }

        private static GameObject Spawn(GameObject prefab)
        {
            if (!Settings.B("ExplosionLimit")) return Object.Instantiate(prefab);
            float now = Time.unscaledTime;
            float scale = Mathf.Clamp(Settings.F("ExplosionScale"), 0.1f, 1f);
            int max = Mathf.Max(1, Mathf.RoundToInt(Settings.F("ExplosionMaxPerSec")));
            if (Settings.B("ExplosionAuto"))
            {
                // Load-based detail: at target FPS or better full effects; below, scale and count go down with the FPS.
                float load = Mathf.Clamp01(Fps() / Mathf.Max(1f, Settings.F("ExplosionTargetFps")));
                scale = Mathf.Lerp(scale, 1f, load);
                max = load >= 0.999f ? 1000 : Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, max * 3f, load)));
            }
            if (now - second >= 1f) { second = now; count = 0; }
            if (++count > max)
            {
                skipped++;
                if (now > nextLog) { nextLog = now + 60f; Util.Log("Explosionen begrenzt: " + skipped + " Effekte ausgelassen (gesamt)"); }
                var dummy = new GameObject("SkippedExplosion");
                Object.Destroy(dummy, 0.1f);
                return dummy;
            }
            var go = Object.Instantiate(prefab);
            float s = scale;
            if (s < 0.999f)
            {
                go.transform.localScale = prefab.transform.localScale * s;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }
            return go;
        }
    }
}
