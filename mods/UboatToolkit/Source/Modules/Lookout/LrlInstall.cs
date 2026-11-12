// Ausguck (Teil des UBOAT Toolkit): LrlInstall. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlInstall
    {
        internal const string ModuleId = "LongRangeLookout";

        private static GameObject _host;
        private static bool _subscribed;

        private static readonly Type[] PatchClasses =
        {
            typeof(LrlSightRangePatch),
            typeof(LrlApparentHeightPatch),
            typeof(LrlAcceptancePatch),
            typeof(LrlConvoyNightBonusPatch),
            typeof(LrlKnownContactCalloutPatch),
            typeof(LrlDarkContactPatch),
            typeof(LrlCalloutContextPatch),
            typeof(LrlCalloutRetryHook),
            typeof(LrlEncounterSpawnerTag),
            typeof(LrlDetectionAuditPatch),
            typeof(LrlReportAudit),
            typeof(LrlContactNoticeAudit),
        };

        internal static void Install(Harmony harmony)
        {
            LrlSettings.Load();
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<LrlRuntime>();
                if (existing != null)
                {
                    _host = existing.gameObject;
                }
                else
                {
                    _host = new GameObject("UboatToolkitLookoutRuntime");
                    UnityEngine.Object.DontDestroyOnLoad(_host);
                    _host.AddComponent<LrlRuntime>();
                }
            }
            catch (Exception ex) { LrlLog.Error("could not start the runtime", ex); }

            int patched = 0;
            foreach (Type t in PatchClasses)
            {
                try
                {
                    List<MethodInfo> done = harmony.CreateClassProcessor(t).Patch();
                    if (done == null) continue;
                    LrlLog.Info(PatchedLine(t, done));
                    patched += done.Count;
                }
                catch (Exception ex) { LrlLog.Error("could not patch " + t.Name + " - that feature stays vanilla", ex); }
            }
            if (patched == 0) LrlLog.Warn("no methods were patched - the sight range will be vanilla");

            LrlLog.Info(string.Format(
                "geladen (v{0}) - {1}, group sight range x{2:0.000}, smoke height {3}, dark contact reports {4}",
                UboatToolkit.Modules.CrewAi.DiagLog.ManifestVersion(),
                LrlSettings.Enabled ? "enabled" : "disabled",
                LrlSettings.GroupSightRangeMultiplier,
                LrlSettings.SmokeHeightEnabled ? "on (all groups, player included)" : "off",
                LrlSettings.ReportDarkContacts ? "on" : "off"));

            if (!_subscribed)
            {
                _subscribed = true;
                SceneEventsListener.OnSceneAwake += OnScene;
                SceneEventsListener.OnSceneStart += OnScene;
            }
        }

        internal static void SetEnabled(bool on)
        {
            LrlSettings.Load();
            if (on) return;
            try { LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression); }
            catch (Exception ex) { LrlLog.Error("could not release the time compression cap on switch-off", ex); }
            LrlCalloutRetry.Clear();
        }

        internal static string PatchedLine(Type patchClass, IList<MethodInfo> done)
        {
            int n = done == null ? 0 : done.Count;
            return "patched " + patchClass.Name + " (" + n + " method" + (n == 1 ? "" : "s") + ")";
        }

        internal static bool Verbose { get { return UboatToolkit.Settings.B("LookoutLog"); } }

        private static void OnScene(Scene scene)
        {
            LrlPlayer.TryInject();
        }
    }
}
