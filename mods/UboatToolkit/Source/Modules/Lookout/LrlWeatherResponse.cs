// Ausguck (Teil des UBOAT Toolkit): LrlWeatherResponse. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlWeatherResponse
    {
        private const string HistoricalWeatherHarmonyId = "com.kugler.historicalweather";

        private static readonly FieldInfo FogField =
            AccessTools.Field(typeof(Weather), "fogVisibilityFactor");

        internal static float CachedFog = 1f;

        internal static bool HistoricalWeatherPresent { get; private set; }

        private static bool _warned;

        internal static void Refresh(Weather weather)
        {
            try
            {
                HistoricalWeatherPresent = Harmony.HasAnyPatches(HistoricalWeatherHarmonyId);

                var fog = 1f;
                if (weather != null && FogField != null)
                {
                    var boxed = FogField.GetValue(weather);
                    if (boxed is float)
                        fog = (float)boxed;
                }

                CachedFog = float.IsNaN(fog) || float.IsInfinity(fog) ? 1f : Mathf.Clamp01(fog);
            }
            catch (Exception ex)
            {
                CachedFog = 1f;
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("fog read failed - treating fog as clear, weather correction unchanged", ex);
                }
            }
        }

        private static readonly AccessTools.FieldRef<Weather, float> FogRef = MakeFogRef();

        private static AccessTools.FieldRef<Weather, float> MakeFogRef()
        {
            try
            {
                return FogField != null
                    ? AccessTools.FieldRefAccess<Weather, float>("fogVisibilityFactor")
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool _warnedLive;

        internal static void RefreshLive(Weather weather)
        {
            if (weather == null || _warnedLive)
                return;

            try
            {
                LrlSightRangePatch.CachedVisibility = weather.VisualDetectabilityFactors;

                if (FogRef != null)
                {
                    var fog = FogRef(weather);
                    CachedFog = float.IsNaN(fog) || float.IsInfinity(fog) ? 1f : Mathf.Clamp01(fog);
                }
            }
            catch (Exception ex)
            {
                if (!_warnedLive)
                {
                    _warnedLive = true;
                    LrlLog.Error("per-frame weather read failed - falling back to the 5 s refresh", ex);
                }
            }
        }

        internal static float ActiveFogExtra
        {
            get
            {
                if (!LrlSettings.StrengthenFog || HistoricalWeatherPresent)
                    return 0f;
                return Mathf.Max(0f, LrlSettings.FogExtraExponent);
            }
        }

        internal static float EffectiveRest(float rest)
        {
            var floor = LrlSettings.WeatherFloorEnabled ? LrlSettings.VisibilityFloor : 0f;
            if (floor <= 0f || rest >= floor)
                return rest;

            var softness = Mathf.Clamp01(LrlSettings.VisibilityFloorSoftness);
            var restEff = softness <= 0f ? floor : floor * Mathf.Pow(rest / floor, softness);
            return restEff < rest ? rest : restEff;
        }

        internal static float RangeMultiplier(float visibility)
        {
            if (float.IsNaN(visibility) || float.IsInfinity(visibility) || visibility <= 0.0001f)
                return 1f;

            var fog = CachedFog;
            if (fog <= 0.0001f)
                return 1f;   // total fog: vanilla already gives ~zero, leave it

            var rest = Mathf.Clamp01(visibility / fog);
            if (rest <= 0.0001f)
                return 1f;

            var restEff = rest;
            var multiplier = 1f;

            restEff = EffectiveRest(rest);
            if (restEff > rest)
            {
                var lift = restEff / rest;
                multiplier *= lift * lift;   // squared, because the game squares the term
            }

            if (LrlSettings.CancelWeatherSquaring)
                multiplier *= Mathf.Min(1f / restEff, LrlSettings.MaxWeatherCompensation);

            var extra = ActiveFogExtra;
            if (extra > 0f && fog < 1f)
                multiplier *= Mathf.Pow(fog, extra);

            multiplier *= LrlDaylightTrim.RangeFactor(visibility);

            return multiplier;
        }

        internal static float EffectiveVisibility(float visibility)
        {
            if (float.IsNaN(visibility) || float.IsInfinity(visibility) || visibility <= 0.0001f)
                return visibility;

            var fog = CachedFog;
            if (fog <= 0.0001f)
                return visibility;

            var rest = Mathf.Clamp01(visibility / fog);
            return fog * EffectiveRest(rest);
        }
    }
}
