// Ausguck (Teil des UBOAT Toolkit): LrlRuntime. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Text;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    [NonSerializedInGameState]
    internal class LrlRuntime : MonoBehaviour
    {
        private const float ScrubInterval = 5.1f;

        private static readonly List<ObservationSpot> Spots = new List<ObservationSpot>();

        private float _nextScrub;
        private float _nextDebug;
        private int _lastRemovedCount;
        private bool _announcedPatchLive;
        private bool _announcedSmokeLive;
        private float _nextCacheClear;

        private void Update()
        {
            var now = Time.unscaledTime;

            if (!LrlSettings.Enabled)
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            LrlWeatherResponse.RefreshLive(LrlPlayer.InjectedWeather);

            RunTimeCompressionGuard();

            LrlCalloutRetry.Tick(now);

            if (now >= _nextScrub)
            {
                _nextScrub = now + ScrubInterval;
                RunScrub();
            }

            if (LrlSettings.DebugLogging && LrlInstall.Verbose && now >= _nextDebug)
            {
                _nextDebug = now + Mathf.Max(1f, LrlSettings.DebugInterval);
                RunDiagnostics();
            }
        }

        private SandboxPlayerWolfpack _wolfpack;
        private float _nextWolfpackScan;
        private float _cachedSearchRadiusKm;
        private float _cachedMaxProviderKm;
        private SandboxGroup _repairedGroup;

        internal static SandboxPlayerWolfpack CurrentWolfpack;

        private static float GuardSearchRadiusKm(float maxProviderKm)
        {
            var v = LrlSightRangePatch.CachedVisibility;
            var comp = LrlWeatherResponse.RangeMultiplier(v);
            return maxProviderKm * LrlSettings.GroupSightRangeMultiplier * comp * v * v;
        }

        internal static bool GroupIsReady(SandboxGroup group)
        {
            if (group == null)
                return false;

            var entities = group.Entities;
            if (entities == null)
                return false;

            for (var i = 0; i < entities.Count; i++)
            {
                if (entities[i] != null && entities[i].Enabled)
                    return true;
            }

            return false;
        }

        private static bool RevalidateSightListener(SandboxGroup group)
        {
            if (GroupSightListenerField == null || group == null)
                return false;

            var listener = GroupSightListenerField.GetValue(group);
            if (listener == null)
                return false;

            var validate = HarmonyLib.AccessTools.Method(listener.GetType(), "ValidateEntities");
            if (validate == null)
                return false;

            validate.Invoke(listener, null);
            return true;
        }

        private void RepairSightListener(SandboxGroup group)
        {
            if (_repairedGroup == group)
                return;

            _repairedGroup = group;

            try
            {
                if (GroupSightListenerField == null)
                    return;

                var listener = GroupSightListenerField.GetValue(group);
                if (listener == null)
                    return;

                var validate = HarmonyLib.AccessTools.Method(listener.GetType(), "ValidateEntities");
                if (validate == null)
                    return;

                validate.Invoke(listener, null);
                LrlLog.Info("sight listener revalidated for the current player group");
            }
            catch (Exception ex)
            {
                LrlLog.Error("sight listener repair failed", ex);
            }
        }

        private static float MaxProviderGroupRangeKm(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            if (ship == null)
                return 0f;

            LrlProviders.GetAll(ship, ProviderBuffer);

            var best = 0f;
            for (var i = 0; i < ProviderBuffer.Count; i++)
            {
                var p = ProviderBuffer[i].GroupDetectionRange;
                if (p != null && p.Value > best)
                    best = p.Value;
            }

            ProviderBuffer.Clear();
            return best;
        }

        private static readonly List<ObservationSpot> ProviderBuffer = new List<ObservationSpot>();

        private static string DiagnoseBlindness(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            if (ship == null)
                return "no player ship";

            LrlProviders.GetAll(ship, ProviderBuffer);

            var manned = 0;
            var mannedButHidden = 0;
            var withRange = 0;

            for (var i = 0; i < ProviderBuffer.Count; i++)
            {
                var spot = ProviderBuffer[i];
                var perf = spot.Performance;
                var group = spot.GroupDetectionRange;

                if (perf == null || perf.Value <= 0.001f)
                    continue;

                manned++;

                if (group == null)
                    continue;

                if (group.Value > 0.001f)
                    withRange++;
                else
                    mannedButHidden++;
            }

            ProviderBuffer.Clear();

            if (withRange > 0)
                return "listener latched - " + withRange + " provider(s) have range but the group does not";
            if (mannedButHidden > 0)
                return "optics submerged - " + mannedButHidden + " manned provider(s) with Hidden=0 (below periscope depth)";
            if (manned == 0)
                return "no manned observation provider";

            return "providers manned but contributing nothing";
        }

        private void RunTimeCompressionGuard()
        {
            if (!LrlSettings.TimeCompressionGuard)
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            if (_wolfpack == null && Time.unscaledTime >= _nextWolfpackScan)
            {
                _nextWolfpackScan = Time.unscaledTime + 2f;
                _wolfpack = UnityEngine.Object.FindObjectOfType<SandboxPlayerWolfpack>();
                _cachedSearchRadiusKm = 0f;
                _cachedMaxProviderKm = 0f;
            }

            CurrentWolfpack = _wolfpack;

            if (!GroupIsReady(_wolfpack))
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            RepairSightListener(_wolfpack);

            if (_cachedMaxProviderKm > 0f)
                _cachedSearchRadiusKm = GuardSearchRadiusKm(_cachedMaxProviderKm);

            LrlTimeCompressionGuard.Evaluate(_wolfpack, _cachedSearchRadiusKm);
        }

        private void RunScrub()
        {
            try
            {
                LrlOrphanScrub.Sweep(LrlPlayer.Ship);

                if (LrlOrphanScrub.RemovedCount != _lastRemovedCount)
                {
                    _lastRemovedCount = LrlOrphanScrub.RemovedCount;
                    LrlLog.Info(string.Format(
                        "scrub: {0} legacy modifier(s) removed so far this session", _lastRemovedCount));
                }

                if (!_announcedPatchLive && LrlSightRangePatch.HasRun)
                {
                    _announcedPatchLive = true;
                    LrlLog.Info(string.Format(
                        "group sight range patch is live - player group range x{0:0.000}",
                        LrlSettings.GroupSightRangeMultiplier));
                }

                if (!_announcedSmokeLive && LrlApparentHeightPatch.HasRun)
                {
                    _announcedSmokeLive = true;
                    LrlLog.Info("smoke-height patch is live - apparent height is being raised");
                }

                var w = LrlPlayer.Weather;
                if (w != null)
                    LrlSightRangePatch.CachedVisibility = w.VisualDetectabilityFactors;

                LrlWeatherResponse.Refresh(w);

                LrlConvoyNightBonus.RefreshPool(GroupIsReady(_wolfpack) ? _wolfpack : null);

                var maxProvider = LrlHiddenFloor.MaxGroupRangeKm(LrlPlayer.Ship);
                _cachedMaxProviderKm = maxProvider;
                if (maxProvider > 0f)
                {
                    _cachedSearchRadiusKm = GuardSearchRadiusKm(maxProvider);

                    if (GroupIsReady(_wolfpack) && _wolfpack.SightRange <= 0f)
                    {
                        if (RevalidateSightListener(_wolfpack))
                        {
                            LrlLog.Throttled("sight-relatch", 10f, string.Format(
                                "sight listener latched at 0 while providers read {0:0.00}km - revalidated, now {1:0.00}km",
                                maxProvider, _wolfpack.SightRange));
                        }
                    }
                }

                if (Time.unscaledTime >= _nextCacheClear)
                {
                    _nextCacheClear = Time.unscaledTime + 60f;
                    LrlSmokeModel.ClearCache();
                    LrlSightRangePatch.ClearCaches();
                }
            }
            catch (Exception ex)
            {
                LrlLog.Error("scrub pass failed", ex);
            }
        }

        private void RunDiagnostics()
        {
            try
            {
                var ship = LrlPlayer.Ship;
                if (ship == null)
                {
                    LrlLog.Throttled("diag-no-ship", 60f, "no player ship yet - diagnostics idle");
                    return;
                }

                var weather = LrlPlayer.Weather;
                var visibility = weather != null ? weather.VisualDetectabilityFactors : 1f;

                LrlWeatherDiagnostics.Log(weather);

                LrlProviders.GetAll(ship, Spots);

                if (Spots.Count == 0)
                {
                    LrlLog.Throttled("diag-no-spots", 60f, "player ship has no ObservationSpot equipment");
                    return;
                }

                var sb = DiagBuilder;
                sb.Length = 0;
                sb.Append("diagnostics  visibility=").Append(visibility.ToString("0.00"));
                sb.Append("  visibility^2=").Append((visibility * visibility).ToString("0.00"));

                var visEff = LrlWeatherResponse.EffectiveVisibility(visibility);
                if (visEff > visibility)
                    sb.Append("  [floored to ").Append(visEff.ToString("0.00"))
                      .Append(", x").Append(LrlWeatherResponse.RangeMultiplier(visibility).ToString("0.0"))
                      .Append(" on range]");

                var trimWeight = LrlDaylightTrim.Weight(visibility);
                if (trimWeight > 0f)
                    sb.Append("  [daylight trim ").Append((trimWeight * 100f).ToString("0"))
                      .Append("%: range x").Append(LrlDaylightTrim.RangeFactor(visibility).ToString("0.000"))
                      .Append(", smoke x").Append(LrlDaylightTrim.SmokeFactor(visibility).ToString("0.00"))
                      .Append("]");

                var wolfpack = UnityEngine.Object.FindObjectOfType<SandboxPlayerWolfpack>();

                if (wolfpack != null && !GroupIsReady(wolfpack))
                {
                    sb.Append("  resolvedGroupRange=<group not populated yet>");
                    wolfpack = null;
                }

                if (wolfpack != null)
                {
                    sb.Append("  resolvedGroupRange=").Append(wolfpack.SightRange.ToString("0.00")).Append("km");
                    sb.Append("  searchRadius=")
                      .Append((wolfpack.SightRange * visibility * visibility).ToString("0.00")).Append("km");
                    sb.Append("  observerHorizon=")
                      .Append((wolfpack.GetObserverHorizonDistance() / 1000f).ToString("0.00")).Append("km");

                    if (wolfpack.SightRange <= 0.01f)
                        sb.Append("  [BLIND: ").Append(DiagnoseBlindness(ship)).Append(']');

                    sb.Append("  playerStation=").Append(LrlPlayerStation.Describe());

                    if (!LrlPlayerStation.NamedAStation)
                    {
                        var hint = LrlPlayerStation.CameraProximityHint(ship);
                        if (!string.IsNullOrEmpty(hint))
                            sb.Append("  ").Append(hint);
                    }

                    LrlLog.Info(sb.ToString());
                    sb.Length = 0;

                    LogNearbyTargets(wolfpack, wolfpack.SightRange * visibility * visibility);
                    LrlLog.Info("  " + LrlAcceptancePatch.Report());
                    var convoyBonus = LrlConvoyNightBonus.Report();
                    if (convoyBonus != null)
                        LrlLog.Info("  " + convoyBonus);

                    var guardBand = wolfpack.SightRange * visibility * visibility * LrlSettings.GuardRadiusFactor;
                    LrlStepAudit.CrossCheckCoverage(wolfpack, guardBand);
                    LrlLog.Info("  " + LrlStepAudit.Describe());

                    LogSightListener(wolfpack);

                    string tier;
                    var smoke = LrlSmokeModel.GetSmokeHeight(wolfpack, out tier);
                    var mast = wolfpack.GetMaximumMastHeight();
                    var apparent = wolfpack.GetApparentHeight();

                    sb.Append("  own signature: mast=").Append(mast.ToString("0.0")).Append("m");
                    sb.Append(" smoke=").Append(smoke.ToString("0.0")).Append("m(").Append(tier).Append(')');
                    sb.Append(" apparent=").Append(apparent.ToString("0.0")).Append("m");
                    sb.Append(" maxSeenFrom=")
                      .Append(((wolfpack.GetObserverHorizonDistance() + Mathf.Sqrt(2f * 6371000f * apparent)) / 1000f)
                          .ToString("0.0")).Append("km");
                }
                else
                {
                    sb.Append("  resolvedGroupRange=<no sandbox wolfpack>");
                }

                LrlLog.Info(sb.ToString());

                for (var i = 0; i < Spots.Count; i++)
                {
                    var spot = Spots[i];

                    var groupRange = spot.GroupDetectionRange;
                    var directRange = spot.DetectionRange;
                    var performance = spot.Performance;

                    LrlLog.Info(string.Format(
                        "  provider={0} type={1} performance={2} groupRange={3}km directRange={4}m playerHere={5}",
                        spot.Name,
                        spot.GetType().Name,
                        performance != null ? performance.Value.ToString("0.000") : "n/a",
                        groupRange != null ? groupRange.Value.ToString("0.00") : "n/a",
                        directRange != null ? directRange.Value.ToString("0") : "n/a",
                        LrlPlayerStation.IsAt(spot) ? "YES" : "no"));

                    LrlLog.Info("    crew   " + LrlCrewStations.Describe(spot));

                    LrlLog.Info("    group  " + DescribeModifiers(groupRange));
                    LrlLog.Info("    direct " + DescribeModifiers(directRange));
                }

                Spots.Clear();
            }
            catch (Exception ex)
            {
                LrlLog.Error("diagnostic pass failed", ex);
            }
        }

        private const float EarthRadius = 6371000f;

        internal static float OcclusionFactor(float distM, float observerHorizonM, float apparentM)
        {
            if (float.IsNaN(apparentM) || float.IsInfinity(apparentM) || apparentM <= 0f)
                return float.NaN;

            var beyond = distM - observerHorizonM;
            if (beyond <= 0f)
                return 0f;

            var hidden = Mathf.Sqrt(beyond * beyond + EarthRadius * EarthRadius) - EarthRadius;
            return Mathf.Clamp01(hidden / apparentM);
        }

        internal static float TargetDetectability(SandboxGroup g)
        {
            if (g == null || !GroupIsReady(g))
                return float.NaN;

            if (g.IsGroupPublic)
                return 0f;

            var listener = g.VisualDetectability;
            if (listener == null)
                return float.NaN;

            return listener.Value * g.NpcDetectionModifier;
        }

        private static readonly string[] ListenerFieldNames =
        {
            "parameterName", "mode", "max",
            "parametersListIsValid", "resolvedValueIsValid", "resolvedValue"
        };

        private const int MaxTargetsLogged = 12;

        private static readonly StringBuilder MethodsBuilder = new StringBuilder();
        private static readonly StringBuilder ModifiersBuilder = new StringBuilder();
        private static readonly StringBuilder ListenerBuilder = new StringBuilder();
        private static readonly StringBuilder DiagBuilder = new StringBuilder();

        internal static string DescribeMethods(int m)
        {
            if (m == 0) return "none";
            var sb = MethodsBuilder;
            sb.Length = 0;
            if ((m & 1) != 0) sb.Append("HQ+");
            if ((m & 2) != 0) sb.Append("direct+");
            if ((m & 4) != 0) sb.Append("lookout+");
            if ((m & 8) != 0) sb.Append("hydrophone+");
            if ((m & 16) != 0) sb.Append("radioDF+");
            if ((m & 32) != 0) sb.Append("radar+");
            if ((m & 64) != 0) sb.Append("radarDetector+");
            if ((m & 128) != 0) sb.Append("console+");
            sb.Length--;
            return sb.ToString();
        }

        private static readonly List<SandboxGroup> Candidates = new List<SandboxGroup>();

        private const float TargetScanRadiusKm = 60f;

        private static void LogNearbyTargets(SandboxPlayerWolfpack wolfpack, float searchRadiusKm)
        {
            try
            {
                var origin = wolfpack.Position;
                var observerHorizon = wolfpack.GetObserverHorizonDistance();
                var logged = 0;

                var nearby = wolfpack.NearbyGroups;
                var nearbyCount = nearby == null ? 0 : nearby.Count;

                var ok = LrlSandboxGroups.GetNearest(origin, TargetScanRadiusKm, Candidates);

                if (!ok)
                {
                    LrlLog.Info("  targets: sandbox group list unreachable (" +
                                LrlSandboxGroups.LastError + ") - falling back to NearbyGroups, which holds " +
                                nearbyCount);
                    Candidates.Clear();
                    if (nearby != null)
                    {
                        for (var n = 0; n < nearby.Count; n++)
                            if (nearby[n] != null)
                                Candidates.Add(nearby[n]);
                    }
                }
                else
                {
                    LrlLog.Info(string.Format(
                        "  targets: {0} within {1:0}km of {2} groups in the world ({3} public); " +
                        "NearbyGroups holds {4}; logging up to {5}",
                        Candidates.Count, TargetScanRadiusKm, LrlSandboxGroups.LastTotal,
                        LrlSandboxGroups.LastPublic, nearbyCount, MaxTargetsLogged));
                }

                for (var i = 0; i < Candidates.Count && logged < MaxTargetsLogged; i++)
                {
                    var g = Candidates[i];
                    if (g == null || ReferenceEquals(g, wolfpack))
                        continue;

                    var distKm = (g.Position - origin).magnitude;

                    string tier;
                    var smoke = LrlSmokeModel.GetSmokeHeight(g, out tier);
                    var mast = g.GetMaximumMastHeight();
                    var apparent = g.GetApparentHeight();

                    var maxDistKm = float.IsNaN(apparent) || float.IsInfinity(apparent) || apparent <= 0f
                        ? float.NaN
                        : (observerHorizon + Mathf.Sqrt(2f * EarthRadius * apparent)) / 1000f;

                    LrlLog.Info(string.Format(
                        "  target{7} dist={0:0.0}km mast={1:0.0}m smoke={2:0.0}m({3}) apparent={4:0.0}m maxDist={5:0.0}km detected={6}",
                        distKm, mast, smoke, tier, apparent, maxDistKm, g.DetectedByPlayer,
                        (g.IsGroupPublic ? " [public]" : "") +
                        (nearby != null && nearby.Contains(g) ? " [nearby]" : "")));

                    var det = TargetDetectability(g);
                    var occl = OcclusionFactor(distKm * 1000f, observerHorizon, apparent);
                    var areaRadiusKm = g.AreaRadius;

                    var bonus = LrlConvoyNightBonus.Factor(g);
                    var groupRadiusKm = searchRadiusKm * bonus;

                    var reachKm = float.NaN;
                    if (!float.IsNaN(det) && !float.IsNaN(occl))
                        reachKm = groupRadiusKm * det * Mathf.Sqrt(Mathf.Max(0f, 1f - occl));

                    var verdict = "?";
                    if (!float.IsNaN(reachKm))
                    {
                        if (searchRadiusKm < 0.05f) verdict = "no range - see [BLIND] on the diagnostics line";
                        else if (det <= 0.001f) verdict = "det=0 (not visible to the sandbox at all)";
                        else if (distKm < reachKm) verdict = "QUALIFIES";
                        else if (groupRadiusKm < distKm) verdict = "radius short";
                        else if (occl >= 0.999f) verdict = "hull down";
                        else verdict = "detectability cut";
                    }

                    var observation = wolfpack.GetObservation(g);
                    var obsText = observation == null ? "none" : DescribeMethods((int)observation.DetectionMethod);

                    LrlLog.Info(string.Format(
                        "     areaRadius={0:0.0}km det={1:0.000} occl={2:0.000} " +
                        "searchRadius={3:0.0}km reach={4:0.0}km -> {5}  obs={6}{7}",
                        areaRadiusKm, det, occl, searchRadiusKm, reachKm, verdict, obsText,
                        bonus > 1.0001f ? string.Format("  convoyNightBonus=x{0:0.00}", bonus) : ""));

                    logged++;
                }

                if (logged == 0)
                    LrlLog.Info("  targets: no groups at all within " + TargetScanRadiusKm + " km");
            }
            catch (Exception ex)
            {
                LrlLog.Error("target dump failed", ex);
            }
        }

        private static readonly System.Reflection.FieldInfo GroupSightListenerField =
            HarmonyLib.AccessTools.Field(typeof(SandboxGroup), "sightRange");

        private static void LogSightListener(SandboxGroup wolfpack)
        {
            try
            {
                if (GroupSightListenerField == null)
                {
                    LrlLog.Throttled("no-sightrange-field", 120f,
                        "SandboxGroup.sightRange field not found - listener dump unavailable");
                    return;
                }

                var listener = GroupSightListenerField.GetValue(wolfpack);
                if (listener == null)
                {
                    LrlLog.Info("  sightRange listener is NULL on the player group");
                    return;
                }

                var t = listener.GetType();
                var sb = ListenerBuilder;
                sb.Length = 0;
                sb.Append("  listener");

                foreach (var name in ListenerFieldNames)
                {
                    var f = HarmonyLib.AccessTools.Field(t, name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append(f == null ? "<missing>" : (f.GetValue(listener) ?? "null").ToString());
                }

                var sourcesField = HarmonyLib.AccessTools.Field(t, "sourceParameters");
                var sources = sourcesField != null
                    ? sourcesField.GetValue(listener) as System.Collections.IList
                    : null;

                sb.Append("  sources=").Append(sources == null ? "<null>" : sources.Count.ToString());
                LrlLog.Info(sb.ToString());

                if (sources != null)
                {
                    for (var i = 0; i < sources.Count && i < 12; i++)
                    {
                        var p = sources[i] as UBOAT.Game.Core.Data.Parameter;
                        LrlLog.Info("    source[" + i + "] " +
                            (p == null ? "<null or not a Parameter>"
                                       : p.Name + "=" + p.Value.ToString("0.000")));
                    }
                }
            }
            catch (Exception ex)
            {
                LrlLog.Error("listener dump failed", ex);
            }
        }

        private static string DescribeModifiers(UBOAT.Game.Core.Data.Parameter parameter)
        {
            if (parameter == null)
                return "n/a";

            var sb = ModifiersBuilder;
            sb.Length = 0;
            sb.Append("value=").Append(parameter.Value.ToString("0.000"));

            var deltas = parameter.DeltaModifiers;
            if (deltas != null && deltas.Count > 0)
            {
                sb.Append("  deltas[");
                for (var i = 0; i < deltas.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(deltas[i].Name).Append('=').Append(deltas[i].Value.ToString("0.000"));
                    if (deltas[i].Simulated) sb.Append("(sim)");
                }
                sb.Append(']');
            }

            var scales = parameter.ScaleModifiers;
            if (scales != null && scales.Count > 0)
            {
                sb.Append("  scales[");
                for (var i = 0; i < scales.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(scales[i].Name).Append('=').Append(scales[i].Value.ToString("0.000"));
                    if (scales[i].Simulated) sb.Append("(sim)");
                }
                sb.Append(']');
            }

            return sb.ToString();
        }
    }
}
