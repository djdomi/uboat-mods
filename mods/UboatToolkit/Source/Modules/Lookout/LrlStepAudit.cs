// Ausguck (Teil des UBOAT Toolkit): LrlStepAudit. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlStepAudit
    {
        private static readonly Dictionary<SandboxGroup, Vector2> LastPosition =
            new Dictionary<SandboxGroup, Vector2>();

        private static readonly List<SandboxGroup> CrossCheckBuffer = new List<SandboxGroup>();

        private static float _nextClear;

        internal static float MaxJumpKm { get; private set; }
        internal static int BandSkips { get; private set; }
        internal static int CoverageGaps { get; private set; }

        internal static void Reset()
        {
            LastPosition.Clear();
            MaxJumpKm = 0f;
            BandSkips = 0;
            CoverageGaps = 0;
        }

        internal static void Observe(SandboxGroup group, Vector2 playerPos, float thresholdKm)
        {
            if (group == null || thresholdKm <= 0f)
                return;

            var now = Time.unscaledTime;
            if (now >= _nextClear)
            {
                _nextClear = now + 60f;
                LastPosition.Clear();
            }

            var position = group.Position;

            Vector2 previous;
            if (!LastPosition.TryGetValue(group, out previous))
            {
                LastPosition[group] = position;
                return;
            }

            LastPosition[group] = position;

            var jump = (position - previous).magnitude;
            if (jump > MaxJumpKm)
                MaxJumpKm = jump;

            if (group.DetectedByPlayer)
                return;

            var beforeKm = (previous - playerPos).magnitude;
            var afterKm = (position - playerPos).magnitude;

            if (beforeKm <= thresholdKm || afterKm <= thresholdKm)
                return;

            var closestKm = DistanceToSegment(playerPos, previous, position);
            if (closestKm > thresholdKm)
                return;

            BandSkips++;

            if (LrlLog.ShouldLog("band-skip", 20f))
            {
                LrlLog.Warn(string.Format(
                    "OVERSHOOT: an undetected group crossed the {0:0.0}km detection band without being " +
                    "sampled inside it - {1:0.0}km -> {2:0.0}km in one step ({3:0.0}km of travel, closest " +
                    "approach {4:0.0}km). This is the failure the time-compression guard exists to prevent.",
                    thresholdKm, beforeKm, afterKm, jump, closestKm));
            }
        }

        internal static void CrossCheckCoverage(SandboxPlayerWolfpack wolfpack, float thresholdKm)
        {
            if (wolfpack == null || thresholdKm <= 0f)
                return;

            try
            {
                var origin = wolfpack.Position;
                if (!LrlSandboxGroups.GetNearest(origin, thresholdKm, CrossCheckBuffer))
                    return;

                var nearby = wolfpack.NearbyGroups;
                var missed = 0;

                for (var i = 0; i < CrossCheckBuffer.Count; i++)
                {
                    var group = CrossCheckBuffer[i];
                    if (group == null || ReferenceEquals(group, wolfpack))
                        continue;
                    if (group.DetectedByPlayer || group.IsGroupPublic)
                        continue;
                    if (nearby != null && nearby.Contains(group))
                        continue;

                    missed++;
                }

                CrossCheckBuffer.Clear();

                if (missed <= 0)
                    return;

                CoverageGaps += missed;

                if (LrlLog.ShouldLog("guard-coverage", 30f))
                {
                    LrlLog.Warn(string.Format(
                        "GUARD BLIND SPOT: {0} undetected group(s) inside the {1:0.0}km band are absent from " +
                        "NearbyGroups, which the guard searches. NearbyGroups is a ~32km circle, so a band " +
                        "wider than that cannot be fully covered by it.",
                        missed, thresholdKm));
                }
            }
            catch (Exception ex)
            {
                CrossCheckBuffer.Clear();
                if (LrlLog.ShouldLog("guard-coverage-failed", 300f))
                    LrlLog.Error("guard coverage cross-check failed", ex);
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSq = ab.sqrMagnitude;

            if (lengthSq <= 1e-9f)
                return (point - a).magnitude;

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return (point - (a + ab * t)).magnitude;
        }

        internal static string Describe()
        {
            return string.Format(
                "stepAudit maxGroupJump={0:0.00}km bandSkips={1} coverageGaps={2}",
                MaxJumpKm, BandSkips, CoverageGaps);
        }
    }
}
