using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Spaces;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI.CharacterSwitchUI;
using GameActivator = UBOAT.Game.Scene.Characters.Activators.Activator;

namespace UboatToolkit.Modules
{
    // Light modes: each of the three switch positions (Normal / Rot / Blau) gets its own style:
    // 0 = Spiel-Original, 1 = warm (dimmed warm white light), 2 = Notlicht (dimmed red light).
    // Plus warm bulb colours, red conning tower at night, own emergency colour, crew effects,
    // automatic emergency light after damage, automatic silent running and new switch labels.
    internal static class LightCfg
    {
        public const int Original = 0, Warm = 1, Emergency = 2;
        public const float MeasuredGain = 1.42f;
        public static readonly string[] StyleKeys = { "LightStyleNormal", "LightStyleRed", "LightStyleBlue" };
        public static readonly string[] StyleNames = { "Spiel-Original", "warm", "Notlicht" };
        public static readonly string[] PosNames = { "Normal (weiß)", "Rot", "Blau" };

        public static bool On => Settings.B("LightModes");
        public static int Style(int pos) => pos < 0 || pos > 2 ? Original : Mathf.Clamp(Mathf.RoundToInt(Settings.F(StyleKeys[pos])), 0, 2);
        public static int Level(string key) => Mathf.Clamp(Mathf.RoundToInt(Settings.F(key)), 0, 100);
        public static Color Col(string prefix) =>
            new Color(Mathf.Clamp01(Settings.F(prefix + "R") / 255f), Mathf.Clamp01(Settings.F(prefix + "G") / 255f), Mathf.Clamp01(Settings.F(prefix + "B") / 255f), 1f);

        // Output percentage (0 = off, 100 = game's full light) -> preset weight the game reads.
        public static float Weight(int pct, float gain)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            if (!(gain > 0.41f)) gain = 1f;
            return (0.4f + pct / 100f * (gain - 0.4f)) / gain;
        }

        // The current switch position, -1 without a boat.
        public static int Pos => LightLive.Lighting == null ? -1 : (int)LightLive.Lighting.CurrentPreset;
        public static int CurStyle => Style(Pos);
        // "Night light": a dimmed warm light on the red or blue position.
        public static bool NightStyle => On && Pos > 0 && CurStyle == Warm;
        public static bool EmergencyStyle => On && Pos >= 0 && CurStyle == Emergency;

