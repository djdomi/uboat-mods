using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // Realistic thermal layer ("Sprungschicht"), own model:
    // - depth 25-150 m from the sea temperature (warm water = shallow layer), slow random drift,
    //   sunny calm afternoons push it a bit higher
    // - strength from temperature: cold water weak layer (max -50 %), warm water strong (max -90 %)
    // - below the layer: enemy sonar/hydrophones find you worse, and your hydrophone hears surface ships worse
    internal static class SeaLayer
    {
        internal static float Depth = float.NaN;
        internal static float MaxCut = 0.9f;
        private static GameTime gameTime;
        private static bool wasBelow;

        internal static bool On => Settings.B("SeaLayer");

        internal static void Compute(Weather w, ref float layer)
        {
            float temp = w.Temperature;
            if (gameTime == null) gameTime = SeaFloor.gameTime;
            float sun = 0f;
            if (gameTime != null)
            {
                float hour = (float)gameTime.LocalDateTime.TimeOfDay.TotalHours;
                sun = Mathf.Max(0f, Mathf.Cos((hour - 15f) * Mathf.PI / 12f))
                      * Mathf.Clamp01(1f - w.Cloudiness.Value) * Mathf.Clamp01((12f - w.TotalWindSpeed) / 8f);
            }
            float drift = 0f;
            if (gameTime != null)
                drift = (Mathf.PerlinNoise((float)(gameTime.StoryTicksSinceCampaignStart / 864000000000.0) * 0.6f, 0.71f) - 0.5f) * 30f;
            layer = Mathf.Clamp(115f - temp * 3.5f - 8f * sun + drift, 25f, 150f);
            MaxCut = Mathf.Lerp(0.5f, 0.9f, Mathf.Clamp01((temp - 4f) / 16f + 0.1f * sun));
            Depth = layer;
            Notify();
        }

        internal static float Factor(float depth, float slope)
        {
            if (float.IsNaN(Depth)) return 1f;
            return 1f - Mathf.Clamp((depth - Depth) * slope, 0f, MaxCut);
        }

        // Short note when the boat goes under / comes over the layer.
        private static void Notify()
        {
            if (!Settings.B("SeaLayerNote")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            bool below = ship.Depth > Depth + 3f;
            if (below == wasBelow || (!below && ship.Depth > Depth - 3f)) return;
            wasBelow = below;
            string t = below ? "Unter der Sprungschicht (~" + Mathf.RoundToInt(Depth) + " m): schwerer zu orten, Horcher hört schlechter"
                             : "Über der Sprungschicht (~" + Mathf.RoundToInt(Depth) + " m)";
            var bar = Object.FindObjectOfType<NotificationBarUI>();
            if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", t);
        }
    }

    [HarmonyPatch(typeof(Weather), "LowFrequencyUpdate")]
    internal static class SeaLayer_DepthPatch
    {
        private static void Postfix(Weather __instance, ref float ___sonicLayerDepth)
        {
            if (!SeaLayer.On) { SeaLayer.Depth = float.NaN; return; }
            float d = ___sonicLayerDepth;
            Util.Safe("SeaLayer", () => SeaLayer.Compute(__instance, ref d));
            ___sonicLayerDepth = d;
        }
    }

    [HarmonyPatch(typeof(Entity), "UpdateDetectability")]
    internal static class SeaLayer_ShadowPatch
    {
        private static void Postfix(Entity __instance, Modifier ___sonarShadowZoneModifier, Modifier ___hydrophoneShadowZoneModifier)
        {
            if (!SeaLayer.On || float.IsNaN(SeaLayer.Depth)) return;
            float v = SeaLayer.Factor(__instance.Depth, 0.025f);
            if (___sonarShadowZoneModifier != null) ___sonarShadowZoneModifier.Value = v;
            if (___hydrophoneShadowZoneModifier != null) ___hydrophoneShadowZoneModifier.Value = SeaLayer.Factor(__instance.Depth, 0.02f);
        }
    }

    // Own hydrophone below the layer hears ships above it worse.
    [HarmonyPatch(typeof(SandboxGroup), "HydrophoneDetectabilityPlayerFunc")]
    internal static class SeaLayer_HearingPatch
    {
        private static void Postfix(ref float __result)
        {
            if (!SeaLayer.On || !Settings.B("SeaLayerHearing") || __result <= 0f) return;
            var ship = Util.PlayerShip;
            if (ship != null) __result *= Mathf.Max(0.4f, SeaLayer.Factor(ship.Depth, 0.015f));
        }
    }
}
