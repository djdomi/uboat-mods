using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using PlayWay.Water;
using UnityEngine;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Submerged, the far orbit camera stays below the real (wavy) sea surface, unless a periscope
    // is above water. The ceiling follows the waves smoothly and is released again when surfaced.
    internal sealed class OrbitSea
    {
        private Water sea;
        private WaterSample sample;
        private bool primed;
        public bool Ready => sample != null && sea != null;

        public void Bind(Weather w)
        {
            var s = w != null ? w.Sea : null;
            if (s == null) { Release(); return; }
            if (sample != null && sea == s) return;
            Release();
            sea = s;
            sample = new WaterSample(s, WaterSample.DisplacementMode.Height, 1f);
            primed = false;
        }

        public void Release()
        {
            if (sample != null) { try { sample.Stop(); } catch { } sample = null; }
            sea = null; primed = false;
        }

        public float HeightAt(Vector3 p, out bool found)
        {
            found = false;
            if (!Ready) return 0f;
            if (!primed) { sample.Start(p); primed = true; }
            found = true;
            return sample.GetAndResetFast(p, sea.Time);
        }
    }

    internal sealed class OrbitPeriscopes
    {
        private static readonly FieldInfo LastAbove = typeof(Periscope).GetField("lastTicksAboveWater", BindingFlags.Instance | BindingFlags.NonPublic);
        private Periscope[] scopes;
        private PlayerShip scanned;
        private float nextScan;
        public static bool Usable => LastAbove != null;
        public int Count => scopes == null ? 0 : scopes.Length;

        public bool AnyAboveWater(PlayerShip ship, GameTime time, float now, float grace)
        {
            if (ship == null || time == null || LastAbove == null) return false;
            if (scopes == null || scanned != ship || now >= nextScan) { nextScan = now + 5f; scanned = ship; scopes = ship.GetComponentsInChildren<Periscope>(true); }
            if (scopes.Length == 0) return false;
            long graceTicks = (long)(grace * 1e7f), t = time.UnscaledTicks;
            foreach (var p in scopes)
            {
                if (p == null) continue;
                long stamp;
                try { stamp = (long)LastAbove.GetValue(p); } catch { continue; }
                if (t - stamp <= graceTicks) return true;
            }
            return false;
        }

        public void Invalidate() { scopes = null; scanned = null; nextScan = 0f; }
    }

    [HarmonyPatch(typeof(SectionViewCameraController), "UpdateTransformNormalMode")]
    internal static class OrbitWater_Patch
    {
        private const float VanillaMax = 25f, MaxDt = 0.09f, MinLever = 3f, Eps = 0.05f;
        private const int OrbitPreset = 3;
        private static readonly OrbitSea Sea = new OrbitSea();
        private static readonly OrbitPeriscopes Scopes = new OrbitPeriscopes();
        private static float ceiling = float.NaN, nextLog;
        private static bool converged, clamping;
        private static int lastFrame = -1;

        private static bool On => Settings.B("CamOrbitUnderwater");
        private static bool Log => Settings.B("OrbitLog");

        private static void Prefix(SectionViewCameraController __instance, float ___verticalSpeed)
        {
            if (!On || float.IsNaN(ceiling) || __instance.DistancePresetIndex != OrbitPreset) return;
            float c = ceiling;
            if (Settings.B("OrbitAnticipate"))
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
                float d = ___verticalSpeed * -10f * dt;   // what this frame's mouse input will add
                if (d > 0f) c -= d;
            }
            var r = __instance.FarCameraRotation;
            if (r.y > c) { r.y = c; __instance.FarCameraRotation = r; }
        }

        private static void Postfix(SectionViewCameraController __instance, MainCamera ___mainCamera, PlayerShip ___playerShip, Weather ___weather, GameTime ___gameTime)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (!On || !OrbitPeriscopes.Usable || ___mainCamera == null || ___playerShip == null || ___weather == null || ___gameTime == null) { Standdown(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
            if (dt <= 0f) return;
            if (__instance.DistancePresetIndex != OrbitPreset) { Release(dt); return; }
            Sea.Bind(___weather);
            if (!Sea.Ready) { Release(dt); return; }
            bool want = ___playerShip.Submerged && !Scopes.AnyAboveWater(___playerShip, ___gameTime, now, Settings.F("OrbitGrace"));
            if (!want) { Release(dt); return; }
            var cam = ___mainCamera.transform.position;
            float seaY = Sea.HeightAt(cam, out bool found);
            if (!found) { Standdown(); return; }
            float excess = cam.y - (seaY - Settings.F("OrbitMargin"));
            var pivot = __instance.Target;
            if (pivot == null) { Standdown(); return; }
            float dx = cam.x - pivot.position.x, dz = cam.z - pivot.position.z;
            float lever = Mathf.Max(MinLever, Mathf.Sqrt(dx * dx + dz * dz));
            float target = Mathf.Min(VanillaMax, __instance.FarCameraRotation.y - excess * (Mathf.Rad2Deg / lever) * Settings.F("OrbitGain"));
            if (float.IsNaN(ceiling))
            {
                ceiling = Mathf.Min(VanillaMax, __instance.FarCameraRotation.y);
                converged = false;
                if (Log) Util.Log("Orbit: greift ein, Decke=" + F(ceiling) + " Ziel=" + F(target) + " camY=" + F(cam.y) + " Meer=" + F(seaY) + " Hebel=" + F(lever) + " Sehrohre=" + Scopes.Count);
            }
            else
            {
                float step = Settings.F("OrbitTrack") * dt;
                if (target < ceiling)
                {
                    if (converged) ceiling = target;
                    else { ceiling = Mathf.Max(target, ceiling - step); if (ceiling <= target + Eps) converged = true; }
                }
                else ceiling = Mathf.Min(target, ceiling + step);
            }
            clamping = true;
            if (Log && now >= nextLog)
            {
                nextLog = now + 2f;
                Util.Log("Orbit: Decke=" + F(ceiling) + " Ziel=" + F(target) + " Winkel=" + F(__instance.FarCameraRotation.y) + " über Wasser=" + F(cam.y - seaY) + " Abstand=" + F(__instance.CameraDistance) + " eingerastet=" + converged);
            }
        }

        private static void Release(float dt)
        {
            if (float.IsNaN(ceiling)) return;
            ceiling += Settings.F("OrbitRelax") * dt;
            if (ceiling >= VanillaMax) { if (Log) Util.Log("Orbit: freigegeben"); ceiling = float.NaN; converged = false; }
            clamping = false;
        }

        private static void Standdown()
        {
            if (clamping && Log) Util.Log("Orbit: abgeschaltet");
            ceiling = float.NaN; converged = false; clamping = false;
        }

        private static string F(float f) => f.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
