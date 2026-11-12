// Ausguck (Teil des UBOAT Toolkit): LrlDarkContactPatch. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch]
    internal static class LrlDarkContactPatch
    {
        private const int Masts = 3;
        private const int Ships = 7;
        private const int Ship = 8;

        private const int IndirectObservation = 4;

        private static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly FieldInfo DarknessField =
            AccessTools.Field(typeof(GroupObservation), "wasSpottedInDarkness");

        private static bool _warned;

        internal static int Rescued { get; private set; }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");
        }

        private static void Prefix(GroupObservation observation, out object __state)
        {
            __state = null;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.ReportDarkContacts)
                    return;
                if (observation == null || PerceivedField == null || DarknessField == null)
                    return;

                if (((int)observation.DetectionMethod & IndirectObservation) == 0)
                    return;

                if (!(bool)DarknessField.GetValue(observation))
                    return;

                var current = (int)observation.PerceivedContactType;
                if (current != Ships && current != Ship)
                    return;

                __state = PerceivedField.GetValue(observation);
                PerceivedField.SetValue(observation, Enum.ToObject(PerceivedField.FieldType, Masts));

                Rescued++;
                LrlLog.Throttled("dark-contact", 30f,
                    "dark contact would have been reported silently (PerceivedContactType=" +
                    (current == Ships ? "Ships" : "Ship") +
                    ", no voice line exists) - speaking it as Masts instead");
            }
            catch (Exception ex)
            {
                __state = null;
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("dark-contact prefix failed - dark contacts stay silent", ex);
                }
            }
        }

        private static void Finalizer(GroupObservation observation, object __state)
        {
            if (__state == null || observation == null || PerceivedField == null)
                return;

            try
            {
                PerceivedField.SetValue(observation, __state);
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("dark-contact finalizer failed to restore the contact type", ex);
                }
            }
        }
    }
}