        // Fingerprint of everything that changes the look, to re-tint after a menu change.
        public static string Stamp()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var k in new[] { "LightModes", "LightWarm", "LightLevelNormal", "LightLevelNight", "LightLevelEmerg", "LightRedTower", "LightCustomEmerg",
                                      "LightStyleNormal", "LightStyleRed", "LightStyleBlue",
                                      "LightDayR", "LightDayG", "LightDayB", "LightNightR", "LightNightG", "LightNightB",
                                      "LightTowerR", "LightTowerG", "LightTowerB", "LightEmergR", "LightEmergG", "LightEmergB" })
                sb.Append(Settings.F(k)).Append('|');
            return sb.ToString();
        }
    }

    internal static class LightLook
    {
        private static readonly FieldInfo PresetsF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "presets");
        private static readonly FieldInfo SwappingF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "swappingPreset");
        private static readonly FieldInfo ProbeSetsF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "reflectionProbeSets");
        private static readonly FieldInfo ControllersF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightControllers");
        private static readonly FieldInfo WhiteF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightsColorMultiplier");
        private static readonly FieldInfo RedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "alarmLightsColorMultiplier");
        private static readonly FieldInfo BlueF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "silentRunLightsColorMultiplier");
        private static readonly MethodInfo GameApplyColour = AccessTools.Method(typeof(PlayerShipInteriorLighting), "ApplyColorMultiplier");
        private static readonly FieldInfo MultipliedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "multipliedPresets");
        private static readonly FieldInfo PresetMultipliersF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "presetMultipliers");
        private static readonly FieldInfo IntensityF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightsIntensityMultiplier");
        private static readonly FieldInfo BulbF = AccessTools.Field(typeof(LightController), "normalColor");
        private static readonly FieldInfo PresetChangedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "PresetChanged");

        private static PlayerShipInteriorLighting gainFor, reportedFor, towerFor, towerLoggedFor;
        private static float whiteGain = LightCfg.MeasuredGain, redGain = LightCfg.MeasuredGain;
        private static bool[] towerMask;
        private static int fadeFrame = -10;

        public static void ResetTower() { towerFor = null; towerMask = null; }

        public static void RaisePresetChanged(PlayerShipInteriorLighting l)
        {
            if (PresetChangedF == null || l == null) return;
            (PresetChangedF.GetValue(l) as Action)?.Invoke();
        }

        private static void ReadGains(PlayerShipInteriorLighting l)
        {
            if (ReferenceEquals(gainFor, l)) return;
            gainFor = l;
            whiteGain = redGain = LightCfg.MeasuredGain;
            try
            {
                float k = IntensityF == null ? LightCfg.MeasuredGain : (float)IntensityF.GetValue(l);
                var pm = PresetMultipliersF?.GetValue(l) as float[];
                whiteGain = k * (pm != null && pm.Length > 0 ? pm[0] : 1f);
                redGain = k * (pm != null && pm.Length > 1 ? pm[1] : 1f);
            }
            catch (Exception e) { Util.Log("Licht: Helligkeits-Faktoren nicht lesbar, nehme " + LightCfg.MeasuredGain + ": " + e.Message); }
        }

        // Which light set a position lights up with its style.
        public static int SetFor(int pos)
        {
            int s = LightCfg.Style(pos);
            if (s == LightCfg.Warm) return 0;
            if (s == LightCfg.Emergency) return 1;
            return Mathf.Clamp(pos, 0, 2);
        }

        public static float[] Targets(PlayerShipInteriorLighting l, int pos)
        {
            var t = new float[3];
            pos = Mathf.Clamp(pos, 0, 2);
            if (!LightCfg.On) { t[pos] = 1f; return t; }
            ReadGains(l);
            int s = LightCfg.Style(pos);
            if (s == LightCfg.Warm) t[0] = LightCfg.Weight(LightCfg.Level(pos == 0 ? "LightLevelNormal" : "LightLevelNight"), whiteGain);
            else if (s == LightCfg.Emergency) t[1] = LightCfg.Weight(LightCfg.Level("LightLevelEmerg"), redGain);
            else t[pos] = 1f;
            return t;
        }

        public static void ReportGame(PlayerShipInteriorLighting l)
        {
            if (l == null || ReferenceEquals(reportedFor, l)) return;
            reportedFor = l;
            Util.Safe("Licht.Report", () =>
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var pm = PresetMultipliersF?.GetValue(l) as float[];
                var sb = new System.Text.StringBuilder("Licht (Spielwerte): Set-Faktoren ");
                sb.Append(pm == null ? "?" : string.Join("/", Array.ConvertAll(pm, f => f.ToString("0.###", ic))));
                sb.Append(", Grundhelligkeit ").Append(IntensityF == null ? "?" : ((float)IntensityF.GetValue(l)).ToString("0.###", ic));
                sb.Append(", weiß ").Append(Rgb(WhiteF, l)).Append(", rot ").Append(Rgb(RedF, l)).Append(", blau ").Append(Rgb(BlueF, l));
                var ctrls = ControllersF?.GetValue(l) as LightController[];
                if (ctrls != null && BulbF != null)
                {
                    Color w = Color.black, r = Color.black; int n = 0;
                    foreach (var c in ctrls) { if (c == null) continue; w += (Color)BulbF.GetValue(c); r += c.AlarmColor; n++; }
                    if (n > 0) sb.Append(", Lampen ").Append(n).Append(" Schnitt weiß ").Append(Rgb(w / n)).Append(" rot ").Append(Rgb(r / n));
                }
                int missing = 0;
                foreach (object o in new object[] { PresetsF, SwappingF, ProbeSetsF, ControllersF, WhiteF, RedF, BlueF, GameApplyColour, MultipliedF })
                    if (o == null) missing++;
                sb.Append(missing == 0 ? " | alle Spiel-Felder gefunden" : " | FEHLENDE Spiel-Felder: " + missing);
                Util.Log(sb.ToString());
            });
        }

        private static string Rgb(FieldInfo f, object o) => f == null ? "?" : Rgb((Color)f.GetValue(o));
        private static string Rgb(Color c)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return c.r.ToString("0.###", ic) + "," + c.g.ToString("0.###", ic) + "," + c.b.ToString("0.###", ic);
        }

        public static bool FadeAlive => Time.frameCount - fadeFrame <= 2;
        public static bool Swapping(PlayerShipInteriorLighting l) => l != null && SwappingF != null && (bool)SwappingF.GetValue(l);

        // Brings the weights back to our target if the game moved them (e.g. its own depth switch).
        public static void Reconcile(PlayerShipInteriorLighting l)
        {
            if (l == null || PresetsF == null || FadeAlive || Swapping(l)) return;
            var target = Targets(l, (int)l.CurrentPreset);
            var w = (float[])PresetsF.GetValue(l);
            bool off = false;
            for (int i = 0; i < Mathf.Min(w.Length, 3); i++) if (Mathf.Abs(w[i] - target[i]) > 0.001f) off = true;
            if (!off) return;
            StartFade(l);
            if (LightCfg.On) ApplyTint(l);
        }

        public static void StartFade(PlayerShipInteriorLighting l)
        {
            if (l == null || PresetsF == null || SwappingF == null || FadeAlive) return;
            fadeFrame = Time.frameCount;
            l.StartCoroutine(Fade(l));
        }

        private static IEnumerator Fade(PlayerShipInteriorLighting l)
        {
            SwappingF.SetValue(l, true);
            while (l != null)
            {
                fadeFrame = Time.frameCount;
                int pos = (int)l.CurrentPreset;
                var target = Targets(l, pos);
                var p = (float[])PresetsF.GetValue(l);
                bool done = true;
                for (int i = 0; i < 3; i++)
                {
                    float step = Time.deltaTime;
                    if (p[i] < target[i]) p[i] = Mathf.Min(p[i] + step, target[i]);
                    else if (p[i] > target[i]) p[i] = Mathf.Max(p[i] - step, target[i]);
                    if (p[i] != target[i]) done = false;
                }
                SetProbes(l, LightCfg.On ? SetFor(pos) : pos);
                l.ApplyLightControllersPresets();
                if (done) break;
                yield return null;
            }
            fadeFrame = -10;
            if (l != null) SwappingF.SetValue(l, false);
        }

        private static void SetProbes(PlayerShipInteriorLighting l, int active)
        {
            var sets = ProbeSetsF?.GetValue(l) as GameObject[];
            if (sets == null) return;
            for (int i = 0; i < sets.Length; i++)
                if (sets[i] != null && sets[i].activeSelf != (i == active)) sets[i].SetActive(i == active);
        }

        public static void ApplyTint(PlayerShipInteriorLighting l)
        {
            var ctrls = ControllersF?.GetValue(l) as LightController[];
            if (ctrls == null) return;
            int pos = (int)l.CurrentPreset;
            int s = LightCfg.Style(pos);
            Color white = WhiteF == null ? Color.white : (Color)WhiteF.GetValue(l);
            Color red = RedF == null ? Color.white : (Color)RedF.GetValue(l);
            Color blue = BlueF == null ? Color.white : (Color)BlueF.GetValue(l);
            Color c;
            if (s == LightCfg.Emergency) c = Settings.B("LightCustomEmerg") ? Color.white : red;
            else if (s == LightCfg.Warm) c = !Settings.B("LightWarm") ? white : LightCfg.Col(pos == 0 ? "LightDay" : "LightNight");
            else c = pos == 0 ? white : pos == 1 ? red : blue;

            bool[] tower = (s == LightCfg.Warm && pos > 0 && Settings.B("LightRedTower")) ? TowerMask(l, ctrls) : null;
            Color towerCol = LightCfg.Col("LightTower");
            for (int i = 0; i < ctrls.Length; i++)
                if (ctrls[i] != null) ctrls[i].ApplyColorMultiplier(tower != null && i < tower.Length && tower[i] ? towerCol : c);
            Push(l, ctrls);
        }

        private static void Push(PlayerShipInteriorLighting l, LightController[] ctrls)
        {
            var multiplied = MultipliedF?.GetValue(l) as float[];
            var presets = PresetsF?.GetValue(l) as float[];
            bool remix = multiplied != null && presets != null && multiplied.Length >= 3 && presets.Length >= 3 && multiplied[0] + multiplied[1] + multiplied[2] > 0f;
            foreach (var c in ctrls)
            {
                if (c == null || c.Light == null) continue;   // not prepared yet
                if (remix) c.UpdatePresets(multiplied, presets); else c.UpdatePresets();
            }
        }

        public static void RestoreGameTint(PlayerShipInteriorLighting l)
        {
            if (l == null || GameApplyColour == null) return;
            GameApplyColour.Invoke(l, null);   // our postfix does nothing while the mode is off
            var ctrls = ControllersF?.GetValue(l) as LightController[];
            if (ctrls != null) Push(l, ctrls);
        }

        private static bool[] TowerMask(PlayerShipInteriorLighting l, LightController[] ctrls)
        {
            if (ReferenceEquals(towerFor, l) && towerMask != null && towerMask.Length == ctrls.Length) return towerMask;
            var mask = new bool[ctrls.Length];
            int n = 0;
            var interior = l.GetComponent<PlayerShipInterior>();
            Compartment ct = interior == null ? null : interior.GetCompartment("Conning Tower Interior");
            if (ct == null)
            {
                if (!ReferenceEquals(towerLoggedFor, l)) { towerLoggedFor = l; Util.Log("Licht: Turm nicht gefunden (0 von " + ctrls.Length + " Lampen)"); }
                return mask;
            }
            for (int i = 0; i < ctrls.Length; i++)
                if (ctrls[i] != null && ct.Contains(ctrls[i].transform.position)) { mask[i] = true; n++; }
            towerFor = l; towerMask = mask;
            Util.Log("Licht: Turm hat " + n + " von " + ctrls.Length + " Lampen");
            return mask;
        }
    }

    // Live state: the lighting, the switch, damage, saving, settings changes.
    internal static class LightLive
    {
        public static PlayerShipInteriorLighting Lighting;
        public static LightSwitch Switch;
        private static SavesManager saves;
        private static readonly MethodInfo OnPresetChanged = AccessTools.Method(typeof(LightSwitch), "OnLightPresetChanged");
        private static float nextTick, nextDamage, nextReconcile, saveHoldUntil = -1f;
        private static string stamp;
        private static bool? wasOn;
        public static bool Damaged;
        public static bool Saving => (saves != null && saves.IsSaving) || Time.unscaledTime < saveHoldUntil;

        public static void Remember(PlayerShipInteriorLighting l) { if (l != null) Lighting = l; }

        public static void RefreshCrewNoise()
        {
            if (Switch == null || Lighting == null || OnPresetChanged == null) return;
            OnPresetChanged.Invoke(Switch, new object[] { Lighting.CurrentPreset });
        }

        public static PlayerCrew Crew()
        {
            try { return InjectionFramework.Instance.GetInstance<PlayerCrew>(); } catch { return null; }
        }

        // Called from the light switch every frame (cheap: work is throttled).
        public static void Frame(PlayerShip ship, SavesManager sm)
        {
            if (sm != null) saves = sm;
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + 1f;
            LightEffects.SyncPerformance(false);
            LightAuto.TickSilent(ship);
            if (now >= nextDamage) { nextDamage = now + 6.1f; RefreshDamage(ship); }
        }

        private static void RefreshDamage(PlayerShip ship)
        {
            if (ship == null || ship.Equipment == null) { Damaged = false; return; }
            float missing = 0f; int leaks = 0;
            var list = ship.Equipment;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || e.MaxHealth == null) continue;
                float max = e.MaxHealth.Value;
                if (max <= 0f) continue;
                missing += max - e.Health;
                var le = e as LeakableEquipment;
                if (le != null && le.IsLeaking && (le.Compartment == null || !le.Compartment.Evacuation)) leaks++;
            }
            bool was = Damaged;
            Damaged = leaks > Settings.F("LightLeaks") || missing > Settings.F("LightDamage");
            if (was != Damaged) Util.Log("Licht: Schaden " + (Damaged ? "SCHWER" : "ok") + " (fehlende Haltbarkeit " + missing.ToString("F1") + ", Lecks " + leaks + ")");
        }

        public static void BeforeSave()
        {
            saveHoldUntil = Time.unscaledTime + 1.5f;
            LightEffects.SyncPerformance(true);
            LightAction.RemoveQueued();
        }

        // Once per second: notices menu changes (on/off, styles, colours) and keeps the weights right.
        public static void Reconcile()
        {
            float now = Time.unscaledTime;
            if (now < nextReconcile) return;
            nextReconcile = now + 1f;
            bool on = LightCfg.On;
            if (wasOn != on)
            {
                bool first = wasOn == null;
                wasOn = on;
                if (!first || on) Switched(on);
            }
            if (Lighting == null) return;
            string s = LightCfg.Stamp();
            if (on && s != stamp)
            {
                stamp = s;
                Util.Safe("Licht.Tint", () => { LightLook.ApplyTint(Lighting); LightLabels.Apply(); LightLook.RaisePresetChanged(Lighting); });
            }
            Util.Safe("Licht.Reconcile", () => LightLook.Reconcile(Lighting));
        }

        private static void Switched(bool on)
        {
            Util.Safe("Licht.Switch", () =>
            {
                if (on) LightLabels.Apply(); else LightLabels.Restore();
                LightEffects.SyncPerformance(!on);
                RefreshCrewNoise();
                stamp = null;
                if (Lighting == null) return;
                LightLook.StartFade(Lighting);
                if (on) LightLook.ApplyTint(Lighting); else LightLook.RestoreGameTint(Lighting);
                LightLook.RaisePresetChanged(Lighting);
            });
        }

        // Buttons in the F10 tab: switch the light position right now.
        public static void SwitchTo(int pos)
        {
            Util.Safe("Licht.SwitchTo", () =>
            {
                var ship = Util.PlayerShip;
                var sw = Switch != null ? Switch : ship?.GetComponentInChildren<LightSwitch>(true);
                if (sw != null) sw.SetColor((LightController.Preset)pos, quiet: true);
                else Lighting?.SetPreset((LightController.Preset)pos);
                if (Lighting != null) Lighting.WasPresetSetManually = true;
            });
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "SetPreset")]
    internal static class Light_SetPresetPatch
    {
        private static bool Prefix(PlayerShipInteriorLighting __instance, LightController.Preset newPreset, ref LightController.Preset ___currentPreset, ref float ___presetChangeTime)
        {
            if (!LightCfg.On) return true;
            var oldPreset = ___currentPreset;
            float oldTime = ___presetChangeTime;
            try
            {
                LightLive.Remember(__instance);
                bool changed = ___currentPreset != newPreset;
                ___currentPreset = newPreset;
                ___presetChangeTime = float.PositiveInfinity;
                LightLook.StartFade(__instance);
                if (changed)
                {
                    LightLook.ApplyTint(__instance);
                    LightEffects.SyncPerformance(false);
                    try { LightLook.RaisePresetChanged(__instance); } catch (Exception e) { Util.Log("Licht: Schalter-Anzeige nicht aktualisiert: " + e.Message); }
                }
                return false;
            }
            catch (Exception e)
            {
                Util.Log("Licht.SetPreset: " + e.Message);
                ___currentPreset = oldPreset;
                ___presetChangeTime = oldTime;
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "Start")]
    internal static class Light_StartPatch
    {
        private static void Postfix(PlayerShipInteriorLighting __instance)
        {
            LightLive.Remember(__instance);
            LightLook.ResetTower();
            LightLook.ReportGame(__instance);
            if (!LightCfg.On) return;
            Util.Safe("Licht.Start", () => { LightLook.StartFade(__instance); LightLook.ApplyTint(__instance); });
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "ApplyColorMultiplier")]
    internal static class Light_ApplyColourPatch
    {
        private static void Postfix(PlayerShipInteriorLighting __instance)
        {
            if (LightCfg.On) Util.Safe("Licht.Tint", () => LightLook.ApplyTint(__instance));
        }
    }

    // Own emergency colour: swaps the red bulb colour while the lights are mixed.
    [HarmonyPatch(typeof(LightController), "UpdatePresets", new[] { typeof(float[]), typeof(float[]) })]
    internal static class Light_EmergencyColourPatch
    {
        private static void Prefix(LightController __instance, out Color? __state)
        {
            __state = null;
            if (!LightCfg.On || !Settings.B("LightCustomEmerg") || LightCfg.CurStyle != LightCfg.Emergency) return;
            try { __state = __instance.AlarmColor; __instance.AlarmColor = LightCfg.Col("LightEmerg"); } catch { }
        }
        private static Exception Finalizer(LightController __instance, Color? __state, Exception __exception)
        {
            if (__state.HasValue) try { __instance.AlarmColor = __state.Value; } catch { }
            return __exception;
        }
    }

    internal static class LightEffects
    {
        // Night light: crew performance lower by x points ("Base" delta modifier, back to 1 before saving).
        public static void SyncPerformance(bool forceGame)
        {
            var crew = LightLive.Crew();
            if (crew == null) return;
            bool night = !forceGame && !LightLive.Saving && LightCfg.NightStyle;
            float target = night ? 1f - Settings.F("LightNightPerf") / 100f : 1f;
            for (int i = 0; i < crew.Count; i++)
            {
                var pc = crew[i];
                if (pc == null || pc.Performance == null) continue;
                var m = pc.Performance.GetDeltaModifier("Base");
                if (m != null && m.Value != target) m.Value = target;
            }
        }
    }

    // The game ties oxygen / lookout effects to the brightest light set. With our styles the
    // switch POSITION keeps its tactical meaning: Blau = silent oxygen use, Rot = night vision.
    [HarmonyPatch(typeof(LightSwitch), "Update")]
    internal static class Light_SwitchUpdatePatch
    {
        private static void Postfix(LightSwitch __instance, Resource ___oxygen, Modifier ___oxygenUseModifier, Parameter ___silentRunOxygenUsageScale,
                                    Modifier[] ___sightRangeModifiers, Parameter ___redLightObservationModifier,
                                    PlayerShipInteriorLighting ___lighting, Weather ___weather, SavesManager ___savesManager)
        {
            LightLive.Remember(___lighting);
            LightLive.Reconcile();
            if (!LightCfg.On) return;
            try
            {
                var ship = __instance.ParentEntity as PlayerShip;
                if (ship == null || ___lighting == null) return;
                LightLive.Switch = __instance;
                var pos = ___lighting.CurrentPreset;
                if (pos == LightController.Preset.SilentRun && ___oxygen != null && ___oxygenUseModifier != null && ___silentRunOxygenUsageScale != null && ___oxygen.Gain.Value < 0f)
                    ___oxygenUseModifier.Value = ___silentRunOxygenUsageScale.Value;
                if (___sightRangeModifiers != null && ___redLightObservationModifier != null && ___weather != null)
                {
                    float f = 1f;
                    float vis = ___weather.SunPositionVisibilityFactor * ___weather.CloudinessVisibilityFactor * ___weather.LightingsVisibilityFactor;
                    if (pos == LightController.Preset.Alarm && vis > 0f) f = 1f + (1f / vis - 1f) * ___redLightObservationModifier.Value;
                    foreach (var m in ___sightRangeModifiers) if (m != null) m.Value = f;
                }
                LightLive.Frame(ship, ___savesManager);
            }
            catch (Exception e) { Util.Log("Licht.Switch: " + e.Message); }
        }
    }

    // The blue light's crew-noise cut belongs to the blue light: gone when Blau shows warm or Notlicht.
    [HarmonyPatch(typeof(LightSwitch), "OnLightPresetChanged")]
    internal static class Light_CrewNoisePatch
    {
        private static void Postfix(Modifier ___crewNoiseModifier, LightController.Preset preset)
        {
            if (!LightCfg.On || ___crewNoiseModifier == null) return;
            if (LightCfg.Style((int)preset) != LightCfg.Original) ___crewNoiseModifier.Value = 1f;
        }
    }

    // Night light: sleeping crew recover faster.
    [HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.SleepBehaviour), "OnBehaviourUpdate")]
    internal static class Light_SleepPatch
    {
        private static Need scaled;
        private static float saved;
        private static void Prefix(Need ___fatigue)
        {
            scaled = null;
            if (___fatigue == null || !LightCfg.NightStyle) return;
            float f = 1f + Settings.F("LightNightSleep") / 100f;
            if (f == 1f) return;
            saved = ___fatigue.satiationSpeed;
            ___fatigue.satiationSpeed = saved * f;
            scaled = ___fatigue;
        }
        private static Exception Finalizer(Exception __exception)
        {
            if (scaled != null) { scaled.satiationSpeed = saved; scaled = null; }
            return __exception;
        }
    }

    // Emergency light: the electric motor draws less from the batteries.
    [HarmonyPatch(typeof(PlayerShipEngine), "DoUpdate")]
    internal static class Light_ElectricPatch
    {
        private static float lastWritten = float.NaN;
        private static void Postfix(PlayerShipEngine __instance, Modifier ___energyModifier, PlayerShip ___playerShip)
        {
            if (!LightCfg.On || ___energyModifier == null || ___playerShip == null) return;
            if (!ReferenceEquals(__instance, ___playerShip.ElectricEngine)) return;
            if (!LightCfg.EmergencyStyle) { lastWritten = float.NaN; return; }
            float v = ___energyModifier.Value;
            if (v == lastWritten) return;
            lastWritten = v * (1f - Settings.F("LightEmergPower") / 100f);
            ___energyModifier.Value = lastWritten;
        }
    }

    [HarmonyPatch(typeof(SavesManager), "Save")]
    internal static class Light_BeforeSavePatch
    {
        private static void Prefix() { Util.Safe("Licht.BeforeSave", LightLive.BeforeSave); }
    }

    internal static class LightAuto
    {
        private static bool weEnabled, suppressed;

        // Auto silent running: on below the swap depth, off again above it. A manual "off" while deep is respected.
        public static void TickSilent(PlayerShip ship)
        {
            if (ship == null) return;
            ISilentRunningController src;
            try { src = InjectionFramework.Instance.GetInstance<ISilentRunningController>(); } catch { return; }
            if (src == null) return;
            bool autoOn = LightCfg.On && Settings.B("LightAutoSilent");
            bool isOn = src.IsSilentRunningEnabled;
            float swap = Settings.F("LightSwapDepth");
            bool deep = ship.DeckDepth > swap && ship.TargetDepth > swap;
            bool shallow = ship.DeckDepth < swap && ship.TargetDepth < swap;
            if (weEnabled && !isOn) { weEnabled = false; suppressed = true; }
            if (shallow) suppressed = false;
            if (!autoOn) { weEnabled = false; return; }
            int step = 0;
            if (shallow && weEnabled && isOn) { weEnabled = false; step = -1; }
            else if (deep && !isOn && !suppressed && ship.DepthPreset != DepthPreset.HydrophoneCheck && (!Settings.B("LightSilentAlarmOnly") || ship.Alarmed))
            { weEnabled = true; step = 1; }
            if (step == 0) return;
            src.SetSilentRunningEnabled(step > 0, true);
            Util.Log("Licht: Schleichfahrt automatisch " + (step > 0 ? "AN" : "AUS") + " bei " + ship.DeckDepth.ToString("F0") + " m");
        }

        // The position that shows Notlicht (Blau first, then Rot); Blau if none does.
        public static LightController.Preset EmergencyPos()
        {
            if (LightCfg.Style(2) == LightCfg.Emergency) return LightController.Preset.SilentRun;
            if (LightCfg.Style(1) == LightCfg.Emergency) return LightController.Preset.Alarm;
            return LightController.Preset.SilentRun;
        }
    }

    // The game's "lighting management" (crew switch the light themselves).
    [HarmonyPatch(typeof(AutomaticActionsController), "GetPreferredLighting")]
    internal static class Light_PreferredPatch
    {
        private static void Postfix(ref LightController.Preset __result, Weather ___weather)
        {
            if (!LightCfg.On) return;
            try
            {
                if (Settings.B("LightAutoEmerg") && LightLive.Damaged) { __result = LightAuto.EmergencyPos(); return; }
                var game = __result;
                bool night = LightCfg.Style(1) == LightCfg.Original ? game == LightController.Preset.Alarm : (___weather != null && ___weather.IsNight);
                if (game == LightController.Preset.SilentRun && LightCfg.Style(2) == LightCfg.Original) return;   // game: blue while silent running
                __result = night ? LightController.Preset.Alarm : LightController.Preset.Surface;
            }
            catch (Exception e) { Util.Log("Licht.Preferred: " + e.Message); }
        }
    }

    // Light switch action "Auto silent running" (toggles the setting). Never saved in the game state.
    [NonSerializedInGameState]
    internal sealed class ToolkitAutoSilentAction : DeviceUseAction
    {
        public ToolkitAutoSilentAction(GameDataRow data) : base(data) { Init(); }
        public ToolkitAutoSilentAction(ToolkitAutoSilentAction source, GameActivator activator) : base(source, activator) { Init(); }
        private void Init() { StateDescription = LocalizedString.CreateUnlocalized("Stellt automatische Schleichfahrt um"); }

        protected override void StartMainCharacter(PlayableCharacter character)
        {
            UseDevice(character, null, null, int.MinValue, null, true, SlotUsageFlags.AllowUnderWaterAlways);
        }
        protected override ActionState GetState(GameActivator activator, PlayableCharacter character)
        {
            if (!LightCfg.On) return ActionState.Hidden;
            return base.GetState(activator, character);
        }
        public override string GetLocalizationKeyword(GameActivator activator, PlayableCharacter character) => "Auto Silent Running";
        public override LocalizedString GetLocalizedName(GameActivator activator, PlayableCharacter character, ActionActivationContext context) =>
            LocalizedString.CreateUnlocalized(Settings.B("LightAutoSilent") ? "Auto-Schleichfahrt: AN (klicken = aus)" : "Auto-Schleichfahrt: AUS (klicken = an)");
        public override string GetLocalizedDescription(GameActivator activator, PlayableCharacter character) =>
            "Unter " + Settings.F("LightSwapDepth") + " m schaltet die Crew Schleichfahrt selbst ein, beim Auftauchen wieder aus.";
        public override string GetLocalizedState() => "Stellt automatische Schleichfahrt um";
        public override string GetIconName(GameActivator activator, PlayableCharacter character) => "Switch Light to SilentRun";
        public override int GetSortingPriority(GameActivator activator, PlayableCharacter character) => 3;
        protected override void OnDeviceUsed()
        {
            var o = Settings.Get("LightAutoSilent");
            if (o != null) { o.Value = o.On ? 0 : 1; Settings.Save(); }
            CompleteAction();
        }
    }

    internal static class LightAction
    {
        public const string ActionName = "UboatToolkit.AutoSilentRunning";
        private const string Tag = "Light Switch";

        public static void RemoveQueued()
        {
            var crew = LightLive.Crew();
            if (crew == null) return;
            var buffer = new List<CharacterAction>();
            for (int i = 0; i < crew.Count; i++)
            {
                var pc = crew[i];
                if (pc == null) continue;
                if (pc.Action is ToolkitAutoSilentAction) pc.StopCurrentAction(true);
                RemoveFrom(pc.ActionQueue, buffer);
            }
            RemoveFrom(crew.GlobalActionQueue, buffer);
        }

        private static void RemoveFrom(ActionQueue q, List<CharacterAction> buffer)
        {
            if (q == null || q.Actions == null) return;
            buffer.Clear();
            foreach (CharacterAction a in q.Actions) if (a is ToolkitAutoSilentAction) buffer.Add(a);
            foreach (var a in buffer) q.Remove(a, false);
        }

        public static void AddTo(Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> map, string key, CharacterActionsDatabase.CharacterActionInfo info)
        {
            if (!map.TryGetValue(key, out var list)) { list = new List<CharacterActionsDatabase.CharacterActionInfo>(); map[key] = list; }
            list.Add(info);
        }
    }

    [HarmonyPatch(typeof(CharacterActionsDatabase), "LoadActions")]
    internal static class Light_RegisterActionPatch
    {
        private static void Postfix(Dictionary<string, CharacterActionsDatabase.CharacterActionInfo> ___actionsByName,
                                    Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> ___actionsByActivatorTag,
                                    Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> ___actionsByCharacterTag)
        {
            Util.Safe("Licht.Action", () =>
            {
                var action = new ToolkitAutoSilentAction((GameDataRow)null);
                var info = new CharacterActionsDatabase.CharacterActionInfo(LightAction.ActionName, action, new[] { "Light Switch" }, new[] { "*" }, ActionContext.Normal | ActionContext.Queue, "", "");
                ___actionsByName[LightAction.ActionName] = info;
                LightAction.AddTo(___actionsByActivatorTag, "Light Switch", info);
                LightAction.AddTo(___actionsByCharacterTag, "*", info);
                Util.Log("Licht: Aktion „Auto-Schleichfahrt“ am Lichtschalter angemeldet");
            });
        }
    }

    // Switch labels follow the chosen style (e.g. "Rot" shows "Nachtlicht" when it is warm).
    internal static class LightLabels
    {
        private static readonly FieldInfo StringsF = AccessTools.Field(typeof(Locale), "strings");
        private static readonly FieldInfo ComparerF = AccessTools.Field(typeof(Locale), "stringComparer");
        private static readonly Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.Ordinal);

        private static Dictionary<string, string> Table()
        {
            var locale = ScriptableObjectSingleton.LoadSingleton<Locale>();
            if (locale == null || StringsF == null || ComparerF == null) return null;
            var cmp = ComparerF.GetValue(locale) as Locale.LocaleStringComparer;
            if (cmp != null) cmp.comparisonBufferLen = 0;
            return StringsF.GetValue(locale) as Dictionary<string, string>;
        }

        private static void Set(Dictionary<string, string> t, string key, string value)
        {
            if (!originals.ContainsKey(key)) originals[key] = t.TryGetValue(key, out var old) ? old : null;
            t[key] = value;
        }
        private static void Back(Dictionary<string, string> t, string key)
        {
            if (!originals.TryGetValue(key, out var old)) return;
            if (old == null) t.Remove(key); else t[key] = old;
            originals.Remove(key);
        }

        public static void Apply()
        {
            if (!LightCfg.On || !Settings.B("LightLabels")) { Restore(); return; }
            Util.Safe("Licht.Labels", () =>
            {
                var t = Table();
                if (t == null) return;
                string[] ui = { "UI/CharacterSwitchUI/White Lighting", "UI/CharacterSwitchUI/Red Lighting", "UI/CharacterSwitchUI/Blue Lighting" };
                string[] act = { "Activators/Switch Light to Surface", "Activators/Switch Light to Alarm", "Activators/Switch Light to SilentRun" };
                string night = "Gedämpftes warmes Licht für die Nacht: die Crew arbeitet etwas langsamer und schläft besser.";
                string emerg = "Gedämpftes rotes Notlicht: weniger Strom für die E-Maschinen.";
                for (int p = 1; p < 3; p++)
                {
                    int s = LightCfg.Style(p);
                    if (s == LightCfg.Original) { Back(t, ui[p]); Back(t, act[p]); Back(t, act[p] + " Description"); Back(t, act[p] + " Short Description"); continue; }
                    bool warm = s == LightCfg.Warm;
                    Set(t, ui[p], warm ? "Nacht" : "Notlicht");
                    Set(t, act[p], warm ? "Nachtlicht einschalten" : "Notlicht einschalten");
                    Set(t, act[p] + " Description", warm ? night : emerg);
                    if (t.ContainsKey(act[p] + " Short Description") || originals.ContainsKey(act[p] + " Short Description"))
                        Set(t, act[p] + " Short Description", warm ? night : emerg);
                }
                Set(t, "Activators/Auto Silent Running", "Auto-Schleichfahrt");
                Set(t, "Activators/Auto Silent Running Description", "Unter " + Settings.F("LightSwapDepth") + " m schaltet die Crew Schleichfahrt selbst ein, beim Auftauchen wieder aus.");
            });
        }

        public static void Restore()
        {
            if (originals.Count == 0) return;
            Util.Safe("Licht.LabelsBack", () =>
            {
                var t = Table();
                if (t == null) return;
                foreach (var kv in originals) { if (kv.Value == null) t.Remove(kv.Key); else t[kv.Key] = kv.Value; }
                originals.Clear();
            });
        }
    }

    [HarmonyPatch(typeof(CharacterSwitchLightingUI), "Start")]
    internal static class Light_SwitchPanelPatch
    {
        private static void Prefix() { if (LightCfg.On) LightLabels.Apply(); }
    }

    // F10 tab "Licht": style per switch position, colours, switch buttons. Levels and effects follow below (generic list).
    internal static class LightTab
    {
        public static void Draw()
        {
            Imgui.Label("Licht-Modi: jede Schalter-Stellung bekommt ihren eigenen Stil. Erst oben „Licht-Modi an“ anhaken (unten in der Liste).");
            if (!LightCfg.On) Imgui.Label("(Licht-Modi sind gerade AUS: das Spiel zeigt sein normales Licht.)");
            for (int p = 0; p < 3; p++)
            {
                var o = Settings.Get(LightCfg.StyleKeys[p]);
                if (o == null) continue;
                Imgui.BeginH();
                Imgui.Label("Stellung " + LightCfg.PosNames[p] + ":", 160);
                for (int s = 0; s < 3; s++)
                    if (Imgui.Button((LightCfg.Style(p) == s ? "> " : "") + LightCfg.StyleNames[s], 150)) { o.Value = s; Settings.Save(); }
                Imgui.EndH();
            }
            Imgui.Space(6);
            Imgui.BeginH();
            Imgui.Label("Jetzt umschalten:", 160);
            for (int p = 0; p < 3; p++)
                if (Imgui.Button((LightCfg.Pos == p ? "> " : "") + LightCfg.PosNames[p], 150)) LightLive.SwitchTo(p);
            Imgui.EndH();
            Imgui.Space(6);
            Colour("Warmes Licht tagsüber (Stellung Normal)", "LightDay");
            Colour("Warmes Licht nachts (Stellung Rot/Blau mit „warm“)", "LightNight");
            Colour("Turm-Licht nachts (wenn „Turm rot“ an)", "LightTower");
            Colour("Eigene Notlicht-Farbe (wenn „eigene Notlicht-Farbe“ an)", "LightEmerg");
            if (Imgui.Button("Farben auf Standard", 200))
                foreach (var k in new[] { "LightDay", "LightNight", "LightTower", "LightEmerg" })
                    foreach (var ch in new[] { "R", "G", "B" }) { var o = Settings.Get(k + ch); if (o != null) o.Value = o.Default; }
            if (Imgui.Button("Licht-Werte ins Log schreiben", 300) && LightLive.Lighting != null)
                Util.Safe("Licht.Log", () => { Util.Log("Licht: Stellung " + LightCfg.Pos + ", Stil " + LightCfg.CurStyle + ", Schaden " + LightLive.Damaged); });
            Imgui.Space(8);
        }

        private static void Colour(string label, string prefix)
        {
            Imgui.Label(label + ":");
            Imgui.BeginH();
            foreach (var ch in new[] { "R", "G", "B" })
            {
                var o = Settings.Get(prefix + ch);
                if (o == null) continue;
                Imgui.Label(ch + " " + Mathf.RoundToInt(o.Value), 60);
                o.Value = Mathf.Round(Imgui.Slider(Mathf.Clamp(o.Value, 0, 255), 0, 255, 140));
            }
            Imgui.EndH();
        }
    }
}
