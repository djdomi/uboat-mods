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

        private GameObject host;
        private PostProcessVolume volume;
        private ColorGrading grading;
        private MainCamera cam;
        private bool failed;

        public override void Tick()
        {
            if (Time.unscaledTime - lastLook > 5f || weather == null)
            {
                lastLook = Time.unscaledTime;
                if (weather == null) weather = Object.FindObjectOfType<Weather>();
            }
            if (weather != null) Vis = Mathf.Clamp01(weather.VisualDetectabilityFactors);
            Util.Safe("Lookout.Interior", Interior);
        }

        public override void Frame()
        {
            if (volume != null && volume.weight > 0f && cam != null && cam.InteriorPostProcessVolume != null)
                volume.weight = cam.InteriorPostProcessVolume.weight;
        }

        private void Interior()
        {
            float pct = Settings.F("InteriorBright");
            if (cam == null) cam = Object.FindObjectOfType<MainCamera>();
            var inner = cam != null ? cam.InteriorPostProcessVolume : null;
            if (pct <= 0f || inner == null || failed) { if (volume != null) volume.weight = 0f; return; }
            if (volume == null)
            {
                try
                {
                    host = new GameObject("Toolkit Interior Light");
                    Object.DontDestroyOnLoad(host);
                    host.layer = inner.gameObject.layer;
                    volume = host.AddComponent<PostProcessVolume>();
                    volume.isGlobal = true;
                    volume.priority = 210f;
                    volume.sharedProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
                    grading = volume.sharedProfile.AddSettings<ColorGrading>();
                    grading.enabled.Override(true);
                }
                catch { failed = true; return; }
            }
            float baseEv = 0f;
            var prof = inner.sharedProfile;
            if (prof != null && prof.TryGetSettings(out ColorGrading cg) && cg.postExposure.overrideState) baseEv = cg.postExposure.value;
            grading.postExposure.Override(baseEv + Mathf.Log(1f + pct / 100f, 2f));
            volume.weight = inner.weight;
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

    // Brighter nights outside: makes the game's own night adaptation stronger.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.BrighterNights), "DoUpdate")]
    internal static class Lookout_NightPatch
    {
        private static void Postfix(AutoExposure ___autoExposure, float ___minExposure)
        {
            float pct = Settings.F("NightBright");
            if (pct <= 0f || ___autoExposure == null) return;
            float v = ___autoExposure.minLuminance.value;
            ___autoExposure.minLuminance.value = ___minExposure - (___minExposure - v) * (1f + pct / 100f);
        }
    }
}
