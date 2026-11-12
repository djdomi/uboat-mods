using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Environment;

namespace UboatToolkit.Modules
{
    // Lookouts see ships further away (less in bad weather), smoke makes moving ships visible earlier,
    // stopped ships no longer become "invisible". Plus brighter nights outside and a brighter boat interior.
    public class Lookout : ModuleBase
    {
        internal static float Vis = 1f;
        private static Weather weather;
        private static float lastLook = -100f;

        public override void Tick()
        {
            if (Time.unscaledTime - lastLook > 5f || weather == null)
            {
                lastLook = Time.unscaledTime;
                if (weather == null) weather = Object.FindObjectOfType<Weather>();
            }
            if (weather != null) Vis = Mathf.Clamp01(weather.VisualDetectabilityFactors);
        }

    }

    // Wolfpack (= own boat) sight range for ships.
    [HarmonyPatch(typeof(GroupParameterListener), "Value", MethodType.Getter)]
    internal static class Lookout_RangePatch
    {
        internal static float Factor()
        {
            float m = Settings.F("LookoutRange");
            return m <= 1f ? 1f : 1f + (m - 1f) * Lookout.Vis;
        }
        internal static bool Mine(SandboxGroup g, string n) => g is SandboxPlayerWolfpack && n == "GroupSightRange";
        private static void Postfix(SandboxGroup ___group, string ___parameterName, ref float __result)
        {
            if (Mine(___group, ___parameterName)) __result *= Factor();
        }
    }
    [HarmonyPatch(typeof(GroupParameterListener), "ValueSquared", MethodType.Getter)]
    internal static class Lookout_RangeSqPatch
    {
        private static void Postfix(SandboxGroup ___group, string ___parameterName, ref float __result)
        {
            if (Lookout_RangePatch.Mine(___group, ___parameterName)) { float f = Lookout_RangePatch.Factor(); __result *= f * f; }
        }
    }

    // Smoke above moving ships; stopped ships keep their mast height (game gives "not a number" there).
    [HarmonyPatch(typeof(SandboxMobileGroup), "GetApparentHeight")]
    internal static class Lookout_HeightPatch
    {
        private static void Postfix(SandboxMobileGroup __instance, ref float __result)
        {
            if (!Settings.B("LookoutSmoke") || __instance.IsAirborne) return;
            if (float.IsNaN(__result) || float.IsInfinity(__result))
            {
                float mast = __instance.GetMaximumMastHeight();
                if (!float.IsNaN(mast) && mast > 0f && !float.IsInfinity(mast)) __result = mast;
                return;
            }
            if (__instance.MaxVelocity > 0f && __instance.Velocity > 0f)
                __result += 12f * Mathf.Clamp01(__instance.Velocity / __instance.MaxVelocity);
        }
    }
}
