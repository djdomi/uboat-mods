// Ausguck (Teil des UBOAT Toolkit): LrlDetectionAudit. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlDetectionAudit
    {
        internal const int IndirectObservation = 4;
        internal const int DirectObservation = 2;
        internal const int Hydrophone = 8;

        internal static bool InSpawner;
        internal static bool SpawnerVisual;

        private static bool _warned;

        internal static void Warn(string where, Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            LrlLog.Error("detection audit " + where + " failed - disabled for this session", ex);
        }

        internal static bool Disabled
        {
            get { return _warned; }
        }

        internal static void Report(SandboxPlayerWolfpack wolfpack, SandboxGroup group,
            int methodsBefore, int methodsAfter)
        {
            var delta = group.Position - wolfpack.Position;
            var distKm = delta.magnitude;
            var bearing = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
            if (bearing < 0f)
                bearing += 360f;

            string source;
            if (InSpawner)
                source = SpawnerVisual ? "spawner(visual)" : "spawner(hydrophone)";
            else
                source = "search";

            var weather = LrlPlayer.InjectedWeather;
            var vis = weather != null ? weather.VisualDetectabilityFactors : float.NaN;
            var searchKm = float.NaN;
            if (!float.IsNaN(vis) && LrlRuntime.GroupIsReady(wolfpack))
                searchKm = wolfpack.SightRange * vis * vis;

            var det = LrlRuntime.TargetDetectability(group);
            var apparent = group.GetApparentHeight();
            var occl = LrlRuntime.OcclusionFactor(distKm * 1000f, wolfpack.GetObserverHorizonDistance(), apparent);

            var bonus = LrlConvoyNightBonus.Factor(group);

            var reachKm = float.NaN;
            if (!float.IsNaN(searchKm) && !float.IsNaN(det) && !float.IsNaN(occl))
                reachKm = searchKm * bonus * det * Mathf.Sqrt(Mathf.Max(0f, 1f - occl));

            var nearKm = float.NaN;
            var ship = LrlPlayer.Ship;
            if (ship != null && !float.IsNaN(vis))
            {
                var baseKm = (float)ship.DirectSightRange * 0.001f * vis;
                nearKm = baseKm + group.GetEstimatedOccupiedRadius() * 0.5f * Mathf.InverseLerp(0f, 0.3f, baseKm);
            }

            string verdict;
            if (InSpawner) verdict = "placed by the spawner, no sighting test";
            else if (!float.IsNaN(reachKm) && distKm <= reachKm * 1.02f) verdict = "within reach";
            else if (!float.IsNaN(nearKm) && distKm <= nearKm * 1.02f) verdict = "within close-range sight";
            else if (float.IsNaN(reachKm)) verdict = "reach unknown";
            else verdict = "BEYOND REACH";

            LrlLog.Info(string.Format(
                "new lookout observation: {0} dist={1:0.0}km bearing={2:000} source={3} obs={4}->{5}  " +
                "vis={6:0.00} searchRadius={7:0.0}km det={8:0.00} apparent={9:0.0}m occl={10:0.00} reach={11:0.0}km " +
                "closeSight={12:0.0}km convoyNightBonus=x{14:0.00} -> {13}",
                group.name, distKm, bearing, source,
                LrlRuntime.DescribeMethods(methodsBefore), LrlRuntime.DescribeMethods(methodsAfter),
                vis, searchKm, det, apparent, occl, reachKm, nearKm, verdict, bonus));
        }
    }

    [HarmonyPatch]
    internal static class LrlEncounterSpawnerTag
    {
        private static MethodBase _target;

        private static bool Prepare()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Tasks.AdditionalEncountersController");
            _target = t != null ? AccessTools.Method(t, "TrySpawnGroup", new[] { typeof(bool) }) : null;
            if (_target == null && LrlSettings.DebugLogging)
                LrlLog.Warn("AdditionalEncountersController.TrySpawnGroup not found - spawned contacts will read as source=search");
            return _target != null;
        }

        private static MethodBase TargetMethod()
        {
            return _target;
        }

        private static void Prefix(bool visualContact)
        {
            LrlDetectionAudit.InSpawner = true;
            LrlDetectionAudit.SpawnerVisual = visualContact;
        }

        private static void Finalizer()
        {
            LrlDetectionAudit.InSpawner = false;
        }
    }

    [HarmonyPatch(typeof(SandboxGroup), "AddObservation")]
    internal static class LrlDetectionAuditPatch
    {
        private static void Prefix(SandboxGroup __instance, SandboxGroup group,
            GroupDetectionMethod detectionMethod, out int __state)
        {
            __state = -1;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || LrlDetectionAudit.Disabled)
                    return;
                if (!(__instance is SandboxPlayerWolfpack) || group == null)
                    return;

                var adding = (int)detectionMethod;
                if (!LrlDetectionAudit.InSpawner && (adding & LrlDetectionAudit.IndirectObservation) == 0)
                    return;

                var existing = __instance.GetObservation(group);
                var before = existing == null ? 0 : (int)existing.DetectionMethod;

                if (!LrlDetectionAudit.InSpawner &&
                    (before & (LrlDetectionAudit.IndirectObservation | LrlDetectionAudit.DirectObservation)) != 0)
                    return;

                __state = before;
            }
            catch (Exception ex)
            {
                __state = -1;
                LrlDetectionAudit.Warn("prefix", ex);
            }
        }

        private static void Postfix(SandboxGroup __instance, SandboxGroup group,
            GroupObservation __result, int __state)
        {
            if (__state < 0 || __result == null)
                return;

            try
            {
                LrlDetectionAudit.Report((SandboxPlayerWolfpack)__instance, group,
                    __state, (int)__result.DetectionMethod);
            }
            catch (Exception ex)
            {
                LrlDetectionAudit.Warn("postfix", ex);
            }
        }
    }
}
