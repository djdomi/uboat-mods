// Ausguck (Teil des UBOAT Toolkit): LrlKnownContactCalloutPatch. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch(typeof(SandboxGroup), "AddObservation")]
    internal static class LrlKnownContactCalloutPatch
    {
        private const int DirectObservation = 2;
        private const int IndirectObservation = 4;

        private const int FunnelSmoke = 2;
        private const int Masts = 3;
        private const int Contrail = 4;

        private static readonly MethodInfo SayReport =
            AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");

        private static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly FieldInfo DarknessField =
            AccessTools.Field(typeof(GroupObservation), "wasSpottedInDarkness");

        private static bool _warned;
        private static bool _speaking;

        private static void Prefix(SandboxGroup __instance, SandboxGroup group,
            GroupDetectionMethod detectionMethod, out GroupObservation __state)
        {
            __state = null;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.CalloutKnownContacts || _speaking)
                    return;
                if (!(__instance is SandboxPlayerWolfpack) || group == null)
                    return;
                if (((int)detectionMethod & IndirectObservation) == 0)
                    return;

                var existing = __instance.GetObservation(group);
                if (existing == null)
                    return;   // brand new: vanilla calls this one out itself

                if (((int)existing.DetectionMethod & (DirectObservation | IndirectObservation)) != 0)
                    return;   // already seen by eye

                __state = existing;
            }
            catch (Exception ex)
            {
                __state = null;
                Warn("prefix", ex);
            }
        }

        private static void Postfix(SandboxGroup __instance, SandboxGroup group,
            GroupObservation __result, GroupObservation __state)
        {
            if (__state == null || _speaking || !ReferenceEquals(__result, __state))
                return;
            if (((int)__state.DetectionMethod & IndirectObservation) == 0)
                return;
            if (SayReport == null || PerceivedField == null)
                return;

            object original = null;
            var swapped = false;

            try
            {
                var type = LookoutContactType(group, __state);
                if (type < 0)
                    return;

                original = PerceivedField.GetValue(__state);
                var forced = Enum.ToObject(PerceivedField.FieldType, type);
                PerceivedField.SetValue(__state, forced);
                swapped = true;

                _speaking = true;
                SayReport.Invoke(null, new object[] { __instance, __state });

                LrlLog.Info("known contact first seen by lookouts - calling it out as " + forced +
                            " (it was previously known only as " + original + ")");
            }
            catch (Exception ex)
            {
                Warn("postfix", ex);
            }
            finally
            {
                _speaking = false;
                if (swapped)
                {
                    try { PerceivedField.SetValue(__state, original); }
                    catch (Exception ex) { Warn("restore", ex); }
                }
            }
        }

        private static int LookoutContactType(SandboxGroup group, GroupObservation observation)
        {
            if (group == null)
                return -1;
            if (group.IsAirborne)
                return Contrail;

            var mobile = group as SandboxMobileGroup;
            if (mobile == null || group.Type == "Wreck")
                return -1;

            var dark = DarknessField != null && (bool)DarknessField.GetValue(observation);
            if (dark)
                return Masts;   // a silhouette, as LrlDarkContactPatch does

            return mobile.Velocity > 0f ? FunnelSmoke : Masts;
        }

        private static void Warn(string where, Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            LrlLog.Error("known-contact callout " + where + " failed - callouts for HQ-known ships off this session", ex);
        }
    }
}
