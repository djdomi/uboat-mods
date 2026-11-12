using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;

namespace UboatToolkit.Modules
{
    // Instrument stations (hydrophone, TDC, ...): Ctrl + mouse wheel zooms in.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.DeviceControllers.BasicDeviceController), "UpdateController")]
    internal static class CameraTools_StationZoomPatch
    {
        private static object last;
        private static int seen = -10;
        private static float start = float.NaN, target;
        private static Camera cam;

        private static void Postfix(object __instance)
        {
            if (!Settings.B("StationZoom")) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            if (__instance != last || Time.frameCount - seen > 2) { last = __instance; start = target = cam.fieldOfView; }
            seen = Time.frameCount;
            float wheel = Keys.Ctrl ? Input.GetAxis("Mouse ScrollWheel") : 0f;
            if (wheel != 0f) target = Mathf.Clamp(target - wheel * 40f, 10f, start);
            if (Mathf.Abs(cam.fieldOfView - target) > 0.01f) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, Mathf.Clamp01(Time.unscaledDeltaTime * 12f));
        }

        // called every frame by the main camera: left the station -> give the lens back
        internal static void Check()
        {
            if (float.IsNaN(start) || Time.frameCount - seen <= 2) return;
            if (cam != null && Mathf.Abs(cam.fieldOfView - target) < 0.5f) cam.fieldOfView = start;
            start = float.NaN; last = null;
        }
    }

    // Submerged with the orbit camera: the camera stays below the water surface (you are "blind" down there).
    [HarmonyPatch(typeof(SectionViewCameraController), "UpdateTransformNormalMode")]
    internal static class CameraTools_OrbitPatch
    {
        private static float ceiling = float.NaN;
        private static Weather weather;

        private static void Prefix(SectionViewCameraController __instance)
        {
            if (float.IsNaN(ceiling) || __instance.DistancePresetIndex != 3) return;
            var r = __instance.FarCameraRotation;
            if (r.y > ceiling) { r.y = ceiling; __instance.FarCameraRotation = r; }
        }

        private static void Postfix(SectionViewCameraController __instance, PlayerShip ___playerShip)
        {
            var ship = ___playerShip;
            if (!Settings.B("CamOrbitUnderwater") || ship == null || __instance.DistancePresetIndex != 3 || ship.DeckDepth < 3f) { ceiling = float.NaN; return; }
            if (weather == null) weather = Object.FindObjectOfType<Weather>();
            var cam = Camera.main;
            if (weather == null || cam == null) { ceiling = float.NaN; return; }
            float sea = weather.Sea.VerticalOffset;
            float y = cam.transform.position.y;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.09f);
            float now = __instance.FarCameraRotation.y;
            if (float.IsNaN(ceiling)) ceiling = 25f;
            if (y > sea - 1.5f) ceiling = Mathf.Min(ceiling, now) - 90f * dt;
            else if (y < sea - 4f) ceiling += 20f * dt;
            ceiling = Mathf.Clamp(ceiling, -100f, 25f);
        }
    }
}
