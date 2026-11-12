using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Scene.Audio;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Reticle light at periscopes / UZO / binoculars:
    //  - key (default F) switches it on/off while looking through, with a click sound
    //  - own colour + brightness, a factor per optic
    //  - bearing scale balanced to the reticle (mask copy with scaled alpha, labels lit too,
    //    weak masks normalised) and compensated for the per-sight night grading
    public class Reticle : ModuleBase
    {
        internal static bool Lit = true;
        private static readonly FieldInfo GameInputF = AccessTools.Field(typeof(MainCamera), "gameInput");
        private static readonly FieldInfo AudioLibF = AccessTools.Field(typeof(Periscope), "audioLib");
        private static readonly FieldInfo AudioTargetF = AccessTools.Field(typeof(Periscope), "audioTarget");
        private static readonly FieldInfo TagsF = AccessTools.Field(typeof(AudioLib), "tags");
        private static readonly HashSet<int> loggedLibs = new HashSet<int>();
        private static readonly string[] SoundTags = { "Switch", "Click", "Button", "Zoom Swap" };
        private static GameInput input;
        private static string stamp;

        public override void Frame()
        {
            // re-balance the masks when a reticle setting changed
            string s = Settings.F("ReticleBearing") + "|" + Settings.B("ReticleLabels") + "|" + Settings.B("ReticleCustom");
            if (s != stamp) { bool first = stamp == null; stamp = s; if (!first) Util.Safe("Reticle.Reapply", ReticleMask.ReapplyAll); }

            if (!Keys.Down("KeyReticleLight")) return;
            if (input == null && GameInputF != null) input = GameInputF.GetValue(null) as GameInput;
            if (input != null && input.HasActiveBlockades()) return;
            var p = Current();
            if (p == null) return;
            Lit = !Lit;
            ToolkitRunner.ShowNotice(Lit ? "Fadenkreuz-Licht an" : "Fadenkreuz-Licht aus", 2f);
            if (Settings.B("ReticleSound")) Util.Safe("Reticle.Sound", () => Sound(p));
        }

        private static Periscope Current()
        {
            var cam = MainCamera.Instance;
            var pc = cam?.GenericController?.ExternalController as PeriscopeController;
            return pc != null && pc.Periscope != null && pc.Periscope.ManualControl ? pc.Periscope : null;
        }

        private static void Sound(Periscope p)
        {
            var lib = AudioLibF?.GetValue(p) as AudioLib;
            if (lib == null) return;
            var target = AudioTargetF?.GetValue(p) as Transform ?? p.transform;
            string played = null;
            foreach (var tag in SoundTags)
            {
                var src = lib.GetClip3D(tag, target);
                if (src == null) continue;
                src.volume *= Settings.F("ReticleSoundVolume");
                src.Play();
                played = tag;
                break;
            }
            if (TagsF != null && loggedLibs.Add(lib.GetInstanceID()) && TagsF.GetValue(lib) is IDictionary tags && tags.Count > 0)
            {
                var names = new List<string>();
                foreach (var k in tags.Keys) names.Add(Convert.ToString(k));
                names.Sort(StringComparer.OrdinalIgnoreCase);
                Util.Log("Fadenkreuz: Töne in '" + lib.name + "': " + string.Join(", ", names.ToArray()));
            }
            if (played == null) Util.Log("Fadenkreuz: kein passender Klick-Ton bei " + p.name);
        }

        // ---- colour ----
        private static readonly Color VanillaGreen = new Color(0.001456f, 0.014f, 0.001665f, 1f);
        private static readonly FieldInfo OverlayNameF = AccessTools.Field(typeof(Periscope), "screenOverlayName");
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Configured()
        {
            var tint = new Color(Settings.F("ReticleR") / 255f, Settings.F("ReticleG") / 255f, Settings.F("ReticleB") / 255f, 1f);
            float step = Mathf.Clamp(Settings.F("ReticleStep"), 0f, 100f);
            float brightness = 0.5f * Mathf.Pow(16f / 0.5f, step / 100f);
            float y = Lum(tint);
            if (!(y > 1e-6f)) return new Color(0f, 0f, 0f, 1f);
            float k = Lum(VanillaGreen) * brightness / y;
            return new Color(tint.r * k, tint.g * k, tint.b * k, 1f);
        }

        private static float ScopeFactor(Periscope p)
        {
            string o = p != null && OverlayNameF != null ? OverlayNameF.GetValue(p) as string : null;
            switch (o)
            {
                case "Targeting Sight": return Settings.F("ReticleMulUzo");
                case "Periscope": return Settings.F("ReticleMulAttack");
                case "Observation Periscope": return Settings.F("ReticleMulObservation");
                case "Binoculars": return Settings.F("ReticleMulBino");
                default: return 1f;
            }
        }

        internal static Color ColourFor(Periscope p)
        {
            var c = Configured();
            if (!Lit) return new Color(0f, 0f, 0f, c.a);
            float m = ScopeFactor(p) * ReticleMask.ColourMultiplierFor(p) * ReticleMask.MaskGainFor(p);
            return NightCompensate(new Color(c.r * m, c.g * m, c.b * m, c.a));
        }

        // The reticle is drawn before colour grading, so the per-sight grading would dim it: undo that.
        internal static Color NightCompensate(Color c)
        {
            int age = Time.frameCount - NightGrading.PublishedFrame;
            if (NightGrading.PublishedFrame < 0 || age < 0 || age > 1) return c;
            float ev = NightGrading.PublishedEvDelta, satBase = NightGrading.PublishedSatBase, satDelta = NightGrading.PublishedSatDelta;
            if (ev == 0f && satDelta == 0f) return c;
            float gain = Mathf.Pow(2f, -ev);
            float r = c.r * gain, g = c.g * gain, b = c.b * gain;
            float with = (satBase + satDelta) / 100f + 1f, without = satBase / 100f + 1f;
            if (with > 1e-3f && without > 0f)
            {
                float ratio = without / with, l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                r = l + (r - l) * ratio; g = l + (g - l) * ratio; b = l + (b - l) * ratio;
            }
            return new Color(Mathf.Max(0f, r), Mathf.Max(0f, g), Mathf.Max(0f, b), c.a);
        }
    }

    [HarmonyPatch(typeof(Periscope), "ReticleIlluminationColor", MethodType.Getter)]
    internal static class Reticle_ColourPatch
    {
        private static bool Prefix(Periscope __instance, ref Color __result)
        {
            if (!Settings.B("ReticleCustom")) return true;
            __result = Reticle.ColourFor(__instance);
            return false;
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Periscope __instance, ref Color __result)
        {
            if (Settings.B("ReticleCustom")) { __result = Reticle.ColourFor(__instance); return; }
            float m = Reticle.Lit ? Settings.F("ReticleBright") : 0f;
            if (!Mathf.Approximately(m, 1f)) __result = new Color(__result.r * m, __result.g * m, __result.b * m, __result.a);
        }
    }

    // Bearing scale: the game's illumination mask is far brighter than the reticle. A copy with
    // scaled alpha (and the scale's numbers merged in) evens it out; the reticle colour is raised to match.
    internal static class ReticleMask
    {
        private const float ShaderGain = 4.6f, MaxMaskGain = 16f;
        private const int MaxCopies = 8;
        private static readonly FieldInfo MaterialF = AccessTools.Field(typeof(PeriscopeIME), "material");
        private static int illumId = -1, overlayId = -1;
        private static int IllumId => illumId < 0 ? (illumId = Shader.PropertyToID("_IlluminationTex")) : illumId;
        private static int OverlayId => overlayId < 0 ? (overlayId = Shader.PropertyToID("_OverlayTex")) : overlayId;
        private static bool missingLogged;

        private sealed class Copy { public Texture2D Scaled; public float Multiplier, PeakAlpha; }
        private sealed class Applied { public PeriscopeIME Ime; public Texture Original; public Copy Copy; }
        private static readonly Dictionary<int, Copy> copies = new Dictionary<int, Copy>();
        private static readonly List<Applied> live = new List<Applied>();
        private static readonly Dictionary<int, float> multByScope = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> gainByScope = new Dictionary<int, float>();

        public static float ColourMultiplierFor(Periscope p) => p != null && multByScope.TryGetValue(p.GetInstanceID(), out var m) ? m : 1f;
        public static float MaskGainFor(Periscope p) => Settings.B("ReticleNormalize") && p != null && gainByScope.TryGetValue(p.GetInstanceID(), out var g) ? g : 1f;
        private static float MaskGain(float peak) { if (float.IsNaN(peak) || peak <= 0f) return 1f; float g = 1f / (peak * peak); return Mathf.Clamp(g, 1f, MaxMaskGain); }

        public static void Apply(PeriscopeIME ime)
        {
            try
            {
                if (!Settings.B("ReticleCustom") || ime == null) return;
                if (MaterialF == null) { if (!missingLogged) { missingLogged = true; Util.Log("Fadenkreuz: Masken-Material nicht gefunden (Spiel-Update?) - Peilskala bleibt wie im Spiel"); } return; }
                var mat = MaterialF.GetValue(ime) as Material;
                if (mat == null) return;
                var scope = ime.Periscope;
                var current = mat.GetTexture(IllumId);
                var existing = Find(ime);
                var source = existing != null && existing.Copy != null && ReferenceEquals(current, existing.Copy.Scaled) ? existing.Original : current;
                if (source == null || source.width <= 4 || source.height <= 4)
                {
                    Forget(ime, false);
                    if (scope != null) gainByScope.Remove(scope.GetInstanceID());
                    return;
                }
                var overlay = Settings.B("ReticleLabels") ? mat.GetTexture(OverlayId) : null;
                if (overlay != null && (overlay.width <= 4 || overlay.height <= 4)) overlay = null;
                var copy = CopyFor(source, overlay);
                if (copy == null) return;
                if (scope != null) gainByScope[scope.GetInstanceID()] = MaskGain(copy.PeakAlpha);
                if (copy.Scaled == null)
                {
                    if (existing != null) Forget(ime, true);
                    if (scope != null) multByScope.Remove(scope.GetInstanceID());
                    return;
                }
                mat.SetTexture(IllumId, copy.Scaled);
                if (existing == null) { existing = new Applied { Ime = ime }; live.Add(existing); }
                existing.Original = source; existing.Copy = copy;
                if (scope != null) multByScope[scope.GetInstanceID()] = copy.Multiplier;
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Peilskala nicht angepasst, Spiel-Maske bleibt: " + e.Message); }
        }

        private static Applied Find(PeriscopeIME ime)
        {
            for (int i = live.Count - 1; i >= 0; i--) { if (live[i].Ime == null) { live.RemoveAt(i); continue; } if (ReferenceEquals(live[i].Ime, ime)) return live[i]; }
            return null;
        }
        private static void Forget(PeriscopeIME ime, bool restore)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var a = live[i];
                if (a.Ime == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(a.Ime, ime)) continue;
                if (restore) Restore(a);
                live.RemoveAt(i);
            }
        }
        private static void Restore(Applied a)
        {
            try
            {
                var mat = a.Ime != null ? MaterialF.GetValue(a.Ime) as Material : null;
                if (mat != null && a.Copy != null && ReferenceEquals(mat.GetTexture(IllumId), a.Copy.Scaled)) mat.SetTexture(IllumId, a.Original);
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Zurücksetzen fehlgeschlagen: " + e.Message); }
        }
        public static void RestoreAll()
        {
            for (int i = live.Count - 1; i >= 0; i--) Restore(live[i]);
            live.Clear(); multByScope.Clear(); gainByScope.Clear();
            foreach (var kv in copies) if (kv.Value.Scaled != null) UnityEngine.Object.Destroy(kv.Value.Scaled);
            copies.Clear();
        }
        public static void ReapplyAll()
        {
            var imes = new List<PeriscopeIME>();
            foreach (var a in live) if (a.Ime != null) imes.Add(a.Ime);
            if (imes.Count == 0) foreach (var i in UnityEngine.Object.FindObjectsOfType<PeriscopeIME>()) imes.Add(i);
            RestoreAll();
            if (!Settings.B("ReticleCustom")) return;
            foreach (var i in imes) Apply(i);
        }

        private static Texture2D ReadBack(Texture src, string name)
        {
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = name, hideFlags = HideFlags.HideAndDontSave };
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                return t;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        // Ink of the overlay that is NOT connected to the edge (= the numbers inside the scale).
        private static byte[] IsolatedInk(Texture overlay, int w, int h)
        {
            Texture2D ov = null;
            try
            {
                ov = ReadBack(overlay, "overlay readback");
                int ow = ov.width, oh = ov.height;
                var op = ov.GetPixels32();
                var surround = new byte[op.Length];
                var queue = new int[op.Length];
                int head = 0, tail = 0;
                const byte Ink = 8;
                void Seed(int i) { if (op[i].a >= Ink && surround[i] == 0) { surround[i] = 1; queue[tail++] = i; } }
                for (int x = 0; x < ow; x++) { Seed(x); Seed((oh - 1) * ow + x); }
                for (int y = 0; y < oh; y++) { Seed(y * ow); Seed(y * ow + ow - 1); }
                while (head < tail)
                {
                    int i = queue[head++], x = i % ow, y = i / ow;
                    if (x > 0) Seed(i - 1);
                    if (x < ow - 1) Seed(i + 1);
                    if (y > 0) Seed(i - ow);
                    if (y < oh - 1) Seed(i + ow);
                }
                var ink = new byte[w * h];
                if (ow == w && oh == h) for (int i = 0; i < ink.Length; i++) ink[i] = surround[i] == 0 ? op[i].a : (byte)0;
                else
                    for (int y = 0; y < h; y++)
                    {
                        int oy = Mathf.Min(oh - 1, y * oh / h);
                        for (int x = 0; x < w; x++) { int o = oy * ow + Mathf.Min(ow - 1, x * ow / w); ink[y * w + x] = surround[o] == 0 ? op[o].a : (byte)0; }
                    }
                return ink;
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Zahlen der Skala nicht lesbar: " + e.Message); return null; }
            finally { if (ov != null) UnityEngine.Object.Destroy(ov); }
        }

        private static Copy CopyFor(Texture source, Texture overlay)
        {
            int id = source.GetInstanceID() * 31 + (overlay == null ? 0 : overlay.GetInstanceID());
            if (copies.TryGetValue(id, out var copy)) return copy;
            if (copies.Count >= MaxCopies) { foreach (var kv in copies) if (kv.Value.Scaled != null) UnityEngine.Object.Destroy(kv.Value.Scaled); copies.Clear(); }
            int w = source.width, h = source.height;
            var scaled = ReadBack(source, source.name + " (Toolkit)");
            var px = scaled.GetPixels32();
            int peak = 0;
            foreach (var p in px) if (p.a > peak) peak = p.a;
            float peakAlpha = peak / 255f;
            int labels = 0;
            if (overlay != null && peak > 0)
            {
                var ink = IsolatedInk(overlay, w, h);
                if (ink != null)
                    for (int i = 0; i < px.Length; i++) { int lit = ink[i] * peak / 255; if (lit > px[i].a) { var c = px[i]; c.a = (byte)lit; px[i] = c; labels++; } }
            }
            float m = Mathf.Max(1f, Mathf.Clamp(Settings.F("ReticleBearing"), 0.2f, 2f) * ShaderGain * peakAlpha * peakAlpha);
            float k = 1f / Mathf.Sqrt(m);
            if (m > 1.0001f || labels > 0)
            {
                if (m > 1.0001f) for (int i = 0; i < px.Length; i++) { var c = px[i]; c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * k), 0, 255); px[i] = c; }
                scaled.SetPixels32(px);
                scaled.Apply(true, true);
            }
            else { UnityEngine.Object.Destroy(scaled); scaled = null; }
            copy = new Copy { Scaled = scaled, Multiplier = m, PeakAlpha = peakAlpha };
            copies[id] = copy;
            return copy;
        }
    }

    [HarmonyPatch(typeof(PeriscopeIME), "ValidateOverlay")]
    internal static class Reticle_OverlayPatch
    {
        private static void Postfix(PeriscopeIME __instance) => ReticleMask.Apply(__instance);
    }
}
