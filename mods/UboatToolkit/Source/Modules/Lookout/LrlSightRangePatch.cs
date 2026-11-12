// Ausguck (Teil des UBOAT Toolkit): LrlSightRangePatch. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch(typeof(GroupParameterListener))]
    [HarmonyPatch("Value", MethodType.Getter)]
    internal static class LrlSightRangePatch
    {
        private const string SightParameterName = "GroupSightRange";

        private static readonly FieldInfo GroupField =
            AccessTools.Field(typeof(GroupParameterListener), "group");

        private static readonly FieldInfo ParameterNameField =
            AccessTools.Field(typeof(GroupParameterListener), "parameterName");

        private static bool _warned;

        internal static bool HasRun { get; private set; }

        private static GroupParameterListener _match;
        private static readonly HashSet<GroupParameterListener> Rejected =
            new HashSet<GroupParameterListener>();

        internal static float CachedVisibility = 1f;

        internal static void ClearCaches()
        {
            _match = null;
            Rejected.Clear();
        }

        private static void Postfix(GroupParameterListener __instance, ref float __result)
        {
            try
            {
                if (!LrlSettings.Enabled)
                    return;

                if (GroupField == null || ParameterNameField == null)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        LrlLog.Warn("GroupParameterListener field layout changed - range unmodified");
                    }
                    return;
                }

                if (!ReferenceEquals(__instance, _match))
                {
                    if (Rejected.Contains(__instance))
                        return;

                    if (!(GroupField.GetValue(__instance) is SandboxPlayerWolfpack) ||
                        !SightParameterName.Equals(ParameterNameField.GetValue(__instance) as string))
                    {
                        Rejected.Add(__instance);
                        return;
                    }

                    _match = __instance;
                }

                if (LrlHiddenFloor.Active)
                {
                    var floored = LrlHiddenFloor.MaxGroupRangeKm(LrlPlayer.Ship);
                    if (floored > __result)
                    {
                        if (LrlLog.ShouldLog("hidden-floor", 30f))
                        {
                            LrlLog.Info(string.Format(
                                "Hidden floor lifted the group range {0:0.00} -> {1:0.00} km (awash optic)",
                                __result, floored));
                        }
                        __result = floored;
                    }
                }

                __result *= LrlSettings.GroupSightRangeMultiplier;

                __result *= LrlWeatherResponse.RangeMultiplier(CachedVisibility);

                HasRun = true;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("sight range postfix failed - range left unmodified", ex);
                }
            }
        }
    }
}
