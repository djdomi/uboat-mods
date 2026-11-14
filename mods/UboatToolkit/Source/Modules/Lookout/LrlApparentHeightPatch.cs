// Ausguck (Teil des UBOAT Toolkit): LrlApparentHeightPatch. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch]
    internal static class LrlApparentHeightPatch
    {
        private static bool _warned;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SandboxGroup), "GetApparentHeight");
            yield return AccessTools.Method(typeof(SandboxMobileGroup), "GetApparentHeight");
        }

        internal static bool HasRun { get; private set; }

        private static void Postfix(SandboxGroup __instance, ref float __result)
        {
            try
            {
                // 0.3.37 (DoMe): only helps the player see others; our own group never gets taller for enemies
                if (!LrlSettings.Enabled || !LrlSettings.SmokeHeightEnabled || __instance is SandboxPlayerWolfpack)
                    return;

                if (float.IsInfinity(__result))
                {
                    if (LrlLog.ShouldLog("apparent-infinite", 300f))
                    {
                        LrlLog.Info(
                            "vanilla GetApparentHeight returned " +
                            (__result > 0f ? "+Infinity (Velocity > 0" : "-Infinity (Velocity < 0") +
                            ", MaxVelocity 0) - left untouched; occlusion resolves to 0 either way");
                    }
                    return;
                }

                var smoke = LrlSmokeModel.GetSmokeHeight(__instance);

                if (float.IsNaN(__result))
                {
                    var mast = __instance.GetMaximumMastHeight();
                    if (float.IsNaN(mast) || float.IsInfinity(mast) || mast <= 0f)
                        return;

                    __result = mast + smoke;
                    HasRun = true;

                    if (LrlLog.ShouldLog("apparent-nan", 60f))
                    {
                        LrlLog.Info(string.Format(
                            "vanilla GetApparentHeight returned NaN (velocity and MaxVelocity both 0) - " +
                            "substituted mast {0:0.0}m + smoke {1:0.0}m; without this the group is undetectable",
                            mast, smoke));
                    }
                    return;
                }

                if (smoke <= 0f)
                    return;

                __result += smoke;
                HasRun = true;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("apparent-height postfix failed - smoke height disabled for this session", ex);
                }
            }
        }
    }
}
