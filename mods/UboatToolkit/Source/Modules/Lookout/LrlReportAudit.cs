// Ausguck (Teil des UBOAT Toolkit): LrlReportAudit. Schalter: F10 > QoL > "Ausguck".
using System;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch(typeof(PlayerShip), "DetectionReportInternal")]
    internal static class LrlReportAudit
    {
        private static bool _warned;

        private static void Postfix(PlayerShip __instance, string statementId1,
            GroupDetectionMethod observationType, bool __result)
        {
            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || LrlCalloutRetry.Retrying)
                    return;
                if (statementId1 == null || !statementId1.StartsWith("Group Contact"))
                    return;

                if (__result)
                {
                    LrlLog.Info("callout VOICED: \"" + statementId1 + "\" (method " + (int)observationType + ")");
                }
                else
                {
                    LrlLog.Info("callout DROPPED: \"" + statementId1 + "\" (method " + (int)observationType +
                                ") - " + LrlCrewStations.Summarise(__instance, statementId1) +
                                (LrlCalloutRetry.Active
                                    ? ". Queued for retry."
                                    : ". Retry is off, so it is lost: the contact now exists, and a later sighting adds nothing that speaks."));
                }
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("callout audit failed - disabled for this session", ex);
                }
            }
        }
    }
}
