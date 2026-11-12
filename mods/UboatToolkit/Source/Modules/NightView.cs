using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // Night view, hooked on the game's own night adaptation (BrighterNights.DoUpdate):
    //  - brighter nights outside (strengthens the game's adaptation, with a safety ceiling)
    //  - brighter boat interior (own post-exposure volume over the interior one)
    //  - per-optic exposure: removes the observation periscope's extra brightness, gives every
    //    sight (attack / observation periscope, UZO, binoculars) its own exposure and glass look
    //    (haze by day, dimming and colour loss at night), plus a per-sight night gain.
    internal enum NightSight { None, Attack, Observation, Uzo, Binoculars, UnknownPeriscope }

    internal struct NightOptic
    {
        public float Ev, Contrast, Saturation, Lift, Temperature, Tint;
        public static NightOptic Lerp(NightOptic a, NightOptic b, float t) => new NightOptic
        {
            Ev = Mathf.Lerp(a.Ev, b.Ev, t), Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t), Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            Lift = Mathf.Lerp(a.Lift, b.Lift, t), Temperature = Mathf.Lerp(a.Temperature, b.Temperature, t), Tint = Mathf.Lerp(a.Tint, b.Tint, t)
        };
        public NightOptic Scale(float k) => new NightOptic { Ev = Ev * k, Contrast = Contrast * k, Saturation = Saturation * k, Lift = Lift * k, Temperature = Temperature * k, Tint = Tint * k };
    }

    internal static class NightCfg
    {
        public static bool Optics => Settings.B("NightOptics");
        public static NightOptic Day => new NightOptic { Ev = -0.25f, Contrast = -10f, Saturation = -12f, Lift = Settings.F("NightOpticDayLift") / 1000f };
        public static NightOptic Night => new NightOptic { Ev = -1.10f, Contrast = -4f, Saturation = -Settings.F("NightOpticColourLoss"), Lift = -0.020f };

        public static float ExposureFor(NightSight s)
        {
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightAttackEv");
                case NightSight.Observation: return Settings.F("NightObservationEv");
                case NightSight.Uzo: return Settings.B("NightUzoBinoGrading") ? Settings.F("NightUzoEv") : float.NaN;
                case NightSight.Binoculars: return Settings.B("NightUzoBinoGrading") ? Settings.F("NightBinoEv") : float.NaN;
                default: return float.NaN;
            }
        }
        public static float GlassFor(NightSight s)
        {
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightAttackGlass") / 100f;
                case NightSight.Observation: return Settings.F("NightObservationGlass") / 100f;
                case NightSight.Uzo: case NightSight.Binoculars: return 1f;
                default: return 0f;
            }
        }
        // Night adaptation gain per view (100 % = game).
        public static float GainFor(NightSight s)
        {
            if (!Optics) return 1f;
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightGainAttack") / 100f;
                case NightSight.Observation: return Settings.F("NightGainObservation") / 100f;
                case NightSight.Uzo: return Settings.F("NightGainUzo") / 100f;
                case NightSight.Binoculars: return Settings.F("NightGainBino") / 100f;
                default: return Settings.F("NightGainOther") / 100f;
            }
        }
        public static float ExteriorGain => 1f + Settings.F("NightBright") / 100f;
        public static float InteriorEv { get { float p = Settings.F("InteriorBright"); return p <= 0f ? 0f : Mathf.Log(1f + p / 100f, 2f); } }
    }

    internal static class NightViewRead
    {
        public static NightSight Classify(string overlay)
        {
            if (string.IsNullOrEmpty(overlay)) return NightSight.UnknownPeriscope;
            if (overlay.StartsWith("Observation", StringComparison.OrdinalIgnoreCase)) return NightSight.Observation;
            if (overlay.StartsWith("Targeting", StringComparison.OrdinalIgnoreCase)) return NightSight.Uzo;
            if (overlay.StartsWith("Binocular", StringComparison.OrdinalIgnoreCase)) return NightSight.Binoculars;
            if (overlay.StartsWith("Periscope", StringComparison.OrdinalIgnoreCase)) return NightSight.Attack;
            return NightSight.UnknownPeriscope;
        }

        public static void Read(MainCamera cam, out CameraMode mode, out bool active, out NightSight sight)
        {
            mode = CameraMode.Generic; active = false; sight = NightSight.None;
            if (cam == null) return;
            try
            {
                mode = cam.CurrentMode;
                active = cam.isActiveAndEnabled;
                var pc = cam.GenericController?.ExternalController as PeriscopeController;
                var p = pc?.Periscope;
                if (p == null) return;
                string name = p.ScreenOverlayName;
                if (string.IsNullOrEmpty(name)) name = p.FallbackScreenOverlayName;
                if (mode == CameraMode.Generic) sight = Classify(name);
            }
            catch (Exception e) { Util.Log("Nacht: Ansicht nicht lesbar: " + e.Message); }
        }
    }

    // Per-sight grading volume (layer 16 = periscope volumes, priority above their 128).
    internal static class NightGrading
    {
        private static GameObject host;
        private static PostProcessVolume volume;
        private static ColorGrading grading;
        private static bool failed, clampWarned;
        private static bool baseValid;
        private static float basePe, baseCon, baseSat, baseTemp, baseTint, baseAt = -999f;
        private static Vector4 baseLift;
        private static float lastEv = float.NaN;
        private static NightSight lastSight = NightSight.None;
        public static float PublishedEvDelta, PublishedSatBase, PublishedSatDelta;
        public static int PublishedFrame = -1;

        public static void Tick(NightSight sight, CameraMode mode, float nightFactor)
        {
            try
            {
                float sightEv = NightCfg.ExposureFor(sight);
                if (!NightCfg.Optics || mode != CameraMode.Generic || sight == NightSight.None || float.IsNaN(sightEv)) { Off(); return; }
                if (!Ensure()) return;
                ReadBase();
                if (!baseValid) { Off(); return; }
                float f = float.IsNaN(nightFactor) ? 0f : Mathf.Clamp01(nightFactor);
                var optic = NightOptic.Lerp(NightCfg.Day, NightCfg.Night, f).Scale(Mathf.Max(0f, Settings.F("NightOpticStrength") / 100f) * Mathf.Max(0f, NightCfg.GlassFor(sight)));
                float target = basePe + sightEv + optic.Ev;
                float ev = Mathf.Clamp(target, -8f, 4f);
                if (Mathf.Abs(ev - target) > 1e-4f && !clampWarned) { clampWarned = true; Util.Log("Nacht: Belichtung " + target.ToString("F2") + " EV außerhalb -8..+4, nehme " + ev.ToString("F2")); }
                float comp = Mathf.Clamp(Settings.F("NightReticleComp") / 100f, 0f, 2f);
                PublishedEvDelta = (ev - basePe) * comp; PublishedSatBase = baseSat; PublishedSatDelta = optic.Saturation * comp; PublishedFrame = Time.frameCount;
                grading.postExposure.overrideState = true;
                grading.postExposure.value = ev;
                SetF(grading.contrast, baseCon + optic.Contrast, optic.Contrast);
                SetF(grading.saturation, baseSat + optic.Saturation, optic.Saturation);
                SetF(grading.temperature, baseTemp + optic.Temperature, optic.Temperature);
                SetF(grading.tint, baseTint + optic.Tint, optic.Tint);
                if (Mathf.Abs(optic.Lift) > 1e-5f) { grading.lift.overrideState = true; var l = baseLift; l.w += optic.Lift; grading.lift.value = l; }
                else grading.lift.overrideState = false;
                volume.weight = 1f;
                if (sight != lastSight || float.IsNaN(lastEv) || Mathf.Abs(ev - lastEv) > 1e-3f)
                {
                    lastSight = sight;
                    if (Settings.B("NightLog")) Util.Log("Nacht: " + sight + " Nacht-Anteil " + f.ToString("F2") + " -> Belichtung " + ev.ToString("F3") + " EV, Sättigung " + (baseSat + optic.Saturation).ToString("F1"));
                }
                lastEv = ev;
            }
            catch (Exception e) { failed = true; Off(); Util.Log("Nacht: Sicht-Belichtung abgeschaltet: " + e.Message); }
        }

        private static void SetF(FloatParameter p, float abs, float delta)
        {
            if (Mathf.Abs(delta) > 1e-5f) { p.overrideState = true; p.value = abs; } else p.overrideState = false;
        }

        public static void Off()
        {
            PublishedFrame = -1; lastEv = float.NaN; lastSight = NightSight.None;
            if (volume != null) volume.weight = 0f;
        }

        private static bool Ensure()
        {
            if (failed) return false;
            if (volume != null && grading != null) return true;
            try
            {
                if (host == null) { host = new GameObject("Toolkit Sight Grading") { layer = 16, hideFlags = HideFlags.DontSave }; UnityEngine.Object.DontDestroyOnLoad(host); }
                var prof = ScriptableObject.CreateInstance<PostProcessProfile>();
                prof.hideFlags = HideFlags.DontSave;
                grading = prof.AddSettings<ColorGrading>();
                grading.active = true; grading.enabled.Override(true); grading.postExposure.Override(0f);
                volume = host.AddComponent<PostProcessVolume>();
                volume.isGlobal = true; volume.priority = 200f; volume.weight = 0f; volume.sharedProfile = prof;
                return true;
            }
            catch (Exception e) { failed = true; Util.Log("Nacht: Sicht-Volumen nicht erstellbar: " + e.Message); return false; }
        }

        // The game's exterior grading ("Base - Exterior"), refreshed every 5 s.
        private static void ReadBase()
        {
            if (baseValid && Time.unscaledTime - baseAt < 5f) return;
            baseAt = Time.unscaledTime;
            baseValid = false;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (v == null || v.gameObject == null || v.gameObject.name != "Base - Exterior") continue;
                var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (prof == null || !prof.TryGetSettings(out ColorGrading cg) || cg == null) continue;
                baseValid = true;
                basePe = cg.postExposure.value; baseCon = cg.contrast.value; baseSat = cg.saturation.value;
                baseLift = cg.lift.value; baseTemp = cg.temperature.value; baseTint = cg.tint.value;
                return;
            }
        }
    }

    // Brighter interior: own volume on the interior layer, base exposure + log2(1 + %).
    internal static class NightInterior
    {
        private static GameObject host;
        private static PostProcessVolume volume;
        private static ColorGrading grading;
        private static bool failed;
        private static float basePe, baseAt = -999f;
        private static PostProcessVolume baseFrom;
        private static float lastEv = float.NaN;

        public static void Tick(MainCamera cam)
        {
            try
            {
                float ev = NightCfg.InteriorEv;
                var inner = cam != null ? cam.InteriorPostProcessVolume : null;
                if (ev <= 0f || inner == null) { Off(); return; }
                if (failed) return;
                if (volume == null)
                {
                    host = new GameObject("Toolkit Interior Light") { hideFlags = HideFlags.DontSave };
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    var prof = ScriptableObject.CreateInstance<PostProcessProfile>();
                    prof.hideFlags = HideFlags.DontSave;
                    grading = prof.AddSettings<ColorGrading>();
                    grading.active = true; grading.enabled.Override(true); grading.postExposure.Override(0f);
                    volume = host.AddComponent<PostProcessVolume>();
                    volume.isGlobal = true; volume.priority = 210f; volume.weight = 0f; volume.sharedProfile = prof;
                }
                if (host.layer != inner.gameObject.layer) host.layer = inner.gameObject.layer;
                if (inner != baseFrom || Time.unscaledTime - baseAt >= 5f)
                {
                    baseFrom = inner; baseAt = Time.unscaledTime; basePe = 0f;
                    var prof = inner.HasInstantiatedProfile() ? inner.profile : inner.sharedProfile;
                    if (prof != null && prof.TryGetSettings(out ColorGrading cg) && cg != null && cg.postExposure.overrideState) basePe = cg.postExposure.value;
                }
                grading.postExposure.overrideState = true;
                grading.postExposure.value = basePe + ev;
                volume.weight = inner.weight;
                if (float.IsNaN(lastEv) || Mathf.Abs(ev - lastEv) > 1e-3f) { lastEv = ev; Util.Log("Nacht: Innenraum +" + ev.ToString("F2") + " EV über " + basePe.ToString("F2")); }
            }
            catch (Exception e) { failed = true; Off(); Util.Log("Nacht: Innenraum-Aufhellung abgeschaltet: " + e.Message); }
        }

        public static void Off() { lastEv = float.NaN; if (volume != null) volume.weight = 0f; }
    }

    [HarmonyPatch(typeof(BrighterNights), "DoUpdate")]
    internal static class Night_AdaptationPatch
    {
        private static bool clampWarned;
        private static float nextLog;

        private static void Postfix(AutoExposure ___autoExposure, float ___minExposure, float ___shelterFactor,
                                    GameDataParameter ___adaptation, GameDataParameter ___adaptationUnrealisticNights, GameDataParameter ___adaptationNightLights,
                                    MainCamera ___mainCamera, PlayerShipInteriorLighting ___submarineLighting, UserSettings ___userSettings, IStoryPlayerUI ___storyPlayerUI)
        {
            try
            {
                // which adaptation value the game used this frame
                float param = 0f;
                if (___userSettings != null && ___userSettings.GameplaySettings != null)
                {
                    if (!___userSettings.GameplaySettings.realisticNights) param = Val(___adaptationUnrealisticNights);
                    else if (___submarineLighting != null && ___submarineLighting.CurrentPreset == LightController.Preset.Alarm && (___storyPlayerUI == null || !___storyPlayerUI.IsOpened)) param = Val(___adaptationNightLights);
                    else param = Val(___adaptation);
                }
                float written = ___autoExposure != null ? ___autoExposure.minLuminance.value : float.NaN;
                float f = float.NaN;
                if (___autoExposure != null && Mathf.Abs(param) > 1e-6f) f = (___minExposure - written) / param;
                NightViewRead.Read(___mainCamera, out var mode, out var active, out var sight);
                bool zeroExpected = mode == CameraMode.Map || !active;
                // only build on what the game really wrote this frame
                bool wrote = ___submarineLighting != null
                             && !(!float.IsNaN(f) && (f < -0.001f || f > 1.001f))
                             && !(zeroExpected && Mathf.Abs(___minExposure - written) > 1e-4f);
                if (wrote && ___autoExposure != null)
                {
                    float gain = NightCfg.GainFor(sight) * NightCfg.ExteriorGain;
                    if (Mathf.Abs(gain - 1f) >= 1e-5f)
                    {
                        float ff = float.IsNaN(f) ? 0f : Mathf.Clamp01(f);
                        float target = ___minExposure - gain * ff * param;
                        float ceiling = ___autoExposure.maxLuminance.value - 0.01f;
                        if (target > ceiling)
                        {
                            target = ceiling;
                            if (!clampWarned) { clampWarned = true; Util.Log("Nacht: Aufhellung an der Obergrenze " + ___autoExposure.maxLuminance.value.ToString("F3") + " gekappt (Faktor " + gain.ToString("F2") + ")"); }
                        }
                        ___autoExposure.minLuminance.value = target;
                    }
                }
                NightGrading.Tick(sight, mode, wrote ? f : float.NaN);
                NightInterior.Tick(___mainCamera);
                if (Settings.B("NightLog") && Time.unscaledTime >= nextLog)
                {
                    nextLog = Time.unscaledTime + 5f;
                    Util.Log("Nacht: Ansicht " + mode + "/" + sight + ", Nacht-Anteil " + (float.IsNaN(f) ? "-" : f.ToString("F2")) + ", Spiel schrieb " + (wrote ? "ja" : "nein") + ", minLuminance " + (___autoExposure != null ? ___autoExposure.minLuminance.value.ToString("F3") : "-"));
                }
            }
            catch (Exception e) { Util.Log("Nacht: " + e.Message); }
        }

        private static float Val(GameDataParameter p) { try { return p != null ? p.Value : 0f; } catch { return 0f; } }
    }
}
