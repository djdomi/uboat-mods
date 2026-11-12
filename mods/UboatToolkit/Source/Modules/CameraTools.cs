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
}
