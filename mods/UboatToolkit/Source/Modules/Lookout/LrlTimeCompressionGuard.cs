// Ausguck (Teil des UBOAT Toolkit): LrlTimeCompressionGuard. Schalter: F10 > QoL > "Ausguck".
using System;
using UBOAT.Game.Sandbox;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlTimeCompressionGuard
    {
        private static readonly object BlockadeKey = new object();

        private static bool _engaged;
        private static bool _ladderReported;
        private static bool _warned;

        internal static bool Engaged { get { return _engaged; } }

        internal static void Evaluate(SandboxPlayerWolfpack wolfpack, float searchRadiusKm)
        {
            var controller = LrlPlayer.TimeCompression;
            if (controller == null)
                return;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.TimeCompressionGuard || wolfpack == null)
                {
                    Release(controller);
                    return;
                }

                var threshold = searchRadiusKm * LrlSettings.GuardRadiusFactor;
                if (threshold <= 0f)
                {
                    Release(controller);
                    return;
                }

                if (IsAnyUndetectedGroupWithin(wolfpack, threshold))
                {
                    if (!_engaged)
                    {
                        controller.SetTimeCompressionBlockade(BlockadeKey, LrlSettings.TimeCompressionCap);
                        _engaged = true;
                        ReportLadderOnce();

                        var effective = LrlCompressionLadder.EffectiveCompression(LrlSettings.TimeCompressionCap);
                        LrlLog.Info(string.Format(
                            "time compression capped: cap={0:0} -> rung {1} - undetected group within {2:0.0}km band",
                            LrlSettings.TimeCompressionCap,
                            float.IsNaN(effective) ? "unknown (ladder unreadable)" : effective.ToString("0"),
                            threshold));
                    }
                }
                else
                {
                    Release(controller);
                }
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("time compression guard failed - releasing and standing down", ex);
                }

                try { Release(controller); } catch { }
                LrlSettings.TimeCompressionGuard = false;
            }
        }

        internal static void Release(UBOAT.Game.Core.Time.TimeCompressionController controller)
        {
            if (!_engaged || controller == null)
                return;

            controller.RemoveTimeCompressionBlockade(BlockadeKey);
            _engaged = false;
            LrlLog.Info("time compression cap released");
        }

        private static bool IsAnyUndetectedGroupWithin(SandboxPlayerWolfpack wolfpack, float thresholdKm)
        {
            var nearby = wolfpack.NearbyGroups;
            if (nearby == null || nearby.Count == 0)
                return false;

            var origin = wolfpack.Position;
            var thresholdSq = thresholdKm * thresholdKm;
            var playerCountry = wolfpack.Country;
            var found = false;

            for (var i = 0; i < nearby.Count; i++)
            {
                var group = nearby[i];
                if (group == null)
                    continue;

                if (group.IsGroupPublic)
                    continue;

                LrlStepAudit.Observe(group, origin, thresholdKm);

                if (group.DetectedByPlayer)
                    continue;

                if (LrlSettings.GuardIgnoresAllies && playerCountry != null && group.Country != null)
                {
                    if (playerCountry.GetRelationWith(group.Country) == Country.Relation.Ally)
                        continue;
                }

                var delta = group.Position - origin;
                var bonus = LrlConvoyNightBonus.Factor(group);
                if (delta.sqrMagnitude <= thresholdSq * bonus * bonus)
                    found = true;
            }

            return found;
        }

        private static void ReportLadderOnce()
        {
            if (_ladderReported)
                return;

            _ladderReported = true;

            float[] rungs;
            if (!LrlCompressionLadder.TryRead(out rungs))
            {
                LrlLog.Warn("compression ladder unreadable (" + LrlCompressionLadder.WhyNot +
                            ") - the cap may or may not bind, and this line cannot say which");
                return;
            }

            var top = LrlCompressionLadder.Top;
            var cap = LrlSettings.TimeCompressionCap;
            var effective = LrlCompressionLadder.EffectiveCompression(cap);

            LrlLog.Info(string.Format(
                "compression ladder (live): {0:0} / {1:0} / {2:0} / {3:0} / {4:0}; cap {5:0} selects rung {6:0}",
                rungs[0], rungs[1], rungs[2], rungs[3], rungs[4], cap, effective));

            if (cap >= top)
            {
                LrlLog.Warn(string.Format(
                    "THE COMPRESSION CAP IS INERT: cap {0:0} is at or above the top rung {1:0}, so the " +
                    "blockade admits full compression and this guard does nothing. Set " +
                    "LrlSettings.TimeCompressionCap below {1:0}.", cap, top));
            }
        }
    }
}
