// Ausguck (Teil des UBOAT Toolkit): LrlConvoyNightBonus. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlConvoyNightBonus
    {
        internal static float NightWeight
        {
            get
            {
                if (!LrlSettings.Enabled || !LrlSettings.ConvoyNightBonusEnabled)
                    return 0f;

                var v = LrlSightRangePatch.CachedVisibility;
                if (float.IsNaN(v) || float.IsInfinity(v))
                    return 0f;

                var full = LrlSettings.ConvoyNightBonusFullVisibility;
                var end = LrlSettings.ConvoyNightBonusEndVisibility;
                if (end <= full)
                    return v <= full ? 1f : 0f;

                return Mathf.Clamp01((end - v) / (end - full));
            }
        }

        internal static float MaxFactor
        {
            get { return Mathf.Lerp(1f, Mathf.Max(1f, LrlSettings.ConvoyNightBonusMax), NightWeight); }
        }

        internal static float Factor(SandboxGroup group)
        {
            var weight = NightWeight;
            if (weight <= 0f || group == null)
                return 1f;
            if (group.IsGroupPublic || group.IsAirborne || !(group is SandboxMobileGroup) ||
                group is SandboxPlayerWolfpack)
                return 1f;

            var entities = group.Entities;
            var ships = entities != null ? entities.Count : 0;
            if (ships < 2)
                return 1f;

            var size = Mathf.Min(1f + LrlSettings.ConvoyNightBonusPerExtraShip * (ships - 1),
                                 Mathf.Max(1f, LrlSettings.ConvoyNightBonusMax));
            return Mathf.Lerp(1f, size, weight);
        }

        internal static readonly List<SandboxGroup> Pool = new List<SandboxGroup>();

        private const float PoolRadiusKm = 120f;
        private static readonly List<SandboxGroup> Scratch = new List<SandboxGroup>();

        internal static void RefreshPool(SandboxPlayerWolfpack wolfpack)
        {
            Pool.Clear();
            if (wolfpack == null || NightWeight <= 0f)
                return;

            if (!LrlSandboxGroups.GetNearest(wolfpack.Position, PoolRadiusKm, Scratch))
                return;

            for (var i = 0; i < Scratch.Count; i++)
            {
                if (Factor(Scratch[i]) > 1.0001f)
                    Pool.Add(Scratch[i]);
            }
            Scratch.Clear();
        }

        internal static int Rolls;
        internal static int Accepted;
        internal static string LastAccepted;

        internal static string Report()
        {
            var max = MaxFactor;
            if (max <= 1.0001f && Rolls == 0)
                return null;

            var s = string.Format(
                "convoy night bonus: up to x{0:0.00} (night {1:0%}) pool={5} bonus-only rolls={2} accepted={3}{4}",
                max, NightWeight, Rolls, Accepted,
                LastAccepted != null ? " last=" + LastAccepted : "", Pool.Count);
            Rolls = 0;
            Accepted = 0;
            LastAccepted = null;
            return s;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Sandbox.Sandbox), "GetRandomGroupInRangeWithHorizonOcclusion")]
    internal static class LrlConvoyNightBonusPatch
    {
        private static readonly SandboxGroup[] Candidates = new SandboxGroup[64];
        private static readonly float[] Weights = new float[64];

        private static bool _failed;

        private static bool Prefix(Vector2 point, float range,
            Func<SandboxGroup, float> detectabilityFunc, float observerHorizonDistance,
            bool updateCandidates, ref SandboxGroup __result)
        {
            if (!updateCandidates || _failed || detectabilityFunc == null || range <= 0f)
                return true;

            try
            {
                var maxFactor = LrlConvoyNightBonus.MaxFactor;
                if (maxFactor <= 1.0001f)
                    return true;

                var pool = LrlConvoyNightBonus.Pool;
                var count = 0;
                var summed = 0f;
                var rangeSq = range * range;

                for (var i = 0; i < pool.Count && count < Candidates.Length; i++)
                {
                    var group = pool[i];
                    if (group == null)   // Unity null: destroyed since the pool was built
                        continue;

                    var factor = LrlConvoyNightBonus.Factor(group);
                    if (factor <= 1.0001f)
                        continue;

                    group.UpdateGroup();
                    var distSq = (group.Position - point).sqrMagnitude;
                    if (distSq >= rangeSq * factor * factor)
                        continue;

                    var det = detectabilityFunc(group);
                    var weight = det * det;
                    if (!(weight > 0f))
                        continue;

                    weight *= 1f - group.ComputeOcclusionByHorizonFactor(observerHorizonDistance, distSq * 1000000f);
                    if (!(weight > 0f))
                        continue;

                    if (distSq < rangeSq * weight)
                        continue;

                    if (distSq >= rangeSq * factor * factor * weight)
                        continue;

                    Candidates[count] = group;
                    Weights[count] = weight;
                    count++;
                    summed += weight;
                }

                if (count == 0)
                    return true;

                LrlConvoyNightBonus.Rolls++;

                var roll = UnityEngine.Random.value;
                var accept = LrlSettings.FixInvertedAcceptance
                    ? roll < summed
                    : (summed >= 1f || roll >= summed);

                SandboxGroup picked = null;
                if (accept)
                {
                    var pick = UnityEngine.Random.value * summed;
                    for (var i = 0; i < count; i++)
                    {
                        pick -= Weights[i];
                        if (pick < 0f || i == count - 1)
                        {
                            picked = Candidates[i];
                            break;
                        }
                    }
                }

                for (var i = 0; i < count; i++)
                    Candidates[i] = null;

                if (picked == null)
                    return true;

                LrlConvoyNightBonus.Accepted++;
                LrlConvoyNightBonus.LastAccepted = picked.name;
                __result = picked;
                return false;
            }
            catch (Exception ex)
            {
                _failed = true;
                LrlLog.Error("convoy night bonus failed - off for this session, search left vanilla", ex);
                return true;
            }
        }
    }
}
