using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Audio;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Telescopic snorkel on U-1337:
    //  - the mast is "WhSnorkelExtra" m longer, so the boat can snorkel deeper than periscope depth
    //  - with "WhSnorkelAuto" it extends further by itself in waves (up to +3 m) when the tip gets wet
    //  - exhaust goes out under water: smoke / visibility / radar size x "WhSnorkelExhaust" (0 = invisible)
    // Done by running our copy of Snorkel.Update with the depth limits moved down (the game hard-codes 10 m).
    public class WarthogSnorkel : ModuleBase
    {
        internal static float WaveExtra;   // current automatic extension (m)
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhSnorkelTelescope");
        internal static float Extra => On ? Settings.F("WhSnorkelExtra") + WaveExtra : 0f;
    }

    [HarmonyPatch(typeof(Snorkel), "Update")]
    internal static class WarthogSnorkel_UpdatePatch
    {
        private static readonly AccessTools.FieldRef<Snorkel, Transform> Tube = AccessTools.FieldRefAccess<Snorkel, Transform>("tube");
        private static readonly AccessTools.FieldRef<Snorkel, float> Angle = AccessTools.FieldRefAccess<Snorkel, float>("angle");
        private static readonly AccessTools.FieldRef<Snorkel, bool> Raise = AccessTools.FieldRefAccess<Snorkel, bool>("shouldMastBeRaised");
        private static readonly AccessTools.FieldRef<Snorkel, bool> Auto = AccessTools.FieldRefAccess<Snorkel, bool>("automaticRaise");
        private static Vector3? tubeBase;

        private static bool Prefix(Snorkel __instance, DeepAudioSource ___audioFx, ParticleSystem ___smoke, float ___riseUpSpeed, float ___riseDownSpeed,
            Modifier ___performanceModifier, Modifier ___oxygenModifier, Modifier ___visibilityModifier, Modifier ___signatureRadiusModifier, Modifier ___radarDetectabilityModifier,
            Parameter ___oxygenRegeneration, Parameter ___signatureRadius, Parameter ___radarDetectability, Parameter ___visibility,
            float ___smokeEffectMultiplier, ParticleSystemIntensityController ___smokeIntensityController)
        {
            if (!WarthogSnorkel.On || !Util.IsPlayers(__instance)) { RestoreTube(__instance); return true; }
            var sub = (PlayableSubmarine)__instance.ParentEntity;
            var gt = Traverse.Create(typeof(Snorkel)).Field("gameTime").GetValue<GameTime>();
            float ts = gt != null ? gt.ArtificialTimeScale : 1f;

            // telescopic tube: move the whole mast up by the extra length
            var tube = Tube(__instance);
            if (tube != null)
            {
                if (tubeBase == null) tubeBase = tube.localPosition;
                tube.localPosition = tubeBase.Value + Vector3.up * WarthogSnorkel.Extra;
            }
            float limit = 10f + WarthogSnorkel.Extra;   // the game: 10 m

            float deckDepth = sub.DeckDepth, targetDepth = sub.TargetDepth;
            if (Auto(__instance)) Raise(__instance) = targetDepth > 1f && targetDepth < limit && deckDepth < limit;
            var engines = ((Ship)sub).ActiveEngines;
            float num = (engines is PlayerShipEngine pse && pse.FuelUsage.Value < 0f) ? engines.Performance.Value : 0f;
            ref float angle = ref Angle(__instance);
            if (Raise(__instance))
            {
                if (angle != 0f) { angle = Mathf.Max(0f, angle - Time.deltaTime * ts * ___riseUpSpeed); if (tube) tube.localEulerAngles = new Vector3(angle, 0f, 0f); }
                if (angle < 10f && num > 0f) { if (!___audioFx.isPlaying) { ___audioFx.Play(); ___smoke.Play(); } }
                else if (___audioFx.isPlaying) { ___audioFx.Stop(); ___smoke.Stop(); }
            }
            else
            {
                if (angle != 90f) { angle = Mathf.Min(90f, angle + Time.deltaTime * ts * ___riseDownSpeed); if (tube) tube.localEulerAngles = new Vector3(angle, 0f, 0f); }
                if (angle >= 10f && ___audioFx.isPlaying) { ___audioFx.Stop(); ___smoke.Stop(); }
            }
            float up = Mathf.Max(0f, (10f - angle) * 0.1f);
            float tipAboveWater = ___audioFx.transform.position.y - sub.CurrentWaterElevation;
            float wet = Mathf.Clamp01((2f + tipAboveWater) * 4f) * (1f - Mathf.Clamp01(angle / 90f));
            if (targetDepth >= limit) wet = 0f;
            up *= wet;

            // automatic extension in waves: tip closer than 0.7 m to the water -> extend, well above -> retract
            if (Settings.B("WhSnorkelAuto") && Raise(__instance))
            {
                float target = tipAboveWater < 0.7f ? 3f : tipAboveWater > 2f ? 0f : WarthogSnorkel.WaveExtra;
                WarthogSnorkel.WaveExtra = Mathf.MoveTowards(WarthogSnorkel.WaveExtra, target, Time.deltaTime * ts * 0.5f);
            }
            else WarthogSnorkel.WaveExtra = Mathf.MoveTowards(WarthogSnorkel.WaveExtra, 0f, Time.deltaTime * ts * 0.5f);

            var ps = sub as PlayerShip;
            if (ps != null && ps.DieselEngine) ps.DieselEngine.ExhaustEffectsIntensityScale = 1f - up;
            float exhaust = Settings.F("WhSnorkelExhaust");   // 0 = exhaust under water, invisible
            float smoke = num * up * ___smokeEffectMultiplier * exhaust;
            ___smokeIntensityController.Intensity = smoke;
            if (smoke > 0f && !___smokeIntensityController.ParticleSystemComponent.isPlaying) ___smokeIntensityController.ParticleSystemComponent.Play();
            ___performanceModifier.Value = up;
            if (___oxygenModifier != null) ___oxygenModifier.Value = Raise(__instance) ? __instance.Performance.Value * ___oxygenRegeneration.Value : 0f;
            float sig = wet * ___signatureRadius.Value * exhaust;
            var maxMod = sub.SandboxEntity.SignatureRadius.GetMaxDeltaModifier();
            if (maxMod != null && (maxMod == ___signatureRadiusModifier || maxMod.Value < sig))
            {
                ___radarDetectabilityModifier.Value = Mathf.Lerp(1f, ___radarDetectability.Value, wet);
                ___signatureRadiusModifier.Value = sig;
            }
            else { ___radarDetectabilityModifier.Value = 1f; ___signatureRadiusModifier.Value = 0f; }
            ___visibilityModifier.Value = Mathf.Max(wet * ___visibility.Value * Mathf.Max(exhaust, 0.15f), 0f); // the mast itself stays a little visible
            return false;
        }

        private static void RestoreTube(Snorkel s)
        {
            if (tubeBase == null) return;
            var tube = Tube(s);
            if (tube != null) tube.localPosition = tubeBase.Value;
            tubeBase = null;
        }
    }
}
