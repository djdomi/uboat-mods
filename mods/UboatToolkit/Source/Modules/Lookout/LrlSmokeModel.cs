// Ausguck (Teil des UBOAT Toolkit): LrlSmokeModel. Schalter: F10 > QoL > "Ausguck".
using System.Collections.Generic;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlSmokeModel
    {
        private const EntityTypeCategory SmokeProducers =
            EntityTypeCategory.Submarine |
            EntityTypeCategory.Freighter |
            EntityTypeCategory.Destroyer |
            EntityTypeCategory.Cruiser |
            EntityTypeCategory.Battleship |
            EntityTypeCategory.EscortCarrier |
            EntityTypeCategory.FastAttackCraft |
            EntityTypeCategory.Tanker |
            EntityTypeCategory.Corvette |
            EntityTypeCategory.FishingBoat |
            EntityTypeCategory.Coaster |
            EntityTypeCategory.Trawler |
            EntityTypeCategory.PatrolBoat;

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        private struct Entry
        {
            public float Time;
            public float Height;
            public string Tier;
        }

        internal static void ClearCache()
        {
            Cache.Clear();
        }

        internal static float GetSmokeHeight(SandboxGroup group)
        {
            string tier;
            return GetSmokeHeight(group, out tier);
        }

        internal static float GetSmokeHeight(SandboxGroup group, out string tier)
        {
            tier = "none";

            if (group == null || !LrlSettings.SmokeHeightEnabled)
                return 0f;

            var id = group.GetInstanceID();
            var now = Time.unscaledTime;

            var trim = LrlDaylightTrim.SmokeFactor(LrlSightRangePatch.CachedVisibility);

            Entry cached;
            if (Cache.TryGetValue(id, out cached) && now - cached.Time < LrlSettings.SmokeCacheSeconds)
            {
                tier = cached.Tier;
                return cached.Height * trim;
            }

            var height = Compute(group, out tier);

            Cache[id] = new Entry { Time = now, Height = height, Tier = tier };
            return height * trim;
        }

        private static float Compute(SandboxGroup group, out string tier)
        {
            tier = "none";

            var entities = group.Entities;
            if (entities == null || entities.Count == 0)
                return 0f;

            var best = 0f;
            var smokingShips = 0;
            var anyCrippled = false;

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null)
                    continue;

                var type = entity.Type;
                if (type == null || (type.Category & SmokeProducers) == 0)
                    continue;

                var crippled = entity.IsCrippled;

                var speed = entity.Velocity != null ? Mathf.Abs(entity.Velocity.Value) : 0f;

                if (!crippled && speed < LrlSettings.MinimumSmokingSpeed)
                    continue;

                smokingShips++;
                if (crippled)
                    anyCrippled = true;

                var h = crippled
                    ? Mathf.Max(LrlSettings.SmokeSlow, TierHeight(speed))
                    : TierHeight(speed);

                if (h > best)
                {
                    best = h;
                    tier = TierName(speed, crippled);
                }
            }

            if (smokingShips == 0)
                return 0f;

            if (smokingShips > 1)
            {
                best += LrlSettings.SmokePerExtraShip * (smokingShips - 1);
                tier += "+convoy" + smokingShips;
            }

            if (anyCrippled)
                best += LrlSettings.SmokeCrippledBonus;

            return Mathf.Min(best, LrlSettings.SmokeGroupCap);
        }

        private static float TierHeight(float speed)
        {
            if (speed < LrlSettings.SpeedSlow) return LrlSettings.SmokeBarelyMoving;
            if (speed < LrlSettings.SpeedNormal) return LrlSettings.SmokeSlow;
            if (speed < LrlSettings.SpeedFast) return LrlSettings.SmokeNormal;
            return LrlSettings.SmokeFast;
        }

        private static string TierName(float speed, bool crippled)
        {
            var name = speed < LrlSettings.SpeedSlow ? "barely"
                     : speed < LrlSettings.SpeedNormal ? "slow"
                     : speed < LrlSettings.SpeedFast ? "normal"
                     : "fast";

            return crippled ? name + "+crippled" : name;
        }
    }
}
